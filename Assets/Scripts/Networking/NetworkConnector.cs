using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkConnector : MonoBehaviour
{
    public bool UseRelay = true;

    public GameObject networkDebug;

    private void Start()
    {
        if (UseRelay)
            InitRelay();
        else
            InitDebug();
    }

    public void InitRelay()
    {

    }

    public void InitDebug()
    {
        Instantiate(networkDebug, Vector3.zero, Quaternion.identity);

        Destroy(gameObject);
    }
}

