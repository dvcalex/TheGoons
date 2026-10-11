using Unity.Netcode;
using UnityEngine;

public class ToggleFractureMesh : NetworkBehaviour
{
    public GameObject fracturedObject;

    [Min(0f)]
    [Tooltip("Minimum collision impulse from the Destructive layer needed to break this object. Lower values make it easier to break.")]
    public float minimumImpactImpulse = 0.1f;

    private MeshRenderer[] fo_renderers;
    private MeshRenderer self_renderer;

    void Start()
    {
        // Includes the fractured object's own renderer (if any) plus all children
        fo_renderers = fracturedObject.GetComponentsInChildren<MeshRenderer>(true);
        self_renderer = gameObject.GetComponent<MeshRenderer>();

        SetFracturedRenderers_RPC(false);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void SetFracturedRenderers_RPC(bool enabled)
    {
        if (!IsServer)
            self_renderer.enabled = false;

        foreach (MeshRenderer r in fo_renderers)
        {
            r.enabled = enabled;
        }
        fracturedObject.GetComponent<MeshRenderer>().enabled = !enabled;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer)
            return;

        if (collision.gameObject.layer != LayerMask.NameToLayer("Destructive"))
            return;
        if (collision.gameObject.layer == LayerMask.NameToLayer("Broken"))
            return;
        Debug.Log("HIT!!");
        if (collision.impulse.magnitude < minimumImpactImpulse)
            return;

        SetFracturedRenderers_RPC(true);
        self_renderer.enabled = false;
    }
}
