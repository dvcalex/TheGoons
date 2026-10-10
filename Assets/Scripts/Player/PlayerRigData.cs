using UnityEngine;

public class PlayerRigData : MonoBehaviour
{
    public Transform[] rigData;
    [HideInInspector]
    public PlayerRigData _currentLerpData;
    public float lerpThreshold = 0.15f;

    public void SyncRig(PlayerRigData _data)
    {
        for(int i = 0; i < rigData.Length; i++)
        {
            rigData[i].position = _data.rigData[i].position;
            rigData[i].rotation = _data.rigData[i].rotation;
        }
    }

    private void LerpToRig(PlayerRigData _data, float _lerp)
    {
        int unsafeCount = 0;

        for(int i = 0; i < rigData.Length; i++)
        {
            rigData[i].position = Vector3.Lerp(rigData[i].position, _data.rigData[i].position, _lerp);
            rigData[i].rotation = Quaternion.Lerp(rigData[i].rotation, _data.rigData[i].rotation, _lerp);

            if(Vector3.Distance(rigData[i].position, _data.rigData[i].position) > lerpThreshold)
            {
                unsafeCount++;
            }
        }

        Debug.Log("Unsafe Count: " + unsafeCount);

        if (unsafeCount <= 3)
        {
            SyncRig(_currentLerpData);
            _currentLerpData = null;
            SetRigidbodyStates(true);
        }
    }

    public void SetRigidbodyStates(bool _state)
    {
        for(int i = 0; i < rigData.Length; i++)
        {
            Rigidbody rb = rigData[i].GetComponent<Rigidbody>();
            if(rb != null)
            {
                rb.isKinematic = !_state;
                rb.useGravity = _state;
            }
        }
    }

    public void SetRigLerp(PlayerRigData _data)
    {
        _currentLerpData = _data;
    }

    private void Update()
    {
        if(_currentLerpData != null)
        {
            SetRigidbodyStates(false);
            LerpToRig(_currentLerpData, Time.deltaTime * 15f);
        }
    }
}
