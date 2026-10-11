using UnityEngine;

public class Splash : MonoBehaviour
{
    public GameObject waterFX;
    private ParticleSystem waterFXParticleSystem;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        waterFXParticleSystem = waterFX.GetComponent<ParticleSystem>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            waterFXParticleSystem.Play();
        }
    }
}
