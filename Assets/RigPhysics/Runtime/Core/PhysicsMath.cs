using UnityEngine;

namespace MechLab
{
    public static class PhysicsMath
    {
        public static bool Finite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        public static bool Finite(Vector3 v)
        {
            return Finite(v.x) && Finite(v.y) && Finite(v.z);
        }

        public static bool Finite(Quaternion q)
        {
            return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        }

        public static Vector3 Bounded(Vector3 v, float maximum)
        {
            return Finite(v) && Finite(maximum) ? Vector3.ClampMagnitude(v, Mathf.Max(0f, maximum)) : Vector3.zero;
        }

        public static JointDrive Drive(float mass, float frequency, float dampingRatio, float cap, float dt)
        {
            if (!Finite(mass) || !Finite(frequency) || !Finite(dampingRatio) || !Finite(cap) || !Finite(dt))
            {
                return default;
            }

            // Independent mass-scaled spring. Bound frequency relative to simulation rate.
            float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 0f, 0.15f / Mathf.Max(dt, 0.001f));
            float inertia = Mathf.Max(0.001f, mass);
            return new JointDrive
            {
                positionSpring = inertia * w * w,
                positionDamper = 2f * inertia * Mathf.Max(0f, dampingRatio) * w,
                maximumForce = Mathf.Max(0f, cap)
            };
        }

        public static Vector3 AngularVelocity(Quaternion earlier, Quaternion later, float dt)
        {
            if (dt <= 0.00001f || !Finite(earlier) || !Finite(later))
            {
                return Vector3.zero;
            }

            Quaternion d = later * Quaternion.Inverse(earlier);
            if (d.w < 0f)
            {
                d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
            }

            d.ToAngleAxis(out float degrees, out Vector3 axis);
            return Finite(axis) ? axis * (degrees * Mathf.Deg2Rad / dt) : Vector3.zero;
        }

        public static float EffectiveMass(Rigidbody body, Vector3 point, Vector3 normal, float cap)
        {
            if (!body || body.isKinematic)
            {
                return float.PositiveInfinity;
            }

            Vector3 cross = Vector3.Cross(point - body.worldCenterOfMass, normal);
            Quaternion axes = body.rotation * body.inertiaTensorRotation;
            Vector3 local = Quaternion.Inverse(axes) * cross;
            Vector3 tensor = body.inertiaTensor;
            float inverse = 1f / Mathf.Max(body.mass, 0.001f)
                + local.x * local.x / Mathf.Max(tensor.x, 0.001f)
                + local.y * local.y / Mathf.Max(tensor.y, 0.001f)
                + local.z * local.z / Mathf.Max(tensor.z, 0.001f);
            return Mathf.Min(Mathf.Max(cap, 0.001f), 1f / inverse);
        }

        public static float PairMass(float a, float b)
        {
            if (float.IsPositiveInfinity(a))
            {
                return b;
            }

            if (float.IsPositiveInfinity(b))
            {
                return a;
            }

            return 1f / (1f / Mathf.Max(a, 0.001f) + 1f / Mathf.Max(b, 0.001f));
        }

        public static void Limit(Rigidbody body, float speed, float spin)
        {
            if (!body || body.isKinematic)
            {
                return;
            }

            body.linearVelocity = Bounded(body.linearVelocity, speed);
            body.angularVelocity = Bounded(body.angularVelocity, spin);
        }
    }
}
