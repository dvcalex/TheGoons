using UnityEngine;

namespace MechLab
{
    // Sample after gameplay velocity assignments, before Unity resolves this physics step.
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ContactMotionSnapshot : MonoBehaviour
    {
        private Rigidbody body;
        private Vector3 velocity, spin, center;
        private double sampledTime = double.NegativeInfinity;
        private void Awake() { body = GetComponent<Rigidbody>(); }
        private void FixedUpdate() => Capture();
        public void Capture()
        {
            if (!body) body = GetComponent<Rigidbody>();
            velocity = body.linearVelocity; spin = body.angularVelocity; center = body.worldCenterOfMass;
            sampledTime = Time.fixedTimeAsDouble;
        }
        public bool TryPointVelocity(Vector3 point, out Vector3 result)
        {
            result = velocity + Vector3.Cross(spin, point - center);
            return isActiveAndEnabled && System.Math.Abs(Time.fixedTimeAsDouble - sampledTime) < 0.000001
                && PhysicsMath.Finite(result);
        }
        private void OnDisable() { sampledTime = double.NegativeInfinity; }
    }
}
