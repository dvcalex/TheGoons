using UnityEngine;
using UnityEngine.XR;

namespace MechLab
{
    // Travel moves the hull through forces. TrackingRootFollower carries the XR space with it.
    [DefaultExecutionOrder(-50)]
    public sealed class ThumbstickSlewMotor : MonoBehaviour
    {
        public HullMotor hull;
        public PilotPoseStream input;
        public SurfacePullMotor surfacePull;
        public Transform view;

        [Min(0f)] public float speed = 2f;
        [Min(0.01f)] public float responseSeconds = 0.35f;
        [Min(0f)] public float acceleration = 3f;
        [Min(0f)] public float brakingAcceleration = 4f;
        [Range(0f, 0.95f)] public float deadzone = 0.2f;

        private Vector2 stick;
        private bool armed;
        private bool paused;

        private void Update()
        {
            stick = Vector2.zero;

            var controller = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (paused || !Application.isFocused || input == null || !input.headTracked
                || !controller.isValid
                || !controller.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked
                || !controller.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            {
                armed = false;
                return;
            }

            float magnitude = axis.magnitude;
            if (magnitude <= deadzone)
            {
                armed = true;
                return;
            }

            // Require a released stick after tracking loss or putting the headset back on.
            if (armed)
            {
                stick = axis.normalized * Mathf.InverseLerp(deadzone, 1f, magnitude);
            }
        }

        private void FixedUpdate()
        {
            if (hull == null || hull.body == null || input == null || input.trackingSpace == null)
            {
                return;
            }

            // Let a planted hand pull the player without a second motor braking against it.
            if (surfacePull != null && surfacePull.isActiveAndEnabled && surfacePull.engagedArms > 0)
            {
                return;
            }

            Vector3 forward = input.trackingSpace.forward;
            Vector3 right = input.trackingSpace.right;
            if (view != null)
            {
                forward = view.forward;
                right = view.right;
            }

            Vector3 desiredVelocity = (forward * stick.y + right * stick.x) * speed;
            Vector3 velocity = hull.body.linearVelocity;
            if (!armed)
            {
                // Without tracking, brake horizontal drift but allow gravity to act normally.
                velocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            }

            float limit = acceleration;
            if (stick.sqrMagnitude < 0.0001f)
            {
                limit = brakingAcceleration;
            }

            Vector3 change = (desiredVelocity - velocity) / Mathf.Max(0.01f, responseSeconds);
            Vector3 travelAcceleration = Vector3.ClampMagnitude(change, limit);

            // Support the chassis separately so gravity does not consume the travel acceleration.
            if (armed && hull.body.useGravity)
            {
                travelAcceleration -= Physics.gravity;
            }

            hull.RequestForce(travelAcceleration * hull.body.mass);
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            stick = Vector2.zero;
            armed = false;
        }

        private void OnDisable()
        {
            stick = Vector2.zero;
            armed = false;
        }
    }
}
