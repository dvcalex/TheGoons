using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager _instance;

    [Header("UI Reference")]
    public TextMeshProUGUI timerText;

    [Header("Timer Settings")]
    public float timeRemaining = 60.0f;
    public bool timerIsRunning = false;

    [Header("Game State")]
    public int HidersCount = 0;

    public Player[] players;

    private void Awake()
    {
        // Enforce Singleton pattern safely in Awake
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start()
    {

        // Starts the timer automatically on scene load
        timerIsRunning = true;
    }

    private void Update()
    {
        if (timerIsRunning)
        {
            if (timeRemaining > 0)
            {
                timeRemaining -= Time.deltaTime;
                DisplayTime(timeRemaining);
            }
            else
            {
                Debug.Log("Time has run out!");
                timeRemaining = 0;
                timerIsRunning = false;
                CheckWinCondition();
            }
        }
    }

    void DisplayTime(float timeToDisplay)
    {
        timeToDisplay += 1;
        float minutes = Mathf.FloorToInt(timeToDisplay / 60);
        float seconds = Mathf.FloorToInt(timeToDisplay % 60);
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    void CheckWinCondition()
    {
        if (HidersCount > 0)
        {
            HidersWin();
        }
        else
        {
            GodWins();
        }
    }

    // Made public so hider scripts can call this when they die
    public void OnHiderDeath()
    {
        HidersCount--;
        Debug.Log("A hider was caught! Remaining hiders: " + HidersCount);

        if (HidersCount <= 0)
        {
            timerIsRunning = false; // Stop timer since game is over
            GodWins();
        }
    }

    void GodWins()
    {
        Debug.Log("God wins!");
        // Implement god win UI or logic here
    }

    void HidersWin()
    {
        Debug.Log("Hiders win!");
        // Implement hiders win UI or logic here
    }
}