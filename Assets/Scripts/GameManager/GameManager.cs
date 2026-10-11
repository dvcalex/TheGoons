using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase
{
    Lobby,
    Playing,
    Results
}

public enum RoundResult
{
    None,
    HidersWin,
    SeekerWins,
    Cancelled
}

public class GameManager : NetworkBehaviour
{
    public static GameManager _instance
    {
        get;
        private set;
    }

    public static event Action<GameManager> InstanceChanged;

    public GamePhase CurrentPhase
    {
        get;
        private set;
    } = GamePhase.Lobby;
    public RoundResult Result
    {
        get;
        private set;
    }

    public float timeRemaining
    {
        get;
        private set;
    }

    public bool timerIsRunning
    {
        get
        {
            return CurrentPhase == GamePhase.Playing;
        }
    }

    public int HidersCount
    {
        get
        {
            return remainingHiders.Count;
        }
    }

    public event Action<GamePhase> PhaseChanged;
    public event Action PreStart;
    public event Action PostStart;
    public event Action<RoundResult> PreEnd;
    public event Action<RoundResult> PostEnd;
    public event Action<Player> HiderCaught;
    public event Action<int> HidersCountChanged;

    [Header("Round")]
    [SerializeField, Min(0.1f)]
    private float roundDuration = 60f;
    public Player[] players = Array.Empty<Player>();

    [Header("Optional UI")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI hidersCountText;
    public TextMeshProUGUI lobbyCodeText;

    [Header("Debug")]
    [SerializeField]
    private bool isDebugging;
    [SerializeField]
    private GamePhase debugPhase = GamePhase.Lobby;
    [SerializeField]
    private RoundResult debugResult = RoundResult.None;
    private readonly List<Player> remainingHiders = new();
    private bool changingPhase;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        timeRemaining = roundDuration;

        if (isDebugging)
        {
            debugPhase = CurrentPhase;
            debugResult = Result;
        }

        UpdateUI();
        InstanceChanged?.Invoke(this);
    }

    private void OnDestroy()
    {
        if (_instance != this)
        {
            return;
        }

        _instance = null;
        InstanceChanged?.Invoke(null);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || !isDebugging)
        {
            return;
        }

        // Handle Phase changes from Inspector
        if (debugPhase != CurrentPhase)
        {
            if (debugPhase == GamePhase.Lobby)
            {
                ReturnToLobbyInternal();
            }
            else if (debugPhase == GamePhase.Playing)
            {
                StartGameInternal();
            }
            else if (debugPhase == GamePhase.Results)
            {
                // Fallback to a default result if none selected when switching to Results via Inspector
                RoundResult targetResult = debugResult == RoundResult.None ? RoundResult.HidersWin : debugResult;
                EndGameInternal(targetResult);
            }
        }

        // Handle Result changes while already in Results phase
        else if (CurrentPhase == GamePhase.Results && debugResult != Result && debugResult != RoundResult.None)
        {
            EndGameInternal(debugResult);
        }
    }

    private void Update()
    {
        if (!IsServer)
            return;

        if (Player.me == null)
            return;

        if (Player.me._input.actions["StartGame"].WasPressedThisFrame())
        {
            StartGame_RPC();
        }

        if (!timerIsRunning || !CanControlRound())
        {
            return;
        }

        if (remainingHiders.RemoveAll(IsMissingPlayer) > 0)
        {
            HidersCountChanged?.Invoke(HidersCount);
        }

        timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);
        UpdateUI();

        if (HidersCount == 0 || timeRemaining == 0f)
        {
            OnEndGame_RPC();
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    public void StartGame_RPC()
    {
        if (!isDebugging && (CurrentPhase != GamePhase.Lobby || !CanControlRound()))
        {
            return;
        }

        StartGameInternal();
    }

    private void StartGameInternal()
    {
        IEnumerable<Player> hiders = players;

        if (Server._instance != null && Server._instance.IsSpawned)
        {
            hiders = Server._instance.GetClientPlayers();
        }

        remainingHiders.Clear();

        if (hiders != null)
        {
            foreach (Player hider in hiders)
            {
                if (hider != null && !remainingHiders.Contains(hider))
                {
                    remainingHiders.Add(hider);
                }
            }
        }

        if (HidersCount == 0 && !isDebugging)
        {
            return;
        }

        timeRemaining = roundDuration;
        ChangePhase(GamePhase.Playing);
        debugPhase = CurrentPhase;
        debugResult = Result;
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    public void OnHiderDeath_RPC(ulong playerObjId)
    {
        Player hider = null;
        hider = NetworkManager.SpawnManager.SpawnedObjects[playerObjId].GetComponent<Player>();

        if (!timerIsRunning || !CanControlRound() || hider == null || !remainingHiders.Remove(hider))
        {
            return;
        }

        HidersCountChanged?.Invoke(HidersCount);
        HiderCaught?.Invoke(hider);
        UpdateUI();

        if (HidersCount == 0 && IsServer)
        {
            OnEndGame_RPC();
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    public void OnEndGame_RPC()
    {
        if (HidersCount > 0)
        {
            EndGame(RoundResult.HidersWin);
        }
        else
        {
            EndGame(RoundResult.SeekerWins);
        }
    }

    public void EndGame(RoundResult result)
    {
        if (!isDebugging && (!timerIsRunning || !CanControlRound() || result == RoundResult.None))
        {
            return;
        }

        EndGameInternal(result);
    }

    private void EndGameInternal(RoundResult result)
    {
        ChangePhase(GamePhase.Results, result);
        debugPhase = CurrentPhase;
        debugResult = Result;
    }

    public void ReturnToLobby()
    {
        if (!isDebugging && (CurrentPhase == GamePhase.Lobby || !CanControlRound()))
        {
            return;
        }

        ReturnToLobbyInternal();
    }

    private void ReturnToLobbyInternal()
    {
        if (timerIsRunning)
        {
            EndGame(RoundResult.Cancelled);
        }

        remainingHiders.Clear();
        timeRemaining = roundDuration;
        ChangePhase(GamePhase.Lobby);
        debugPhase = CurrentPhase;
        debugResult = Result;
    }

    private void ChangePhase(GamePhase phase, RoundResult result = RoundResult.None)
    {
        changingPhase = true;

        try
        {
            if (phase == GamePhase.Playing)
            {
                PreStart?.Invoke();
            }
            else if (phase == GamePhase.Results)
            {
                PreEnd?.Invoke(result);
            }

            CurrentPhase = phase;
            Result = result;
            UpdateUI();
            PhaseChanged?.Invoke(phase);

            if (phase == GamePhase.Playing)
            {
                HidersCountChanged?.Invoke(HidersCount);
                PostStart?.Invoke();
            }
            else if (phase == GamePhase.Results)
            {
                PostEnd?.Invoke(result);
            }
            else
            {
                HidersCountChanged?.Invoke(0);
            }
        }
        finally
        {
            changingPhase = false;
        }
    }

    private bool CanControlRound()
    {
        if (isDebugging)
        {
            return true;
        }

        var network = Unity.Netcode.NetworkManager.Singleton;

        if (changingPhase)
        {
            return false;
        }

        if (network == null || !network.IsListening)
        {
            return true;
        }

        if (network.IsServer)
        {
            return true;
        }

        return false;
    }

    private static bool IsMissingPlayer(Player player)
    {
        return player == null;
    }

    private void UpdateUI()
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(timeRemaining);
            timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        if (hidersCountText != null)
        {
            hidersCountText.text = $"Hiders Remaining: {HidersCount}";
        }
    }
}
