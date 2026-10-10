using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Netcode.Transports.UTP;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System;
using UnityEngine.InputSystem;
using WebSocketSharp;
using UnityEngine.UI;
using System.Collections;

public class NetworkConnectionManager : NetworkBehaviour
{
    private static readonly string GAMEISFULL = "Failed to connect... server is full."; // Message to display when a player is not allowed to connect because the server cannot support the number of players trying to connect on their client.
    private static readonly string GAMESTARTED = "Failed to connect... match already started."; // Message to display when a player attempts to join an online game that has already begun.

    public static readonly string CONNECTIONTYPE = "udp"; // Unity Relay connection type. Accepted types: udp, wss, dtls

    public static int maxConnections = 10; // Game logic supports up to 10 players

    bool controllerState; // True when we're attempting to connect controllers, false otherwise. Used to determine which menu to display and what actions to take on input
    bool hostState; // True when we're the host and are waiting in the lobby, false otherwise. Used to determine what data to display

    public GameObject networkTransportObj; // Reference to the transport object, used in local connections

    public static NetworkConnectionManager instance; // Current instance

    [HideInInspector]
    public bool menuLock;
    bool onlineConnectionLock;

    [Space]

    // UI References
    public GameObject menu;
    public GameObject connectingControlelrScreen;
    public GameObject connectingScreen;
    public GameObject localOnlineScreen;
    public GameObject waitingScreen;
    public GameObject hostScreen;
    public GameObject joinHostButtons;
    public GameObject hideMenu;
    public GameObject onlineMenu;
    public GameObject gameMenu;
    public GameObject singletonFailedScreen;
    public GameObject initSingletonScreen;

    public GameObject startGameMenu;

    public TMP_InputField joinCodeInput;

    public Button onlineButton;

    public TMP_Text joinCodeText;
    public TMP_Text playersConnectedText;
    public TMP_Text playerCounterText;

    // Input actions for controller support
    [SerializeField] InputAction _startAction;
    [SerializeField] InputAction _leaveAction;

    void Awake()
    {
        if (instance != null && instance != this)
            Destroy(this.gameObject);
        else
            instance = this;
    }

    private void Start()
    {
        MultiplayerManager.Instance.readyToConnectPlayers = true;
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        _startAction.Enable();
        _leaveAction.Enable();

        _startAction.performed += context => StartGame_Action(context);
        _leaveAction.performed += context => LeaveGame_Action(context);

        EnterLocal();
    }

    private void Update()
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        if (controllerState) // If we're attempting to connect controllers, display relevant info
        {
            int playersConnected = MultiplayerManager.Instance._players.Count;
            playersConnectedText.text = $"Players connected: {playersConnected}";
        }

        if (hostState) // If we're the host, display relevant info
        {
            playerCounterText.text = $"Players: {MultiplayerManager.Instance.GetTotalPlayers()} / {maxConnections}";

            if (MultiplayerManager.Instance.GetTotalPlayers() >= maxConnections)
                playerCounterText.color = Color.gray;
            else
                playerCounterText.color = Color.white;
        }

