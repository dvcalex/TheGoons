using UnityEngine;
using UnityEngine.XR;

namespace MechLab
{
    // Reads already-tracked transforms: no dependency on a particular XR Origin or XRI version.
    [DefaultExecutionOrder(100)]
    public sealed class OpenXRTransformInput : MonoBehaviour
    {
        public MechRig rig;
        [Tooltip("Common tracking-space parent of the camera/controllers (usually Camera Offset). Keep unit scale.")]
        public Transform trackingSpace;
        public Transform head;
        public Transform leftController;
        public Transform rightController;
        [Tooltip("Read grip/tracking flags from Unity XR. Disable when your own input adapter calls FeedPoses.")]
        public bool readDeviceInput = true;
        private bool focused = true;
        private bool paused;

        private void Update()
        {
            if (!readDeviceInput || !rig || !trackingSpace || !head || !leftController || !rightController)
            {
                return;
            }

            var h = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            l.TryGetFeatureValue(CommonUsages.grip, out float lg);
            r.TryGetFeatureValue(CommonUsages.grip, out float rg);
            bool present = !h.TryGetFeatureValue(CommonUsages.userPresence, out bool worn) || worn;
            FeedPoses(lg, rg, Valid(h) && present, Valid(l), Valid(r));
        }

        private static bool Valid(InputDevice device)
        {
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        public void FeedPoses(float leftGrip, float rightGrip, bool headValid, bool leftValid, bool rightValid)
        {
            if (!rig || !trackingSpace || !head || !leftController || !rightController)
            {
                return;
            }

            rig.UseExternalInput(true);
            headValid &= focused && !paused;
            Feed(rig.leftHand, leftController, leftGrip, headValid, leftValid);
            Feed(rig.rightHand, rightController, rightGrip, headValid, rightValid);
        }

        private void Feed(PilotPoseStream input, Transform controller, float grip, bool headValid, bool handValid)
        {
            input.trackingSpace = trackingSpace;
            input.trackingOffset = Vector3.zero;
            double now = Time.unscaledTimeAsDouble;
            input.FeedHead(now, trackingSpace.InverseTransformPoint(head.position), headValid);
            input.Feed(
                now,
                trackingSpace.InverseTransformPoint(controller.position),
                Quaternion.Inverse(trackingSpace.rotation) * controller.rotation,
                grip,
                headValid && handValid
            );
        }

        private void Invalidate()
        {
            if (!rig)
            {
                return;
            }

            foreach (var input in new[]
            {
                rig.leftHand,
                rig.rightHand
            }

            )
            {
                if (input && input.useScriptedInput)
                {
                    input.FeedHead(Time.unscaledTimeAsDouble, Vector3.zero, false);
                    input.Feed(Time.unscaledTimeAsDouble, Vector3.zero, Quaternion.identity, 0f, false);
                }
            }
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value)
            {
                Invalidate();
            }
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (value)
            {
                Invalidate();
            }
        }

        private void OnDisable()
        {
            Invalidate();
        }
    }
}
