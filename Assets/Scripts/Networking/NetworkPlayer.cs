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
        SharedGameSession session = NetworkManager.GetComponent<SharedGameSession>();
        if (session != null)
        {
            yield return new WaitUntil(() => session.MapReady && (GameManager._instance == null || !GameManager._instance.IsResetting));
        }

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

        Vector3 position = Vector3.zero;
        bool isVr = prefab.GetComponent<LocalVrPlayer>() != null;
        bool warned = false;

        while (IsSpawned && NetworkManager.ConnectedClients.ContainsKey(OwnerClientId))
        {
            if (isVr)
            {
                // The VR rig uses its own tracking-floor marker after spawning.
                VrSpawnPoint vrSpawn = VrSpawnPoint.FindInScene(SceneManager.GetSceneByName(SharedGameSession.GameScene));
                if (vrSpawn != null)
                {
                    position = vrSpawn.transform.position;
                    break;
                }
            }
            else
            {
                PlayerSpawn spawn = PlayerSpawn._instance;
                if (spawn != null && spawn.gameObject.scene.name == SharedGameSession.GameScene
                    && spawn.TryGetPosition(prefab.gameObject, out position))
                {
                    break;
                }
            }

            if (!warned)
            {
                Debug.LogWarning("Waiting for a clear player spawn in GameplayScene.", this);
                warned = true;
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        if (!IsSpawned || !NetworkManager.ConnectedClients.ContainsKey(OwnerClientId))
        {
            yield break;
        }

        NetworkObject player = NetworkManager.SpawnManager.InstantiateAndSpawn(
            prefab,
            OwnerClientId,
            false,
            true,
            false,
            position,
            Quaternion.identity
        );

        if (player != null)
        {
            NetworkObject.Despawn();
        }
    }
}
