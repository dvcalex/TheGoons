using UnityEngine;
using UnityEngine.XR;

// One owner for XR poses, reach mapping, the physical head, and thumbstick travel.
[DefaultExecutionOrder(-100)]
public sealed class VrRigController : MonoBehaviour
{
    public Rigidbody chassis;
    public Rigidbody physicalHead;
    public Transform trackingFloor;
    public VrPhysicsHand leftHand;
    public VrPhysicsHand rightHand;
    public VrPidMotor headMotor = new VrPidMotor();

    [Header("Hand reach (metres at scale 1)")]
    public float reachMultiplier = 5f;
    public float forwardReach = 2.5f;
    public float maximumReach = 7f;
    public float shoulderWidth = 0.65f;
    public float shoulderDrop = 0.25f;

    [Header("Cover eyes")]
    [Tooltip("Hand poses relative to the physical head. X toggles between these and controller tracking.")]
    public Transform leftEyeHandTarget;
    public Transform rightEyeHandTarget;

    [Header("Travel")]
    public float travelSpeed = 2f;
    public float travelAcceleration = 3f;
    public float brakingAcceleration = 4f;
    public float stickDeadzone = 0.2f;
    public Vector3 floorOffset = new Vector3(0f, -0.85f, 0f);

    // Use a uniform root scale. Distances and linear motion follow it automatically.
    public float WorldScale
    {
        get
        {
            return Mathf.Abs(transform.lossyScale.x);
        }
    }

    public Quaternion Facing
    {
        get;
        private set;
    } = Quaternion.identity;

    private Transform trackingSpace;
    private Quaternion trackingRotation = Quaternion.identity;
    private bool headTracked;
    private bool travelArmed;
    private bool paused;
    private Vector3 headPosition;
    private Quaternion headRotation;
    private Vector2 stick;
    private float headAuthority;
    private bool coveringEyes;
    private bool coverButtonHeld;
    private bool eyeCoverLocked;

    public void BindTracking(Transform space)
    {
        trackingSpace = space;
    }

    public void Relocate(Transform spawn)
    {
        FollowChassis(chassis.position);

        Vector3 oldFloor = trackingFloor.position;
        Quaternion rotation = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f);
        Quaternion turn = rotation * Quaternion.Inverse(trackingRotation);

