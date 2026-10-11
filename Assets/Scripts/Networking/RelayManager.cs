using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using System;
using System.Diagnostics;
using System.Threading;
using Debug = UnityEngine.Debug;
using Mono.Cecil.Cil;

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
    private string lobbyCode;
    private bool startingHost;
    private bool startingClient;
    private SharedGameSession session;
    private TMP_Text connectingStatus;
    private const int ServiceTimeoutSeconds = 30;

    private void Awake()
    {
        connectingStatus = screen_Connecting.GetComponentInChildren<TMP_Text>(true);
        session = GetComponent<SharedGameSession>();
        if (session == null)
        {
            session = gameObject.AddComponent<SharedGameSession>();
        }
    }

    public TMP_Text lobbyCodeDisplay;

    private void Start()
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
        if (startingClient || startingHost || manager.IsListening)
        {
            return;
        }

        startingClient = true;
        errorDisplay.text = "";
        lobbyCode = lobbyCodeInput.text.Trim().ToUpperInvariant();
        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);
        ReportProgress("Starting client");
        try
        {
            bool started = enableOnline ? await StartClientWithRelay(lobbyCode, CONNECTIONTYPE) : manager.StartClient();
            if (!started)
            {
                throw new System.InvalidOperationException("Connection could not start. Check the join code.");
            }

            float deadline = Time.realtimeSinceStartup + 30f;
            ReportProgress("Waiting for host connection");
            while (!manager.IsConnectedClient && manager.IsListening && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(100);
            }

            if (!manager.IsConnectedClient)
            {
                throw new System.TimeoutException("Connection timed out. Check the host and join code.");
            }

            lobbyCodeDisplay.text = lobbyCode;
            Debug.Log("[Connection] Client connected successfully.");
            all.SetActive(false);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[Connection] Client startup failed: {exception}");
            if (manager.IsListening)
            {
                manager.Shutdown();
            }

            errorDisplay.text = exception.Message;
            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
        finally
        {
            startingClient = false;
        }
    }

    public void InitHost()
    {
        _ = Host();
    }

    public async Task Host()
    {
        if (startingHost || startingClient || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        startingHost = true;
        errorDisplay.text = "";
        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);
        ReportProgress("Starting host");
        try
        {
            if (!enableOnline)
            {
                if (!NetworkManager.Singleton.StartHost())
                {
                    throw new System.InvalidOperationException("Local host could not start.");
                }
            }
            else
            {
                string code = await StartHostWithRelay(10, CONNECTIONTYPE);
                GameManager._instance.lobbyCodeText.text = code;
                if (string.IsNullOrEmpty(code))
                {
                    throw new System.InvalidOperationException("Relay host could not start.");
                }

                lobbyCodeDisplay.text = code;
                GUIUtility.systemCopyBuffer = code;
            }

            ReportProgress("Loading gameplay scene");
            session.LoadGame();
            Debug.Log("[Connection] Host started; gameplay scene load requested.");
            all.SetActive(false);
        }
        catch (System.Exception exception)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            errorDisplay.text = exception.Message;
            Debug.LogError($"[Connection] Host startup failed: {exception}");
            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
        finally
        {
            startingHost = false;
        }
    }

    #region RELAY

    public async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        await InitializeOnlineServices();

        var allocation = await RunServiceStep("Creating Relay allocation",
            () => RelayService.Instance.CreateAllocationAsync(maxConnections));
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        var joinCode = await RunServiceStep("Retrieving join code",
            () => RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId));
        ReportProgress("Starting network host");
        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }

    public async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            throw new ArgumentException("Enter a join code first.");
        }

        await InitializeOnlineServices();
        var allocation = await RunServiceStep("Joining Relay allocation",
            () => RelayService.Instance.JoinAllocationAsync(joinCode: joinCode));
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        ReportProgress("Starting network client");
        return NetworkManager.Singleton.StartClient();
    }

    private async Task InitializeOnlineServices()
    {
        Debug.Log($"[Connection] Platform={Application.platform}, reachability={Application.internetReachability}, protocol={CONNECTIONTYPE}");
        await RunServiceStep("Initializing Unity Services", () => UnityServices.InitializeAsync());
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await RunServiceStep("Signing in to Unity Services",
                () => AuthenticationService.Instance.SignInAnonymouslyAsync());
        }
    }

    private Task RunServiceStep(string description, Func<Task> operation)
    {
        return RunServiceStep(description, async () =>
        {
            await operation();
            return true;
        });
    }

    private async Task<T> RunServiceStep<T>(string description, Func<Task<T>> operation)
    {
        ReportProgress(description);
        var timer = Stopwatch.StartNew();
        try
        {
            Task<T> request = operation();
            using (var timeoutCancellation = new CancellationTokenSource())
            {
                Task timeout = Task.Delay(TimeSpan.FromSeconds(ServiceTimeoutSeconds), timeoutCancellation.Token);
                if (await Task.WhenAny(request, timeout) != request)
                {
                    // Service requests cannot be cancelled. Observe a late failure, but never
                    // continue network startup after the user has already seen a timeout.
                    _ = request.ContinueWith(completed =>
                    {
                        _ = completed.Exception;
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException($"{description} timed out after {ServiceTimeoutSeconds}s. Check the headset's internet connection and try again.");
                }

                timeoutCancellation.Cancel();
                T result = await request;
                Debug.Log($"[Connection] {description} completed in {timer.Elapsed.TotalSeconds:F1}s.");
                return result;
            }
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{description} failed: {exception.Message}", exception);
        }
    }

    private void ReportProgress(string description)
    {
        if (connectingStatus != null)
        {
            connectingStatus.text = description + "...";
        }

        Debug.Log($"[Connection] {description}.");
    }
    #endregion
}
