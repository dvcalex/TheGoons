using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AbilityButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class AbilityData
    {
        public string abilityName = "Ability";
        public float totalCooldownTime = 5f;

        [Header("UI References")]
        public RawImage iconImage;             // Raw Image for ability icon
        public Image cooldownOverlay;        // UI Image with Radial 360 Fill
        public TextMeshProUGUI cooldownText;  // TMP timer text
        public Button abilityButton;          // UI Button component

        [HideInInspector] public float currentCooldown = 0f;
        [HideInInspector] public bool isCoolingDown = false;
    }

    [Header("Abilities")]
    [SerializeField] private AbilityData invisAbility;
    [SerializeField] private AbilityData dashAbility;

    [Header("Visual Tuning")]
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private void Start()
    {
        SetupAbility(invisAbility);
        SetupAbility(dashAbility);
    }

    private void SetupAbility(AbilityData ability)
    {
        if (ability.cooldownOverlay != null) ability.cooldownOverlay.fillAmount = 0f;
        if (ability.cooldownText != null) ability.cooldownText.gameObject.SetActive(false);
        if (ability.iconImage != null) ability.iconImage.color = readyColor;

        if (ability.abilityButton != null)
        {
            // Automatically hook up the button click event
            AbilityData targetAbility = ability;
            ability.abilityButton.onClick.AddListener(() => OnAbilityClicked(targetAbility));
        }
    }

    private void Update()
    {
        UpdateCooldown(invisAbility);
        UpdateCooldown(dashAbility);
    }

    private void OnAbilityClicked(AbilityData ability)
    {
        if (ability.isCoolingDown) return;

        // Start Cooldown
        ability.currentCooldown = ability.totalCooldownTime;
        ability.isCoolingDown = true;

        if (ability.abilityButton != null) ability.abilityButton.interactable = false;
        if (ability.iconImage != null) ability.iconImage.color = cooldownColor;
        if (ability.cooldownText != null) ability.cooldownText.gameObject.SetActive(true);
    }

    private void UpdateCooldown(AbilityData ability)
    {
        if (!ability.isCoolingDown) return;

        ability.currentCooldown -= Time.deltaTime;

        // Update radial fill overlay
        if (ability.cooldownOverlay != null)
        {
            ability.cooldownOverlay.fillAmount = ability.currentCooldown / ability.totalCooldownTime;
        }

        // Update TMP timer text
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

        // Check if cooldown finished
        if (ability.currentCooldown <= 0f)
        {
            ability.isCoolingDown = false;

            if (ability.abilityButton != null)
            {
                ability.abilityButton.interactable = true;
                StartCoroutine(PulseOnReady(ability.abilityButton.transform));
            }

            if (ability.iconImage != null)
            {
                ability.iconImage.color = readyColor;
            }
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