
using EZCameraShake;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Player : NetworkBehaviour
{
    [Header("Health")]
    public NetworkVariable<float> health = new(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private readonly NetworkVariable<bool> networkIsDead = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public bool _isDead;

    [Header("Impact Damage")]
    [Min(0f)] public float minimumImpactDeltaV = 5f;
    [Min(0f)] public float damagePerDeltaV = 10f;
    [Min(0f)] public float maxImpactDamage = 100f;
    public bool debugImpactDamage = false;

    [Header("References")]
    public static Player me;
    public PlayerInput _input;
    public PlayerMovement _movement;
    public PlayerRagdollManager _ragdollManager;
    public PlayerLogic _logic;

    public GameObject poop;
    public Transform poopSpawnLocation;
    public GameObject deathGFX;

    [Space]
    public Component[] componentsToDestroy_Other;
    public GameObject[] objects_Disable_Other;
    public Renderer[] renderers_ShadowsOnly_Other;

    [Space]
    public GameObject[] objects_Disable_Local;
    public Renderer[] renderers_ShadowsOnly_Local;

    [Space]
    public GameObject[] onDeathObjs;
    public GameObject spectatorObj;

    [Header("Invisibility Ability")]
    public float invisDuration = 5f;
    public float invisCooldownTime = 12f;

    private float invisCooldownTimer;
    private float invisActiveTimer;
    private bool isInvisible;
    private bool isCoolingDown;

    [Header("Invisibility Renderers")]
    public SkinnedMeshRenderer[] stealthRenderers;

    [Header("Post Processing")]
    public Volume volume;
    public Camera playerCamera;

    [Range(0f, 1f)]
    public float maximumVignetteIntensity = 0.65f;

    private Vignette vignette;

    // ==========================================
    // INITIALIZATION
    // ==========================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        health.OnValueChanged += OnHealthChanged;
        networkIsDead.OnValueChanged += OnDeathStateChanged;

        if (IsOwner)
            InitLocal();
        else
            InitOther();

        ApplyDeathState(networkIsDead.Value);

        if (IsOwner)
            UpdateHealthVignette(health.Value);
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
        networkIsDead.OnValueChanged -= OnDeathStateChanged;

        if (me == this)
            me = null;

        base.OnNetworkDespawn();
    }

    public void InitLocal()
    {
        me = this;

        if (_input != null)
            _input.enabled = true;

        foreach (GameObject g in objects_Disable_Local)
        {
            if (g != null)
                g.SetActive(false);
        }

        foreach (Renderer r in renderers_ShadowsOnly_Local)
        {
            if (r != null)
            {
                r.shadowCastingMode =
                    ShadowCastingMode.ShadowsOnly;
            }
        }

        if (Server._instance != null &&
            Server._instance.IsSpawned)
        {
            Server._instance.ConnectPlayer_RPC(NetworkObjectId);
        }

        transform.position = PlayerSpawn._instance.transform.position;
        InitializePostProcessing();

        StartCoroutine(SpawnAfter());
    }

    IEnumerator SpawnAfter()
    {
        yield return new WaitForSeconds(0.125f);

        Vector3 position = GameObject.Find("PlayerSpawn").transform.position;
        transform.position = position;
    }

    public void InitOther()
    {
        if (volume != null)
            volume.enabled = false;

        foreach (Component c in componentsToDestroy_Other)
        {
            if (c != null)
                Destroy(c);
        }

        foreach (GameObject g in objects_Disable_Other)
        {
            if (g != null)
                g.SetActive(false);
        }

        foreach (Renderer r in renderers_ShadowsOnly_Other)
        {
            if (r != null)
            {
                r.shadowCastingMode =
                    ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    public override void OnNetworkPreDespawn()
    {
        if (IsOwner &&
            Server._instance != null &&
            Server._instance.IsSpawned)
        {
            Server._instance.DisconnectPlayer_RPC(NetworkObjectId);
        }

        base.OnNetworkPreDespawn();
    }

    // ==========================================
    // IMPACT DAMAGE
    // ==========================================

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsOwner || _isDead)
            return;

        if (_movement == null || _movement.rb == null)
            return;

        Rigidbody rb = _movement.rb;

        // Impulse-based damage requires a dynamic Rigidbody.
        if (rb.isKinematic)
            return;

        // Impulse = mass * change in velocity.
        // This approximates collision-induced delta-V.
        float deltaV = collision.impulse.magnitude /
                       Mathf.Max(rb.mass, 0.0001f);

        if (debugImpactDamage)
        {
            Debug.Log(
                $"Impact with {collision.gameObject.name} | " +
                $"Delta-V: {deltaV:F2} m/s"
            );
        }

        if (deltaV <= minimumImpactDeltaV)
            return;

        // Only velocity change ABOVE the threshold
        // contributes to damage.
        float excessDeltaV = deltaV - minimumImpactDeltaV;

        float damage = Mathf.Clamp(
            excessDeltaV * damagePerDeltaV,
            0f,
            maxImpactDamage
        );

        Damage(damage);
    }

    // ==========================================
    // HEALTH / DAMAGE
    // ==========================================

    public void Damage(float damage)
    {
        if (!IsSpawned || damage <= 0f)
            return;

        if (IsOwner)
        {
            ApplyDamage(damage);
        }
        else
        {
            Damage_RPC(damage);
        }
    }

    [Rpc(
        SendTo.Owner,
        InvokePermission = RpcInvokePermission.Everyone
    )]
    public void Damage_RPC(float damage)
    {
        ApplyDamage(damage);
    }

    private void ApplyDamage(float damage)
    {
        if (!IsOwner || _isDead || damage <= 0f)
            return;

        health.Value = Mathf.Max(0f, health.Value - damage);

        ShakeCamera(2f, 3f, 0.1f, 0.5f);

        if (health.Value <= 0f)
        {
            SetDeathState_RPC(true);
        }
    }

    private void OnHealthChanged(float previous, float current)
    {
        if (IsOwner)
            UpdateHealthVignette(current);
    }

    // ==========================================
    // POST PROCESSING
    // ==========================================

    private void InitializePostProcessing()
    {
        if (volume == null)
        {
            Debug.LogWarning(
                "Player: No post-processing Volume assigned."
            );
            return;
        }

        // This Volume belongs only to the local player.
        volume.enabled = true;
        volume.isGlobal = true;
        volume.weight = 1f;

        if (!volume.gameObject.activeInHierarchy)
        {
            Debug.LogWarning(
                "Player: The Volume GameObject is inactive."
            );
        }

        VolumeProfile profile = volume.profile;

        if (profile == null)
        {
            Debug.LogWarning(
                "Player: No Volume Profile available."
            );
            return;
        }

        // Find or create the Vignette override.
        if (!profile.TryGet(out vignette))
        {
            vignette = profile.Add<Vignette>(true);
        }

        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0f;

        // Enable URP post-processing on the local camera.
        if (playerCamera != null)
        {
            UniversalAdditionalCameraData cameraData =
                playerCamera.GetUniversalAdditionalCameraData();

            cameraData.renderPostProcessing = true;

            // Ensure the camera can see this Volume's layer.
            cameraData.volumeLayerMask =
                cameraData.volumeLayerMask.value |
                (1 << volume.gameObject.layer);
        }
        else
        {
            Debug.LogWarning(
                "Player: Assign the local player camera."
            );
        }
    }

    private void UpdateHealthVignette(float currentHealth)
    {
        float healthPercent = Mathf.Clamp01(
            currentHealth / 100f
        );

        // 100 HP -> no vignette
        //   0 HP -> maximum vignette
        float intensity =
            (1f - healthPercent) * maximumVignetteIntensity;

        SetVignette(intensity);
    }

    public void SetVignette(float intensity)
    {
        if (!IsOwner || vignette == null)
            return;

        vignette.intensity.Override(
            Mathf.Clamp01(intensity)
        );
    }

    // ==========================================
    // DEATH / RESPAWN
    // ==========================================

    // Keeps your original method name for compatibility.
    // Death synchronization is now handled by a NetworkVariable.
    public void SetDeathState_RPC(bool state)
    {
        Instantiate(deathGFX, transform.position, Quaternion.identity);

        if (!IsOwner || !IsSpawned)
            return;

        if (networkIsDead.Value == state)
            return;

        Instantiate(deathGFX, transform.position, transform.rotation);

        networkIsDead.Value = state;

        // Report a kill only when transitioning
        // from alive to dead.
        if (state && GameManager._instance != null)
        {
            GameManager._instance.OnHiderDeath_RPC(
                NetworkObjectId
            );
        }
    }

    private void OnDeathStateChanged(bool previous, bool current)
    {
        ApplyDeathState(current);
    }

    private void ApplyDeathState(bool state)
    {
        _isDead = state;

        // Spectator camera is LOCAL ONLY.
        if (spectatorObj != null)
        {
            if (IsOwner)
            {
                if (state && _movement != null)
                {
                    spectatorObj.transform.position =
                        _movement.transform.position;

                    spectatorObj.transform.rotation =
                        _movement.transform.rotation;
                }

                spectatorObj.SetActive(state);
            }
            else
            {
                spectatorObj.SetActive(false);
            }
        }

        foreach (GameObject obj in onDeathObjs)
        {
            if (obj != null)
                obj.SetActive(!state);
        }
    }

    public void OnReset()
    {
        if (!IsOwner)
            return;

        health.Value = 100f;
        SetDeathState_RPC(false);
        SetVignette(0f);

        // Reset invisibility.
        isInvisible = false;
        isCoolingDown = false;
        invisActiveTimer = 0f;
        invisCooldownTimer = 0f;

        SetInvisibility(false);
    }

    // ==========================================
    // CAMERA SHAKE
    // ==========================================

    public void ShakeCamera(
        float magnitude,
        float roughness,
        float fadeInTime,
        float fadeOutTime)
    {
        if (!IsOwner || CameraShaker.Instance == null)
            return;

        CameraShaker.Instance.ShakeOnce(
            magnitude,
            roughness,
            fadeInTime,
            fadeOutTime
        );
    }

    // ==========================================
    // INVISIBILITY
    // ==========================================

    private void FixedUpdate()
    {
        if (!IsOwner)
            return;

        float dt = Time.fixedDeltaTime;

        if (isInvisible)
        {
            invisActiveTimer -= dt;

            if (invisActiveTimer <= 0f)
            {
                SetInvisibility(false);

                isInvisible = false;
                isCoolingDown = true;
                invisCooldownTimer = invisCooldownTime;

                if (_logic != null)
                    _logic.TriggerInvisCooldown();
            }
        }

        if (isCoolingDown)
        {
            invisCooldownTimer = Mathf.Max(
                0f,
                invisCooldownTimer - dt
            );

            if (invisCooldownTimer <= 0f)
                isCoolingDown = false;
        }
    }

    private void Update()
    {
        if (!IsOwner || _isDead ||
            _input == null || !_input.enabled)
            return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                _ragdollManager.SetRagdoll(
                    !_ragdollManager.isRagdoll
                );
            }

            if (Keyboard.current.fKey.wasPressedThisFrame)
                Damage(34f);
        }

        var invisAction =
            _input.actions?.FindAction("InvisAbility");

        if (invisAction != null &&
            invisAction.WasPressedThisFrame())
        {
            Invisible();
        }
    }

    public void Invisible()
    {
        if (!IsOwner || _isDead ||
            _movement == null ||
            !_movement.HasControl ||
            _movement.IsLocked() ||
            _movement.isStunned ||
            isInvisible ||
            isCoolingDown)
        {
            return;
        }

        isInvisible = true;
        invisActiveTimer = invisDuration;

        SetInvisibility(true);
        ShakeCamera(1f, 3f, 0.1f, 0.5f);

        if (_logic != null)
            _logic.SetInvisActiveUI();
    }

    private void SetInvisibility(bool invisible)
    {
        if (stealthRenderers == null)
            return;

        foreach (var rend in stealthRenderers)
        {
            if (rend != null)
                rend.enabled = !invisible;
        }
    }

    // ==========================================
    // POOP MECHANIC
    // ==========================================

    public void TURDMECHANICWHATTHEFUCKKKKKKKKKHOOLYSHITTURDMECHANIC()
    {
        if (!IsOwner || _isDead)
            return;

        if (Random.Range(0f, 1f) < 0.05f)
        {
            ShakeCamera(1f, 10f, 0.1f, 0.5f);
            FuckingPoop_RPC();
        }
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner
    )]
    public void FuckingPoop_RPC()
    {
        NetworkObject obj =
            NetworkManager.SpawnManager.InstantiateAndSpawn(
                poop.GetComponent<NetworkObject>(),
                0,
                false,
                false,
                false,
                poopSpawnLocation.position,
                poopSpawnLocation.rotation
            );

        Rigidbody rb = obj.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.AddForce(
                (obj.transform.position - transform.position)
                    .normalized,
                ForceMode.Impulse
            );

            rb.AddTorque(
                obj.transform.forward,
                ForceMode.Impulse
            );
        }
    }
}
