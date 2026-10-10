using UnityEngine;

namespace MechLab
{
    public static class PullControlMath
    {
        // The recovered single-player anchor path removes normal motion, then
        // maps that surface's tangent plane onto the ground plane.
        public static Vector3 SurfaceToGround(Vector3 motion, Vector3 surfaceNormal)
        {
            if (!PhysicsMath.Finite(motion) || !PhysicsMath.Finite(surfaceNormal)
                || surfaceNormal.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector3 normal = surfaceNormal.normalized;
            return Quaternion.FromToRotation(normal, Vector3.up) * Vector3.ProjectOnPlane(motion, normal);
        }

        // Independent hybrid controller: anchor error supplies a persistent target,
        // while a measured stroke derivative can contribute immediate response.
        public static Vector3 DesiredVelocity(Vector3 anchorError, Vector3 strokeVelocity,
            float positionGain, float strokeGain, float blend, float maximum)
        {
            if (!PhysicsMath.Finite(positionGain) || !PhysicsMath.Finite(strokeGain) || !PhysicsMath.Finite(blend))
                return Vector3.zero;
            Vector3 fromPosition = PhysicsMath.Bounded(anchorError, float.MaxValue) * Mathf.Max(0f, positionGain);
            Vector3 fromStroke = -PhysicsMath.Bounded(strokeVelocity, float.MaxValue) * Mathf.Max(0f, strokeGain);
            return PhysicsMath.Bounded(Vector3.Lerp(fromStroke, fromPosition, Mathf.Clamp01(blend)), maximum);
        }
    }
}
