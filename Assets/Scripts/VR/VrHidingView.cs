using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The physical eye-cover pose is cosmetic; this prevents room-scale peeking.
public sealed class VrHidingView : MonoBehaviour
{
    private Canvas cover;
    private TextMeshProUGUI label;

    private void Start()
    {
        Camera view = GetComponent<Camera>();
        GameObject obj = new GameObject("Hiding View", typeof(Canvas));
        obj.transform.SetParent(transform, false);
        cover = obj.GetComponent<Canvas>();
        cover.renderMode = RenderMode.ScreenSpaceCamera;
        cover.worldCamera = view;
        cover.planeDistance = view.nearClipPlane + 0.01f;
        cover.sortingOrder = 32767;

        GameObject background = new GameObject("Cover", typeof(RectTransform), typeof(Image));
        RectTransform rect = background.GetComponent<RectTransform>();
        rect.SetParent(obj.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = Color.black;
        background.GetComponent<Image>().raycastTarget = false;

        GameObject text = new GameObject("Countdown", typeof(RectTransform), typeof(TextMeshProUGUI));
        label = text.GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(obj.transform, false);
        label.rectTransform.sizeDelta = new Vector2(600f, 150f);
        label.fontSize = 36f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }

    private void LateUpdate()
    {
        GameManager game = GameManager._instance;
        cover.enabled = game != null && game.IsHiding;
        if (cover.enabled)
        {
            label.text = "Hiders are hiding\nSeeking in " + Mathf.CeilToInt(game.timeRemaining);
        }
    }
}
