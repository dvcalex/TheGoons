using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class DestroyOnConnection : MonoBehaviour
{
    public float waitTime = 0f;

    void Start()
    {
        StartCoroutine(WaitTillConnection());
    }

    IEnumerator WaitTillConnection()
    {
        yield return new WaitUntil(() => NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsServer);

        Destroy(gameObject);
    }
}
