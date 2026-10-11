using UnityEngine;

namespace MechLab
{
    // Runs before wrist servos and elbow guidance. Moves the actual structural anchor.
    [DefaultExecutionOrder(-250)]
    [RequireComponent(typeof(ServoLimb))]
    public sealed class HeadTrackedArmRoot : MonoBehaviour
    {
        public ServoLimb limb;
        public ConfigurableJoint shoulderJoint;
        [Tooltip("Authored shoulder position relative to the tracking floor, in metres.")]
        public Vector3 shoulderFromFloor;
        [Min(.1f)]
        public float maximumRootSpeed = 8f;

        public void CaptureAuthoredPose(ServoLimb arm)
        {
            limb = arm;
            shoulderFromFloor = arm.input.trackingSpace.InverseTransformPoint(arm.shoulder.position);
            foreach (var segment in arm.segments)
            {
                foreach (var joint in segment.GetComponents<ConfigurableJoint>())
                {
                    if (joint.connectedBody == arm.chassis)
                    {
                        shoulderJoint = joint;
                    }
                }
            }
        }

        private void FixedUpdate()
        {
            Follow(Time.fixedDeltaTime);
        }

        public void Follow(float deltaTime)
        {
            if (!limb
                || !shoulderJoint
                || !limb.input
                || !limb.input.headTracked
                || !limb.input.trackingSpace
                || !limb.chassis
                || !limb.shoulder)
            {
                return;
            }

            Vector3 desired = limb.input.trackingSpace.TransformPoint(shoulderFromFloor + limb.input.HeadTranslation);
            Vector3 current = limb.chassis.transform.TransformPoint(shoulderJoint.connectedAnchor);
            Vector3 next = Vector3.MoveTowards(current, desired, maximumRootSpeed * Mathf.Max(0f, deltaTime));
            // Do not move the tracking floor or camera: that would feed head motion back twice.
            shoulderJoint.connectedAnchor = limb.chassis.transform.InverseTransformPoint(next);
            limb.shoulder.position = next;
        }
    }
}
