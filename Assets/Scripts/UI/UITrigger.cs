using UnityEngine;
using UnityEngine.UI;

public class UITrigger : MonoBehaviour
{
    public bool scaleOnEnable;
    public float scaleIntensity = 1.1f;
    public Transform scaleOffset;

    bool scaling;
    float magnitude = 1f;

    float sliderCooldown = 0.1f;
    bool sliderOnCooldown;

    private void OnEnable()
    {
        if (scaleOnEnable)
            scaling = true;
    }

    private void Update()
    {
        if (!scaleOnEnable)
            return;

        Transform target = scaleOffset;

        if (target == null)
            target = transform;

        if(scaling && magnitude < scaleIntensity)
        {
            magnitude += Time.deltaTime * 2f;
            target.localScale = Vector3.one * magnitude;

            if (magnitude >= scaleIntensity)
                scaling = false;
        }
        else if(!scaling && target.localScale.magnitude > 1f)
        {
            magnitude -= Time.deltaTime * 2f;
            target.localScale = Vector3.one * magnitude;

            if (magnitude <= 1f)
            {
                magnitude = 1f;
                target.localScale = Vector3.one;
            }
        }
    }

    public void TriggerScale()
    {
        scaling = true;
    }

    public void PlayClick() => UIEffects._instance.PlayClick();
    public void PlayHover() => UIEffects._instance.PlayHover();
    public void PlaySlider() 
    {
        if (sliderOnCooldown)
            return;

        sliderOnCooldown = true;
        Invoke(nameof(EndCooldown), sliderCooldown);

        if (GetComponent<Slider>() != null)
            UIEffects._instance.PlaySlider(GetComponent<Slider>().value / GetComponent<Slider>().maxValue);
        else
            UIEffects._instance.PlaySlider(Random.Range(0f, 1f));
    }

    void EndCooldown()
    {
        sliderOnCooldown = false;
    }
}
