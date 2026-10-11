using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections;

// The server replaces each connection placeholder after the map is loaded.
public class NetworkPlayer : NetworkBehaviour
{
    public bool VR_Override;
    public GameObject player_Host;
    public GameObject player_Client;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartCoroutine(SpawnWhenMapReady());
        }
    }

    private IEnumerator SpawnWhenMapReady()
    {
        yield return new WaitUntil(() => SceneManager.GetSceneByName(SharedGameSession.GameScene).isLoaded);

        if (!IsSpawned || !NetworkManager.ConnectedClients.ContainsKey(OwnerClientId))
        {
            yield break;
        }

        NetworkObject prefab;

        // use vr player
        if (OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId && !VR_Override)
        {
            prefab = player_Host.GetComponent<NetworkObject>();
        }
        else
        {
            prefab = player_Client.GetComponent<NetworkObject>();
        }

        yield return new WaitForSeconds(0.125f);

        NetworkObject player = NetworkManager.SpawnManager.InstantiateAndSpawn(
            prefab,
            OwnerClientId,
            false,
            true,
            false,
            Vector3.zero,
            Quaternion.identity
        );

        if (player != null)
        {
            NetworkObject.Despawn();
        }
    }
}
