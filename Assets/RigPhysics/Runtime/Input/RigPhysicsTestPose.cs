using UnityEngine;

namespace MechLab
{
    // Explicit scene-only input source for testing the real motors without XR devices.
    [DefaultExecutionOrder(-400)]
    public sealed class RigPhysicsTestPose : MonoBehaviour
    {
        public MechRig rig;

        private ServoLimb[] limbs;
        private Transform[] handTargets;
        private bool[] originalScriptedInput;
        private Transform headTarget;
        private bool slewWasEnabled;
        private bool initialized;

        private void OnEnable()
        {
            if (rig == null || rig.trackingFloor == null || rig.physicalHead == null)
            {
                enabled = false;
                return;
            }

            limbs = rig.GetComponentsInChildren<ServoLimb>();
            handTargets = new Transform[limbs.Length];
            originalScriptedInput = new bool[limbs.Length];
            headTarget = CreateTarget("Test Head Target", rig.physicalHead.transform);

            for (int i = 0; i < limbs.Length; i++)
            {
                handTargets[i] = CreateTarget("Test " + limbs[i].name + " Target", limbs[i].transform);
                originalScriptedInput[i] = limbs[i].input.useScriptedInput;
                limbs[i].input.useScriptedInput = true;
            }

            if (rig.slew != null)
            {
                slewWasEnabled = rig.slew.enabled;
                rig.slew.enabled = false;
            }

            initialized = true;
            FeedTargets();
        }

        private Transform CreateTarget(string label, Transform source)
        {
            var target = new GameObject(label).transform;
            target.SetPositionAndRotation(source.position, source.rotation);
            target.SetParent(rig.trackingFloor, true);
            return target;
        }

        private void FixedUpdate()
        {
            if (initialized)
            {
                FeedTargets();
            }
        }

        private void FeedTargets()
        {
            double now = Time.unscaledTimeAsDouble;
            for (int i = 0; i < limbs.Length; i++)
            {
                PilotPoseStream input = limbs[i].input;
                Transform space = input.trackingSpace;
                Vector3 headPosition = space.InverseTransformPoint(headTarget.position);
                input.FeedHead(now, headPosition - input.trackingOffset, true);

                // Invert the normal reach amplification so the target is the physical fist pose.
                Vector3 pivot = input.tuning.trackingPivot + input.HeadTranslation;
                Vector3 handPosition = space.InverseTransformPoint(handTargets[i].position);
                Vector3 controllerPosition = pivot
                    + (handPosition - pivot) / Mathf.Max(0.001f, input.tuning.handPositionScale)
                    - input.trackingOffset;
                Quaternion rotation = Quaternion.Inverse(space.rotation) * handTargets[i].rotation;
                input.Feed(now, controllerPosition, rotation, 0f, true);
            }

            Transform headSpace = rig.physicalHead.input.trackingSpace;
            rig.physicalHead.localRotation = Quaternion.Inverse(headSpace.rotation) * headTarget.rotation;
        }

        private void OnDisable()
        {
            if (!initialized)
            {
                return;
            }

            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] != null && limbs[i].input != null)
                {
                    PilotPoseStream input = limbs[i].input;
                    input.FeedHead(Time.unscaledTimeAsDouble, Vector3.zero, false);
                    input.Feed(Time.unscaledTimeAsDouble, Vector3.zero, Quaternion.identity, 0f, false);
                    input.useScriptedInput = originalScriptedInput[i];
                }

                if (handTargets[i] != null)
                {
                    Destroy(handTargets[i].gameObject);
                }
            }

            if (headTarget != null)
            {
                Destroy(headTarget.gameObject);
            }

            if (rig != null && rig.slew != null)
            {
                rig.slew.enabled = slewWasEnabled;
            }

            initialized = false;
        }
    }
}
