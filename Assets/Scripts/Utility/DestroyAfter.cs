using UnityEngine;

public class DestroyAfter : MonoBehaviour
{
    public float destroyAfter = 0f;

    void Start()
    {
        Destroy(gameObject, destroyAfter);
    }
}
