using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The host chooses the hand. The desktop owner moves its existing networked body.
public sealed class PlayerGrab : NetworkBehaviour
{
    public float maximumThrowSpeed = 25f;
    [Tooltip("Extra palm offset in hand local space. Scales with the VR rig.")]
    public Vector3 palmOffset;
    public VrPidMotor followMotor = new VrPidMotor();

    public struct GrabState : INetworkSerializable
    {
        public bool held;
        public NetworkObjectReference holder;
        public bool leftHand;
        public Vector3 handOffset;
        public Quaternion bodyRotationOffset;
        public Vector3 releaseVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref held);
            serializer.SerializeValue(ref holder);
            serializer.SerializeValue(ref leftHand);
            serializer.SerializeValue(ref handOffset);
            serializer.SerializeValue(ref bodyRotationOffset);
            serializer.SerializeValue(ref releaseVelocity);
        }
    }

    private readonly NetworkVariable<GrabState> state = new NetworkVariable<GrabState>();
    private Player player;
    private VrPhysicsHand serverHand;
    private float nextHitTime;
    private VrPhysicsHand collisionHand;
    private float restoreCollisionsAt;
    private readonly List<CollisionPair> ignoredCollisions = new List<CollisionPair>();

    private struct CollisionPair
    {
        public Collider playerCollider;
        public Collider handCollider;
        public bool wasIgnored;
    }

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
        if (GameManager._instance != null && !GameManager._instance.IsSeeking)
        {
            return false;
        }

        if (!IsSpawned || !IsServer || IsHeld || player == null || hand == null || !hand.Tracked)
        {
            return false;
        }

        if (player._isDead || player._ragdollManager == null || player._ragdollManager.GrabBody == null)
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

        // Copy the animated pose before measuring the torso's orientation in the hand.
        player._ragdollManager.BeginGrabRagdoll();
        var holder = hand.GetComponentInParent<LocalVrPlayer>();
        serverHand = hand;
        state.Value = new GrabState
        {
            held = true,
            holder = new NetworkObjectReference(holder.NetworkObject),
            leftHand = hand == holder.GetHand(true),
            handOffset = hand.grabPoint + palmOffset,
            bodyRotationOffset = Quaternion.Inverse(hand.transform.rotation) * player._ragdollManager.GrabBody.rotation
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
        if (current.held)
        {
            RestoreHandCollisions();
            player._ragdollManager.BeginGrabRagdoll();
        }
        else if (previous.held)
        {
            // Give the thrown player a moment to leave the palm before colliding again.
            restoreCollisionsAt = Time.time + 0.35f;
            player._ragdollManager.SetReleaseVelocity(current.releaseVelocity);
        }

        if (!IsOwner)
        {
            return;
        }

        player._movement.ClearGrabMovement();
    }

    private void FixedUpdate()
    {
        if (!IsHeld && ignoredCollisions.Count > 0 && Time.time >= restoreCollisionsAt && HasClearedHand())
        {
            RestoreHandCollisions();
        }

        if (!IsSpawned || !IsHeld)
        {
            return;
        }

        if (IsServer && (player._isDead || (GameManager._instance != null && !GameManager._instance.IsSeeking)))
        {
            EndGrab(serverHand);
            return;
        }

        if (IsServer && (serverHand == null || !serverHand.isActiveAndEnabled || !serverHand.Tracked))
        {
            EndGrab(serverHand);
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

        IgnoreHandCollisions(hand);
        Rigidbody body = player._ragdollManager.GrabBody;
        if (!IsOwner || body == null || body.isKinematic)
        {
            return;
        }

        Vector3 target = hand.transform.TransformPoint(state.Value.handOffset);
        // Hold the torso in the palm; joints let the head and limbs flop naturally.
        Quaternion rotation = hand.transform.rotation * state.Value.bodyRotationOffset;
        followMotor.Drive(body, target, rotation, 1f, hand.WorldScale);
        player._ragdollManager.SupportGrabbedLimbs();
    }

    private void IgnoreHandCollisions(VrPhysicsHand hand)
    {
        if (collisionHand == hand)
        {
            return;
        }

        RestoreHandCollisions();
        collisionHand = hand;
        var playerColliders = GetComponentsInChildren<Collider>(true);
        var handColliders = hand.GetComponentsInChildren<Collider>(true);
        foreach (Collider playerCollider in playerColliders)
        {
            foreach (Collider handCollider in handColliders)
            {
                ignoredCollisions.Add(new CollisionPair
                {
                    playerCollider = playerCollider,
                    handCollider = handCollider,
                    wasIgnored = Physics.GetIgnoreCollision(playerCollider, handCollider)
                });
                Physics.IgnoreCollision(playerCollider, handCollider, true);
            }
        }
    }

    private void RestoreHandCollisions()
    {
        foreach (CollisionPair pair in ignoredCollisions)
        {
            if (pair.playerCollider != null && pair.handCollider != null)
            {
                Physics.IgnoreCollision(pair.playerCollider, pair.handCollider, pair.wasIgnored);
            }
        }

        ignoredCollisions.Clear();
        collisionHand = null;
    }

    private bool HasClearedHand()
    {
        foreach (CollisionPair pair in ignoredCollisions)
        {
            if (pair.playerCollider == null || pair.handCollider == null ||
                !pair.playerCollider.enabled || !pair.handCollider.enabled)
            {
                continue;
            }

            Vector3 direction;
            float distance;
            if (Physics.ComputePenetration(
                pair.playerCollider, pair.playerCollider.transform.position, pair.playerCollider.transform.rotation,
                pair.handCollider, pair.handCollider.transform.position, pair.handCollider.transform.rotation,
                out direction, out distance))
            {
                return false;
            }
        }

        return true;
    }

    // Only host hand contacts can send a knockback to the movement owner.
    public void Hit(Vector3 velocity, float collisionImpulse)
    {
        if (GameManager._instance != null && !GameManager._instance.IsSeeking)
        {
            return;
        }

        if (!IsSpawned || !IsServer || IsHeld || Time.time < nextHitTime || velocity.magnitude < 1f)
        {
            return;
        }

        nextHitTime = Time.time + 0.25f;

        // Use the movement body's mass on every peer, even when its replica is kinematic.
        float impulseDeltaV = collisionImpulse / Mathf.Max(player._movement.rb.mass, 0.0001f);
        float damage = player.CalculateImpactDamage(velocity.magnitude, impulseDeltaV);
        player.Damage(damage);

        HitOwnerRpc(Vector3.ClampMagnitude(velocity, maximumThrowSpeed));
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void HitOwnerRpc(Vector3 velocity)
    {
        if (IsHeld)
        {
            return;
        }

        if (player._ragdollManager.isRagdoll)
        {
            player._ragdollManager.SetReleaseVelocity(player._ragdollManager.GrabBody.linearVelocity + velocity);
            return;
        }

        player._movement.AddKnockback(velocity.normalized, velocity.magnitude, true, 0.25f);
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= GrabChanged;
        serverHand = null;
        followMotor.Reset();
        RestoreHandCollisions();
    }

    private void OnDisable()
    {
        EndGrab(serverHand);
        RestoreHandCollisions();
    }
}
