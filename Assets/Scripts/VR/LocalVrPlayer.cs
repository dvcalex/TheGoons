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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref head);
            serializer.SerializeValue(ref left);
            serializer.SerializeValue(ref right);
            serializer.SerializeValue(ref headRotation);
            serializer.SerializeValue(ref leftRotation);
            serializer.SerializeValue(ref rightRotation);
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
    private Transform[] remote;
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
        if (origin == null)
        {
            return;
        }

        // The local physics rig and tracking share the player's ownership lifecycle.
        if (localHeadVisual != null && IsOwner)
        {
            localHeadVisual.gameObject.SetActive(false);
        }

        origin.gameObject.SetActive(IsOwner);

        if (trollRig != null)
        {
            trollRig.SetActive(IsOwner);
        }

        var offset = origin.CameraFloorOffsetObject.transform;
        left = offset.Find("Left Controller");
        right = offset.Find("Right Controller");
        head = origin.Camera.transform;

        if (IsOwner && trollRig != null)
        {
            var rig = trollRig.GetComponent<VrRigController>();
            if (rig != null)
            {
                rig.BindTracking(offset);
                head = rig.physicalHead.transform;
                left = rig.leftHand.transform;
                right = rig.rightHand.transform;
            }
        }

        if (!IsOwner)
        {
            remote = new Transform[3];
            remote[0] = CreateMarker("VR Head", PrimitiveType.Sphere, new Vector3(.22f, .26f, .22f));
            remote[1] = CreateMarker("VR Left Hand", PrimitiveType.Cube, Vector3.one * .16f);
            remote[2] = CreateMarker("VR Right Hand", PrimitiveType.Cube, Vector3.one * .16f);
            pose.OnValueChanged += PoseChanged;
            PoseChanged(default(RigPose), pose.Value);
        }
    }

    private Transform CreateMarker(string label, PrimitiveType type, Vector3 size)
    {
        var marker = GameObject.CreatePrimitive(type);
        marker.name = label;
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = size;
        // Host physics handles gameplay contacts; remote markers are presentation only.
        var collider = marker.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        return marker.transform;
    }

    private void PoseChanged(RigPose previous, RigPose current)
    {
        if (remote == null)
        {
            return;
        }

        if (!received)
        {
            remote[0].SetPositionAndRotation(current.head, current.headRotation);
            remote[1].SetPositionAndRotation(current.left, current.leftRotation);
            remote[2].SetPositionAndRotation(current.right, current.rightRotation);
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
                rightRotation = right.rotation
            };
        }
        else if (!IsOwner && remote != null)
        {
            var current = pose.Value;
            float t = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            Move(remote[0], current.head, current.headRotation, t);
            Move(remote[1], current.left, current.leftRotation, t);
            Move(remote[2], current.right, current.rightRotation, t);
        }
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
