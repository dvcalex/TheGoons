using EZCameraShake;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Player : NetworkBehaviour
{
    public NetworkVariable<float> health = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool _isDead;

    public static Player me;
    public PlayerInput _input;
    public PlayerMovement _movement;
    public PlayerRagdollManager _ragdollManager;
    public PlayerLogic _logic;

    public GameObject poop;
    public Transform poopSpawnLocation;

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
    public float invisDuration = 5f; // Set this to 5 seconds
    public float invisCooldownTime = 12f; // Cooldown starts AFTER invisibility ends
    private float invisCooldownTimer = 0f;
    private float invisActiveTimer = 0f;
    private bool isInvisible = false;
    private bool isCoolingDown = false;

    [Header("Invisibility Renderers")]
    public SkinnedMeshRenderer[] stealthRenderers; // Drag your 4 skinned mesh renderers here in the Inspector

    public Volume volume;

    private Vignette vignette;

    Vector3 lastVelocity;


    private void Start()
    {
        if (IsOwner)
        {
            InitLocal();
        }
        else
        {
            InitOther();
        }
    }

    public void InitLocal()
    {
        _input.enabled = true;

        if (me == null)
        {
            me = this;
        }

        foreach (GameObject g in objects_Disable_Local)
        {
            g.SetActive(false);
        }

        foreach (Renderer r in renderers_ShadowsOnly_Local)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        Server._instance.ConnectPlayer_RPC(NetworkObjectId);

        if (volume.profile.TryGet(out vignette))
        {
            vignette.intensity.overrideState = true;
        }

        spectatorObj.SetActive(false);
    }

    public void InitOther()
    {
        foreach (Component c in componentsToDestroy_Other)
        {
            Destroy(c);
        }

        foreach (GameObject g in objects_Disable_Other)
        {
            g.SetActive(false);
        }

        foreach (Renderer r in renderers_ShadowsOnly_Other)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        spectatorObj.SetActive(false);
    }

    public override void OnNetworkPreDespawn()
    {
        if (IsOwner)
        {
            Server._instance.DisconnectPlayer_RPC(NetworkObjectId);
        }
    }

    public void ShakeCamera(float magnitude, float roughness, float fadeInTime, float fadeOutTime)
    {
        if (IsOwner)
        {
            CameraShaker.Instance.ShakeOnce(magnitude, roughness, fadeInTime, fadeOutTime);
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;

        // 1. Active Stealth Duration (5 seconds)
        if (isInvisible)
        {
            invisActiveTimer -= dt;

            if (invisActiveTimer <= 0f)
            {
                // Stealth ended: Turn renderers back on
                SetInvisibility(false);
                isInvisible = false;
                isCoolingDown = true;
                invisCooldownTimer = invisCooldownTime;

                // ---> START THE UI COOLDOWN WIPEPAGE HERE <---
                if (_logic != null)
                {
                    _logic.TriggerInvisCooldown();
                }
            }
        }

        // 2. Cooldown Duration
        if (isCoolingDown)
        {
            invisCooldownTimer = Mathf.Max(0f, invisCooldownTimer - dt);

            if (invisCooldownTimer <= 0f)
            {
                isCoolingDown = false;
            }
        }

        lastVelocity = _movement.rb.linearVelocity;
    }

    private void Update()
    {
        if (IsOwner && _input != null && _input.enabled)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                _ragdollManager.SetRagdoll(!_ragdollManager.isRagdoll);
            }

            if (Keyboard.current.fKey.wasPressedThisFrame)
                Damage(34f);

            // Check input for Invisibility ability
            if (_input.actions["InvisAbility"] != null && _input.actions["InvisAbility"].WasPressedThisFrame())
            {
                Invisible();
            }
        }
    }

    public void Invisible()
    {
        if (_movement == null || !_movement.HasControl || _movement.IsLocked() || _movement.isStunned || isInvisible || isCoolingDown)
        {
            return;
        }

        // Activate Invisibility
        isInvisible = true;
        invisActiveTimer = invisDuration;
        SetInvisibility(true);

        ShakeCamera(1f, 3f, 0.1f, 0.5f);

        // ---> GRAY OUT UI ICON WHILE ACTIVE <---
        if (_logic != null)
        {
            _logic.SetInvisActiveUI();
        }
    }

    private void SetInvisibility(bool invisible)
    {
        Debug.Log("Setting invisibility to " + invisible);

        if (stealthRenderers != null)
        {
            foreach (var rend in stealthRenderers)
            {
                if (rend != null)
                {
                    rend.enabled = !invisible; // Disables mesh when invisible, enables when visible
                }
            }
        }
    }


    public void TURDMECHANICWHATTHEFUCKKKKKKKKKHOOLYSHITTURDMECHANIC()
    {
        if (Random.Range(0f, 1f) < 0.05f)
        {
            ShakeCamera(1f, 3f, 0.1f, 0.5f);
            FuckingPoop_RPC();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void FuckingPoop_RPC()
    {
        NetworkObject obj = NetworkManager.SpawnManager.InstantiateAndSpawn(poop.GetComponent<NetworkObject>(), 0, false, false, false, poopSpawnLocation.position, poopSpawnLocation.rotation);

        obj.GetComponent<Rigidbody>().AddForce((obj.transform.position - transform.position).normalized, ForceMode.Impulse);
        obj.GetComponent<Rigidbody>().AddTorque(obj.transform.forward, ForceMode.Impulse);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Everyone)]
    public void Damage_RPC(float damage)
    {
        health.Value -= damage;
        SetVignette(Mathf.Clamp(health.Value, 0f, 100f) / 100f);

        ShakeCamera(1f, 3f, 0.1f, 0.5f);

        if (health.Value <= 0f)
            SetDeathState_RPC(true);
    }

    public void Damage(float damage)
    {
        health.Value -= damage;
        SetVignette(1f - (Mathf.Clamp(health.Value, 0f, 100f) / 100f));

        if (health.Value <= 0f)
            SetDeathState_RPC(true);
    }

    public void OnReset()
    {
        SetDeathState_RPC(false);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    public void SetDeathState_RPC(bool state)
    {
        if (IsOwner)
        {
            GameManager._instance.OnHiderDeath_RPC(NetworkObjectId);
        }
       
        Debug.Log("DEATH");
        // Player has died

        spectatorObj.SetActive(state);
        spectatorObj.transform.position = _movement.transform.position;

        //foreach (GameObject obj in spectatorObjs)
        //    obj.SetActive(state);

        foreach (GameObject obj in onDeathObjs)
            obj.SetActive(!state);

        _isDead = state;
    }

    public void SetVignette(float intensity)
    {
        if (vignette != null)
        {
            vignette.intensity.value = Mathf.Clamp01(intensity);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsOwner)
            return;

        Vector3 velocityNow = _movement.rb.linearVelocity;

        if (Mathf.Abs((velocityNow - lastVelocity).magnitude) > 5f)
        {
            Damage(34f);
        }
    }
}
