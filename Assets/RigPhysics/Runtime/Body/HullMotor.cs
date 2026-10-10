using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class HullMotor : MonoBehaviour
    {
        public MechTuning tuning;
        public Rigidbody body { get; private set; }
        public bool stabilize = true;
        public Vector3 commandedForce { get; private set; }
        private Vector3 requestedForce;
        private RigidbodyConstraints originalConstraints;
        private void Awake() { body = GetComponent<Rigidbody>(); }
        private void Start()
        {
            if (!tuning) { enabled = false; return; }
            body.mass = tuning.chassisMass;
            body.linearDamping = tuning.bodyLinearDrag;
            body.angularDamping = tuning.bodyAngularDrag;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = tuning.solverPasses;
            body.solverVelocityIterations = tuning.velocityPasses;
            originalConstraints = body.constraints;
            ApplyRotationPolicy();
        }
        private void ApplyRotationPolicy()
        {
            body.constraints = tuning.lockBodyRotation
                ? originalConstraints | RigidbodyConstraints.FreezeRotation : originalConstraints;
        }
        public void RequestForce(Vector3 force) { requestedForce += PhysicsMath.Bounded(force, tuning.maximumPullForce); }
        private void FixedUpdate()
        {
            if (!body || !tuning) return;
            ApplyRotationPolicy();
            commandedForce = PhysicsMath.Bounded(requestedForce, tuning.maximumPullForce);
            requestedForce = Vector3.zero;
            body.AddForce(commandedForce, ForceMode.Force);
            if (stabilize && !tuning.lockBodyRotation) {
                Vector3 up = body.rotation * Vector3.up;
                Vector3 axis = Vector3.Cross(up, Vector3.up);
                if (axis.sqrMagnitude < 0.00001f && Vector3.Dot(up, Vector3.up) < 0f)
                    axis = body.rotation * Vector3.right;
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(up, Vector3.up), -1f, 1f));
                Vector3 error = axis.normalized * angle;
                float inertia = Mathf.Max(body.inertiaTensor.x, body.inertiaTensor.z);
                JointDrive drive = PhysicsMath.Drive(inertia, tuning.uprightFrequency,
                    tuning.uprightDampingRatio, tuning.maximumUprightTorque, Time.fixedDeltaTime);
                Vector3 rollPitchSpin = body.angularVelocity - Vector3.Project(body.angularVelocity, Vector3.up);
                body.AddTorque(PhysicsMath.Bounded(error * drive.positionSpring - rollPitchSpin * drive.positionDamper,
                    tuning.maximumUprightTorque), ForceMode.Force);
            }
            PhysicsMath.Limit(body, tuning.maximumBodySpeed, tuning.maximumBodySpin);
        }
    }
}
