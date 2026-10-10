using System.Collections.Generic;
using UnityEngine;

namespace MechLab
{
    public sealed class SurfaceContacts : MonoBehaviour
    {
        public MechTuning tuning;
        private struct Surface { public Vector3 point, normal; }
        private readonly Dictionary<Collider, Surface> environment = new Dictionary<Collider, Surface>();
        public bool TryPoint(out Vector3 point)
        {
            return TrySurface(out point, out Vector3 ignored);
        }
        public bool TrySurface(out Vector3 point, out Vector3 normal)
        {
            foreach (var entry in environment) if (entry.Key && entry.Key.enabled) {
                point = entry.Value.point; normal = entry.Value.normal; return true;
            }
            point = normal = Vector3.zero; return false;
        }
        private void OnCollisionEnter(Collision hit) => Keep(hit);
        private void OnCollisionStay(Collision hit) => Keep(hit);
        private void Keep(Collision hit)
        {
            // Prop and scenery masks can overlap in an imported kit. Anchors are only
            // supported on stationary scenery, never on dynamic or kinematic bodies.
            if (!tuning || hit.rigidbody || (tuning.environmentLayers.value & (1 << hit.gameObject.layer)) == 0 || hit.contactCount == 0) return;
            ContactPoint contact = hit.GetContact(0);
            environment[hit.collider] = new Surface { point = contact.point, normal = contact.normal };
        }
        private void OnCollisionExit(Collision hit) => environment.Remove(hit.collider);
        private void OnDisable() => environment.Clear();
    }
}
