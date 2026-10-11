using UnityEngine;
using UnityEngine.XR;

namespace MechLab
{
    // The physical head follows tracking; it never drives the camera back toward itself.
    [DefaultExecutionOrder(-275)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrackedHeadMotor : MonoBehaviour
    {
        public PilotPoseStream input;
        public HullMotor hull;
        [Min(0f)] public float frequency = 6f;
        [Min(0f)] public float maximumAcceleration = 80f;
        [Min(0f)] public float maximumSpeed = 12f;
        [Min(0f)] public float recoverySeconds = 0.25f;

        public Quaternion localRotation = Quaternion.identity;

        private Rigidbody body;
        private float authority;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 20f;
            body.solverIterations = 16;
            body.solverVelocityIterations = 6;
        }

        private void FixedUpdate()
        {
            if (input == null || input.trackingSpace == null || !input.headTracked)
            {
                authority = 0f;
                return;
            }

            if (!input.useScriptedInput)
            {
                var headset = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                if (headset.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                {
                    localRotation = rotation;
                }
            }

            float dt = Time.fixedDeltaTime;
            authority = Mathf.MoveTowards(authority, 1f, dt / Mathf.Max(0.01f, recoverySeconds));
            Vector3 target = input.trackingSpace.TransformPoint(input.headLocalPosition + input.trackingOffset);
            Vector3 targetVelocity = input.trackingSpace.rotation * input.headLocalVelocity;
            if (hull != null && hull.body != null)
            {
                targetVelocity += hull.body.linearVelocity;
            }

            // Implicit spring coefficients keep the drive damped at different fixed timesteps.
            float omega = 2f * Mathf.PI * frequency;
            float denominator = 1f + 2f * omega * dt + omega * omega * dt * dt;
            float spring = omega * omega / denominator;
            float damping = (2f * omega + omega * omega * dt) / denominator;
            Vector3 force = (target - body.position) * spring
                + (targetVelocity - body.linearVelocity) * damping;
            body.AddForce(Vector3.ClampMagnitude(force, maximumAcceleration) * authority, ForceMode.Acceleration);

            Quaternion targetRotation = input.trackingSpace.rotation * localRotation;
            Vector3 rotationError = PhysicsMath.AngularVelocity(body.rotation, targetRotation, 1f);
            Vector3 torque = rotationError * spring - body.angularVelocity * damping;
            body.AddTorque(Vector3.ClampMagnitude(torque, maximumAcceleration) * authority, ForceMode.Acceleration);
            PhysicsMath.Limit(body, maximumSpeed, 20f);
        }

        private void OnDisable()
        {
            authority = 0f;
        }
    }
}
