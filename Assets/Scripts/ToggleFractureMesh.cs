using UnityEngine;

public class ToggleFractureMesh : MonoBehaviour
{
    public GameObject fracturedObject;
    private MeshRenderer[] fo_renderers;
    private MeshRenderer self_renderer;

    private void Start()
    {
        // Includes the fractured object's own renderer (if any) plus all children
        fo_renderers = fracturedObject.GetComponentsInChildren<MeshRenderer>(true);
        self_renderer = gameObject.GetComponent<MeshRenderer>();
        SetFracturedRenderers(false);
    }

    private void SetFracturedRenderers(bool enabled)
    {
        foreach (MeshRenderer r in fo_renderers)
        {
            r.enabled = enabled;
        }

        fracturedObject.GetComponent<MeshRenderer>().enabled = !enabled;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer != LayerMask.NameToLayer("Destructive"))
        {
            return;
        }

        if (collision.gameObject.layer == LayerMask.NameToLayer("Broken"))
        {
            return;
        }

        Debug.Log("HIT!!");

        if (collision.impulse.magnitude < 1f)
        {
            return;
        }

        SetFracturedRenderers(true);
        self_renderer.enabled = false;
    }
}
