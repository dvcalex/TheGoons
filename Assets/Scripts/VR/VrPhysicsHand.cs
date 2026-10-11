using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class VrPhysicsHand : MonoBehaviour
{
    public VrPidMotor motor = new VrPidMotor();
    public LayerMask grabbableLayers = 1 << 10;
    public float grabRadius = 0.5f;
    public float maximumGrabMass = 50f;
    public float grabBreakForce = 5000f;
    public float maximumSpeed = 25f;

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
        motor.Drive(Body, TargetPosition, targetRotation, authority);
        Body.linearVelocity = Vector3.ClampMagnitude(Body.linearVelocity, maximumSpeed);

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

        int count = Physics.OverlapSphereNonAlloc(Body.position, grabRadius, candidates, grabbableLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        Rigidbody nearest = null;
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

            float distance = (candidates[i].ClosestPoint(Body.position) - Body.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
        {
            return;
        }

        heldBody = nearest;
        grabJoint = heldBody.gameObject.AddComponent<FixedJoint>();
        grabJoint.anchor = heldBody.transform.InverseTransformPoint(Body.position);
        grabJoint.connectedBody = Body;
        grabJoint.breakForce = grabBreakForce;
        grabJoint.breakTorque = grabBreakForce;
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
}
