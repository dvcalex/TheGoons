using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Local presentation. All round requests are validated by GameManager on the server.
public sealed class RoundControls : MonoBehaviour
{
    private GameManager game;
    private GameObject panel;
    private TextMeshProUGUI status;
    private TextMeshProUGUI caption;
    private Button button;

    private void Start()
    {
        game = GetComponent<GameManager>();
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            return;
        }

        panel = new GameObject("Round Controls", typeof(RectTransform));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 30f);
        rect.sizeDelta = new Vector2(600f, 140f);

        status = CreateLabel("Status", rect, new Vector2(0f, 50f));
        GameObject action = new GameObject("Round Button", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform actionRect = action.GetComponent<RectTransform>();
        actionRect.SetParent(rect, false);
        actionRect.sizeDelta = new Vector2(360f, 60f);
        action.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.95f);
        button = action.GetComponent<Button>();
        button.targetGraphic = action.GetComponent<Image>();
        button.onClick.AddListener(RequestAction);
        caption = CreateLabel("Caption", actionRect, Vector2.zero);
    }

    private TextMeshProUGUI CreateLabel(string label, RectTransform parent, Vector2 position)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(parent, false);
        text.rectTransform.sizeDelta = new Vector2(580f, 60f);
        text.rectTransform.anchoredPosition = position;
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        if (game.timerText != null)
        {
            text.font = game.timerText.font;
        }
        return text;
    }

    private void Update()
    {
        if (panel == null)
        {
            return;
        }

        panel.SetActive(game.IsSpawned);
        if (!game.IsSpawned)
        {
            return;
        }

        button.gameObject.SetActive(game.CurrentPhase != GamePhase.Playing);
        button.interactable = false;
        status.text = "Waiting for players";
        caption.text = "Start game";

        if (game.IsResetting)
        {
            status.text = "Preparing the next round...";
        }
        else if (game.CurrentPhase == GamePhase.Lobby)
        {
            status.text = "Lobby - " + game.HidersCount + " hiders";
            button.interactable = game.CanRequestStart;
        }
        else if (game.IsHiding)
        {
            status.text = "Hide! The seeker is covering their eyes";
        }
        else if (game.IsSeeking)
        {
            status.text = "The seeker is coming!";
        }
        else if (game.CurrentPhase == GamePhase.Results)
        {
            status.text = "Round ended";
            if (game.Result == RoundResult.HidersWin)
            {
                status.text = "Hiders win!";
            }
            else if (game.Result == RoundResult.SeekerWins)
            {
                status.text = "Seeker wins!";
            }

            caption.text = "Waiting for host";
            if (game.IsServer)
            {
                caption.text = "Next round";
                button.interactable = true;
            }
        }

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame && game.CanRequestStart)
        {
            game.StartGame_RPC();
        }
    }

    private void LateUpdate()
    {
        if (game == null || !game.IsSpawned || Player.me == null)
        {
            return;
        }

        if (game.CurrentPhase == GamePhase.Playing)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void RequestAction()
    {
        if (game.CurrentPhase == GamePhase.Lobby)
        {
            game.StartGame_RPC();
        }
        else if (game.CurrentPhase == GamePhase.Results && game.IsServer)
        {
            game.ReturnToLobby();
        }
    }
}
