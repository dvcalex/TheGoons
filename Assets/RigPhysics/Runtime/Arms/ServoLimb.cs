using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ServoLimb : MonoBehaviour
    {
        public MechTuning tuning;
        public AttachmentTuning attachment;
        public PilotPoseStream input;
        public Rigidbody chassis;
        public Transform shoulder;
        public Rigidbody[] segments;
        public Rigidbody wrist { get; private set; }
        public Rigidbody proxy { get; private set; }
        public bool available = true;
        public Vector3 targetPosition { get; private set; }
        public float constraintError { get; private set; }
        public float authority { get; private set; }
        public float physicalReach { get; private set; }
        public bool operational => isActiveAndEnabled && available && input && input.tracked;
        private ConfigurableJoint servo;
        private TetherGrip grip;
        private float recovery, disruption;
        private bool wasOperational;

        private void Start()
        {
            if (!tuning || !input || !chassis || !shoulder) { enabled = false; return; }
            wrist = GetComponent<Rigidbody>();
            grip = GetComponent<TetherGrip>();
            // Measure the authored joint chain rather than asking a shorter, edited arm
            // to satisfy an unreachable world-space target. Leave some elbow bend.
            physicalReach = MeasureReach();
            wrist.mass = Mathf.Max(0.1f, tuning.wristMass + (attachment ? attachment.addedMass : 0f));
            wrist.linearDamping = tuning.armLinearDrag;
            wrist.angularDamping = tuning.armAngularDrag;
            wrist.interpolation = RigidbodyInterpolation.Interpolate;
            wrist.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            wrist.solverIterations = tuning.solverPasses;
            wrist.solverVelocityIterations = tuning.velocityPasses;
            var node = new GameObject(name + " pose proxy");
            node.transform.SetPositionAndRotation(wrist.position, wrist.rotation);
            proxy = node.AddComponent<Rigidbody>();
            proxy.isKinematic = true;
            proxy.useGravity = false;
            servo = gameObject.AddComponent<ConfigurableJoint>();
            servo.connectedBody = proxy;
            servo.autoConfigureConnectedAnchor = false;
            servo.anchor = servo.connectedAnchor = Vector3.zero;
            servo.xMotion = servo.yMotion = servo.zMotion = ConfigurableJointMotion.Free;
            servo.angularXMotion = servo.angularYMotion = servo.angularZMotion = ConfigurableJointMotion.Free;
            servo.rotationDriveMode = RotationDriveMode.Slerp;
            servo.enableCollision = false;
            servo.enablePreprocessing = true;
            targetPosition = wrist.position;
        }

        public void DisruptAfterImpact(float intensity)
        {
            if (tuning) disruption = Mathf.Max(disruption, tuning.recoverySeconds * Mathf.Clamp01(intensity));
        }

        private void FixedUpdate()
        {
            if (!wrist || !proxy) return;
            float dt = Time.fixedDeltaTime;
            bool active = operational;
            if (active && (!wasOperational || input.discontinuity)) recovery = 0f;
            wasOperational = active;
            recovery = active ? Mathf.Min(1f, recovery + dt / Mathf.Max(0.01f, tuning.reacquireSeconds)) : 0f;
            disruption = Mathf.Max(0f, disruption - dt);
            float impactRecovery = Mathf.SmoothStep(tuning.impactDriveFraction, 1f,
                1f - disruption / Mathf.Max(0.01f, tuning.recoverySeconds));
            authority = recovery * (attachment ? Mathf.Max(0f, attachment.authorityMultiplier) : 1f)
                * impactRecovery;
            if (active) {
                Vector3 requested = input.WorldPosition;
                float reach = Mathf.Min(tuning.limbReach, physicalReach * 0.98f);
                Vector3 reached = shoulder.position + PhysicsMath.Bounded(requested - shoulder.position, reach);
                targetPosition = Vector3.MoveTowards(proxy.position, reached, tuning.targetSpeed * impactRecovery * dt);
                proxy.MovePosition(targetPosition);
                proxy.MoveRotation(Quaternion.RotateTowards(proxy.rotation, input.WorldRotation, tuning.targetSpinDegrees * dt));
            }
            else {
                // Release authority on invalid tracking. Preserve physical limb continuity.
                targetPosition = proxy.position;
            }
            float carriedMass = wrist.mass;
            if (segments != null) foreach (Rigidbody segment in segments) if (segment) carriedMass += segment.mass;
            float load = grip ? grip.supportedMass : 0f;
            carriedMass += load;
            // Powered arms support gravity, but still accelerate real masses and yield to contacts.
            float support = active ? Mathf.Clamp01(tuning.armGravitySupport * recovery) : 0f;
            support *= Mathf.Min(1f, tuning.maximumServoForce / Mathf.Max(1f, carriedMass * Physics.gravity.magnitude));
            SupportGravity(wrist, support);
            if (segments != null) foreach (var segment in segments) SupportGravity(segment, support);
            JointDrive positional = PhysicsMath.Drive(carriedMass, tuning.positionFrequency * Mathf.Sqrt(impactRecovery),
                tuning.positionDampingRatio, tuning.maximumServoForce * authority, dt);
            servo.xDrive = servo.yDrive = servo.zDrive = positional;
            float inertia = (wrist.inertiaTensor.x + wrist.inertiaTensor.y + wrist.inertiaTensor.z) / 3f;
            // The wrist also turns the articulated forearm. Its small spherical collider
            // alone grossly understates the motor's effective rotational load.
            if (segments != null) foreach (var segment in segments) if (segment) {
                Vector3 tensor = segment.inertiaTensor;
                inertia += (tensor.x + tensor.y + tensor.z) / 3f;
            }
            if (grip && grip.holding) {
                Vector3 tensor = grip.heldBody.inertiaTensor;
                inertia += (tensor.x + tensor.y + tensor.z) / 3f * grip.supportShare
                    + load * (grip.heldBody.worldCenterOfMass - wrist.worldCenterOfMass).sqrMagnitude;
            }
            servo.slerpDrive = PhysicsMath.Drive(inertia, tuning.rotationFrequency,
                tuning.rotationDampingRatio, tuning.maximumServoTorque * authority, dt);
            constraintError = Vector3.Distance(wrist.position, targetPosition);
            PhysicsMath.Limit(wrist, tuning.maximumArmSpeed, tuning.maximumArmSpin);
            if (segments != null) foreach (Rigidbody segment in segments)
                PhysicsMath.Limit(segment, tuning.maximumArmSpeed, tuning.maximumArmSpin);
        }

        private static void SupportGravity(Rigidbody body, float support)
        {
            if (body && body.useGravity && !body.isKinematic && support > 0f)
                body.AddForce(-Physics.gravity * body.mass * support, ForceMode.Force);
        }

        private float MeasureReach()
        {
            float length = 0f;
            Vector3 distalPoint = Vector3.zero;
            Rigidbody child = wrist;
            for (int i = 0; i < 16 && child && child != chassis; i++) {
                ConfigurableJoint link = null;
                foreach (var joint in child.GetComponents<ConfigurableJoint>())
                    if (joint.connectedBody && joint.connectedBody != proxy) { link = joint; break; }
                if (!link) return tuning.limbReach;
                Vector3 point = child.transform.TransformPoint(link.anchor);
                if (child != wrist) length += Vector3.Distance(point, distalPoint);
                distalPoint = link.connectedBody.transform.TransformPoint(link.connectedAnchor);
                child = link.connectedBody;
            }
            return child == chassis && length > 0.01f ? length : tuning.limbReach;
        }

        private void OnDestroy()
        {
            if (proxy) Destroy(proxy.gameObject);
        }

        private void OnDisable()
        {
            recovery = disruption = authority = 0f;
            wasOperational = false;
            if (servo) {
                servo.xDrive = servo.yDrive = servo.zDrive = default;
                servo.slerpDrive = default;
            }
        }
    }
}
