using Unity.Netcode;
using UnityEngine;
using EZCameraShake;

public class AddCameraShaker : NetworkBehaviour
{
    void Start()
    {
        if(IsOwner)
            gameObject.AddComponent<CameraShaker>();
    }
}
