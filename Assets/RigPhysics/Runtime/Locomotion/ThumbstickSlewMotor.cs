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
            if (view != null)
            {
                Vector3 headForward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
                if (headForward.sqrMagnitude > 0.01f)
                {
                    forward = headForward.normalized;
                }
            }

            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 desiredVelocity = (forward * stick.y + right * stick.x) * speed;
            Vector3 velocity = Vector3.ProjectOnPlane(hull.body.linearVelocity, Vector3.up);
            float limit = acceleration;
            if (stick.sqrMagnitude < 0.0001f)
            {
                limit = brakingAcceleration;
            }

            Vector3 change = (desiredVelocity - velocity) / Mathf.Max(0.01f, responseSeconds);
            hull.RequestForce(Vector3.ClampMagnitude(change, limit) * hull.body.mass);
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
