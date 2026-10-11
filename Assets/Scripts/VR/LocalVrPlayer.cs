using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>Local tracking plus replicated world-space poses for the VR host.</summary>
public sealed class LocalVrPlayer : NetworkBehaviour
{
    public XROrigin origin;
    public GameObject trollRig;
    public Transform localHeadVisual;

    public struct RigPose : INetworkSerializable
    {
        public Vector3 head;
        public Vector3 left;
        public Vector3 right;
        public Quaternion headRotation;
        public Quaternion leftRotation;
        public Quaternion rightRotation;
        public byte leftGrip;
        public byte rightGrip;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref head);
            serializer.SerializeValue(ref left);
            serializer.SerializeValue(ref right);
            serializer.SerializeValue(ref headRotation);
            serializer.SerializeValue(ref leftRotation);
            serializer.SerializeValue(ref rightRotation);
            serializer.SerializeValue(ref leftGrip);
            serializer.SerializeValue(ref rightGrip);
        }
    }

    private readonly NetworkVariable<RigPose> pose = new NetworkVariable<RigPose>(new RigPose
    {
        headRotation = Quaternion.identity,
        leftRotation = Quaternion.identity,
        rightRotation = Quaternion.identity
    });
    private Transform left;
    private Transform right;
    private Transform head;
    private VrRigController rig;
    private float nextSend;
    private bool received;

    private void Awake()
    {
        if (origin != null)
        {
            origin.gameObject.SetActive(false);
        }

        if (trollRig != null)
        {
            trollRig.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (origin == null || trollRig == null)
        {
            return;
        }

        received = false;
        rig = trollRig.GetComponent<VrRigController>();
        head = rig.physicalHead.transform;
        left = rig.leftHand.transform;
        right = rig.rightHand.transform;

        if (localHeadVisual != null)
        {
            localHeadVisual.gameObject.SetActive(!IsOwner);
        }

        origin.gameObject.SetActive(IsOwner);

        if (IsOwner)
        {
            trollRig.SetActive(true);
            rig.BindTracking(origin.CameraFloorOffsetObject.transform);
        }
        else
        {
            PrepareRemoteRig();
            pose.OnValueChanged += PoseChanged;
            PoseChanged(default(RigPose), pose.Value);
        }
    }

    public VrPhysicsHand GetHand(bool leftSide)
    {
        if (rig == null)
        {
            return null;
        }

        if (leftSide)
        {
            return rig.leftHand;
        }

        return rig.rightHand;
    }

    private void PrepareRemoteRig()
    {
        // Keep the visual arm solver active, but never run remote input or physics.
        rig.enabled = false;
        rig.leftHand.enabled = false;
        rig.rightHand.enabled = false;

        foreach (var effects in trollRig.GetComponentsInChildren<TrollPresentation.TrollContactEffects>(true))
        {
            effects.enabled = false;
        }

        foreach (var collider in trollRig.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (var body in trollRig.GetComponentsInChildren<Rigidbody>(true))
        {
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        // The chassis is a local locomotion body, not part of the visible avatar.
        rig.chassis.gameObject.SetActive(false);
        trollRig.SetActive(true);

        // Received poses already interpolate here; Rigidbody interpolation would fight them.
        foreach (var body in trollRig.GetComponentsInChildren<Rigidbody>(true))
        {
            body.interpolation = RigidbodyInterpolation.None;
        }
    }

    private void PoseChanged(RigPose previous, RigPose current)
    {
        if (rig == null)
        {
            return;
        }

        if (!received)
        {
            head.SetPositionAndRotation(current.head, current.headRotation);
            left.SetPositionAndRotation(current.left, current.leftRotation);
            right.SetPositionAndRotation(current.right, current.rightRotation);
            UpdateRemoteVisuals(current);
            received = true;
        }
    }

    private void LateUpdate()
    {
        if (!IsSpawned || origin == null)
        {
            return;
        }

        if (IsOwner && IsServer && left != null && right != null && Time.unscaledTime >= nextSend)
        {
            nextSend = Time.unscaledTime + 1f / 30f;
            pose.Value = new RigPose
            {
                head = head.position,
                headRotation = head.rotation,
                left = left.position,
                leftRotation = left.rotation,
                right = right.position,
                rightRotation = right.rotation,
                leftGrip = (byte)Mathf.RoundToInt(rig.leftHand.Grip * 255f),
                rightGrip = (byte)Mathf.RoundToInt(rig.rightHand.Grip * 255f)
            };
        }
        else if (!IsOwner && rig != null)
        {
            var current = pose.Value;
            float t = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            Move(head, current.head, current.headRotation, t);
            Move(left, current.left, current.leftRotation, t);
            Move(right, current.right, current.rightRotation, t);
            UpdateRemoteVisuals(current);
        }
    }

    private void UpdateRemoteVisuals(RigPose current)
    {
        rig.UpdateFacing(head.rotation);
        rig.leftHand.SetVisualGrip(current.leftGrip / 255f);
        rig.rightHand.SetVisualGrip(current.rightGrip / 255f);
    }

    private static void Move(Transform target, Vector3 position, Quaternion rotation, float t)
    {
        target.SetPositionAndRotation(Vector3.Lerp(target.position, position, t), Quaternion.Slerp(target.rotation, rotation, t));
    }

    public override void OnNetworkDespawn()
    {
        pose.OnValueChanged -= PoseChanged;
        if (origin != null)
        {
            origin.gameObject.SetActive(false);
        }

        if (trollRig != null)
        {
            trollRig.SetActive(false);
        }
    }
}
