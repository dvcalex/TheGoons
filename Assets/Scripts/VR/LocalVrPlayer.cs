using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>Local tracking plus replicated world-space poses for the VR host.</summary>
public sealed class LocalVrPlayer : NetworkBehaviour
{
    public XROrigin origin;

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
    private Transform[] remote;
    private float nextSend;
    private bool received;

    private void Awake()
    {
        if (origin != null)
        {
            origin.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (origin == null)
        {
            return;
        }

        origin.gameObject.SetActive(IsOwner);
        var offset = origin.CameraFloorOffsetObject.transform;
        left = offset.Find("Left Controller");
        right = offset.Find("Right Controller");
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
        // Markers only depict the host; gameplay collisions come from the future character rig.
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
                head = origin.Camera.transform.position,
                headRotation = origin.Camera.transform.rotation,
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
    }
}
