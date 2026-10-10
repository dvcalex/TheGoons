using System.Collections;
using UnityEngine;

public class PlayerRagdollManager : MonoBehaviour
{
    public PlayerRigData playerRigData;
    public PlayerRigData ragdollRigData;

    public GameObject defaultCam;

    public Transform repositionPivot;
    public Transform root;

    [Space]

    public GameObject ragdollObj;
    public Renderer[] renderers;

    public bool isRagdoll = false;

    public void SetRagdoll(bool state)
    {
        

        if(!state)
        {
            // Remember where the ragdoll currently is.
            Vector3 ragdollWorldPos = ragdollRigData.transform.position;
            Quaternion ragdollWorldRot = ragdollRigData.transform.rotation;

            // Calculate where the player should wake up.
            Vector3 targetPosition = repositionPivot.position;

            // Move the player root.
            root.position = targetPosition;

            // Restore the ragdoll's world transform.
            ragdollRigData.transform.SetPositionAndRotation(
                ragdollWorldPos,
                ragdollWorldRot
            );

            Physics.SyncTransforms();

            // Start transitioning back to the player rig.
            ragdollRigData.SetRigLerp(playerRigData);

            StartCoroutine(WaitTillRagdollComplete());
        }
        else
        {
            // Knock out

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
