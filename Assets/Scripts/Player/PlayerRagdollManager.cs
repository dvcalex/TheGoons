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
    private PlayerMovement movement;
    private Vector3 torsoOffset;
    private bool capsuleWasEnabled;
    private readonly NetworkVariable<RagdollState> ragdollState = new NetworkVariable<RagdollState>(
        default(RagdollState), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public struct RagdollState : INetworkSerializable
    {
        public bool active;
        public Vector3 wakePosition;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref active);
            serializer.SerializeValue(ref wakePosition);
        }
    }

    public Rigidbody GrabBody
    {
        get
        {
            if (repositionPivot == null)
            {
                return null;
            }

            return repositionPivot.GetComponent<Rigidbody>();
        }
    }

    private void Awake()
    {
        movement = GetComponentInParent<Player>()._movement;
    }

    private void Start()
    {
        if (!isRagdoll)
        {
            ragdollObj.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        ragdollState.OnValueChanged += RagdollChanged;
        if (ragdollState.Value.active)
        {
            EnableRagdoll();
        }
    }

    public override void OnNetworkDespawn()
    {
        ragdollState.OnValueChanged -= RagdollChanged;
    }

    // GrabState already tells every client when to enable the ragdoll.
    public void BeginGrabRagdoll()
    {
        if (wakeCoroutine != null)
        {
            StopCoroutine(wakeCoroutine);
            wakeCoroutine = null;
            ragdollRigData._currentLerpData = null;
            ragdollRigData.SetRigidbodyStates(true);
            GrabBody.isKinematic = !IsOwner;
        }

        EnableRagdoll();
        if (IsSpawned && IsOwner)
        {
            ragdollState.Value = new RagdollState { active = true };
        }
    }

    public void SetReleaseVelocity(Vector3 velocity)
    {
        foreach (Transform bone in ragdollRigData.rigData)
        {
            var body = bone.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = velocity;
            }
        }
    }

    public void SupportGrabbedLimbs()
    {
        float supportedMass = 0f;
        foreach (Transform bone in ragdollRigData.rigData)
        {
            var body = bone.GetComponent<Rigidbody>();
            if (body != null && body != GrabBody && !body.isKinematic && body.useGravity)
            {
                supportedMass += body.mass;
            }
        }

        // The torso motor cancels its own gravity; it also carries the hanging limbs.
        GrabBody.AddForce(-Physics.gravity * supportedMass, ForceMode.Force);
    }

    private void FixedUpdate()
    {
        if (!IsOwner || !isRagdoll || wakeCoroutine != null)
        {
            return;
        }

        FollowMovementBody();
    }

    private void FollowMovementBody()
    {
        // Keep the existing player position stream with the torso, without moving its bones.
        Vector3 ragdollPosition = ragdollRigData.transform.position;
        Quaternion ragdollRotation = ragdollRigData.transform.rotation;
        movement.rb.position = repositionPivot.position - torsoOffset;
        // The torso supplies motion. The hidden capsule must not integrate it a second time.
        movement.rb.linearVelocity = Vector3.zero;
        movement.rb.angularVelocity = Vector3.zero;
        ragdollRigData.transform.SetPositionAndRotation(ragdollPosition, ragdollRotation);
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

        Vector3 wakePosition = Vector3.zero;
        if (!state)
        {
            wakePosition = repositionPivot.position - torsoOffset;
        }

        ragdollState.Value = new RagdollState
        {
            active = state,
            wakePosition = wakePosition
        };
    }

    private void RagdollChanged(RagdollState previous, RagdollState current)
    {
        if (!current.active && movement.IsGrabbed)
        {
            return;
        }

        if (wakeCoroutine != null)
        {
            StopCoroutine(wakeCoroutine);
            wakeCoroutine = null;
            if (current.active)
            {
                ragdollRigData._currentLerpData = null;
                ragdollRigData.SetRigidbodyStates(true);
                GrabBody.isKinematic = !IsOwner;
            }
        }

        if (current.active)
        {
            EnableRagdoll();
        }
        else
        {
            DisableRagdoll(current.wakePosition);
        }
    }

    private void EnableRagdoll()
    {
        if (isRagdoll)
        {
            return;
        }

        ragdollRigData._currentLerpData = null;
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
        ragdollRigData.SetRigidbodyStates(true);
        if (IsSpawned)
        {
            // The torso's existing NetworkTransform stays owner-authoritative.
            GrabBody.isKinematic = !IsOwner;
        }
        torsoOffset = repositionPivot.position - root.position;
        capsuleWasEnabled = movement.capsule.enabled;
        movement.capsule.enabled = false;
        movement.ClearGrabMovement();
        SetReleaseVelocity(movement.rb.linearVelocity);
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
        movement.capsule.enabled = capsuleWasEnabled;

        // Only reactivate the local player's camera.
        if (IsOwner && defaultCam != null)
        {
            defaultCam.SetActive(true);
        }

        wakeCoroutine = null;
        Debug.Log("Ragdoll Disabled");
    }
}
