using EZCameraShake;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : NetworkBehaviour
{
    public Player _player;
    //public PlayerInput _input;

    public CharacterController controller;
    [HideInInspector]
    public float speed = 12f;
    float actualSpeed;
    public float speedChangeTime = 10f;
    public float runSpeedLossPercent = 0.8f;

    public float walkSpeed = 4f;
    public float crouchWalkSpeed = 2f;
    public float runSpeed = 8f;
    public float runSpeedAir = 8f;

    float currentStamina = 1f;
    bool rechargingStamina;
    float rechargeTimer;
    public float maxStamina = 1f;
    public float staminaCost = 0.33f;
    public float staminaGain = 0.4f;
    public float staminaRechargeThreshold = 0.25f;
    public float intialRechargeWait = 0.5f;

    public float gravity = -9.81f;
    public float fallingGravityWeight = 1.5f;
    public float jumpHeight = 3f;
    public Transform groundCheck;
    Vector3 defaultGroundCheckPos;
    Vector3 crouchGroundCheckPos;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;
    public float crouchHeight = 1f;
    public float standingHeight = 2f;
    public float currentChargeState = 0f;

    public float groundedCoyoteTime = 0.2f;
    float groundedTimeLeft;

    public float crouchSpeed = 1f;

    public bool walking;
    public bool running;
    public bool aiming;

    Vector3 velocity;
    public bool isGrounded;
    bool actuallyGrounded;
    public bool hasKnockback;
    public bool isStunned;
    public bool crouching;
    public bool isMoving;

    bool locked;

    public PlayerMouseLook mouseLook;

    public Transform playerGFX;
    public float playerGFXCrouchMultiplier = 0.25f;

    public Animator anim;
    public string moveXVar = "MoveX";
    public string moveZVar = "MoveZ";
    public string jumpTrigger = "Jump";

    float lerpedX;
    float lerpedZ;

    float jumpCooldown;

    public float animChangeSpeed = 5f;

    public List<Knockback> _knockbacks = new List<Knockback>();

    // Settings

    private void Start()
    {
        groundedTimeLeft = groundedCoyoteTime;

        float percentHeight = crouchHeight / standingHeight;

        defaultGroundCheckPos = groundCheck.localPosition;
        crouchGroundCheckPos = new Vector3(0f, ((crouchHeight + standingHeight) / 2), 0f);
    }

    void Update()
    {
        if (!IsOwner)
            return;

        if (Player.me == null)
            return;

        if (controller.enabled == false)
            return;

        jumpCooldown -= Time.deltaTime;

        float oldHeight = controller.height;

        bool attemptingToCrouch = _player._input.actions["Crouch"].IsPressed(); // Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        bool attemptingToRun = _player._input.actions["Run"].IsPressed(); //Input.GetKey(KeyCode.LeftShift);
        actuallyGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        //actuallyGrounded = Physics.Raycast(groundCheck.position, Vector3.down, groundDistance, groundMask);

        if (!actuallyGrounded)
        {
            groundedTimeLeft -= Time.deltaTime;

            if (groundedTimeLeft <= 0f)
                isGrounded = false;
            else
                isGrounded = true;
        }
        else
        {
            groundedTimeLeft = groundedCoyoteTime;
            isGrounded = true;
        }

        if(anim != null)
            anim.SetBool("IsGrounded", isGrounded);

        ProcessKnockback();

        if (attemptingToCrouch)
        {
            crouching = true;

            if (running)
                OnEndRun();
        }
        else
            crouching = false;

        if (attemptingToRun /* && isGrounded */ && !crouching && !aiming)
            running = true;
        else
            running = false;

        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = -4f;
        }

        speed = walkSpeed;

        if (crouching)
        {
            controller.height = Mathf.Lerp(controller.height, crouchHeight, crouchSpeed * Time.deltaTime);

            controller.Move(new Vector3(0f, (controller.height - oldHeight) / 2f, 0f));
            groundCheck.transform.position -= new Vector3(0f, (controller.height - oldHeight) / 2f, 0f);

            speed = crouchWalkSpeed;
        }
        else
        {
            controller.height = Mathf.Lerp(controller.height, standingHeight, crouchSpeed * Time.deltaTime);

            controller.Move(new Vector3(0f, (controller.height - oldHeight) / 2f, 0f));
            groundCheck.transform.position -= new Vector3(0f, (controller.height - oldHeight) / 2f, 0f);
        }

        if(anim != null)
            anim.SetBool("Crouching", crouching);

        if (locked || isStunned)
        {
            walking = false;
            running = false;
            crouching = false;
            isMoving = false;
            return;
        }

        playerGFX.localPosition = new Vector3(0f, -(controller.height - standingHeight) / 2f, 0f);

        if (running && currentStamina > 0f && !rechargingStamina && isMoving)
        {
            currentStamina -= Time.deltaTime * staminaCost;

            if (currentStamina <= 0f)
            {
                rechargingStamina = true;
                rechargeTimer = intialRechargeWait;
            }

            if (isGrounded)
                speed = runSpeed;
            else
                speed = runSpeedAir;

            OnStartRun();
        }
        else
        {
            rechargeTimer -= Time.deltaTime;

            if (rechargeTimer <= 0f)
                currentStamina += Time.deltaTime * staminaGain;

            if (rechargingStamina && currentStamina > staminaRechargeThreshold)
                rechargingStamina = false;

            OnEndRun();
        }

        currentStamina = Mathf.Clamp01(currentStamina);
        //Player.me._ui.UpdateStamina(currentStamina, rechargingStamina);

        if (crouching)
            speed = crouchWalkSpeed;

        Vector2 move2d = _player._input.actions["Move"].ReadValue<Vector2>();

        float x = move2d.x; //Input.GetAxis("Horizontal");
        float z = move2d.y; //Input.GetAxis("Vertical");

        //if (Player.me._pause.isPaused)
        //{
        //    x = 0f;
        //    z = 0f;
        //}

        if (!running)
        {
            lerpedX = Mathf.Lerp(lerpedX, x, Time.deltaTime * animChangeSpeed);
            lerpedZ = Mathf.Lerp(lerpedZ, z, Time.deltaTime * animChangeSpeed);

            if (crouching)
                lerpedX = lerpedZ;
        }
        else
        {
            lerpedX = Mathf.Lerp(lerpedX, x * 2, Time.deltaTime * animChangeSpeed * 1.5f);
            lerpedZ = Mathf.Lerp(lerpedZ, z * 2, Time.deltaTime * animChangeSpeed * 1.5f);
        }

        if(anim != null)
        {
            anim.SetFloat(moveXVar, lerpedX);
            anim.SetFloat(moveZVar, lerpedZ);
        }

        walking = new Vector2(x, z).magnitude > 0 && !running && !crouching && isGrounded;
        isMoving = new Vector2(x, z).magnitude > 0;

        Vector3 move = transform.right * x + transform.forward * z;

        if (actualSpeed > walkSpeed + 0.5f)
            actualSpeed = Mathf.Lerp(actualSpeed, speed, speedChangeTime * runSpeedLossPercent * Time.deltaTime);
        else
            actualSpeed = Mathf.Lerp(actualSpeed, speed, speedChangeTime * Time.deltaTime);

        float aimModifier = 1f;

        if (aiming)
            aimModifier = Mathf.Clamp01((1f - currentChargeState) + 0.65f);

        controller.Move(move.normalized * actualSpeed * aimModifier * Time.deltaTime);

        if (_player._input.actions["Jump"].WasPressedThisFrame() && isGrounded && !crouching && jumpCooldown <= 0f)
        {
            if (running)
                OnEndRun();

            jumpCooldown = 0.1f;

            if(anim != null)
                anim.SetTrigger(jumpTrigger);

            velocity.y = Mathf.Sqrt(jumpHeight * -2 * gravity);
        }

        if (!isGrounded && !hasKnockback)
        {
            float gravityMultiplier = 1f;

            if (velocity.y < 0f)
                gravityMultiplier = fallingGravityWeight;

            velocity.y += gravity * gravityMultiplier * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);

        mouseLook.SetZoom(mouseLook.defaultFOV + (((actualSpeed * aimModifier) / runSpeed) * 20f));
    }

    public void SetLock(bool state)
    {
        locked = state;
    }

    void OnStartRun()
    {

    }

    void OnEndRun()
    {

    }

    public void ResetVelocity() => velocity = Vector3.zero;

    public void ProcessKnockback()
    {
        if (_knockbacks.Count <= 0)
        {
            isStunned = false;
            hasKnockback = false;
            return;
        }

        bool stunnedThisFrame = false;
        hasKnockback = true;

        List<Knockback> dispose = new List<Knockback>();

        foreach (Knockback knockback in _knockbacks)
        {
            float realIntensity = knockback.intensity;

            if (knockback.time <= knockback.fadeOutTime)
                realIntensity *= (knockback.time / knockback.fadeOutTime);

            controller.Move(realIntensity * knockback.direction * Time.deltaTime);

            if (knockback.stun)
                stunnedThisFrame = true;

            knockback.time -= Time.deltaTime;

            if (knockback.time <= 0f)
                dispose.Add(knockback);
        }

        foreach (Knockback knockback in dispose)
            _knockbacks.Remove(knockback);

        dispose.Clear();

        isStunned = stunnedThisFrame;
    }

    public void AddKnockback(Vector3 direction, float intensity = 2f, bool stunPlayer = false, float time = 0.25f, float fadeOutTime = 0.15f)
    {
        Knockback k = new Knockback(direction, intensity, stunPlayer, time, fadeOutTime);
        _knockbacks.Add(k);
    }

    public void AddKnockback(Vector3 direction, Knockback knockback) => AddKnockback(direction, knockback.intensity, knockback.stun, knockback.time, knockback.fadeOutTime);

    public void MoveTo(Vector3 position)
    {
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(groundCheck.position, Vector3.down * groundDistance);

        Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
    }

    public bool IsLocked() => locked;
}

[System.Serializable]
public class Knockback
{
    public Knockback(Vector3 direction, float intensity = 2f, bool stunPlayer = false, float time = 1f, float fadeOutTime = 0.5f)
    {
        this.direction = direction;
        this.intensity = intensity;
        this.stun = stunPlayer;
        this.time = time;
        this.fadeOutTime = fadeOutTime;
    }

    public float intensity;
    public float time;
    public float fadeOutTime;

    [HideInInspector]
    public Vector3 direction;

    public bool stun;
}