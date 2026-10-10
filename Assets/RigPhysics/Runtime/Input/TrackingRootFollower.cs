using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-450)]
    public sealed class TrackingRootFollower : MonoBehaviour
    {
        public Rigidbody body;
        public Vector3 floorOffset;
        public bool yawOnly;
        private void Update() => Follow(false);
        private void FixedUpdate() => Follow(true);
        private void Follow(bool physicsStep)
        {
            if (!body) return;
            // This is a tracking-space transform with no rigidbody; the dynamic hull is untouched.
            // Physics targets use the current simulation pose; rendering uses Unity's
            // interpolated transform for both position and yaw, avoiding mixed-frame jitter.
            Vector3 position = physicsStep ? body.position : body.transform.position;
            Quaternion pose = physicsStep ? body.rotation : body.transform.rotation;
            Quaternion rotation = yawOnly ? Quaternion.Euler(0f, pose.eulerAngles.y, 0f) : pose;
            transform.SetPositionAndRotation(position + rotation * floorOffset, rotation);
        }
    }
}
