
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSpectatorMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float sprintMultiplier = 2.5f;
    public float acceleration = 12f;
    public float deceleration = 8f;

    [Header("Camera")]
    public float mouseSensitivity = 0.15f;
    public float lookSmoothing = 15f;
    public float minPitch = -89f;
    public float maxPitch = 89f;

    [Header("Options")]
    public bool lockCursor = true;

    private Vector3 velocity;
    private Vector2 currentLook;
    private Vector2 targetLook;

    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        Vector3 rotation = transform.eulerAngles;

        yaw = rotation.y;
        pitch = Mathf.DeltaAngle(0f, rotation.x);

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        HandleMovement();
        HandleLook();
    }

    private void HandleMovement()
    {
        Keyboard keyboard = Keyboard.current;

        Vector3 input = Vector3.zero;

        // Horizontal movement
        if (keyboard.wKey.isPressed)
            input += Vector3.forward;
        if (keyboard.sKey.isPressed)
            input += Vector3.back;
        if (keyboard.dKey.isPressed)
            input += Vector3.right;
        if (keyboard.aKey.isPressed)
            input += Vector3.left;

        // Vertical movement
        if (keyboard.spaceKey.isPressed)
            input += Vector3.up;
        if (keyboard.leftCtrlKey.isPressed)
            input += Vector3.down;

        input = input.normalized;

        // Movement relative to camera
        Vector3 direction =
            transform.right * input.x +
            transform.forward * input.z +
            Vector3.up * input.y;

        float speed = moveSpeed;

        if (keyboard.leftShiftKey.isPressed)
            speed *= sprintMultiplier;

        Vector3 targetVelocity = direction * speed;

        float smoothRate = input.sqrMagnitude > 0f
            ? acceleration
            : deceleration;

        velocity = Vector3.Lerp(
            velocity,
            targetVelocity,
            1f - Mathf.Exp(-smoothRate * Time.unscaledDeltaTime)
        );

        transform.position += velocity * Time.unscaledDeltaTime;
    }

    private void HandleLook()
    {
        if (Mouse.current == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        targetLook = mouseDelta * mouseSensitivity;

        currentLook = Vector2.Lerp(
            currentLook,
            targetLook,
            1f - Mathf.Exp(-lookSmoothing * Time.unscaledDeltaTime)
        );

        yaw += currentLook.x;
        pitch -= currentLook.y;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    public void SetActive(bool active)
    {
        enabled = active;
    }
}
