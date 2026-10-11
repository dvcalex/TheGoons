using UnityEngine;
using UnityEngine.XR;

namespace MechLab
{
    [DefaultExecutionOrder(-300)]
    public sealed class PilotPoseStream : MonoBehaviour
    {
        public MechTuning tuning;
        public XRNode hand = XRNode.LeftHand;
        public Transform trackingSpace;
        public Vector3 trackingOffset;
        public bool useScriptedInput;

        public Vector3 localPosition
        {
            get;
            private set;
        }
        public Quaternion localRotation
        {
            get;
            private set;
        } = Quaternion.identity;

        public float grip
        {
            get;
            private set;
        }

        public bool tracked
        {
            get;
            private set;
        }

        public bool discontinuity
        {
            get;
            private set;
        }

        public bool engaged
        {
            get;
            private set;
        }

        public double lastValidTime
        {
            get;
            private set;
        }

        public Vector3 localVelocity
        {
            get;
            private set;
        }

        public Vector3 localAngularVelocity
        {
            get;
            private set;
        }

        public Vector3 headLocalVelocity
        {
            get;
            private set;
        }

        public Vector3 headLocalPosition
        {
            get;
            private set;
        }

        public bool headTracked
        {
            get;
            private set;
        }

        public double lastHeadTime
        {
            get;
            private set;
        }

        public Vector3 referenceHeadPosition = new Vector3(0f, 1.65f, 0f);

        public Vector3 HeadTranslation
        {
            get
            {
                return headTracked ? headLocalPosition + trackingOffset - referenceHeadPosition : Vector3.zero;
            }
        }

        public readonly PoseWindow history = new PoseWindow();
        private readonly PoseWindow headHistory = new PoseWindow();

        public Vector3 WorldPosition
        {
            get
            {
                return trackingSpace.TransformPoint(
                    tuning.trackingPivot + HeadTranslation
                    + (localPosition + trackingOffset - tuning.trackingPivot - HeadTranslation) * tuning.handPositionScale
                );
            }
        }

        public Quaternion WorldRotation
        {
            get
            {
                return trackingSpace.rotation * localRotation;
            }
        }

        private bool hadPose;
        private bool gripArmed;

        private void Update()
        {
            if (useScriptedInput || !tuning || !trackingSpace)
            {
                return;
            }

            InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
            bool valid = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool flag) && flag;
            Vector3 p = localPosition;
            Quaternion q = localRotation;
            float g = 0f;
            valid = valid
                && device.TryGetFeatureValue(CommonUsages.devicePosition, out p)
                && device.TryGetFeatureValue(CommonUsages.deviceRotation, out q);
            device.TryGetFeatureValue(CommonUsages.grip, out g);
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            bool headValid = head.isValid
                && head.TryGetFeatureValue(CommonUsages.isTracked, out bool headFlag)
                && headFlag;
            headValid &= head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hp);
            FeedHead(Time.unscaledTimeAsDouble, hp, headValid);
            Feed(Time.unscaledTimeAsDouble, p, q, g, valid);
        }

        // External XR adapters can preserve the locomotion head-compensation contract.
        public void FeedHead(double time, Vector3 position, bool valid)
        {
            if (!tuning)
            {
                return;
            }

            if (!valid || !PhysicsMath.Finite(time) || !PhysicsMath.Finite(position))
            {
                headTracked = false;
                headHistory.Clear();
                headLocalVelocity = Vector3.zero;
                return;
            }

            headTracked = true;
            lastHeadTime = time;
            headLocalPosition = position;
            headHistory.Add(time, position, Quaternion.identity);
            headHistory.Estimate(tuning.poseHistorySeconds, false, out Vector3 velocity, out Vector3 ignored);
            headLocalVelocity = PhysicsMath.Bounded(velocity, tuning.maximumTrackingSpeed);
        }

        // Public injection permits deterministic tests without an XR device.
        public void Feed(double time, Vector3 position, Quaternion rotation, float gripAxis, bool valid)
        {
            if (!tuning)
            {
                return;
            }

            valid &= PhysicsMath.Finite(time)
                && PhysicsMath.Finite(position)
                && PhysicsMath.Finite(rotation)
                && rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w > 0.0001f;
            if (!valid)
            {
                tracked = engaged = gripArmed = false;
                history.Clear();
                headHistory.Clear();
                hadPose = false;
                localVelocity = localAngularVelocity = headLocalVelocity = Vector3.zero;
                return;
            }

            discontinuity = !hadPose || (position - localPosition).magnitude > tuning.trackingJumpDistance;
            if (discontinuity)
            {
                history.Clear();
                gripArmed = false;
                engaged = false;
            }

            localPosition = position;
            localRotation = rotation.normalized;
            lastValidTime = time;
            tracked = hadPose = true;
            grip = Mathf.Clamp01(PhysicsMath.Finite(gripAxis) ? gripAxis : 0f);
            if (grip < tuning.gripDisengage)
            {
                gripArmed = true;
            }

            engaged = gripArmed && (engaged ? grip > tuning.gripDisengage : grip >= tuning.gripEngage);
            history.Add(time, localPosition, localRotation);
            history.Estimate(tuning.poseHistorySeconds, false, out Vector3 v, out Vector3 w);
            localVelocity = PhysicsMath.Bounded(v, tuning.maximumTrackingSpeed);
            localAngularVelocity = PhysicsMath.Bounded(w, tuning.maximumTrackingSpin);
        }

        private void FixedUpdate()
        {
            if (tuning && Time.unscaledTimeAsDouble - lastHeadTime > tuning.lostPoseGrace)
            {
                headTracked = false;
            }

            if (tuning && Time.unscaledTimeAsDouble - lastValidTime > tuning.lostPoseGrace)
            {
                tracked = engaged = gripArmed = false;
                history.Clear();
                headHistory.Clear();
                hadPose = false;
                localVelocity = localAngularVelocity = headLocalVelocity = Vector3.zero;
            }
        }

        private void OnDisable()
        {
            headTracked = false;
            tracked = engaged = gripArmed = hadPose = false;
            history.Clear();
            headHistory.Clear();
            localVelocity = localAngularVelocity = headLocalVelocity = Vector3.zero;
        }
    }
}
