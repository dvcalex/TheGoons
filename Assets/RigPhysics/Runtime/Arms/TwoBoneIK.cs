using UnityEngine;

namespace MechLab
{
    public static class TwoBoneIK
    {
        // World-space, axis-independent solution. Clamping prevents straight-arm singularities.
        public static bool Solve(
            Vector3 root,
            Vector3 target,
            Vector3 hint,
            float upper,
            float lower,
            ref Vector3 previousBend,
            out Vector3 elbow,
            out Vector3 end
        )
        {
            elbow = end = root;
            if (!PhysicsMath.Finite(root)
                || !PhysicsMath.Finite(target)
                || !PhysicsMath.Finite(hint)
                || !PhysicsMath.Finite(upper)
                || !PhysicsMath.Finite(lower)
                || upper < .001f
                || lower < .001f)
            {
                return false;
            }

            Vector3 delta = target - root;
            Vector3 axis = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            float margin = Mathf.Min(upper, lower) * .001f;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + margin, upper + lower - margin);
            Vector3 bend = Vector3.ProjectOnPlane(hint - root, axis);
            if (bend.sqrMagnitude < .0001f)
            {
                bend = Vector3.ProjectOnPlane(previousBend, axis);
            }

            if (!PhysicsMath.Finite(bend) || bend.sqrMagnitude < .0001f)
            {
                bend = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) < .9f ? Vector3.down : Vector3.forward, axis);
            }

            bend.Normalize();
            previousBend = bend;
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            elbow = root + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            end = root + axis * distance;
            return true;
        }
    }
}
