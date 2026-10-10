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
        errorDisplay.text = "";
        lobbyCode = lobbyCodeInput.text.ToUpper();

        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);

        if (!enableOnline)
        {
            NetworkManager.Singleton.StartClient();
            all.SetActive(false);
            return;
        }

        bool result = await StartClientWithRelay(lobbyCode, CONNECTIONTYPE);

        if (result)
        {
            lobbyCodeDisplay.text = lobbyCode.ToString();
            errorDisplay.text = "Client started successfully.";

            all.SetActive(false);
        }
        else
        {
            errorDisplay.text = "Failed to start client.";

            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
    }

    public void InitHost()
    {
        _ = Host();
    }

    public async Task Host()
    {
        errorDisplay.text = "";
        screen_JoinHost.SetActive(false);
        screen_Connecting.SetActive(true);

        if (!enableOnline)
        {
            NetworkManager.Singleton.StartHost();
            all.SetActive(false);
            return;
        }

        string code = await StartHostWithRelay(10, CONNECTIONTYPE);

        if (!string.IsNullOrEmpty(code))
        {
            lobbyCodeDisplay.text = code.ToString();
            //errorDisplay.text = $"Host started successfully. Join code: {code}";

            GUIUtility.systemCopyBuffer = code;

            all.SetActive(false);
        }
        else
        {
            errorDisplay.text = "Failed to start host.";

            screen_Connecting.SetActive(false);
            screen_JoinHost.SetActive(true);
        }
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
