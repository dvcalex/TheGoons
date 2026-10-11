using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMouseLook : NetworkBehaviour
{
    public static PlayerMouseLook _mine;
    public Player _player;
    public float mouseSens = 5f;
    private float sensMultiplier = 1f;
    public Transform playerBody;
    public PlayerMovement movement;
    private float xRotation = 0f;
    public float Pitch
    {
        get
        {
            return xRotation;
        }
    }

    private float yaw;
    private Vector3 standingPosition;
    public bool canLook;
    public Transform followPoint;
    public Camera cam;
    private float currentRecoil;
    public float recoilSpeed = 50f;
    private bool recoiling;
    private bool zooming;
    private float currentZoom;
    [HideInInspector]
    public float defaultFOV;
    private float simulatedFOV;
    public float zoomSpeed;
    private bool disabled;
    private bool invertMouse = false;

    private void Start()
    {
        if (_mine == null && IsOwner)
        {
            _mine = this;
        }
        else
        {
            return;
        }

        currentZoom = cam.fieldOfView;
        defaultFOV = cam.fieldOfView;
        simulatedFOV = cam.fieldOfView;
        yaw = movement.FacingYaw;
        standingPosition = transform.localPosition;
        Cursor.lockState = CursorLockMode.Locked;

        //PlayerSettings._instance.onSettingsUpdated += OnSettingsUpdated;
    }

    private void OnDisable()
    {
        if (_mine == this)
        {
            _mine = null;
        }

        //if(PlayerSettings._instance != null)
        //    PlayerSettings._instance.onSettingsUpdated -= OnSettingsUpdated;
    }

    private //private void OnSettingsUpdated()

            //{
            //    defaultFOV = PlayerSettings._instance.fov;
            //    sensMultiplier = PlayerSettings._instance.sensitivity;
            //    invertMouse = PlayerSettings._instance.invertCamera;
            //}
            // Update is called once per frame
    void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (disabled)
        {
            return;
        }

        if (Player.me._ragdollManager.isRagdoll)
        {
            return;
        }

        //if (Player.me != null && Player.me._pause.isPaused)
        //    return;
        if (Player.me != null && Player.me._movement.IsLocked())
        {
            return;
        }

        UpdateZoom();

        if (followPoint != null)
        {
            FollowTarget();
            return;
        }

        float iSens = mouseSens * sensMultiplier * 0.1f;

        if (movement.aiming)
        {
            iSens = mouseSens / 1.5f;
        }

        if (recoiling)
        {
            xRotation -= (currentRecoil * Time.deltaTime) * recoilSpeed;
            currentRecoil -= Time.deltaTime * recoilSpeed;

            if (currentRecoil <= 0f)
            {
                currentRecoil = 0f;
                recoiling = false;
            }
        }

        if (!canLook)
        {
            return;
        }

        if (movement.isStunned)
        {
            return;
        }

        Vector2 look = _player._input.actions["Look"].ReadValue<Vector2>();
        float mouseX = look.x * iSens; //Input.GetAxis("Mouse X") * iSens;
        float mouseY = look.y * iSens; //Input.GetAxis("Mouse Y") * iSens;

        if (invertMouse)
        {
            mouseY = -mouseY;
        }

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        transform.localRotation = Quaternion.Euler(xRotation, transform.localEulerAngles.y, transform.localEulerAngles.z);
        yaw = Mathf.Repeat(yaw + mouseX, 360f);
        movement.SetFacingYaw(yaw);
    }

    private void LateUpdate()
    {
        if (!IsOwner || followPoint != null)
        {
            return;
        }

        // Mouse aim is immediate; physics yaw catches up through torque in FixedUpdate.
        // Keep the camera stable instead of attaching it to a wobbling head.
        transform.rotation = Quaternion.Euler(xRotation, yaw, 0f);
        float drop = movement.standingHeight - movement.capsule.height;
        Vector3 target = standingPosition - Vector3.up * drop;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, 1f - Mathf.Exp(-18f * Time.deltaTime));
    }

    public void DisableControl()
    {
        disabled = true;
    }

    private void UpdateZoom()
    {
        cam.fieldOfView = simulatedFOV;

        if (zooming)
        {
            if (simulatedFOV > currentZoom)
            {
                simulatedFOV -= zoomSpeed * Time.deltaTime;

                if (simulatedFOV < currentZoom)
                {
                    simulatedFOV = currentZoom;
                }
            }
            else if (simulatedFOV <= currentZoom)
            {
                simulatedFOV += zoomSpeed * Time.deltaTime;

                if (simulatedFOV > currentZoom)
                {
                    simulatedFOV = currentZoom;
                }
            }
        }
        else if (!zooming)
        {
            if (simulatedFOV < defaultFOV)
            {
                simulatedFOV += zoomSpeed * Time.deltaTime;

                if (simulatedFOV > defaultFOV)
                {
                    simulatedFOV = defaultFOV;
                }
            }
            else if (simulatedFOV >= defaultFOV)
            {
                simulatedFOV -= zoomSpeed * Time.deltaTime;

                if (simulatedFOV < defaultFOV)
                {
                    simulatedFOV = defaultFOV;
                }
            }
        }
    }

    private void FollowTarget()
    {
        //transform.localRotation = Quaternion.LookRotation(followPoint.position - transform.position, Vector3.up);
        transform.LookAt(followPoint, Vector3.up);
    }

    public void SetZoom(float toZoom, float newZoomSpeed = 100f)
    {
        zooming = true;
        currentZoom = toZoom;
        zoomSpeed = newZoomSpeed;
    }

    public void AddRecoil(float amount)
    {
        currentRecoil = amount;
        recoiling = true;
    }

    public void ResetZoom()
    {
        zooming = false;
    }
}
