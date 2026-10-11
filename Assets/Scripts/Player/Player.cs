using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using EZCameraShake;

public class Player : NetworkBehaviour
{
    public static Player me;
    public PlayerInput _input;
    public PlayerMovement _movement;
    public PlayerRagdollManager _ragdollManager;
    public PlayerLogic _logic;

    [Space]
    public Component[] componentsToDestroy_Other;
    public GameObject[] objects_Disable_Other;
    public Renderer[] renderers_ShadowsOnly_Other;

    [Space]
    public GameObject[] objects_Disable_Local;
    public Renderer[] renderers_ShadowsOnly_Local;

    [Header("Invisibility Ability")]
    public float invisDuration = 5f; // Set this to 5 seconds
    public float invisCooldownTime = 12f; // Cooldown starts AFTER invisibility ends
    private float invisCooldownTimer = 0f;
    private float invisActiveTimer = 0f;
    private bool isInvisible = false;
    private bool isCoolingDown = false;

    [Header("Invisibility Renderers")]
    public SkinnedMeshRenderer[] stealthRenderers; // Drag your 4 skinned mesh renderers here in the Inspector

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
    }

    private void Update()
    {
        if (IsOwner && _input != null && _input.enabled)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                _ragdollManager.SetRagdoll(!_ragdollManager.isRagdoll);
            }

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
}
