using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using WebSocketSharp;

public class RelayManager : MonoBehaviour
{
    public static readonly string CONNECTIONTYPE = "dtls";

    public bool enableOnline;

    public GameObject all;

    public GameObject screen_Name;
    public GameObject screen_JoinHost;
    public GameObject screen_Connecting;

    public TMP_InputField nameInput;

    public TMP_Text errorDisplay;
    public TMP_InputField lobbyCodeInput;

    string lobbyCode;
    bool startingHost;
    bool startingClient;
    SharedGameSession session;

    void Awake()
    {
        session = GetComponent<SharedGameSession>();
        if (session == null) session = gameObject.AddComponent<SharedGameSession>();
    }

    public TMP_Text lobbyCodeDisplay;

    void Start()
    {
        all.SetActive(true);
        screen_Connecting.SetActive(false);
        screen_JoinHost.SetActive(false);
        screen_Name.SetActive(true);
    }

    public void SubmitName()
    {
        PlayerData._instance.playerName = nameInput.text;
        screen_Name.SetActive(false);
        screen_JoinHost.SetActive(true);
    }

    public void InitClient()
    {
        _ = Client();
    }

    public async Task Client()
    {
        var manager = NetworkManager.Singleton;
        if (startingClient || manager.IsListening) return;
        startingClient = true;
        errorDisplay.text = "";
        lobbyCode = lobbyCodeInput.text.Trim().ToUpperInvariant();
        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);
        try
        {
            bool started = enableOnline ? await StartClientWithRelay(lobbyCode, CONNECTIONTYPE) : manager.StartClient();
            if (!started) throw new System.InvalidOperationException("Connection could not start. Check the join code.");
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!manager.IsConnectedClient && manager.IsListening && Time.realtimeSinceStartup < deadline)
                await Task.Delay(100);
            if (!manager.IsConnectedClient) throw new System.TimeoutException("Connection timed out. Check the host and join code.");
            lobbyCodeDisplay.text = lobbyCode;
            all.SetActive(false);
        }
        catch (System.Exception exception)
        {
            if (manager.IsListening) manager.Shutdown();
            errorDisplay.text = exception.Message;
            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
        finally { startingClient = false; }
    }

    public void InitHost()
    {
        _ = Host();
    }

    public async Task Host()
    {
        if (startingHost || NetworkManager.Singleton.IsListening) return;
        startingHost = true;
        errorDisplay.text = "";
        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);

        try
        {
            if (!enableOnline)
            {
                if (!NetworkManager.Singleton.StartHost())
                    throw new System.InvalidOperationException("Local host could not start.");
            }
            else
            {
                string code = await StartHostWithRelay(10, CONNECTIONTYPE);
                if (string.IsNullOrEmpty(code))
                    throw new System.InvalidOperationException("Relay host could not start.");
                lobbyCodeDisplay.text = code;
                GUIUtility.systemCopyBuffer = code;
            }
            session.LoadGame();
            all.SetActive(false);
        }
        catch (System.Exception exception)
        {
            if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            errorDisplay.text = "Failed to start host. Please try again.";
            Debug.LogWarning($"Host startup failed: {exception.Message}");
            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
        finally { startingHost = false; }
    }

    #region RELAY

    public async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }

    public async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        if (joinCode.IsNullOrEmpty())
        {
            errorDisplay.text = "Join code is empty.";
            return false;
        }

        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

            return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
