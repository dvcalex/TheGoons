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
        get; private set;
    }
    public static event Action<GameManager> InstanceChanged;

    // ==========================================
    // NETWORKED ROUND STATE
    // ==========================================

    private struct RoundState : INetworkSerializable, IEquatable<RoundState>
    {
        public GamePhase Phase;
        public RoundResult Result;
        public int Hiders;
        public double EndTime;
        public float TimeLeft;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            int phase = (int)Phase;
            int result = (int)Result;

            serializer.SerializeValue(ref phase);
            serializer.SerializeValue(ref result);
            serializer.SerializeValue(ref Hiders);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref TimeLeft);

            if (serializer.IsReader)
            {
                Phase = (GamePhase)phase;
                Result = (RoundResult)result;
            }
        }

        public bool Equals(RoundState other)
        {
            return Phase == other.Phase &&
                   Result == other.Result &&
                   Hiders == other.Hiders &&
                   EndTime == other.EndTime &&
                   TimeLeft == other.TimeLeft;
        }

        public override bool Equals(object obj)
        {
            return obj is RoundState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)Phase;
                hash = hash * 31 + (int)Result;
                hash = hash * 31 + Hiders;
                hash = hash * 31 + EndTime.GetHashCode();
                hash = hash * 31 + TimeLeft.GetHashCode();
                return hash;
            }
        }
    }

    private readonly NetworkVariable<RoundState> roundState =
        new NetworkVariable<RoundState>(
            new RoundState
            {
                Phase = GamePhase.Lobby,
                Result = RoundResult.None,
                Hiders = 0,
                EndTime = 0,
                TimeLeft = 60f
            }
        );

    // ==========================================
    // PUBLIC PROPERTIES
    // ==========================================

    public GamePhase CurrentPhase => roundState.Value.Phase;
    public RoundResult Result => roundState.Value.Result;
    public int HidersCount => roundState.Value.Hiders;

    public bool timerIsRunning =>
        CurrentPhase == GamePhase.Playing;

    public float timeRemaining
    {
        get
        {
            if (!IsSpawned || NetworkManager == null)
                return roundDuration;

            RoundState state = roundState.Value;

            if (state.Phase != GamePhase.Playing)
                return state.TimeLeft;

            // Uses synchronized network time instead
            // of updating a timer NetworkVariable every frame.
            return Mathf.Max(
                0f,
                (float)(state.EndTime - NetworkManager.ServerTime.Time)
            );
        }
    }

    // ==========================================
    // EVENTS
    // ==========================================

    public event Action<GamePhase> PhaseChanged;
    public event Action PreStart;
    public event Action PostStart;
    public event Action<RoundResult> PreEnd;
    public event Action<RoundResult> PostEnd;
    public event Action<Player> HiderCaught;
    public event Action<int> HidersCountChanged;

    // ==========================================
    // INSPECTOR
    // ==========================================

    [Header("Round")]
    [SerializeField, Min(0.1f)]
    private float roundDuration = 60f;

    public Player[] players = Array.Empty<Player>();

    [Tooltip("Assign the seeker so they are excluded from the hider count.")]
    public Player seeker;

    [Header("Optional UI")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI hidersCountText;
    public TextMeshProUGUI lobbyCodeText;

    [Header("Debug")]
    [SerializeField] private bool isDebugging;
    [SerializeField] private GamePhase debugPhase = GamePhase.Lobby;
    [SerializeField] private RoundResult debugResult = RoundResult.None;

    // ==========================================
    // PRIVATE FIELDS
    // ==========================================

    // Only the server maintains this collection.
    private readonly HashSet<ulong> remainingHiders = new();

    private bool changingPhase;

    // ==========================================
    // UNITY LIFECYCLE
    // ==========================================

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;

        UpdateUI();
        InstanceChanged?.Invoke(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        roundState.OnValueChanged += OnRoundStateChanged;

        if (IsServer)
        {
            remainingHiders.Clear();

            roundState.Value = new RoundState
            {
                Phase = GamePhase.Lobby,
                Result = RoundResult.None,
                Hiders = 0,
                EndTime = 0,
                TimeLeft = roundDuration
            };

            NetworkManager.OnClientDisconnectCallback +=
                OnClientDisconnected;
        }

        debugPhase = CurrentPhase;
        debugResult = Result;

        UpdateUI();

        // Initialize listeners for late-joining clients.
        PhaseChanged?.Invoke(CurrentPhase);
        HidersCountChanged?.Invoke(HidersCount);
    }

    public override void OnNetworkDespawn()
    {
        roundState.OnValueChanged -= OnRoundStateChanged;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -=
                OnClientDisconnected;
        }

        remainingHiders.Clear();

        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        if (_instance != this)
            return;

        _instance = null;
        InstanceChanged?.Invoke(null);
    }

    private void Update()
    {
        // Runs on both host and clients.
        UpdateUI();

        if (!IsSpawned || !IsServer)
            return;

        HandleDebug();

        // Host start-game input.
        if (Player.me != null &&
            Player.me._input != null &&
            Player.me._input.actions != null)
        {
            var startAction =
                Player.me._input.actions.FindAction("StartGame");

            if (startAction != null &&
                startAction.WasPressedThisFrame())
            {
                StartGame_RPC();
            }
        }

        if (!timerIsRunning || changingPhase)
            return;

        CleanupMissingHiders();

        if (!timerIsRunning)
            return;

        // Server determines the winner.
        if (remainingHiders.Count == 0)
        {
            EndGameInternal(RoundResult.SeekerWins);
        }
        else if (timeRemaining <= 0f)
        {
            EndGameInternal(RoundResult.HidersWin);
        }
    }

    // ==========================================
    // START GAME
    // ==========================================

    // Compatibility with your original method name.
    // Only the server can start the round.
    public void StartGame_RPC()
    {
        if (!CanControlRound())
            return;

        StartGameInternal();
    }

    private void StartGameInternal()
    {
        if (!CanControlRound())
            return;

        if (CurrentPhase == GamePhase.Playing)
            return;

        if (!isDebugging && CurrentPhase != GamePhase.Lobby)
            return;

        remainingHiders.Clear();

        IEnumerable<Player> hiders = players;

        if (Server._instance != null && Server._instance.IsSpawned)
        {
            List<Player> tmp = Server._instance.GetClientPlayers();

            Debug.Log($"{tmp.Count}");

            hiders = Server._instance.GetClientPlayers();
        }

        if (hiders != null)
        {
            foreach (Player player in hiders)
            {
                if (player == null || !player.IsSpawned)
                    continue;

                // Do not count the seeker as a hider.
                if (player == seeker)
                    continue;

                remainingHiders.Add(player.NetworkObjectId);
            }
        }

        if (remainingHiders.Count == 0 && !isDebugging)
        {
            Debug.LogWarning(
                "GameManager: Cannot start without hiders."
            );

            debugPhase = CurrentPhase;
            return;
        }

        double endTime =
            NetworkManager.ServerTime.Time + roundDuration;

        SetRoundState(
            GamePhase.Playing,
            RoundResult.None,
            remainingHiders.Count,
            endTime,
            roundDuration
        );

        Debug.Log(
            $"Game started with {remainingHiders.Count} hiders."
        );
    }

    // ==========================================
    // HIDER DEATH
    // ==========================================

    // Keep the original public method for other scripts.
    //
    // Host: processes directly.
    // Client: sends a request to the server.
    public void OnHiderDeath_RPC(ulong playerObjId)
    {
        if (!IsSpawned)
            return;

        if (IsServer)
        {
            RemoveHider(playerObjId, true);
        }
        else
        {
            ReportHiderDeathRpc(playerObjId);
        }
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone
    )]
    private void ReportHiderDeathRpc(
        ulong playerObjId,
        RpcParams rpcParams = default)
    {
        if (!IsServer || !timerIsRunning)
            return;

        if (!remainingHiders.Contains(playerObjId))
            return;

        // Validate the client reporting the death.
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
            playerObjId,
            out NetworkObject playerObject))
        {
            return;
        }

        ulong senderId = rpcParams.Receive.SenderClientId;

        if (playerObject.OwnerClientId != senderId)
        {
            Debug.LogWarning(
                $"Client {senderId} attempted to report " +
                $"another player's death."
            );

            return;
        }

        RemoveHider(playerObjId, true);
    }

    private void RemoveHider(ulong playerObjId, bool caught)
    {
        if (!IsServer || !timerIsRunning)
            return;

        // HashSet.Remove returns false if already removed.
        // Prevents duplicate deaths from decreasing count.
        if (!remainingHiders.Remove(playerObjId))
            return;

        UpdateHiderCount();

        if (caught)
        {
            NotifyHiderCaughtRpc(playerObjId);
        }

        if (remainingHiders.Count == 0)
        {
            EndGameInternal(RoundResult.SeekerWins);
        }
    }

    [Rpc(
        SendTo.Everyone,
        InvokePermission = RpcInvokePermission.Server
    )]
    private void NotifyHiderCaughtRpc(ulong playerObjId)
    {
        if (NetworkManager == null ||
            NetworkManager.SpawnManager == null)
            return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
            playerObjId,
            out NetworkObject playerObject))
        {
            return;
        }

        Player player = playerObject.GetComponent<Player>();

        if (player != null)
        {
            HiderCaught?.Invoke(player);
        }
    }

    // ==========================================
    // DISCONNECTS / CLEANUP
    // ==========================================

    private void CleanupMissingHiders()
    {
        RemoveDisconnectedHiders(null);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || !timerIsRunning)
            return;

        RemoveDisconnectedHiders(clientId);
    }

    private void RemoveDisconnectedHiders(ulong? clientId)
    {
        if (!IsServer || !timerIsRunning)
            return;

        List<ulong> missing = null;

        foreach (ulong id in remainingHiders)
        {
            bool shouldRemove = false;

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                id,
                out NetworkObject playerObject))
            {
                shouldRemove = true;
            }
            else if (clientId.HasValue &&
                     playerObject.OwnerClientId == clientId.Value)
            {
                shouldRemove = true;
            }

            if (shouldRemove)
            {
                missing ??= new List<ulong>();
                missing.Add(id);
            }
        }

        if (missing == null)
            return;

        foreach (ulong id in missing)
        {
            remainingHiders.Remove(id);
        }

        UpdateHiderCount();

        if (remainingHiders.Count == 0)
        {
            EndGameInternal(RoundResult.SeekerWins);
        }
    }

    private void UpdateHiderCount()
    {
        if (!IsServer || !IsSpawned)
            return;

        RoundState state = roundState.Value;
        state.Hiders = remainingHiders.Count;

        roundState.Value = state;
    }

    // ==========================================
    // END GAME
    // ==========================================

    // Preserves your original method name.
    // Intended for server-side calls.
    public void OnEndGame_RPC()
    {
        if (!CanControlRound() || !timerIsRunning)
            return;

        RoundResult result = remainingHiders.Count > 0
            ? RoundResult.HidersWin
            : RoundResult.SeekerWins;

        EndGameInternal(result);
    }

    public void EndGame(RoundResult result)
    {
        if (!CanControlRound() ||
            !timerIsRunning ||
            result == RoundResult.None)
        {
            return;
        }

        EndGameInternal(result);
    }

    private void EndGameInternal(RoundResult result)
    {
        if (!CanControlRound() || !timerIsRunning)
            return;

        float remaining = timeRemaining;

        SetRoundState(
            GamePhase.Results,
            result,
            remainingHiders.Count,
            0,
            remaining
        );

        Debug.Log($"Game ended: {result}");
    }

    // ==========================================
    // RETURN TO LOBBY
    // ==========================================

    public void ReturnToLobby()
    {
        if (!CanControlRound())
            return;

        ReturnToLobbyInternal();
    }

    private void ReturnToLobbyInternal()
    {
        if (!CanControlRound())
            return;

        if (CurrentPhase == GamePhase.Lobby)
            return;

        remainingHiders.Clear();

        SetRoundState(
            GamePhase.Lobby,
            RoundResult.None,
            0,
            0,
            roundDuration
        );
    }

    // ==========================================
    // NETWORK STATE MANAGEMENT
    // ==========================================

    private void SetRoundState(
        GamePhase phase,
        RoundResult result,
        int hiders,
        double endTime,
        float timeLeft)
    {
        if (!CanControlRound())
            return;

        changingPhase = true;

        try
        {
            roundState.Value = new RoundState
            {
                Phase = phase,
                Result = result,
                Hiders = hiders,
                EndTime = endTime,
                TimeLeft = timeLeft
            };

            debugPhase = phase;
            debugResult = result;
        }
        finally
        {
            changingPhase = false;
        }
    }

    private void OnRoundStateChanged(
        RoundState previous,
        RoundState current)
    {
        bool phaseChanged = previous.Phase != current.Phase;
        bool countChanged = previous.Hiders != current.Hiders;

        if (phaseChanged)
        {
            if (current.Phase == GamePhase.Playing)
            {
                PreStart?.Invoke();
            }
            else if (current.Phase == GamePhase.Results)
            {
                PreEnd?.Invoke(current.Result);
            }
            else if (current.Phase == GamePhase.Lobby &&
                     previous.Phase == GamePhase.Playing)
            {
                PreEnd?.Invoke(RoundResult.Cancelled);
            }
        }

        UpdateUI();

        if (phaseChanged)
        {
            PhaseChanged?.Invoke(current.Phase);
        }

        if (countChanged)
        {
            HidersCountChanged?.Invoke(current.Hiders);
        }

        if (phaseChanged)
        {
            if (current.Phase == GamePhase.Playing)
            {
                PostStart?.Invoke();
            }
            else if (current.Phase == GamePhase.Results)
            {
                PostEnd?.Invoke(current.Result);
            }
            else if (current.Phase == GamePhase.Lobby &&
                     previous.Phase == GamePhase.Playing)
            {
                PostEnd?.Invoke(RoundResult.Cancelled);
            }
        }

        debugPhase = current.Phase;
        debugResult = current.Result;
    }

    // ==========================================
    // DEBUG
    // ==========================================

    private void HandleDebug()
    {
        if (!isDebugging || changingPhase)
            return;

        if (debugPhase != CurrentPhase)
        {
            switch (debugPhase)
            {
                case GamePhase.Lobby:
                    ReturnToLobbyInternal();
                    break;

                case GamePhase.Playing:
                    StartGameInternal();
                    break;

                case GamePhase.Results:
                    if (timerIsRunning)
                    {
                        RoundResult result =
                            debugResult == RoundResult.None
                                ? RoundResult.HidersWin
                                : debugResult;

                        EndGameInternal(result);
                    }
                    else
                    {
                        debugPhase = CurrentPhase;
                    }
                    break;
            }
        }
        else if (CurrentPhase == GamePhase.Results &&
                 debugResult != Result &&
                 debugResult != RoundResult.None)
        {
            RoundState state = roundState.Value;
            state.Result = debugResult;

            roundState.Value = state;
        }
    }

    // ==========================================
    // UTILITIES
    // ==========================================

    private bool CanControlRound()
    {
        return IsServer &&
               IsSpawned &&
               !changingPhase;
    }

    private void UpdateUI()
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(
                Mathf.Max(0f, timeRemaining)
            );

            string formatted =
                $"{seconds / 60:00}:{seconds % 60:00}";

            if (timerText.text != formatted)
            {
                timerText.text = formatted;
            }
        }

        if (hidersCountText != null)
        {
            string formatted =
                $"Hiders Remaining: {HidersCount}";

            if (hidersCountText.text != formatted)
            {
                hidersCountText.text = formatted;
            }
        }
    }
}
