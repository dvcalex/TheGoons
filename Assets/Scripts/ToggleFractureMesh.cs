using UnityEngine;

public class ToggleFractureMesh : MonoBehaviour
{
    public GameObject fracturedObject;
    private MeshRenderer fo_renderer;
    private MeshRenderer self_renderer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        fo_renderer = fracturedObject.GetComponent<MeshRenderer>();
        self_renderer = gameObject.GetComponent<MeshRenderer>();
        fo_renderer.enabled = false;
    }

    // Update is called once per frame
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer != LayerMask.NameToLayer("Destructive"))
            return;
        if (collision.gameObject.layer == LayerMask.NameToLayer("Broken"))
            return;
        Debug.Log("HIT!!");
        if (collision.impulse.magnitude < 1f)
            return;
        fo_renderer.enabled = true;
        self_renderer.enabled = false;
    }
}
