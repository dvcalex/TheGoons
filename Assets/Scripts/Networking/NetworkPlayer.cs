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
        Vector3 position;

        // use vr player
        if (OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId && !VR_Override)
        {
            prefab = player_Host.GetComponent<NetworkObject>();
            position = Vector3.zero;
        }
        else
        {
            prefab = player_Client.GetComponent<NetworkObject>();
            position = new Vector3(2f, 0f, 2f);

            // Client IDs are unsigned, so the desktop host must not subtract from zero.
            if (OwnerClientId > 0)
            {
                position.x += (OwnerClientId - 1) * 1.5f;
            }
        }

        NetworkObject player = NetworkManager.SpawnManager.InstantiateAndSpawn(
            prefab,
            OwnerClientId,
            false,
            true,
            false,
            PlayerSpawn._instance.transform.position,
            Quaternion.identity
        );

        if (player != null)
        {
            NetworkObject.Despawn();
        }
    }
}
