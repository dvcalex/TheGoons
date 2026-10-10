using System.Collections;
using UnityEngine;

public class PlayerRagdollManager : MonoBehaviour
{
    public PlayerRigData playerRigData;
    public PlayerRigData ragdollRigData;

    public GameObject defaultCam;

    [Space]

    public GameObject ragdollObj;
    public Renderer[] renderers;

    public bool isRagdoll = false;

    public void SetRagdoll(bool state)
    {
        

        if(!state)
        {
            // Disable ragdoll

            ragdollRigData.SetRigLerp(playerRigData);

            transform.position = ragdollRigData.transform.position;
            ragdollRigData.transform.localPosition = Vector3.zero;

            StartCoroutine(WaitTillRagdollComplete());
        }
        else
        {
            // Enable ragdoll

            ragdollRigData.transform.position = transform.position;
            ragdollRigData.transform.rotation = transform.rotation;

            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;

            ragdollObj.SetActive(true);
            Debug.Log("Ragdoll Enabled");
            ragdollRigData.SyncRig(playerRigData);

            isRagdoll = true;
            defaultCam.SetActive(false);
        }
    }

    IEnumerator WaitTillRagdollComplete()
    {
        yield return new WaitUntil(() => ragdollRigData._currentLerpData == null);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = true;

        ragdollObj.SetActive(false);
        ragdollRigData.SyncRig(playerRigData);

        isRagdoll = false;

        defaultCam.SetActive(true);
    }
}
