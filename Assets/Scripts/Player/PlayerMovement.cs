using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
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

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float crouchWalkSpeed = 2f;
    public float runSpeed = 8f;
    public float runSpeedAir = 8f;

    [Tooltip("How quickly the player reaches the target ground velocity.")]
    public float groundAcceleration = 45f;

    [Tooltip("How quickly the player changes velocity in the air.")]
    public float airAcceleration = 15f;

    [Tooltip("Deceleration when no movement input is held.")]
    public float groundDeceleration = 55f;

    [Tooltip("Limits how strongly movement corrects external forces.")]
    public float maximumVelocityChange = 8f;

    [Range(0f, 1f)]
    public float runSpeedLossPercent = 0.8f;

    [Header("Stamina")]
    public float maxStamina = 1f;
    public float staminaCost = 0.33f;
    public float staminaGain = 0.4f;
    public float staminaRechargeThreshold = 0.25f;
    public float intialRechargeWait = 0.5f;

    [Header("Jumping and Gravity")]
    public float gravity = -9.81f;
    public float fallingGravityWeight = 1.5f;
    public float jumpHeight = 3f;
    public float groundedCoyoteTime = 0.2f;
    public float jumpBufferTime = 0.1f;

    [Header("Ground Check")]
    public float groundDistance = 0.4f;
    public LayerMask groundMask;
    public bool isGrounded;
    public float maximumGroundAngle = 60f;

    [Header("Crouching")]
    public float crouchHeight = 1f;
    public float standingHeight = 2f;
    public float crouchSpeed = 8f;
    public float playerGFXCrouchMultiplier = 0.25f;
    public LayerMask ceilingMask;

    [Header("State")]
    public bool walking;
    public bool running;
    public bool aiming;
    public bool crouching;
    public bool isMoving;
    public bool hasKnockback;
    public bool isStunned;
    public float currentChargeState;

    [Header("Animation")]
    public string moveXVar = "MoveX";
    public string moveZVar = "MoveZ";
    public string jumpTrigger = "Jump";
    public float animChangeSpeed = 5f;

    public List<Knockback> _knockbacks = new List<Knockback>();

    public float CurrentStamina => currentStamina;

    private Vector2 moveInput;
    private bool crouchInput;
    private bool runInput;
    private bool jumpQueued;
    private bool locked;

    private float currentStamina;
    private float rechargeTimer;
    private float groundedTimeLeft;
    private float jumpBufferLeft;
    private float lerpedX;
    private float lerpedZ;
    private float currentColliderHeight;

    private bool rechargingStamina;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 standingColliderCenter;
    private Vector3 originalGFXPosition;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (capsule == null)
            capsule = GetComponent<CapsuleCollider>();

        // The script supplies its own gravity.
        rb.useGravity = false;

        // Prevent the capsule from tipping over.
        rb.constraints |= RigidbodyConstraints.FreezeRotationX;
        rb.constraints |= RigidbodyConstraints.FreezeRotationZ;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        standingColliderCenter = capsule.center;
        currentColliderHeight = standingHeight;
        currentStamina = maxStamina;

        if (playerGFX != null)
            originalGFXPosition = playerGFX.localPosition;
    }

    private void Start()
    {
        groundedTimeLeft = groundedCoyoteTime;

        capsule.height = standingHeight;
        UpdateColliderCenter(standingHeight);
    }

    private void Update()
    {
        if (!IsOwner || _player == null || rb == null)
            return;

        ReadInput();
        UpdateAnimation();
    }

    private void FixedUpdate()
    {
        if (!IsOwner || _player == null || rb == null)
            return;

        float deltaTime = Time.fixedDeltaTime;

        CheckGrounded(deltaTime);
        ProcessKnockback();

        if (locked || isStunned)
        {
            walking = false;
            running = false;
            isMoving = false;

            ApplyGravity(deltaTime);
            return;
        }

        UpdateCrouching(deltaTime);
        UpdateMovementState(deltaTime);
        ApplyMovement(deltaTime);
        ProcessJump(deltaTime);
        ApplyGravity(deltaTime);
        UpdateZoom();
    }

    private void ReadInput()
    {
        moveInput = _player._input.actions["Move"].ReadValue<Vector2>();
        crouchInput = _player._input.actions["Crouch"].IsPressed();
        runInput = _player._input.actions["Run"].IsPressed();

        if (_player._input.actions["Jump"].WasPressedThisFrame())
        {
            jumpQueued = true;
            jumpBufferLeft = jumpBufferTime;
        }
    }

    private void CheckGrounded(float deltaTime)
    {
        bool foundGround = Physics.SphereCast(
            groundCheck.position + Vector3.up * 0.05f,
            groundDistance,
            Vector3.down,
            out RaycastHit hit,
            groundDistance + 0.15f,
            groundMask,
            QueryTriggerInteraction.Ignore);

        bool validGround = false;

        if (foundGround)
        {
            float groundAngle = Vector3.Angle(hit.normal, Vector3.up);
            validGround = groundAngle <= maximumGroundAngle;

            if (validGround)
                groundNormal = hit.normal;
        }

        if (validGround)
        {
            isGrounded = true;
            groundedTimeLeft = groundedCoyoteTime;
        }
        else
        {
            groundedTimeLeft -= deltaTime;
            isGrounded = groundedTimeLeft > 0f;
            groundNormal = Vector3.up;
        }

        if (anim != null)
            anim.SetBool("IsGrounded", isGrounded);
    }

    private void UpdateMovementState(float deltaTime)
    {
        isMoving = moveInput.sqrMagnitude > 0.01f;

        running =
            runInput &&
            !crouching &&
            !aiming &&
            isMoving &&
            currentStamina > 0f &&
            !rechargingStamina;

        if (running)
        {
            currentStamina -= staminaCost * deltaTime;
            rechargeTimer = intialRechargeWait;

            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                rechargingStamina = true;
                running = false;
            }

            OnStartRun();
        }
        else
        {
            rechargeTimer -= deltaTime;

            if (rechargeTimer <= 0f)
                currentStamina += staminaGain * deltaTime;

            if (rechargingStamina &&
                currentStamina >= staminaRechargeThreshold)
            {
                rechargingStamina = false;
            }

            OnEndRun();
        }

        currentStamina = Mathf.Clamp(
            currentStamina,
            0f,
            maxStamina);

        walking =
            isMoving &&
            !running &&
            !crouching &&
            isGrounded;
    }

    private void ApplyMovement(float deltaTime)
    {
        Vector3 inputDirection =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);

        // Keep movement parallel to slopes.
        if (isGrounded)
        {
            inputDirection = Vector3.ProjectOnPlane(
                inputDirection,
                groundNormal).normalized;
        }

        float targetSpeed = walkSpeed;

        if (crouching)
            targetSpeed = crouchWalkSpeed;
        else if (running)
            targetSpeed = isGrounded ? runSpeed : runSpeedAir;

        float aimModifier = 1f;

        if (aiming)
        {
            aimModifier = Mathf.Clamp01(
                (1f - currentChargeState) + 0.65f);
        }

        Vector3 targetVelocity =
            inputDirection * targetSpeed * aimModifier;

        Vector3 currentHorizontalVelocity =
            Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

        Vector3 requiredVelocityChange =
            targetVelocity - currentHorizontalVelocity;

        float acceleration;

        if (!isGrounded)
        {
            acceleration = airAcceleration;
        }
        else if (inputDirection.sqrMagnitude < 0.01f)
        {
            acceleration = groundDeceleration;
        }
        else
        {
            acceleration = groundAcceleration;
        }

        float maximumChange = Mathf.Min(
            acceleration * deltaTime,
            maximumVelocityChange);

        requiredVelocityChange = Vector3.ClampMagnitude(
            requiredVelocityChange,
            maximumChange);

        // VelocityChange gives direct arcade control while still allowing
        // collisions and external forces to participate in simulation.
        rb.AddForce(
            requiredVelocityChange,
            ForceMode.VelocityChange);
    }

    private void ProcessJump(float deltaTime)
    {
        jumpBufferLeft -= deltaTime;

        if (jumpBufferLeft <= 0f)
            jumpQueued = false;

        if (!jumpQueued || !isGrounded || crouching)
            return;

        jumpQueued = false;
        groundedTimeLeft = 0f;
        isGrounded = false;

        if (running)
            OnEndRun();

        if (anim != null)
            anim.SetTrigger(jumpTrigger);

        // Remove downward velocity before applying the jump.
        if (rb.linearVelocity.y < 0f)
        {
            rb.AddForce(
                Vector3.up * -rb.linearVelocity.y,
                ForceMode.VelocityChange);
        }

        float jumpVelocity = Mathf.Sqrt(
            jumpHeight * -2f * gravity);

        rb.AddForce(
            Vector3.up * jumpVelocity,
            ForceMode.VelocityChange);
    }

    private void ApplyGravity(float deltaTime)
    {
        float gravityMultiplier = rb.linearVelocity.y < 0f
            ? fallingGravityWeight
            : 1f;

        rb.AddForce(
            Vector3.up * gravity * gravityMultiplier,
            ForceMode.Acceleration);
    }

    private void UpdateCrouching(float deltaTime)
    {
        if (crouchInput)
        {
            crouching = true;
        }
        else if (CanStandUp())
        {
            crouching = false;
        }

        float targetHeight = crouching
            ? crouchHeight
            : standingHeight;

        currentColliderHeight = Mathf.MoveTowards(
            currentColliderHeight,
            targetHeight,
            crouchSpeed * deltaTime);

        capsule.height = currentColliderHeight;
        UpdateColliderCenter(currentColliderHeight);

        if (playerGFX != null)
        {
            float heightDifference =
                standingHeight - currentColliderHeight;

            Vector3 targetPosition =
                originalGFXPosition +
                Vector3.down *
                heightDifference *
                playerGFXCrouchMultiplier;

            playerGFX.localPosition = Vector3.Lerp(
                playerGFX.localPosition,
                targetPosition,
                crouchSpeed * deltaTime);
        }

        if (anim != null)
            anim.SetBool("Crouching", crouching);
    }

    private void UpdateColliderCenter(float height)
    {
        Vector3 center = standingColliderCenter;

        // Keep the bottom of the capsule in approximately the same place.
        center.y -= (standingHeight - height) * 0.5f;
        capsule.center = center;
    }

    private bool CanStandUp()
    {
        float extraHeight = standingHeight - currentColliderHeight;

        if (extraHeight <= 0f)
            return true;

        Vector3 origin =
            transform.TransformPoint(capsule.center);

        float radius = capsule.radius * 0.95f;

        return !Physics.SphereCast(
            origin,
            radius,
            Vector3.up,
            out _,
            extraHeight * 0.5f,
            ceilingMask,
            QueryTriggerInteraction.Ignore);
    }

    private void UpdateAnimation()
    {
        float multiplier = running ? 2f : 1f;
        float smoothing = running
            ? animChangeSpeed * 1.5f
            : animChangeSpeed;

        lerpedX = Mathf.Lerp(
            lerpedX,
            moveInput.x * multiplier,
            Time.deltaTime * smoothing);

        lerpedZ = Mathf.Lerp(
            lerpedZ,
            moveInput.y * multiplier,
            Time.deltaTime * smoothing);

        if (anim != null)
        {
            anim.SetFloat(moveXVar, lerpedX);
            anim.SetFloat(moveZVar, lerpedZ);
        }
    }

    private void UpdateZoom()
    {
        if (mouseLook == null)
            return;

        Vector3 horizontalVelocity =
            Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

        float normalizedSpeed = runSpeed > 0f
            ? horizontalVelocity.magnitude / runSpeed
            : 0f;

        mouseLook.SetZoom(
            mouseLook.defaultFOV + normalizedSpeed * 20f);
    }

    public void ProcessKnockback()
    {
        if (_knockbacks.Count == 0)
        {
            isStunned = false;
            hasKnockback = false;
            return;
        }

        hasKnockback = true;
        isStunned = false;

        for (int i = _knockbacks.Count - 1; i >= 0; i--)
        {
            Knockback knockback = _knockbacks[i];

            if (!knockback.applied)
            {
                Vector3 impulse =
                    knockback.direction.normalized *
                    knockback.intensity;

                rb.AddForce(
                    impulse,
                    ForceMode.VelocityChange);

                knockback.applied = true;
            }

            if (knockback.stun)
                isStunned = true;

            knockback.time -= Time.fixedDeltaTime;

            if (knockback.time <= 0f)
                _knockbacks.RemoveAt(i);
        }
    }

    public void AddKnockback(
        Vector3 direction,
        float intensity = 2f,
        bool stunPlayer = false,
        float time = 0.25f,
        float fadeOutTime = 0.15f)
    {
        _knockbacks.Add(new Knockback(
            direction,
            intensity,
            stunPlayer,
            time,
            fadeOutTime));
    }

    public void AddKnockback(
        Vector3 direction,
        Knockback knockback)
    {
        AddKnockback(
            direction,
            knockback.intensity,
            knockback.stun,
            knockback.time,
            knockback.fadeOutTime);
    }

    public void ResetVelocity()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void MoveTo(Vector3 position)
    {
        rb.position = position;
        ResetVelocity();
    }

    public void SetLock(bool state)
    {
        locked = state;
    }

    public bool IsLocked()
    {
        return locked;
    }

    private void OnStartRun()
    {
    }

    private void OnEndRun()
    {
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(
            groundCheck.position,
            groundDistance);

        Gizmos.DrawLine(
            groundCheck.position,
            groundCheck.position +
            Vector3.down * (groundDistance + 0.15f));
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
        float fadeOutTime = 0.5f)
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

    [HideInInspector] public Vector3 direction;
    [HideInInspector] public bool applied;
}
