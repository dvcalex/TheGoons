using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class NetworkDebug : MonoBehaviour
{
    private void Awake()
    {
        //if (NetworkManager.Singleton != null)
        //    Destroy(gameObject);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Server")) NetworkManager.Singleton.StartServer();
            if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
        }

        GUILayout.EndArea();
    }
}
