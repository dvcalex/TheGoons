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

// A round has two timed stages, but GamePhase remains Playing for both.
public enum RoundStage
{
    None,
    Hiding,
    Seeking
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
        public RoundStage Stage;
        public RoundResult Result;
        public int Hiders;
        public double EndTime;
        public float TimeLeft;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            int phase = (int)Phase;
            int stage = (int)Stage;
            int result = (int)Result;

            serializer.SerializeValue(ref phase);
            serializer.SerializeValue(ref stage);
            serializer.SerializeValue(ref result);
            serializer.SerializeValue(ref Hiders);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref TimeLeft);

            if (serializer.IsReader)
            {
                Phase = (GamePhase)phase;
                Stage = (RoundStage)stage;
                Result = (RoundResult)result;
            }
        }

        public bool Equals(RoundState other)
        {
            return Phase == other.Phase &&
                   Stage == other.Stage &&
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
                hash = hash * 31 + (int)Stage;
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
                Stage = RoundStage.None,
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
    public RoundStage CurrentStage => roundState.Value.Stage;
    public bool IsHiding => CurrentPhase == GamePhase.Playing && CurrentStage == RoundStage.Hiding;
    public bool IsSeeking => CurrentPhase == GamePhase.Playing && CurrentStage == RoundStage.Seeking;
    public RoundResult Result => roundState.Value.Result;
    public int HidersCount => roundState.Value.Hiders;

    public bool timerIsRunning =>
        CurrentPhase == GamePhase.Playing;

    public float timeRemaining
    {
        get
        {
            if (!IsSpawned || NetworkManager == null)
                return seekDuration;

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
    // Fires on every connected client when the stage changes.
    public event Action<RoundStage> StageChanged;
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
    private float seekDuration = 60f;
    [Min(0.1f)] public float hideDuration = 30f;

    public Player[] players = Array.Empty<Player>();

    [Tooltip("Assign the seeker so they are excluded from the hider count.")]
    public Player seeker;

    [Header("Optional UI")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI hidersCountText;
    public TextMeshProUGUI stageText;
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
                Stage = RoundStage.None,
                Result = RoundResult.None,
                Hiders = 0,
                EndTime = 0,
                TimeLeft = seekDuration
            };

            NetworkManager.OnClientDisconnectCallback +=
                OnClientDisconnected;
        }

        debugPhase = CurrentPhase;
        debugResult = Result;

        UpdateUI();

        // Initialize listeners for late-joining clients.
        PhaseChanged?.Invoke(CurrentPhase);
        StageChanged?.Invoke(CurrentStage);
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

        // Hider elimination ends the round in either timed stage.
        if (remainingHiders.Count == 0)
        {
            EndGameInternal(RoundResult.SeekerWins);
            return;
        }

        if (timeRemaining > 0f)
            return;

        // Hiding time is up: unlock the seeker and start a fresh countdown.
        if (IsHiding)
            BeginSeekingInternal();
        else if (IsSeeking)
            EndGameInternal(RoundResult.HidersWin);
    }

    // ==========================================
    // START GAME
    // ==========================================

    // Clients send a request to the server. Only the server/host may start rounds.
    public void StartGame_RPC()
    {
        if (IsSpawned)
            RequestStartGameRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestStartGameRpc(RpcParams rpcParams = default)
    {
        if (!IsAuthorizedRoundController(rpcParams.Receive.SenderClientId))
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

        Player activeSeeker = seeker;

        if (Server._instance != null && Server._instance.IsSpawned)
        {
            // Count everyone other than the seeker. Default to host as seeker
            // if no explicit seeker has been selected yet.
            hiders = Server._instance.GetAllPlayers();
            if (activeSeeker == null)
                activeSeeker = Server._instance.GetHostPlayer();
        }

        if (hiders != null)
        {
            foreach (Player player in hiders)
            {
                if (player == null || !player.IsSpawned)
                    continue;

                // Do not count the seeker as a hider.
                if (player == activeSeeker)
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
            NetworkManager.ServerTime.Time + hideDuration;

        SetRoundState(
            GamePhase.Playing,
            RoundStage.Hiding,
            RoundResult.None,
            remainingHiders.Count,
            endTime,
            hideDuration
        );

        Debug.Log($"Round started: Hiding ({hideDuration:0.#}s), {remainingHiders.Count} hiders.");
    }

    // Called only by the server, when the hiding timer expires.
    private void BeginSeekingInternal()
    {
        if (!CanControlRound() || !IsHiding)
            return;

        SetRoundState(
            GamePhase.Playing,
            RoundStage.Seeking,
            RoundResult.None,
            remainingHiders.Count,
            NetworkManager.ServerTime.Time + seekDuration,
            seekDuration
        );

        Debug.Log($"Seeking started ({seekDuration:0.#}s).");
    }

    // ==========================================
    // HIDER DEATH
    // ==========================================

    // Actual network RPC: clients report their own death to the server.
    // Server-initiated eliminations can report any hider.
    public void OnHiderDeath_RPC(ulong playerObjId)
    {
        if (IsSpawned)
            ReportHiderDeathRpc(playerObjId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportHiderDeathRpc(
        ulong playerObjId,
        RpcParams rpcParams = default)
    {
        if (!IsServer || !timerIsRunning ||
            !remainingHiders.Contains(playerObjId))
            return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
            playerObjId, out NetworkObject playerObject))
            return;

        ulong senderId = rpcParams.Receive.SenderClientId;

        // A client may report only its own character; the server can report anyone.
        if (senderId != NetworkManager.ServerClientId &&
            playerObject.OwnerClientId != senderId)
        {
            Debug.LogWarning($"Client {senderId} tried to eliminate player {playerObjId} without authority.");
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

    // Network request for an end-of-round check. Only host/server may initiate it.
    public void OnEndGame_RPC()
    {
        if (IsSpawned)
            RequestEndGameRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndGameRpc(RpcParams rpcParams = default)
    {
        if (!IsAuthorizedRoundController(rpcParams.Receive.SenderClientId) ||
            !timerIsRunning)
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
            RoundStage.None,
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
        if (!IsSpawned)
            return;

        ReturnToLobby_RPC();
    }

    public void ReturnToLobby_RPC()
    {
        if (IsSpawned)
            RequestReturnToLobbyRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestReturnToLobbyRpc(RpcParams rpcParams = default)
    {
        if (!IsAuthorizedRoundController(rpcParams.Receive.SenderClientId))
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
            RoundStage.None,
            RoundResult.None,
            0,
            0,
            seekDuration
        );
    }

    // ==========================================
    // NETWORK STATE MANAGEMENT
    // ==========================================

    private void SetRoundState(
        GamePhase phase,
        RoundStage stage,
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
                Stage = stage,
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
        bool stageChanged = previous.Stage != current.Stage;
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

        if (stageChanged)
        {
            StageChanged?.Invoke(current.Stage);
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

    private bool IsAuthorizedRoundController(ulong senderClientId)
    {
        // Round management is restricted to server/host requests.
        return CanControlRound() && senderClientId == NetworkManager.ServerClientId;
    }

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

        if (stageText != null)
        {
            string label = CurrentStage switch
            {
                RoundStage.Hiding => "HIDING TIME",
                RoundStage.Seeking => "SEEKER TIME",
                _ => CurrentPhase == GamePhase.Results ? "ROUND OVER" : "LOBBY"
            };

            if (stageText.text != label)
                stageText.text = label;
        }
    }
}
