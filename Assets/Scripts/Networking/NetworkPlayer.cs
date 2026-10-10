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
        if (IsServer) StartCoroutine(SpawnWhenMapReady());
    }
    IEnumerator SpawnWhenMapReady()
    {
        yield return new WaitUntil(() => SceneManager.GetSceneByName(SharedGameSession.GameScene).isLoaded);
        if (!IsSpawned || !NetworkManager.ConnectedClients.ContainsKey(OwnerClientId)) yield break;
        bool host = OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId && !VR_Override;
        var prefab = (host ? player_Host : player_Client).GetComponent<NetworkObject>();
        Vector3 position = host ? Vector3.zero : new Vector3(2f + (OwnerClientId - 1) * 1.5f, 0f, 2f);
        var player = NetworkManager.SpawnManager.InstantiateAndSpawn(prefab, OwnerClientId,
            false, true, false, position, Quaternion.identity);
        if (player != null) NetworkObject.Despawn();
    }
}