        // Move every physical part together so the motors do not drag them
        // across the map, and discard motion from the previous location.
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.position = spawn.position + turn * (body.position - oldFloor);
            body.rotation = turn * body.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        trackingRotation = rotation;
        FollowChassis(chassis.position);
        headMotor.Reset();
        leftHand.motor.Reset();
        rightHand.motor.Reset();
        leftHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
        rightHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
        headTracked = false;
        headAuthority = 0f;
        travelArmed = false;
        Physics.SyncTransforms();
    }

    private void Awake()
    {
        if (!enabled)
        {
            return;
        }

        chassis.constraints = RigidbodyConstraints.FreezeRotation;
        chassis.interpolation = RigidbodyInterpolation.Interpolate;
        chassis.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        physicalHead.interpolation = RigidbodyInterpolation.Interpolate;
        physicalHead.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        physicalHead.maxAngularVelocity = 25f;
        physicalHead.maxLinearVelocity *= WorldScale;
        chassis.maxLinearVelocity = Mathf.Max(chassis.maxLinearVelocity, travelSpeed * WorldScale);
        chassis.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity * WorldScale;
        physicalHead.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity * WorldScale;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            for (int j = i + 1; j < colliders.Length; j++)
            {
                Physics.IgnoreCollision(colliders[i], colliders[j]);
            }
        }
    }

    private void Update()
    {
        FollowChassis(chassis.transform.position);
        UpdateEyeCoverLock();

        var controller = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        controller.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed);

        if (Application.isFocused && !paused && headTracked && pressed && !coverButtonHeld)
        {
            ToggleCoverEyes();
        }

        coverButtonHeld = pressed;
    }

    public void ToggleCoverEyes()
    {
        UpdateEyeCoverLock();

        if (eyeCoverLocked)
        {
            return;
        }

        if (coveringEyes)
        {
            coveringEyes = false;
        }
        else if (leftEyeHandTarget != null && rightEyeHandTarget != null)
        {
            coveringEyes = true;
        }
    }

    private void UpdateEyeCoverLock()
    {
        GameManager game = GameManager._instance;

        // GameManager ends Hiding when hideDuration expires. Use its state
        // instead of starting a separate countdown on the VR player.
        if (game != null && game.IsHiding)
        {
            eyeCoverLocked = true;
            coveringEyes = true;
            return;
        }

        if (eyeCoverLocked)
        {
            eyeCoverLocked = false;
            coveringEyes = false;
        }
    }

    private void FixedUpdate()
    {
        FollowChassis(chassis.position);
        var headset = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        bool valid = trackingSpace != null && Application.isFocused && !paused
            && ReadPose(headset, out headPosition, out headRotation);
        if (headset.TryGetFeatureValue(CommonUsages.userPresence, out bool present) && !present)
        {
            valid = false;
        }

        if (!valid)
        {
            headTracked = false;
            headAuthority = 0f;
            travelArmed = false;
            headMotor.Reset();
            leftHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
            rightHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
            return;
        }

        if (!headTracked)
        {
            headMotor.Reset();
        }

        headTracked = true;
        Quaternion worldRotation = trackingSpace.rotation * headRotation;
        UpdateFacing(worldRotation);

        headAuthority = Mathf.MoveTowards(headAuthority, 1f, Time.fixedDeltaTime / 0.12f);
        headMotor.Drive(physicalHead, trackingSpace.TransformPoint(headPosition), worldRotation, headAuthority, WorldScale);
        var leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        DriveHand(leftHand, leftDevice, UnityEngine.InputSystem.XR.XRController.leftHand, -1f);
        DriveHand(rightHand, InputDevices.GetDeviceAtXRNode(XRNode.RightHand), UnityEngine.InputSystem.XR.XRController.rightHand, 1f);
        Travel(leftDevice, worldRotation);
    }

    public void UpdateFacing(Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f)
        {
            Facing = Quaternion.LookRotation(forward, Vector3.up);
        }
    }

    private void FollowChassis(Vector3 position)
    {
        trackingFloor.SetPositionAndRotation(position + trackingRotation * floorOffset * WorldScale, trackingRotation);
    }

    public Vector3 ShoulderPosition(float side)
    {
        return physicalHead.transform.position + Facing * new Vector3(side * shoulderWidth, -shoulderDrop, 0f) * WorldScale;
    }

    private void DriveHand(VrPhysicsHand hand, InputDevice device, UnityEngine.InputSystem.XR.XRController controller, float side)
    {
        if (coveringEyes)
        {
            Transform eyeTarget = leftEyeHandTarget;
            if (side > 0f)
            {
                eyeTarget = rightEyeHandTarget;
            }

            if (eyeTarget != null)
            {
                // Keep the physics motor active, with open fingers and no grabbing.
                hand.SetTarget(eyeTarget.position, eyeTarget.rotation, 0f, true);
                return;
            }
        }

        bool valid = ReadPose(device, out Vector3 position, out Quaternion rotation);
        if (hand.useControllerAim && controller != null && controller.isTracked.isPressed)
        {
            var aim = controller.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("pointerRotation");
            if (aim != null)
            {
                Quaternion aimRotation = aim.ReadValue();
                float length = Quaternion.Dot(aimRotation, aimRotation);
                if (length > 0.001f && !float.IsInfinity(length))
                {
                    rotation = aimRotation.normalized;
                }
            }
        }

        device.TryGetFeatureValue(CommonUsages.grip, out float grip);
        Vector3 headWorld = trackingSpace.TransformPoint(headPosition);
        Vector3 offset = trackingSpace.TransformVector(position - headPosition) * reachMultiplier;
        Vector3 target = headWorld + offset + Facing * Vector3.forward * forwardReach * WorldScale;
        Vector3 shoulder = ShoulderPosition(side);
        target = shoulder + Vector3.ClampMagnitude(target - shoulder, maximumReach * WorldScale);
        Quaternion handRotation = trackingSpace.rotation * rotation * Quaternion.Euler(hand.controllerRotationOffset);
        hand.SetTarget(target, handRotation, grip, valid);
    }

    private void Travel(InputDevice device, Quaternion viewRotation)
    {
        stick = Vector2.zero;
        GameManager game = GameManager._instance;
        if (game != null && (game.IsHiding || game.IsResetting || game.CurrentPhase == GamePhase.Results))
        {
            travelArmed = false;
            Vector3 braking = Vector3.ClampMagnitude(-chassis.linearVelocity / 0.35f, brakingAcceleration * WorldScale);
            chassis.AddForce(braking, ForceMode.Acceleration);
            return;
        }

        if (!leftHand.Tracked || !device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
        {
            travelArmed = false;
            return;
        }

        if (axis.magnitude <= stickDeadzone)
        {
            travelArmed = true;
        }
        else if (travelArmed)
        {
            stick = axis.normalized * Mathf.InverseLerp(stickDeadzone, 1f, axis.magnitude);
        }

        if (!travelArmed)
        {
            return;
        }

        Vector3 desired = viewRotation * new Vector3(stick.x, 0f, stick.y) * travelSpeed * WorldScale;
        float acceleration = travelAcceleration;
        if (stick.sqrMagnitude < 0.001f)
        {
            acceleration = brakingAcceleration;
        }

        Vector3 force = Vector3.ClampMagnitude((desired - chassis.linearVelocity) / 0.35f, acceleration * WorldScale);
        if (chassis.useGravity)
        {
            force -= Physics.gravity;
        }

        chassis.AddForce(force, ForceMode.Acceleration);
    }

    private static bool ReadPose(InputDevice device, out Vector3 position, out Quaternion rotation)
    {
        bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
        bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
        bool tracked = device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
        return device.isValid && tracked && hasPosition && hasRotation;
    }

    private void OnApplicationPause(bool value)
    {
        paused = value;
    }

    private void OnDisable()
    {
        eyeCoverLocked = false;
        coveringEyes = false;
        coverButtonHeld = false;
        headTracked = false;
        travelArmed = false;
        headAuthority = 0f;
        headMotor.Reset();
        if (leftHand != null)
        {
            leftHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
        }

        if (rightHand != null)
        {
            rightHand.SetTarget(Vector3.zero, Quaternion.identity, 0f, false);
        }
    }
}
