using Unity.Netcode;
using UnityEngine;

// The host chooses the hand. The desktop owner moves its existing networked body.
public sealed class PlayerGrab : NetworkBehaviour
{
    public float maximumThrowSpeed = 25f;
    public VrPidMotor followMotor = new VrPidMotor();

    public struct GrabState : INetworkSerializable
    {
        public bool held;
        public NetworkObjectReference holder;
        public bool leftHand;
        public Vector3 handOffset;
        public Vector3 releaseVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref held);
            serializer.SerializeValue(ref holder);
            serializer.SerializeValue(ref leftHand);
            serializer.SerializeValue(ref handOffset);
            serializer.SerializeValue(ref releaseVelocity);
        }
    }

    private readonly NetworkVariable<GrabState> state = new NetworkVariable<GrabState>();
    private Player player;
    private VrPhysicsHand serverHand;
    private float nextHitTime;

    public bool IsHeld
    {
        get
        {
            return state.Value.held;
        }
    }

    private void Awake()
    {
        player = GetComponent<Player>();
    }

    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += GrabChanged;
        GrabChanged(default(GrabState), state.Value);
    }

    public bool CanGrab(VrPhysicsHand hand)
    {
        if (!IsSpawned || !IsServer || IsHeld || player == null || hand == null || !hand.Tracked)
        {
            return false;
        }

        if (player._ragdollManager != null && player._ragdollManager.isRagdoll)
        {
            return false;
        }

        var holder = hand.GetComponentInParent<LocalVrPlayer>();
        if (holder == null || !holder.IsSpawned || !holder.IsServer || !holder.IsOwner)
        {
            return false;
        }

        return true;
    }

    public bool BeginGrab(VrPhysicsHand hand)
    {
        if (!CanGrab(hand))
        {
            return false;
        }

        var holder = hand.GetComponentInParent<LocalVrPlayer>();
        serverHand = hand;
        state.Value = new GrabState
        {
            held = true,
            holder = new NetworkObjectReference(holder.NetworkObject),
            leftHand = hand == holder.GetHand(true),
            handOffset = hand.transform.InverseTransformPoint(player._movement.rb.position)
        };
        return true;
    }

    public void EndGrab(VrPhysicsHand hand)
    {
        if (!IsServer || !IsSpawned || !IsHeld || serverHand != hand)
        {
            return;
        }

        Vector3 velocity = Vector3.zero;
        if (hand != null && hand.Body != null)
        {
            Vector3 point = hand.transform.TransformPoint(state.Value.handOffset);
            velocity = hand.Body.GetPointVelocity(point);
        }

        state.Value = new GrabState
        {
            releaseVelocity = Vector3.ClampMagnitude(velocity, maximumThrowSpeed)
        };
        serverHand = null;
    }

    private void GrabChanged(GrabState previous, GrabState current)
    {
        followMotor.Reset();
        if (!IsOwner)
        {
            return;
        }

        player._movement.ClearGrabMovement();
        if (previous.held && !current.held && !player._movement.rb.isKinematic)
        {
            player._movement.rb.linearVelocity = current.releaseVelocity;
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsHeld)
        {
            return;
        }

        if (IsServer && (serverHand == null || !serverHand.isActiveAndEnabled || !serverHand.Tracked))
        {
            EndGrab(serverHand);
            return;
        }

        if (!IsOwner || player._movement.rb.isKinematic)
        {
            return;
        }

        NetworkObject holderObject;
        if (!state.Value.holder.TryGet(out holderObject, NetworkManager))
        {
            return;
        }

        var holder = holderObject.GetComponent<LocalVrPlayer>();
        if (holder == null)
        {
            return;
        }

        var hand = holder.GetHand(state.Value.leftHand);
        if (hand == null)
        {
            return;
        }

        Vector3 target = hand.transform.TransformPoint(state.Value.handOffset);
        Quaternion rotation = Quaternion.Euler(0f, player._movement.FacingYaw, 0f);
        followMotor.Drive(player._movement.rb, target, rotation, 1f, hand.WorldScale);
    }

    // Only host hand contacts can send a knockback to the movement owner.
    public void Hit(Vector3 velocity)
    {
        if (!IsSpawned || !IsServer || IsHeld || Time.time < nextHitTime || velocity.magnitude < 1f)
        {
            return;
        }

        nextHitTime = Time.time + 0.25f;
        HitOwnerRpc(Vector3.ClampMagnitude(velocity, maximumThrowSpeed));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void HitOwnerRpc(Vector3 velocity)
    {
        if (!IsHeld)
        {
            player._movement.AddKnockback(velocity.normalized, velocity.magnitude, true, 0.25f);
        }
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= GrabChanged;
        serverHand = null;
        followMotor.Reset();
    }
}
