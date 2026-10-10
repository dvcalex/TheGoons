using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-100)]
    public sealed class SurfacePullMotor : MonoBehaviour
    {
        public MechTuning tuning;
        public HullMotor hull;
        public ServoLimb[] limbs;
        public SurfaceContacts[] surfaces;
        public TetherGrip[] grips;
        public int engagedArms { get; private set; }
        public Vector3 pullAcceleration { get; private set; }
        private bool[] anchored;
        private Vector3[] anchor;
        private Vector3[] headAtAnchor, normal, targetAtAnchor;
        private void Start() {
            anchored = new bool[limbs.Length]; anchor = new Vector3[limbs.Length];
            headAtAnchor = new Vector3[limbs.Length]; normal = new Vector3[limbs.Length];
            targetAtAnchor = new Vector3[limbs.Length];
        }
        private void FixedUpdate()
        {
            if (!tuning || !hull || anchored == null) return;
            engagedArms = 0;
            Vector3 requestedVelocity = Vector3.zero;
            for (int i = 0; i < limbs.Length; i++) {
                ServoLimb limb = limbs[i];
                bool eligible = limb && limb.operational && limb.input.engaged && !limb.input.discontinuity
                    && !(grips != null && i < grips.Length && grips[i] && grips[i].holding);
                if (!eligible) { anchored[i] = false; continue; }
                if (!anchored[i] && i < surfaces.Length && surfaces[i]
                    && surfaces[i].TrySurface(out Vector3 p, out Vector3 n)) {
                    anchored[i] = true;
                    // Physical center controls break distance; virtual pose establishes zero initial error.
                    anchor[i] = limb.wrist.position;
                    targetAtAnchor[i] = limb.input.WorldPosition;
                    normal[i] = n;
                    headAtAnchor[i] = limb.input.headLocalPosition;
                }
                if (!anchored[i]) continue;
                if (Vector3.Distance(limb.wrist.position, anchor[i]) > tuning.anchorBreakDistance) {
                    anchored[i] = false; continue;
                }
                // Local pose derivative excludes motion caused by moving the simulation origin.
                Vector3 handMotion = limb.input.trackingSpace.rotation
                    * (limb.input.localVelocity - limb.input.headLocalVelocity) * tuning.handPositionScale;
                Vector3 roomMotion = limb.input.trackingSpace.TransformVector(
                    limb.input.headLocalPosition - headAtAnchor[i]);
                Vector3 error = targetAtAnchor[i] - (limb.input.WorldPosition - roomMotion);
                if (tuning.projectPullToSurface) {
                    if (tuning.rotateSurfacePullToGround) {
                        error = PullControlMath.SurfaceToGround(error, normal[i]);
                        handMotion = PullControlMath.SurfaceToGround(handMotion, normal[i]);
                    } else {
                        error = Vector3.ProjectOnPlane(error, normal[i]);
                        handMotion = Vector3.ProjectOnPlane(handMotion, normal[i]);
                    }
                }
                requestedVelocity += PullControlMath.DesiredVelocity(error, handMotion,
                    tuning.anchorPositionGain, tuning.pullVelocityGain, tuning.anchorControlBlend, tuning.maximumPullSpeed);
                engagedArms++;
            }
            if (engagedArms == 0) { pullAcceleration = Vector3.zero; return; }
            requestedVelocity = PhysicsMath.Bounded(requestedVelocity, tuning.maximumPullSpeed);
            Vector3 actual = hull.body.linearVelocity;
            if (!tuning.verticalPulling) { requestedVelocity.y = 0f; actual.y = 0f; }
            pullAcceleration = PhysicsMath.Bounded((requestedVelocity - actual) / Mathf.Max(0.02f, tuning.pullResponseSeconds),
                tuning.maximumPullAcceleration);
            hull.RequestForce(pullAcceleration * hull.body.mass);
        }
    }
}
