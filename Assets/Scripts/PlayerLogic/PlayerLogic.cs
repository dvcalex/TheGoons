using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerLogic : MonoBehaviour
{
    [System.Serializable]
    public class AbilityData
    {
        public string abilityName = "Ability";
        public float totalCooldownTime = 3f;

        [Header("UI References")]
        public RawImage iconImage;
        public Image cooldownOverlay;
        public TextMeshProUGUI cooldownText;

        [HideInInspector] public float currentCooldown = 0f;
        [HideInInspector] public bool isCoolingDown = false;
        [HideInInspector] public bool isActive = false;
    }

    [Header("Abilities")]
    [SerializeField] private AbilityData dashAbility;
    [SerializeField] private AbilityData invisAbility;

    [Header("Visual Tuning")]
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color activeColor = new Color(0.2f, 0.2f, 0.2f, 1f); // Dark gray while invisible
    [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private void Start()
    {
        InitializeAbility(dashAbility);
        InitializeAbility(invisAbility);
    }

    private void InitializeAbility(AbilityData ability)
    {
        if (ability.cooldownOverlay != null) ability.cooldownOverlay.fillAmount = 0f;
        if (ability.cooldownText != null) ability.cooldownText.gameObject.SetActive(false);
        if (ability.iconImage != null) ability.iconImage.color = readyColor;
    }

    private void Update()
    {
        UpdateCooldown(dashAbility);
        UpdateCooldown(invisAbility);
    }

    public void TriggerDashCooldown()
    {
        if (dashAbility.isCoolingDown) return;

        dashAbility.currentCooldown = dashAbility.totalCooldownTime;
        dashAbility.isCoolingDown = true;

        if (dashAbility.iconImage != null) dashAbility.iconImage.color = cooldownColor;
        if (dashAbility.cooldownText != null) dashAbility.cooldownText.gameObject.SetActive(true);
    }

    // PHASE 1: Call this when player goes invisible -> Grays out icon
    public void SetInvisActiveUI()
    {
        invisAbility.isActive = true;
        if (invisAbility.iconImage != null) invisAbility.iconImage.color = activeColor;
        if (invisAbility.cooldownOverlay != null) invisAbility.cooldownOverlay.fillAmount = 0f;
        if (invisAbility.cooldownText != null) invisAbility.cooldownText.gameObject.SetActive(false);
    }

    // PHASE 2: Call this when player turns visible -> Starts radial wipe & countdown
    public void TriggerInvisCooldown()
    {
        invisAbility.isActive = false;
        if (invisAbility.isCoolingDown) return;

        invisAbility.currentCooldown = invisAbility.totalCooldownTime;
        invisAbility.isCoolingDown = true;

        if (invisAbility.iconImage != null) invisAbility.iconImage.color = cooldownColor;
        if (invisAbility.cooldownText != null) invisAbility.cooldownText.gameObject.SetActive(true);
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

            if (ability.iconImage != null)
            {
                StartCoroutine(PulseOnReady(ability.iconImage.transform));
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