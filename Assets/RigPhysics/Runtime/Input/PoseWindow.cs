using UnityEngine;

namespace MechLab
{
    public struct PoseSample
    {
        public double time;
        public Vector3 position;
        public Quaternion rotation;
    }

    public sealed class PoseWindow
    {
        private readonly PoseSample[] buffer;
        private int head;
        private int count;

        public PoseWindow(int capacity = 128)
        {
            buffer = new PoseSample[Mathf.Max(4, capacity)];
        }

        public void Clear()
        {
            head = count = 0;
        }

        private PoseSample At(int newestOffset)
        {
            return buffer[(head - 1 - newestOffset + buffer.Length) % buffer.Length];
        }

        public void Add(double time, Vector3 position, Quaternion rotation)
        {
            if (!PhysicsMath.Finite(time)
                || !PhysicsMath.Finite(position)
                || !PhysicsMath.Finite(rotation)
                || rotation.x * rotation.x
                    + rotation.y * rotation.y
                    + rotation.z * rotation.z
                    + rotation.w * rotation.w < 0.0001f)
            {
                return;
            }

            if (count > 0 && time <= At(0).time)
            {
                return;
            }

            buffer[head] = new PoseSample
            {
                time = time,
                position = position,
                rotation = rotation
            };
            head = (head + 1) % buffer.Length;
            count = Mathf.Min(count + 1, buffer.Length);
        }

        public bool Estimate(float seconds, bool fastest, out Vector3 linear, out Vector3 angular)
        {
            linear = angular = Vector3.zero;
            if (count < 2 || !PhysicsMath.Finite(seconds) || seconds <= 0f)
            {
                return false;
            }

            PoseSample newest = At(0);
            PoseSample oldest = newest;
            float largest = -1f;
            for (int i = 1; i < count; i++)
            {
                PoseSample previous = At(i);
                PoseSample next = At(i - 1);
                if (newest.time - previous.time > seconds && i > 1)
                {
                    break;
                }

                float dt = (float)(next.time - previous.time);
                if (dt <= 0.00001f)
                {
                    continue;
                }

                Vector3 v = (next.position - previous.position) / dt;
                if (v.sqrMagnitude > largest)
                {
                    largest = v.sqrMagnitude;
                    if (fastest)
                    {
                        linear = v;
                        angular = PhysicsMath.AngularVelocity(previous.rotation, next.rotation, dt);
                    }
                }

                oldest = previous;
            }

            float span = (float)(newest.time - oldest.time);
            if (span <= 0.00001f)
            {
                return false;
            }

            if (!fastest)
            {
                linear = (newest.position - oldest.position) / span;
                angular = PhysicsMath.AngularVelocity(oldest.rotation, newest.rotation, span);
            }

            return PhysicsMath.Finite(linear) && PhysicsMath.Finite(angular);
        }

        // Smooth around the strongest recent stroke rather than selecting one noisy frame.
        public bool EstimatePeak(float seconds, out Vector3 linear, out Vector3 angular)
        {
            linear = angular = Vector3.zero;
            if (count < 2 || !PhysicsMath.Finite(seconds) || seconds <= 0f)
            {
                return false;
            }

            double newest = At(0).time;
            double peakTime = newest;
            float best = -1f;
            for (int i = 1; i < count; i++)
            {
                var a = At(i);
                var b = At(i - 1);
                if (newest - a.time > seconds)
                {
                    break;
                }

                float dt = (float)(b.time - a.time);
                if (dt <= .00001f)
                {
                    continue;
                }

                float speed = ((b.position - a.position) / dt).sqrMagnitude;
                if (speed > best)
                {
                    best = speed;
                    peakTime = (a.time + b.time) * .5;
                }
            }

            if (best < 0f)
            {
                return false;
            }

            float sum = 0f;
            for (int i = 1; i < count; i++)
            {
                var a = At(i);
                var b = At(i - 1);
                if (newest - a.time > seconds)
                {
                    break;
                }

                float dt = (float)(b.time - a.time);
                float proximity = Mathf.Clamp01(1f - (float)System.Math.Abs((a.time + b.time) * .5 - peakTime) / seconds);
                float weight = proximity * proximity * dt;
                if (dt <= .00001f || weight <= 0f)
                {
                    continue;
                }

                linear += (b.position - a.position) / dt * weight;
                angular += PhysicsMath.AngularVelocity(a.rotation, b.rotation, dt) * weight;
                sum += weight;
            }

            if (sum <= .00001f)
            {
                return false;
            }

            linear /= sum;
            angular /= sum;
            return PhysicsMath.Finite(linear) && PhysicsMath.Finite(angular);
        }
    }
}