        onlineButton.interactable = !GameManager.main.gameStarted && MultiplayerManager.Instance._players.Count > 0 && !GameManager.main.countingDownToStart;
    }

    void OnEnable()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectCallback;
    }

    public void LeaveGame(string reason = "")
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        //ResetState(reason);
    }

    void ResetState(string text = "", bool shutdownClient = true, bool enterLocal = true) // Used to reset the state of the game when leaving a lobby or disconnecting from a game.
    {
        //Debug.Log("Resetting state... reason: " + text);

        if (shutdownClient)
        {
            // Shutdown singleton


            // Reset menu states

            controllerState = false;
            hostState = false;

            joinCodeInput.text = string.Empty;
            joinCodeText.text = text;

            // Reset menu

            menu.SetActive(true);
            localOnlineScreen.SetActive(true);

            joinHostButtons.SetActive(false);
            connectingScreen.SetActive(false);
            hostScreen.SetActive(false);
            waitingScreen.SetActive(false);
            connectingControlelrScreen.SetActive(false);
            connectingControlelrScreen.SetActive(false);

            // Clear player counts

            MultiplayerManager.Instance.DestroyAllPlayers();

            MultiplayerManager.Instance.ClearPlayerCount();
            MultiplayerManager.Instance._players.Clear();

            MultiplayerManager.Instance._playersOnline.Clear();

            MultiplayerManager.Instance.SetMultiplayerState(false);

            // Reset game states

            GameManager.main._gameLoaded = false;

            GameManager.main.ResetGame();
            PlayerIdManager._Instance.ResetState();

            if(enterLocal)
                EnterLocal(true);
        }
        else
        {
            // Reset menu

            menu.SetActive(true);
            joinHostButtons.SetActive(true);

            localOnlineScreen.SetActive(false);
            connectingScreen.SetActive(false);
            hostScreen.SetActive(false);
            waitingScreen.SetActive(false);
            connectingControlelrScreen.SetActive(false);

            joinCodeInput.text = string.Empty;
            joinCodeText.text = text;
        }
    }

    private void OnClientDisconnectCallback(ulong clientId)
    {
        if(onlineConnectionLock)
        {
            onlineConnectionLock = false;
            return;
        }

        if (clientId == NetworkManager.LocalClientId || clientId == 0) // Is the client that disconnected the host (clientId == 0) or am I the client who disconnected?
        {
            string disconnectReason = NetworkManager.Singleton.DisconnectReason;
            bool shutdownClient = !(disconnectReason.Equals(GAMEISFULL) || disconnectReason.Equals(GAMESTARTED));

            ResetState(disconnectReason, shutdownClient);
        }

        if (IsHost) // Clear this clients connected player count data
            MultiplayerManager.Instance.ClearPlayerCount(clientId);
    }

    private void StartGame_Action(InputAction.CallbackContext context)
    {
        StartCoroutine(StartGameDelay());
    }

    IEnumerator StartGameDelay()
    {
        yield return new WaitForSeconds(0.1f);

        if (menuLock)
            yield return null;

        if (!GameManager.main.CanStartGame())
            yield return null;

        if(MultiplayerManager.Instance._players.Count == 0)
            yield return null;

        if (controllerState && !MultiplayerManager.Instance.isLocal && MultiplayerManager.Instance._players.Count >= 1)
            ExitControllerState();
        else if (hostState)
            StartOnlineGame();
    }

    private void LeaveGame_Action(InputAction.CallbackContext context)
    {
        //if (MultiplayerManager.Instance.isLocal)
        //    return;

        LeaveGame();
    }

    public void ExitControllerState()
    {
        MultiplayerManager.Instance.readyToConnectPlayers = false;
        controllerState = false;
        connectingControlelrScreen.SetActive(false);
        joinHostButtons.SetActive(true);
    }

    public bool EnterLocal(bool waitTillShutdown = false)
    {
        initSingletonScreen.SetActive(true);

        onlineMenu.SetActive(false);
        gameMenu.SetActive(false);
        menuLock = false;

        //Debug.Log("Starting singleton");

        bool result = false;

        if(!waitTillShutdown)
            result = StartHostSingleton();
        else
            StartCoroutine(WaitTillShutdown());

        MultiplayerManager.Instance.isLocal = true;
        MultiplayerManager.Instance.readyToConnectPlayers = true;
        GameManager.main.readyToStart = true;
        //menu.SetActive(false);

        onlineButton.gameObject.SetActive(true);

        initSingletonScreen.SetActive(false);

        if (result)
        {
            singletonFailedScreen.SetActive(false);
        }

        if (waitTillShutdown)
            return true;
        else
            return result;
    }

    IEnumerator WaitTillShutdown()
    {
        yield return new WaitUntil(() => NetworkManager.Singleton.IsClient);

        bool result = StartHostSingleton();

        initSingletonScreen.SetActive(false);

        if (result)
        {
            
            singletonFailedScreen.SetActive(false);
        }
    }

    public bool StartHostSingleton()
    {
        //singletonFailedScreen.SetActive(false);

        UnityTransport transport = networkTransportObj.GetComponent<UnityTransport>();

        //transport.max

        transport.SetConnectionData("127.0.0.1", 7777);

        bool result = NetworkManager.Singleton.StartHost();
        
        if (!result)
        {
            initSingletonScreen.SetActive(false);
            singletonFailedScreen.SetActive(true);
        }

        return result;
    }

    public void Quit()
    {
        Application.Quit();
    }

    public void EnterOnline()
    {
        onlineConnectionLock = true;
        NetworkManager.Singleton.Shutdown();

        gameMenu.SetActive(true);
        onlineMenu.SetActive(true);
        joinHostButtons.SetActive(true);
        onlineButton.gameObject.SetActive(false);

        localOnlineScreen.SetActive(false);
        ExitControllerState();
        menuLock = true;

        MultiplayerManager.Instance.isLocal = false;

        if(MultiplayerManager.Instance._players.Count > 1)
            MultiplayerManager.Instance.InMultiplayerMode = true;

        MultiplayerManager.Instance.DestroyAllPlayerObjects();

        GameManager.main.readyToStart = false;
        GameManager.main.ResetGame();
        MultiplayerManager.Instance._playersOnline.Clear();
        //MultiplayerManager.Instance.ClearNetworkPlayerCount();
    }

    public async void Host()
    {
        joinHostButtons.SetActive(false);

        await StartHostWithRelay(maxConnections, CONNECTIONTYPE);

        MultiplayerManager.Instance.SetMultiplayerState(true);

        connectingScreen.SetActive(false);
        hostScreen.SetActive(true);
        hostState = true;
        menuLock = false;
    }

    public async void Join()
    {
        joinHostButtons.SetActive(false);

        bool success = await StartClientWithRelay(joinCodeInput.text, CONNECTIONTYPE);

        if(success)
        {
            connectingScreen.SetActive(false);
            waitingScreen.SetActive(true);
            joinCodeText.text = joinCodeInput.text;
            MultiplayerManager.Instance.SetMultiplayerState(true);
            //MultiplayerManager.Instance.JoinCheck_ServerRPC(MultiplayerManager.Instance._players.Count, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            connectingScreen.SetActive(false);
            joinHostButtons.SetActive(true);
            joinCodeText.text = "Failed to join game... try again";
        }
    }

    private void ConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        int playerCount = 0;

        if(request.Payload != null && request.Payload.Length >= sizeof(int)) // Grab the payload
        {
            playerCount = BitConverter.ToInt32(request.Payload, 0);
        }

        if(playerCount + MultiplayerManager.Instance.GetTotalPlayers() > maxConnections)
        {
            response.Approved = false;
            response.Reason = GAMEISFULL;
            response.Pending = false;
            return;
        }

        if (GameManager.main._gameLoaded)
        {
            response.Approved = false;
            response.Reason = GAMESTARTED;
            response.Pending = false;
            return;
        }

        MultiplayerManager.Instance.AddPlayerCount(playerCount, request.ClientNetworkId);

        response.Approved = true;
        response.Pending = false;
    }

    public void StartOnlineGame()
    {
        joinCodeText.text = string.Empty;
        onlineMenu.SetActive(false);
        menu.SetActive(false);
        GameManager.main.readyToStart = true;
        GameManager.main._gameLoaded = true;
        MultiplayerManager.Instance.OnOnlineGameStarted_ClientRPC();
        StartOnlineGame_ClientRPC();

        hostState = false;
    }

    [ClientRpc]
    public void StartOnlineGame_ClientRPC()
    {
        menu.SetActive(false);
        waitingScreen.SetActive(false);
    }

    public async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        connectingScreen.SetActive(true);

        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        joinCodeText.text = "Game code: " + joinCode; //s

        NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApproval;

        Debug.Log("Adding player count... current count: " + MultiplayerManager.Instance._players.Count + ", clientId: " + NetworkManager.Singleton.LocalClientId);
        MultiplayerManager.Instance.AddPlayerCount(MultiplayerManager.Instance._players.Count, NetworkManager.Singleton.LocalClientId);

        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }

    public async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        if(joinCode.IsNullOrEmpty())
        {
            joinCodeText.text = "Join code is empty... try again";
            return false;
        }

        connectingScreen.SetActive(true);

        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);

            byte[] payload = BitConverter.GetBytes(MultiplayerManager.Instance._players.Count);
            NetworkManager.Singleton.NetworkConfig.ConnectionData = payload;

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

            return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
        }
        catch(Exception e)
        {
            return false;
        }
    }
}
