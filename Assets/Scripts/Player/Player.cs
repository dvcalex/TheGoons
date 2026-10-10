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

    [Space]

    public Component[] componentsToDestroy_Other;
    public GameObject[] objects_Disable_Other;
    public Renderer[] renderers_ShadowsOnly_Other;

    [Space]

    public GameObject[] objects_Disable_Local;
    public Renderer[] renderers_ShadowsOnly_Local;  


    private void Start()
    {
        if (IsOwner)
            InitLocal();
        else
            InitOther();
    }

    public void InitLocal()
    {
        _input.enabled = true;

        if (me == null)
            me = this;

        foreach(GameObject g in objects_Disable_Local)
            g.SetActive(false);

        foreach(Renderer r in renderers_ShadowsOnly_Local)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        Server._instance.ConnectPlayer_RPC(NetworkObjectId);
    }

    public void InitOther()
    {
        foreach(Component c in componentsToDestroy_Other)
            Destroy(c);
        
        foreach(GameObject g in objects_Disable_Other)
            g.SetActive(false);

        foreach(Renderer r in renderers_ShadowsOnly_Other)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }

    public override void OnNetworkPreDespawn()
    {
        if(IsOwner)
            Server._instance.DisconnectPlayer_RPC(NetworkObjectId);
    }

    public void ShakeCamera(float magnitude, float roughness, float fadeInTime, float fadeOutTime)
    {
        if (IsOwner)
            CameraShaker.Instance.ShakeOnce(magnitude, roughness, fadeInTime, fadeOutTime);
    }

    private void Update()
    {
        if(IsOwner)
        {
            if(Keyboard.current.qKey.wasPressedThisFrame)
            {
                _ragdollManager.SetRagdoll(!_ragdollManager.isRagdoll);
            }
        }
    }
}
