using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerLogic : MonoBehaviour
{
    public static PlayerLogic Instance; // Singleton reference for easy access

    [System.Serializable]
    public class AbilityData
    {
        public string abilityName = "Ability";
        public float totalCooldownTime = 5f; // Set this to your actual cooldown length in the Inspector

        [Header("UI References")]
        public RawImage iconImage;
        public Image cooldownOverlay;
        public TextMeshProUGUI cooldownText;

        [HideInInspector] public float currentCooldown = 0f;
        [HideInInspector] public bool isCoolingDown = false;
    }

    [Header("Abilities")]
    [SerializeField] private AbilityData dashAbility;

    [Header("Visual Tuning")]
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (dashAbility.cooldownOverlay != null) dashAbility.cooldownOverlay.fillAmount = 0f;
        if (dashAbility.cooldownText != null) dashAbility.cooldownText.gameObject.SetActive(false);
        if (dashAbility.iconImage != null) dashAbility.iconImage.color = readyColor;
    }

    private void Update()
    {
        UpdateCooldown(dashAbility);
    }

    // Call this from your Player Controller when R is pressed and Dash succeeds
    public void TriggerDashCooldown()
    {
        if (dashAbility.isCoolingDown) return;

        dashAbility.currentCooldown = dashAbility.totalCooldownTime;
        dashAbility.isCoolingDown = true;

        if (dashAbility.iconImage != null) dashAbility.iconImage.color = cooldownColor;
        if (dashAbility.cooldownText != null) dashAbility.cooldownText.gameObject.SetActive(true);
    }

    private void UpdateCooldown(AbilityData ability)
    {
        if (!ability.isCoolingDown) return;

        ability.currentCooldown -= Time.deltaTime;

        if (ability.cooldownOverlay != null)
        {
            ability.cooldownOverlay.fillAmount = ability.currentCooldown / ability.totalCooldownTime;
        }

        if (ability.cooldownText != null)
        {
            if (ability.currentCooldown > 0f)
            {
                ability.cooldownText.text = ability.currentCooldown.ToString("F1");
            }
            else
            {
                ability.cooldownText.text = "";
                ability.cooldownText.gameObject.SetActive(false);
            }
        }

        if (ability.currentCooldown <= 0f)
        {
            ability.isCoolingDown = false;
            if (ability.iconImage != null) ability.iconImage.color = readyColor;

            // Optional pulse effect on the icon transform when ready
            StartCoroutine(PulseOnReady(ability.iconImage.transform));
        }
    }

    private IEnumerator PulseOnReady(Transform targetTransform)
    {
        Vector3 originalScale = targetTransform.localScale;
        Vector3 targetScale = originalScale * 1.15f;

        float elapsed = 0f;
        float duration = 0.08f;

        while (elapsed < duration)
        {
            targetTransform.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < duration)
        {
            targetTransform.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        targetTransform.localScale = originalScale;
    }
}