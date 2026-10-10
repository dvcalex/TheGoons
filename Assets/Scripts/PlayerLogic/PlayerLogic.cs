using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerLogic : MonoBehaviour
{
    [Header("Ability Settings")]
    [SerializeField] private float totalCooldownTime = 5f;
    private float currentCooldown = 0f;
    private bool isCoolingDown = false;

    [Header("UI References")]
    [SerializeField] private RawImage iconImage;              // The main ability icon graphic
    [SerializeField] private Image cooldownOverlay;       // The radial filled gray image
    [SerializeField] private TextMeshProUGUI cooldownText; // TMP text for numbers
    [SerializeField] private Button abilityButton;         // The main button component

    [Header("Player Reference")]
    [SerializeField] private GameObject playerObject;      // Reference to your player to trigger invisibility

    [Header("Visual Tuning")]
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 1f); // Darkened icon tint

    private void Start()
    {
        // Initialize UI state
        if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0f;
        if (cooldownText != null) cooldownText.gameObject.SetActive(false);
        if (iconImage != null) iconImage.color = readyColor;

        if (abilityButton != null)
        {
            abilityButton.onClick.AddListener(UseAbility);
        }
        UseAbility();
    }

    private void Update()
    {
        if (isCoolingDown)
        {
            currentCooldown -= Time.deltaTime;

            // Update radial fill (going from 1 down to 0)
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = currentCooldown / totalCooldownTime;
            }

            // Update numerical text
            if (cooldownText != null)
            {
                if (currentCooldown > 0f)
                {
                    cooldownText.text = currentCooldown.ToString("F1"); // Displays 1 decimal place
                }
                else
                {
                    cooldownText.text = "";
                    cooldownText.gameObject.SetActive(false);
                }
            }

            // Check if cooldown has finished
            if (currentCooldown <= 0f)
            {
                isCoolingDown = false;
                if (abilityButton != null) abilityButton.interactable = true;
                if (iconImage != null) iconImage.color = readyColor;

                // Trigger juice feedback when ready
                StartCoroutine(PulseOnReady());
            }
        }
    }

    public void UseAbility()
    {
        if (isCoolingDown) return;

        // Trigger ability logic (e.g., Go Invisible)
        ActivateInvisibility();

        // Start Cooldown
        currentCooldown = totalCooldownTime;
        isCoolingDown = true;
        if (abilityButton != null) abilityButton.interactable = false;

        // Darken icon tint during cooldown
        if (iconImage != null) iconImage.color = cooldownColor;

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(true);
        }
    }

    private IEnumerator PulseOnReady()
    {
        Vector3 originalScale = transform.localScale;
        Vector3 targetScale = originalScale * 1.15f; // Scale up 15%

        float elapsed = 0f;
        float duration = 0.08f;

        // Scale up quickly
        while (elapsed < duration)
        {
            transform.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Scale back down
        elapsed = 0f;
        while (elapsed < duration)
        {
            transform.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localScale = originalScale;
    }

    private void ActivateInvisibility()
    {
        if (playerObject != null)
        {
            Renderer playerRenderer = playerObject.GetComponent<Renderer>();
            if (playerRenderer != null)
            {
                playerRenderer.enabled = false;
            }

            Invoke(nameof(DeactivateInvisibility), 3f);
        }
    }

    private void DeactivateInvisibility()
    {
        if (playerObject != null)
        {
            Renderer playerRenderer = playerObject.GetComponent<Renderer>();
            if (playerRenderer != null)
            {
                playerRenderer.enabled = true;
            }
        }
    }
}