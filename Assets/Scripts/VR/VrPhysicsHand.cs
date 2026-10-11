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
    private PlayerGrab heldPlayer;
    private Vector3 smoothedThrowVelocity;
    private Vector3 peakThrowVelocity;
    private float peakThrowTime;
    private readonly Collider[] candidates = new Collider[24];

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        if (!enabled)
        {
            return;
        }

        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.solverIterations = 12;
        Body.solverVelocityIterations = 4;
        Body.maxAngularVelocity = 25f;
        Body.maxLinearVelocity = Mathf.Max(Body.maxLinearVelocity, maximumSpeed * WorldScale);
        Body.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity * WorldScale;
    }

    // Remote fingers animate without enabling tracking, grabbing or the motor.
    public void SetVisualGrip(float grip)
    {
        Grip = Mathf.Clamp01(grip);
    }

    public void SetTarget(Vector3 position, Quaternion rotation, float grip, bool tracked)
    {
        if (tracked != Tracked)
        {
            motor.Reset();
            authority = 0f;
            smoothedThrowVelocity = Vector3.zero;
            peakThrowVelocity = Vector3.zero;
            peakThrowTime = 0f;
        }

        Tracked = tracked;
        TargetPosition = position;
        targetRotation = rotation;
        Grip = Mathf.Clamp01(grip);
        if (!tracked)
        {
            Grip = 0f;
            gripReleased = false;
            Release(false);
        }
    }

    private void FixedUpdate()
    {
        GameManager game = GameManager._instance;
        bool canInteract = game == null || game.IsSeeking;
        Body.detectCollisions = canInteract;
        if (!canInteract)
        {
            Grip = 0f;
            gripReleased = false;
            Release(false);
        }

        if (!Tracked)
        {
            return;
        }

        authority = Mathf.MoveTowards(authority, 1f, Time.fixedDeltaTime / 0.12f);
        motor.Drive(Body, TargetPosition, targetRotation, authority, WorldScale);
        Body.linearVelocity = Vector3.ClampMagnitude(Body.linearVelocity, maximumSpeed * WorldScale);

        // Keep a short release history so opening the grip just after a flick still throws.
        Vector3 palm = transform.TransformPoint(grabPoint);
        Vector3 velocity = Body.GetPointVelocity(palm);
        float blend = 1f - Mathf.Exp(-Time.fixedDeltaTime / 0.06f);
        smoothedThrowVelocity = Vector3.Lerp(smoothedThrowVelocity, velocity, blend);
        if (Time.time - peakThrowTime > 0.1f || smoothedThrowVelocity.sqrMagnitude >= peakThrowVelocity.sqrMagnitude)
        {
            peakThrowVelocity = smoothedThrowVelocity;
            peakThrowTime = Time.time;
        }

        if (canInteract && Grip < 0.3f)
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
        if (grabJoint != null || heldPlayer != null)
        {
            return;
        }

        Vector3 palm = transform.TransformPoint(grabPoint);
        int count = Physics.OverlapSphereNonAlloc(palm, grabRadius * WorldScale, candidates, grabbableLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        Rigidbody nearest = null;
        PlayerGrab nearestPlayer = null;
        Vector3 contactPoint = palm;
        for (int i = 0; i < count; i++)
        {
            Rigidbody candidate = candidates[i].attachedRigidbody;
            if (candidate == null || candidate.transform.IsChildOf(transform.root))
            {
                continue;
            }

            var player = candidate.GetComponentInParent<PlayerGrab>();
            if (player != null)
            {
                // Network replicas are kinematic; the owner will move this player.
                if (!player.CanGrab(this))
                {
                    continue;
                }
            }
            else if (candidate.isKinematic || candidate.mass > maximumGrabMass || candidate.GetComponent<FixedJoint>() != null)
            {
                continue;
            }

            Vector3 point = candidates[i].ClosestPoint(palm);
            float distance = (point - palm).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
                nearestPlayer = player;
                contactPoint = point;
            }
        }

        if (nearest == null)
        {
            return;
        }

        if (nearestPlayer != null)
        {
            if (nearestPlayer.BeginGrab(this))
            {
                heldPlayer = nearestPlayer;
            }

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

    public Vector3 GetThrowVelocity(Vector3 point)
    {
        Vector3 velocity = smoothedThrowVelocity;
        if (Time.time - peakThrowTime <= 0.1f && Vector3.Dot(peakThrowVelocity, velocity) > 0f)
        {
            velocity = peakThrowVelocity;
        }

        // Include the rotational velocity of an offset grab point.
        Vector3 palm = transform.TransformPoint(grabPoint);
        return velocity + Vector3.Cross(Body.angularVelocity, point - palm);
    }

    private void Release(bool throwPlayer = true)
    {
        if (heldPlayer != null)
        {
            heldPlayer.EndGrab(this, throwPlayer);
            heldPlayer = null;
        }

        if (grabJoint != null)
        {
            Destroy(grabJoint);
            grabJoint = null;
        }

        heldBody = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!Tracked || collision.rigidbody == null || collision.contactCount == 0)
        {
            return;
        }

        var player = collision.rigidbody.GetComponentInParent<PlayerGrab>();
        if (player != null)
        {
            Vector3 direction = -collision.GetContact(0).normal;
            float impactSpeed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
                if (normalSpeed > impactSpeed)
                {
                    impactSpeed = normalSpeed;
                    direction = -normal;
                }
            }

            player.Hit(direction * impactSpeed, collision.impulse.magnitude);
        }
    }

    private void OnDisable()
    {
        Tracked = false;
        Grip = 0f;
        authority = 0f;
        gripReleased = false;
        motor.Reset();
        Release(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(grabPoint), grabRadius * WorldScale);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * WorldScale);
    }
}
