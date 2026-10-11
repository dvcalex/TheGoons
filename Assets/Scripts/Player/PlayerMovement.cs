using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Input requests velocity; only forces move the owner. Replicate root pose, not limbs.
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("References")]
    public Player _player;
    public Rigidbody rb;
    public CapsuleCollider capsule;
    public PlayerMouseLook mouseLook;
    public Transform playerGFX;
    public Transform groundCheck;
    public Animator anim;
    private float currentAnimationSpeed = 0f;
    private float desiredAnimationSpeed = 0f;

    [Header("Movement feel")]
    public float walkSpeed = 4.8f;
    public float crouchWalkSpeed = 2.4f;
    public float runSpeed = 7.2f;
    public float runSpeedAir = 7.2f;
    public float groundAcceleration = 48f;
    public float airAcceleration = 12f;
    public float groundDeceleration = 65f;
    public float directionChangeAcceleration = 75f;

    [Header("Dash")]
    public float dashSpeed = 35f;
    public float dashDuration = 0.10f;
    public float dashBrake = 180f;
    public float dashCooldownTime = 3f;
    private float dashCooldownTimer = 0f;
    private float dashTimer;
    private Vector3 dashDirection;

    [Header("Dash FOV")]
    public float dashFOVBoost = 25f;
    public float dashFOVInSpeed = 35f;
    public float dashFOVOutSpeed = 10f;
    private float currentDashFOV;

    [Space]
    [Tooltip("Velocity servo response in 1/seconds. Higher is snappier.")]
    public float velocityGain = 18f;
    [Tooltip("Damping on measured acceleration, not input changes (no derivative kick).")]
    public float velocityDamping = 0.08f;
    [Tooltip("Zero by default: integral wind-up makes pushing walls feel bad.")]
    public float velocityIntegralGain = 0f;
    public float facingSpring = 110f;
    public float facingDamping = 20f;

    [Header("Optional stamina")]
    public bool useStamina;
    public float maxStamina = 1f;
    public float staminaCost = 0.33f;
    public float staminaGain = 0.4f;
    public float staminaRechargeThreshold = 0.25f;
    public float intialRechargeWait = 0.5f;

    [Header("Small jump")]
    public float gravity = -22f;
    public float fallingGravityWeight = 1.65f;
    public float releasedJumpGravityWeight = 2.2f;
    public float jumpHeight = 0.85f;
    public float groundedCoyoteTime = 0.12f;
    public float jumpBufferTime = 0.14f;

    [Header("Ground")]
    [Tooltip("Probe distance below the capsule feet; separate from coyote time.")]
    public float groundDistance = 0.12f;
    public LayerMask groundMask = ~0;
    public bool isGrounded;
    public float maximumGroundAngle = 50f;
    public float groundStickAcceleration = 12f;

    [Header("Crouching")]
    public float crouchHeight = 1.1f;
    public float standingHeight = 2f;
    public float crouchSpeed = 7f;
    public LayerMask ceilingMask = ~0;

    [Header("State")]
    public bool walking, running, aiming, crouching, isMoving, hasKnockback, isStunned;
    public float currentChargeState;

    [Header("Animation (optional)")]
    public string moveXVar = "MoveX", moveZVar = "MoveZ", jumpTrigger = "Jump";
    public float animChangeSpeed = 10f;
    public List<Knockback> _knockbacks = new List<Knockback>();
    public float CurrentStamina
    {
        get
        {
            return currentStamina;
        }
    }

    public float CrouchAmount
    {
        get
        {
            return Mathf.InverseLerp(standingHeight, crouchHeight, capsule.height);
        }
    }

    public float FacingYaw
    {
        get;
        private set;
    }

    public Vector3 GroundVelocity
    {
        get;
        private set;
    }

    public Vector3 RelativeVelocity
    {
        get
        {
            return rb.linearVelocity - GroundVelocity;
        }
    }

    public bool HasControl
    {
        get
        {
            return IsOwner && _player != null && !IsGrabbed && !_player._ragdollManager.isRagdoll;
        }
    }

    public bool IsGrabbed
    {
        get
        {
            return grab != null && grab.IsHeld;
        }
    }

    public float HeadPitch
    {
        get
        {
            if (IsOwner && mouseLook != null)
            {
                return mouseLook.Pitch;
            }

            return remotePitch;
        }
    }

    // Seven payload bytes. Continuous yaw preserves the direction of fast turns
    // across wraparound and skipped samples; no per-limb transforms are sent.
    public struct PoseState : INetworkSerializable, System.IEquatable<PoseState>
    {
        public float Yaw;
        public byte Crouch;
        public byte Flags;
        public sbyte Pitch;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Crouch);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref Pitch);
        }

        public bool Equals(PoseState other)
        {
            return Yaw == other.Yaw && Crouch == other.Crouch && Flags == other.Flags && Pitch == other.Pitch;
        }
    }

    private readonly NetworkVariable<PoseState> networkPose = new NetworkVariable<PoseState>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private float continuousYaw;
    private float sampledYaw;
    private float remoteYaw;
    private float remotePitch;
    private float poseSendTimer;

    public override void OnNetworkSpawn()
    {
        sampledYaw = rb.rotation.eulerAngles.y;
        continuousYaw = sampledYaw;

        if (IsOwner)
        {
            PublishPose();
        }
        else
        {
            remoteYaw = networkPose.Value.Yaw;
            ApplyRemotePose(1f);
        }
    }

    private void PublishPose()
    {
        networkPose.Value = new PoseState
        {
            Yaw = Mathf.Round(continuousYaw * 2f) * 0.5f,
            Crouch = (byte)Mathf.RoundToInt(CrouchAmount * 255f),
            Flags = (byte)((isGrounded ? 1 : 0) | (isStunned ? 2 : 0)),
            Pitch = (sbyte)Mathf.RoundToInt(Mathf.Clamp(HeadPitch, -90f, 90f) * 127f / 90f)
        };
    }

    private void ApplyRemotePose(float blend)
    {
        var pose = networkPose.Value;

        // Interpolate the unwrapped scalar, never choose a quaternion's shortest
        // path between snapshots that may span more than half a turn.
        // Bound each physics step so even a long delivery gap cannot make the
        // cosmetic kinematic anchor take the opposite quaternion path.
        remoteYaw += Mathf.Clamp((pose.Yaw - remoteYaw) * blend, -90f, 90f);
        remotePitch = Mathf.Lerp(remotePitch, pose.Pitch * 90f / 127f, blend);
        transform.rotation = Quaternion.Euler(0f, remoteYaw, 0f);
        capsule.height = Mathf.Lerp(capsule.height, Mathf.Lerp(standingHeight, crouchHeight, pose.Crouch / 255f), blend);
        capsule.center = standingCenter - Vector3.up * ((standingHeight - capsule.height) * 0.5f);
        crouching = pose.Crouch > 127;
        isGrounded = (pose.Flags & 1) != 0;
        isStunned = (pose.Flags & 2) != 0;
    }

    private Vector2 moveInput;
    private bool crouchInput;
    private bool runInput;
    private bool jumpHeld;
    private bool locked;
    private bool jumpConsumed;
    private bool rechargingStamina;
    private float currentStamina;
    private float rechargeTimer;
    private float coyoteLeft;
    private float jumpBufferLeft;
    private float ignoreGroundLeft;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 standingCenter;
    private Vector3 originalGFXPosition;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private readonly Collider[] clearanceHits = new Collider[16];
    private readonly VelocityServo servo = new VelocityServo();
    private InputAction moveAction;
    private InputAction crouchAction;
    private InputAction runAction;
    private InputAction jumpAction;
    private DesktopRagdoll ragdoll;
    private PlayerGrab grab;

    private void Awake()
    {
        rb = rb != null ? rb : GetComponent<Rigidbody>();
        capsule = capsule != null ? capsule : GetComponent<CapsuleCollider>();
        ragdoll = GetComponent<DesktopRagdoll>();
        grab = GetComponentInParent<PlayerGrab>();
        rb.useGravity = false;

        // Stable locomotion body; the attached ragdoll supplies the wobble.
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.solverIterations = 8;
        rb.solverVelocityIterations = 4;
        standingCenter = capsule.center;
        capsule.height = standingHeight;
        currentStamina = maxStamina;
        FacingYaw = rb.rotation.eulerAngles.y;

        if (playerGFX != null)
        {
            originalGFXPosition = playerGFX.localPosition;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_player != null)
        {
            _player.ApplyImpact(collision);
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        //Debug.Log("Ragdoll State: " + Player.me._ragdollManager.isRagdoll);
        anim.SetBool("Enabled", !Player.me._ragdollManager.isRagdoll);

        if (!HasControl || _player._input == null || !_player._input.enabled || Player.me._ragdollManager.isRagdoll || Player.me._isDead)
        {
            moveInput = Vector2.zero;
            return;
        }

        if (moveAction == null)
        {
            var actions = _player._input.actions;
            moveAction = actions.FindAction("Move");
            crouchAction = actions.FindAction("Crouch");
            runAction = actions.FindAction("Run");
            jumpAction = actions.FindAction("Jump");
        }

        moveInput = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        crouchInput = crouchAction.IsPressed();

        if (crouchAction.WasPressedThisFrame())
        {
            Player.me.TURDMECHANICWHATTHEFUCKKKKKKKKKHOOLYSHITTURDMECHANIC();
        }

        runInput = runAction.IsPressed();
        jumpHeld = jumpAction.IsPressed();

        if (Player.me._input.actions["Dash"].WasPressedThisFrame())
        {
            Dash();
        }

        if (!locked && !isStunned && jumpAction.WasPressedThisFrame())
        {
            jumpBufferLeft = jumpBufferTime;
        }

        if (anim != null)
        {
            //Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, FacingYaw, 0f)) * RelativeVelocity;
            //anim.SetFloat(moveXVar, local.x / walkSpeed, 1f / animChangeSpeed, Time.deltaTime);
            //anim.SetFloat(moveZVar, local.z / walkSpeed, 1f / animChangeSpeed, Time.deltaTime);
            //anim.SetBool("IsGrounded", isGrounded);
            anim.SetBool("Crouching", crouching);
        }

        if (runAction.IsPressed())
        {
            desiredAnimationSpeed = 1f;
        }
        else if (moveInput.magnitude != 0f)
        {
            desiredAnimationSpeed = 0.5f;
        }
        else
        {
            desiredAnimationSpeed = 0f;
        }

        currentAnimationSpeed = Mathf.Lerp(
            currentAnimationSpeed,
            desiredAnimationSpeed,
            1f - Mathf.Exp(-animChangeSpeed * Time.deltaTime)
        );
        anim.SetFloat("Speed", currentAnimationSpeed);
    }

    private void FixedUpdate()
    {
        if (IsSpawned && !IsOwner)
        {
            ApplyRemotePose(1f - Mathf.Exp(-30f * Time.fixedDeltaTime));
        }

        if (IsOwner && (IsGrabbed || _player._ragdollManager.isRagdoll))
        {
            PublishPoseIfNeeded(Time.fixedDeltaTime);
            return;
        }

        if (!HasControl || rb.isKinematic)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        dashCooldownTimer = Mathf.Max(0f, dashCooldownTimer - dt);

        CheckGrounded(dt);
        ProcessKnockback();

        if (!locked && !isStunned)
        {
            UpdateCrouching(dt);
            UpdateMovementState(dt);
            ApplyMovement(dt);
            ProcessJump();

            float yawError = Mathf.DeltaAngle(rb.rotation.eulerAngles.y, FacingYaw) * Mathf.Deg2Rad;
            rb.AddTorque(
                Vector3.up * Mathf.Clamp(yawError * facingSpring - rb.angularVelocity.y * facingDamping, -180f, 180f),
                ForceMode.Acceleration
            );
        }
        else
        {
            walking = running = isMoving = false;
            jumpBufferLeft = 0f;
            servo.Reset();
        }

        jumpBufferLeft = Mathf.Max(0f, jumpBufferLeft - dt);
        float weight = rb.linearVelocity.y < 0f
            ? fallingGravityWeight
            : (!jumpHeld && jumpConsumed ? releasedJumpGravityWeight : 1f);
        rb.AddForce(Vector3.up * gravity * weight, ForceMode.Acceleration);

        if (isGrounded && !jumpConsumed)
        {
            rb.AddForce(-groundNormal * groundStickAcceleration, ForceMode.Acceleration);
        }

        if (mouseLook != null)
        {
            float targetDashFOV = dashTimer > 0f ? dashFOVBoost : 0f;
            float speed = dashTimer > 0f ? dashFOVInSpeed : dashFOVOutSpeed;
            currentDashFOV = Mathf.MoveTowards(currentDashFOV, targetDashFOV, speed * Time.fixedDeltaTime);

            float movementFOV = Mathf.Clamp01(RelativeVelocity.magnitude / runSpeed) * 6f;
            mouseLook.SetZoom(mouseLook.defaultFOV + movementFOV + currentDashFOV);
        }

        PublishPoseIfNeeded(dt);
    }

    private void PublishPoseIfNeeded(float dt)
    {
        if (IsSpawned)
        {
            float yaw = rb.rotation.eulerAngles.y;
            continuousYaw += Mathf.DeltaAngle(sampledYaw, yaw);
            sampledYaw = yaw;
            poseSendTimer -= dt;

            if (poseSendTimer <= 0f)
            {
                PublishPose();
                poseSendTimer = 0.05f; // At most 20 updates/sec; unchanged values are not sent.
            }
        }
    }

    private bool IsSelf(Collider other)
    {
        return other.transform.IsChildOf(transform);
    }

    private void CheckGrounded(float dt)
    {
        ignoreGroundLeft = Mathf.Max(0f, ignoreGroundLeft - dt);

        // Interpolated Transform positions can lag the physics pose by a tick.
        Vector3 center = rb.position + rb.rotation * Vector3.Scale(capsule.center, transform.lossyScale);
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float halfHeight = Mathf.Max(radius, capsule.height * Mathf.Abs(transform.lossyScale.y) * 0.5f);
        float probeRadius = radius * 0.9f;
        Vector3 origin = center - Vector3.up * (halfHeight - radius) + Vector3.up * 0.08f;
        int count = gameObject.scene.GetPhysicsScene().SphereCast(
            origin,
            probeRadius,
            Vector3.down,
            groundHits,
            0.08f + radius - probeRadius + groundDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
        float nearest = float.PositiveInfinity;
        isGrounded = false;
        GroundVelocity = Vector3.zero;
        groundNormal = Vector3.up;

        for (int i = 0; i < count && ignoreGroundLeft <= 0f; i++)
        {
            var hit = groundHits[i];

            if (IsSelf(hit.collider) || hit.distance >= nearest || Vector3.Dot(hit.normal, Vector3.up) < Mathf.Cos(maximumGroundAngle * Mathf.Deg2Rad))
            {
                continue;
            }

            Vector3 supportVelocity = hit.rigidbody != null ? hit.rigidbody.GetPointVelocity(hit.point) : Vector3.zero;

            if (rb.linearVelocity.y - supportVelocity.y > 1.5f)
            {
                continue;
            }

            nearest = hit.distance;
            isGrounded = true;
            groundNormal = hit.normal;
            GroundVelocity = supportVelocity;
        }

        if (isGrounded)
        {
            coyoteLeft = groundedCoyoteTime;
            jumpConsumed = false;
        }
        else
        {
            coyoteLeft = Mathf.Max(0f, coyoteLeft - dt);
        }
    }

    private void UpdateMovementState(float dt)
    {
        isMoving = moveInput.sqrMagnitude > 0.001f;
        running = runInput && isMoving && !crouching && !aiming && (!useStamina || (currentStamina > 0f && !rechargingStamina));

        if (useStamina)
        {
            if (running)
            {
                currentStamina = Mathf.Max(0f, currentStamina - staminaCost * dt);
                rechargeTimer = intialRechargeWait;

                if (currentStamina <= 0f)
                {
                    rechargingStamina = true;
                    running = false;
                }
            }
            else
            {
                rechargeTimer -= dt;

                if (rechargeTimer <= 0f)
                {
                    currentStamina = Mathf.Min(maxStamina, currentStamina + staminaGain * dt);
                }

                if (currentStamina >= staminaRechargeThreshold)
                {
                    rechargingStamina = false;
                }
            }
        }

        walking = isGrounded && isMoving && !running && !crouching;
    }

    public void Dash()
    {
        // Check dashCooldownTimer instead of dashTimer so it respects the cooldown
        if (!HasControl || locked || isStunned || dashCooldownTimer > 0f)
        {
            return;
        }

        dashDirection = Quaternion.Euler(0f, FacingYaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);

        if (dashDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        dashDirection.Normalize();
        Vector3 velocity = rb.linearVelocity;
        Vector3 targetVelocity = dashDirection * dashSpeed;
        Vector3 velocityChange = targetVelocity - new Vector3(velocity.x, 0f, velocity.z);
        rb.AddForce(velocityChange, ForceMode.VelocityChange);

        Player.me.ShakeCamera(1f, 3f, 0.1f, 0.5f);

        if (ragdoll != null)
        {
            ragdoll.AddVelocityChange(velocityChange);
        }

        dashTimer = dashDuration;

        // ---> TRIGGER THE COOLDOWN HERE <---
        dashCooldownTimer = dashCooldownTime;
        servo.Reset();

        // Notify your UI Manager (ensure your UI script class name matches this)
        if (Player.me._logic != null)
        {
            Player.me._logic.TriggerDashCooldown();
        }
    }

    private void ApplyMovement(float dt)
    {
        if (dashTimer > 0f)
        {
            dashTimer -= dt;

            if (dashTimer <= 0f)
            {
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
                Vector3 targetVelocity = dashDirection * walkSpeed;
                Vector3 change = targetVelocity - horizontalVelocity;
                rb.AddForce(change, ForceMode.VelocityChange);

                if (ragdoll != null)
                {
                    ragdoll.AddVelocityChange(change);
                }

                servo.Reset();
            }

            return;
        }

        Vector3 direction = Quaternion.Euler(0f, FacingYaw, 0f) * new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 planeNormal = isGrounded ? groundNormal : Vector3.up;

        if (isGrounded)
        {
            direction = Vector3.ProjectOnPlane(direction, planeNormal).normalized * moveInput.magnitude;
        }

        float speed = crouching ? crouchWalkSpeed : (running ? (isGrounded ? runSpeed : runSpeedAir) : walkSpeed);

        if (aiming)
        {
            speed *= Mathf.Clamp01(1.65f - currentChargeState);
        }

        Vector3 velocity = Vector3.ProjectOnPlane(RelativeVelocity, planeNormal);

        // No input in air preserves momentum. Input steers without braking inherited jump speed.
        if (!isGrounded && !isMoving)
        {
            servo.Reset();
            return;
        }

        Vector3 target = direction * speed;

        if (!isGrounded && velocity.magnitude > speed)
        {
            target = direction.normalized * velocity.magnitude * moveInput.magnitude;
        }

        float limit = !isGrounded ? airAcceleration : !isMoving ? groundDeceleration : Vector3.Dot(direction, velocity) < 0f ? directionChangeAcceleration : groundAcceleration;

        if (hasKnockback)
        {
            limit *= 0.3f;
        }

        Vector3 acceleration = servo.Step(velocity, target, velocityGain, velocityIntegralGain, velocityDamping, limit, dt);
        rb.AddForce(Vector3.ProjectOnPlane(acceleration, planeNormal), ForceMode.Acceleration);
    }

    private void ProcessJump()
    {
        if (jumpBufferLeft <= 0f || jumpConsumed || (!isGrounded && coyoteLeft <= 0f) || crouching)
        {
            return;
        }

        jumpBufferLeft = coyoteLeft = 0f;
        ignoreGroundLeft = 0.16f;
        jumpConsumed = true;
        isGrounded = false;
        float desired = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
        float change = Mathf.Max(0f, desired - (rb.linearVelocity.y - GroundVelocity.y));

        // Give every ragdoll body the same jump impulse so joints don't absorb takeoff.
        Vector3 impulse = Vector3.up * change;
        rb.AddForce(impulse, ForceMode.VelocityChange);

        if (ragdoll != null)
        {
            ragdoll.AddVelocityChange(impulse);
        }

        if (anim != null)
        {
            anim.SetTrigger(jumpTrigger);
        }

        servo.Reset();
    }

    private void UpdateCrouching(float dt)
    {
        crouching = crouchInput || !CanStandUp();
        float height = Mathf.MoveTowards(capsule.height, crouching ? crouchHeight : standingHeight, crouchSpeed * dt);
        capsule.height = Mathf.Max(capsule.radius * 2f, height);
        capsule.center = standingCenter - Vector3.up * ((standingHeight - capsule.height) * 0.5f);

        if (ragdoll == null && playerGFX != null)
        {
            playerGFX.localPosition = originalGFXPosition - Vector3.up * ((standingHeight - height) * 0.5f);
        }
    }

    private bool CanStandUp()
    {
        if (capsule.height >= standingHeight - 0.001f)
        {
            return true;
        }

        float radius = capsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * 0.96f;
        float halfHeight = Mathf.Max(radius, standingHeight * Mathf.Abs(transform.lossyScale.y) * 0.5f - 0.02f);
        Vector3 center = rb.position + rb.rotation * Vector3.Scale(standingCenter, transform.lossyScale);
        int count = gameObject.scene.GetPhysicsScene().OverlapCapsule(
            center - Vector3.up * (halfHeight - radius),
            center + Vector3.up * (halfHeight - radius),
            radius,
            clearanceHits,
            ceilingMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            if (!IsSelf(clearanceHits[i]))
            {
                return false;
            }
        }

        return count < clearanceHits.Length; // A saturated query must never allow standing through a ceiling.
    }

    public void SetFacingYaw(float yaw)
    {
        FacingYaw = Mathf.Repeat(yaw, 360f);
    }

    public void ProcessKnockback()
    {
        hasKnockback = _knockbacks.Count > 0;
        isStunned = false;

        for (int i = _knockbacks.Count - 1; i >= 0; i--)
        {
            var knockback = _knockbacks[i];

            if (!knockback.applied)
            {
                Vector3 impulse = knockback.direction.normalized * knockback.intensity;
                rb.AddForce(impulse, ForceMode.VelocityChange);

                if (ragdoll != null)
                {
                    ragdoll.AddVelocityChange(impulse);
                }

                knockback.applied = true;
                servo.Reset();
            }

            isStunned |= knockback.stun;
            knockback.time -= Time.fixedDeltaTime;

            if (knockback.time <= 0f)
            {
                _knockbacks.RemoveAt(i);
            }
        }
    }

    public void AddKnockback(
        Vector3 direction,
        float intensity = 2f,
        bool stunPlayer = false,
        float time = 0.25f,
        float fadeOutTime = 0.15f
    )
    {
        _knockbacks.Add(new Knockback(direction, intensity, stunPlayer, time, fadeOutTime));
    }

    public void AddKnockback(Vector3 direction, Knockback knockback)
    {
        AddKnockback(direction, knockback.intensity, knockback.stun, knockback.time, knockback.fadeOutTime);
    }

    public void ResetVelocity()
    {
        rb.linearVelocity = rb.angularVelocity = Vector3.zero;
        servo.Reset();

        if (ragdoll != null)
        {
            ragdoll.ResetVelocity();
        }
    }

    public void MoveTo(Vector3 position)
    {
        if (ragdoll != null && ragdoll.Bodies != null)
        {
            ragdoll.MoveRootTo(position);
        }
        else
        {
            rb.position = position;
        }

        ResetVelocity();
        coyoteLeft = jumpBufferLeft = 0f;
        isGrounded = false;
        GroundVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    public void ClearGrabMovement()
    {
        moveInput = Vector2.zero;
        walking = running = isMoving = false;
        jumpBufferLeft = coyoteLeft = dashTimer = 0f;
        isGrounded = false;
        GroundVelocity = Vector3.zero;
        currentAnimationSpeed = desiredAnimationSpeed = 0f;
        if (anim != null)
        {
            anim.SetFloat("Speed", 0f);
        }

        _knockbacks.Clear();
        servo.Reset();
    }

    public void SetLock(bool state)
    {
        locked = state;
        moveInput = Vector2.zero;
        jumpBufferLeft = 0f;
        servo.Reset();
    }

    public bool IsLocked()
    {
        return locked;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            moveInput = Vector2.zero;
            runInput = crouchInput = jumpHeld = false;
            jumpBufferLeft = 0f;
        }
    }
}

// Adapted from the Jalopy-Jetstream PID approach: derivative on measurement,
// filtered collision spikes and conditional integration to prevent wall wind-up.
public sealed class VelocityServo
{
    private Vector3 previous;
    private Vector3 derivative;
    private Vector3 integral;
    private bool initialized;

    public Vector3 Step(Vector3 current, Vector3 target, float p, float i, float d, float limit, float dt)
    {
        if (dt <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 error = target - current;
        Vector3 measured = initialized ? (current - previous) / dt : Vector3.zero;
        derivative = Vector3.Lerp(derivative, measured, dt / (0.08f + dt));
        previous = current;
        initialized = true;
        Vector3 candidate = Vector3.ClampMagnitude(integral + error * dt, 2f);
        Vector3 output = p * error + i * candidate - d * derivative;

        if (output.magnitude <= limit)
        {
            integral = candidate;
        }
        else
        {
            integral = Vector3.zero;
        }

        // Limit both force and one-tick overshoot; no direct writes to velocity.
        return Vector3.ClampMagnitude(output, Mathf.Min(limit, error.magnitude / dt));
    }

    public void Reset()
    {
        initialized = false;
        previous = derivative = integral = Vector3.zero;
    }
}

[System.Serializable]
public class Knockback
{
    public Knockback(
        Vector3 direction,
        float intensity = 2f,
        bool stunPlayer = false,
        float time = 1f,
        float fadeOutTime = 0.5f
    )
    {
        this.direction = direction;
        this.intensity = intensity;
        stun = stunPlayer;
        this.time = time;
        this.fadeOutTime = fadeOutTime;
    }

    public float intensity;
    public float time;
    public float fadeOutTime;
    public bool stun;
    [HideInInspector]
    public Vector3 direction;
    [HideInInspector]
    public bool applied;
}
