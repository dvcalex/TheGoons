using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The host chooses the hand. The desktop owner moves its existing networked body.
[DefaultExecutionOrder(100)]
public sealed class PlayerGrab : NetworkBehaviour
{
    [Header("Throwing")]
    [Min(1f)] public float throwStrength = 1.8f;
    [Min(0f)] public float maximumThrowSpeed = 55f;
    [Tooltip("Extra palm offset in hand local space. Scales with the VR rig.")]
    public Vector3 palmOffset;

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
    private bool snappedToHand;
    private RigidbodyInterpolation torsoInterpolation;
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

    public void EndGrab(VrPhysicsHand hand, bool throwPlayer = true)
    {
        if (!IsServer || !IsSpawned || !IsHeld || serverHand != hand)
        {
            return;
        }

        Vector3 velocity = Vector3.zero;
        if (throwPlayer && hand != null && hand.Tracked && hand.isActiveAndEnabled)
        {
            Vector3 point = hand.transform.TransformPoint(state.Value.handOffset);
            velocity = CalculateThrowVelocity(hand.GetThrowVelocity(point));
        }

        state.Value = new GrabState
        {
            releaseVelocity = Vector3.ClampMagnitude(velocity, maximumThrowSpeed)
        };
        serverHand = null;
    }

    public Vector3 CalculateThrowVelocity(Vector3 handVelocity)
    {
        // Small movements release gently; a deliberate swing gets the extra strength.
        if (handVelocity.magnitude >= 2f)
        {
            handVelocity *= throwStrength;
        }

        return Vector3.ClampMagnitude(handVelocity, maximumThrowSpeed);
    }

    private void GrabChanged(GrabState previous, GrabState current)
    {
        snappedToHand = false;
        if (current.held)
        {
            RestoreHandCollisions();
            player._ragdollManager.BeginGrabRagdoll();
            Rigidbody body = player._ragdollManager.GrabBody;
            torsoInterpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
            UpdateHeldPose();
        }
        else if (previous.held)
        {
            // Give the thrown player a moment to leave the palm before colliding again.
            restoreCollisionsAt = Time.time + 0.35f;
            Rigidbody body = player._ragdollManager.GrabBody;
            body.isKinematic = !IsOwner;
            body.interpolation = torsoInterpolation;
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
            EndGrab(serverHand, false);
            return;
        }

        if (IsServer && (serverHand == null || !serverHand.isActiveAndEnabled || !serverHand.Tracked))
        {
            EndGrab(serverHand, false);
            return;
        }

        UpdateHeldPose();
    }

    private void LateUpdate()
    {
        // Run after remote hand interpolation so everyone sees the torso in the palm.
        if (IsSpawned && IsHeld)
        {
            UpdateHeldPose();
        }
    }

    private void UpdateHeldPose()
    {
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
        if (body == null)
        {
            return;
        }

        Vector3 target = hand.transform.TransformPoint(state.Value.handOffset);
        Quaternion rotation = hand.transform.rotation * state.Value.bodyRotationOffset;
        if (!snappedToHand)
        {
            // Move every bone together once so grabbing does not stretch the ragdoll.
            player._ragdollManager.SnapGrabPose(target, rotation);
            snappedToHand = true;
        }

        // A kinematic torso is a hard anchor. The other bodies keep their normal joints.
        body.isKinematic = true;
        body.position = target;
        body.rotation = rotation;
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
        RestoreHandCollisions();
    }

    private void OnDisable()
    {
        EndGrab(serverHand, false);
        RestoreHandCollisions();
    }
}
