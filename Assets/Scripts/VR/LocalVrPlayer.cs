using Unity.Netcode;
using Unity.XR.CoreUtils;

/// <summary>Only the owning machine runs this network player's headset camera and tracking.</summary>
public sealed class LocalVrPlayer : NetworkBehaviour
{
    public XROrigin origin;

    void Awake()
    {
        if (origin != null) origin.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (origin != null) origin.gameObject.SetActive(IsOwner);
    }

    public override void OnNetworkDespawn()
    {
        if (origin != null) origin.gameObject.SetActive(false);
    }
}
