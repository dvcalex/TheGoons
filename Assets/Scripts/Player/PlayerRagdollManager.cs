using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerRagdollManager : NetworkBehaviour
{
    [Header("Rig References")]
    public PlayerRigData playerRigData;
    public PlayerRigData ragdollRigData;

    [Header("Camera")]
    public GameObject defaultCam;

    [Header("Positioning")]
    public Transform repositionPivot;
    public Transform root;

    [Header("Ragdoll")]
    public GameObject ragdollObj;
    public Renderer[] renderers;

    [Header("State")]
    public bool isRagdoll = false;
    private Coroutine wakeCoroutine;

    private void Start()
    {
        ragdollObj.SetActive(false);
    }

    public void SetRagdoll(bool state)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        var player = GetComponentInParent<Player>();
        if (player != null && player._movement.IsGrabbed)
        {
            return;
        }

        if (isRagdoll == state)
        {
            return;
        }

        Vector3 wakePosition = state ? Vector3.zero : repositionPivot.position;
        SetRagdoll_RPC(state, wakePosition);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    public void SetRagdoll_RPC(bool state, Vector3 wakePosition)
    {
        if (wakeCoroutine != null)
        {
            StopCoroutine(wakeCoroutine);
            wakeCoroutine = null;
        }

        if (state)
        {
            EnableRagdoll();
        }
        else
        {
            DisableRagdoll(wakePosition);
        }
    }

    private void EnableRagdoll()
    {
        // Position the ragdoll at the player's current position.
        ragdollRigData.transform.SetPositionAndRotation(root.position, root.rotation);

        // Hide the normal character renderers.
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        // Enable ragdoll physics.
        ragdollObj.SetActive(true);

        // Copy the player's current pose.
        ragdollRigData.SyncRig(playerRigData);
        isRagdoll = true;

        // Only disable the camera belonging to this client.
        if (IsOwner && defaultCam != null)
        {
            defaultCam.SetActive(false);
        }

        Physics.SyncTransforms();
        Debug.Log("Ragdoll Enabled");
    }

    private void DisableRagdoll(Vector3 wakePosition)
    {
        // Preserve the ragdoll's current world transform.
        Vector3 ragdollWorldPos = ragdollRigData.transform.position;
        Quaternion ragdollWorldRot = ragdollRigData.transform.rotation;

        // Move the player to the owner's supplied position.
        root.position = wakePosition;

        // Restore the ragdoll so moving its parent
        // doesn't move the ragdoll itself.
        ragdollRigData.transform.SetPositionAndRotation(ragdollWorldPos, ragdollWorldRot);
        Physics.SyncTransforms();

        // Begin transitioning the ragdoll into the player pose.
        ragdollRigData.SetRigLerp(playerRigData);
        wakeCoroutine = StartCoroutine(WaitTillRagdollComplete());
    }

    private IEnumerator WaitTillRagdollComplete()
    {
        yield return new WaitUntil(() => ragdollRigData._currentLerpData == null);

        // Restore the normal character renderers.
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = true;
        }

        // Disable the ragdoll.
        ragdollObj.SetActive(false);

        // Reset the ragdoll rig to match the player.
        ragdollRigData.SyncRig(playerRigData);
        isRagdoll = false;

        // Only reactivate the local player's camera.
        if (IsOwner && defaultCam != null)
        {
            defaultCam.SetActive(true);
        }

        wakeCoroutine = null;
        Debug.Log("Ragdoll Disabled");
    }
}
