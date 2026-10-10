using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class NetworkPlayer : NetworkBehaviour
{
    public GameObject player_Host;
    public GameObject player_Client;

    private void Start()
    {
        StartCoroutine(SafeStart());
    }

    IEnumerator SafeStart()
    {
        yield return new WaitUntil(() => NetworkManager.Singleton != null && (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsServer));
        
        if (IsOwner)
        {
            InitPlayer_Local();
        }
        else
        {
            InitPlayer_Other();
        }
    }

    public void InitPlayer_Local()
    {
        if(IsHost)
        {
            NetworkObject playerObject = player_Host.GetComponent<NetworkObject>();
            NetworkManager.Singleton.SpawnManager.InstantiateAndSpawn(playerObject, NetworkObject.OwnerClientId, true, true, false, transform.position, Quaternion.identity);
        }
        else
        {
            SpawnClient_RPC();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SpawnClient_RPC()
    {
        NetworkObject playerObject = player_Client.GetComponent<NetworkObject>();
        NetworkManager.Singleton.SpawnManager.InstantiateAndSpawn(playerObject, NetworkObject.OwnerClientId, true, true, false, transform.position, Quaternion.identity);

        Debug.Log("Spawned Client Player");
    }

    public void InitPlayer_Other()
    {
        Debug.Log("Other Player Initialized");
    }
}
