using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class VrPhysicsHand : MonoBehaviour
{
    public VrPidMotor motor = new VrPidMotor();
    [Header("Controller alignment")]
    [Tooltip("Use the controller's pointing direction for the fingers, instead of its grip pose.")]
    public bool useControllerAim = true;
    [Tooltip("Extra pitch, yaw and roll in controller space. Rotates the physical hand, collider and visual together.")]
    public Vector3 controllerRotationOffset;

    [Header("Grabbing")]
    public LayerMask grabbableLayers = 1 << 10;
    [Tooltip("Local palm position used to find grabbable objects.")]
    public Vector3 grabPoint = new Vector3(0f, 0f, 0.4f);
    public float grabRadius = 0.5f;
    public float maximumGrabMass = 50f;
    public float grabBreakForce = 5000f;
    public float maximumSpeed = 25f;

    // Hand transforms have unit local scale inside the uniformly scaled rig.
    public float WorldScale
    {
        get
        {
            return Mathf.Abs(transform.lossyScale.x);
        }
    }

    public Rigidbody Body
    {
        get;
        private set;
    }

    public bool Tracked
    {
        get;
        private set;
    }

    public float Grip
    {
        get;
        private set;
    }

    public Vector3 TargetPosition
    {
        get;
        private set;
    }

    private Quaternion targetRotation;
    private float authority;
    private bool gripReleased;
    private FixedJoint grabJoint;
    private Rigidbody heldBody;
    private readonly Collider[] candidates = new Collider[24];

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.solverIterations = 12;
        Body.solverVelocityIterations = 4;
        Body.maxAngularVelocity = 25f;
        Body.maxLinearVelocity = Mathf.Max(Body.maxLinearVelocity, maximumSpeed * WorldScale);
        Body.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity * WorldScale;
    }

    public void SetTarget(Vector3 position, Quaternion rotation, float grip, bool tracked)
    {
        if (tracked != Tracked)
        {
            motor.Reset();
            authority = 0f;
        }

        Tracked = tracked;
        TargetPosition = position;
        targetRotation = rotation;
        Grip = Mathf.Clamp01(grip);
        if (!tracked)
        {
            Grip = 0f;
            gripReleased = false;
            Release();
        }
    }

    private void FixedUpdate()
    {
        if (!Tracked)
        {
            return;
        }

        authority = Mathf.MoveTowards(authority, 1f, Time.fixedDeltaTime / 0.12f);
        motor.Drive(Body, TargetPosition, targetRotation, authority, WorldScale);
        Body.linearVelocity = Vector3.ClampMagnitude(Body.linearVelocity, maximumSpeed * WorldScale);

        if (Grip < 0.3f)
        {
            gripReleased = true;
            Release();
        }
        else if (Grip > 0.7f && gripReleased)
        {
            gripReleased = false;
            TryGrab();
        }

        if (grabJoint != null && heldBody != null && heldBody.useGravity)
        {
            heldBody.AddForce(-Physics.gravity, ForceMode.Acceleration);
        }
    }

    private void TryGrab()
    {
        if (grabJoint != null)
        {
            return;
        }

        Vector3 palm = transform.TransformPoint(grabPoint);
        int count = Physics.OverlapSphereNonAlloc(palm, grabRadius * WorldScale, candidates, grabbableLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        Rigidbody nearest = null;
        Vector3 contactPoint = palm;
        for (int i = 0; i < count; i++)
        {
            Rigidbody candidate = candidates[i].attachedRigidbody;
            if (candidate == null || candidate.isKinematic || candidate.mass > maximumGrabMass
                || candidate.transform.IsChildOf(transform.root))
            {
                continue;
            }

            // One hand owns a prop at a time; do not create competing grab joints.
            var existingJoint = candidate.GetComponent<FixedJoint>();
            if (existingJoint != null)
            {
                continue;
            }

            Vector3 point = candidates[i].ClosestPoint(palm);
            float distance = (point - palm).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
                contactPoint = point;
            }
        }

        if (nearest == null)
        {
            return;
        }

        heldBody = nearest;
        grabJoint = heldBody.gameObject.AddComponent<FixedJoint>();
        grabJoint.autoConfigureConnectedAnchor = false;
        grabJoint.anchor = heldBody.transform.InverseTransformPoint(contactPoint);
        grabJoint.connectedBody = Body;
        grabJoint.connectedAnchor = transform.InverseTransformPoint(contactPoint);
        grabJoint.breakForce = grabBreakForce * WorldScale;
        grabJoint.breakTorque = grabBreakForce * WorldScale * WorldScale;
        grabJoint.enableCollision = false;
    }

    private void Release()
    {
        if (grabJoint != null)
        {
            Destroy(grabJoint);
            grabJoint = null;
        }

        heldBody = null;
    }

    private void OnDisable()
    {
        Tracked = false;
        Grip = 0f;
        authority = 0f;
        gripReleased = false;
        motor.Reset();
        Release();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(grabPoint), grabRadius * WorldScale);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * WorldScale);
    }
}
