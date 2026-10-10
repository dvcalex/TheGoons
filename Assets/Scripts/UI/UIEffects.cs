using UnityEngine;
using UnityEngine.UI;

public class UIEffects : MonoBehaviour
{
    public static UIEffects _instance;

    public AudioSource source;
    public AudioClip clickSound;
    public AudioClip hoverSound;
    public AudioClip sliderSound;

    private void Awake()
    {
        if(_instance == null)
            _instance = this;

        source.mute = true;
        Invoke(nameof(Enable), 0.125f);
        //Hello world
    }

    void Enable()
    {
        source.mute = false;
    }

    public void PlayClick()
    {
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(clickSound);
    }

    public void PlayHover()
    {
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(hoverSound);
    }

    public void PlaySlider(float value)
    {
        source.pitch = 0.2f * value + 0.9f;
        source.PlayOneShot(sliderSound);
    }
}
