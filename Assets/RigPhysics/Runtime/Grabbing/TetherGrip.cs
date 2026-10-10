using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-120)]
    public sealed class TetherGrip : MonoBehaviour
    {
        public MechTuning tuning;
        public ServoLimb limb;
        public Rigidbody heldBody { get; private set; }
        public bool holding => heldBody;
        public float anchorError { get; private set; }
        public float supportShare => holding && owners.TryGetValue(heldBody, out var set) ? 1f / Mathf.Max(1, set.Count) : 0f;
        public float supportedMass => holding ? heldBody.mass * supportShare : 0f;
        public event System.Action<bool> GripChanged;
        private ConfigurableJoint tether;
        private readonly Collider[] candidates = new Collider[32];
        private readonly PoseWindow objectHistory = new PoseWindow();
        private float nextGrabTime;
        private static readonly Dictionary<Rigidbody, HashSet<TetherGrip>> owners = new Dictionary<Rigidbody, HashSet<TetherGrip>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwners() => owners.Clear();

        public static void RelayContact(Rigidbody body, float strength, Vector3 point, Vector3 normal)
        {
            if (!body || !owners.TryGetValue(body, out var set)) return;
            foreach (var owner in set) {
                var feedback = owner ? owner.GetComponent<MechanicalFeedback>() : null;
                if (feedback) feedback.Contact(strength, point, normal, false);
                if (owner && owner.limb) owner.limb.DisruptAfterImpact(strength);
            }
        }

        private void FixedUpdate()
        {
            if (!tuning || !limb || !limb.wrist) return;
            if (!ReferenceEquals(heldBody, null) && !heldBody) Release(false);
            bool pressed = limb.operational && limb.input.engaged;
            if (holding && tether) objectHistory.Add(Time.fixedTimeAsDouble, heldBody.position, heldBody.rotation);
            if (holding && (!pressed || !tether)) Release(limb.operational && !limb.input.discontinuity && tether);
            if (pressed && !holding && Time.time >= nextGrabTime) TryGrab();
            if (!holding || !tether) return;
            objectHistory.Add(Time.fixedTimeAsDouble, heldBody.position, heldBody.rotation);
            Vector3 objectAnchor = heldBody.transform.TransformPoint(tether.anchor);
            Vector3 handAnchor = limb.wrist.transform.TransformPoint(tether.connectedAnchor);
            anchorError = Vector3.Distance(objectAnchor, handAnchor);
            if (anchorError > tuning.grabBreakError) { Release(false); return; }
            float mass = supportedMass;
            if (heldBody.useGravity) heldBody.AddForce(-Physics.gravity * mass * Mathf.Clamp01(tuning.armGravitySupport * limb.authority), ForceMode.Force);
            JointDrive linear = PhysicsMath.Drive(mass, tuning.grabFrequency, tuning.grabDampingRatio,
                tuning.maximumGrabForce, Time.fixedDeltaTime);
            tether.xDrive = tether.yDrive = tether.zDrive = linear;
            Vector3 inertia = heldBody.inertiaTensor;
            float anchorInertia = ((inertia.x + inertia.y + inertia.z) / 3f
                + heldBody.mass * (heldBody.worldCenterOfMass - objectAnchor).sqrMagnitude) * supportShare;
            tether.slerpDrive = PhysicsMath.Drive(anchorInertia,
                tuning.grabFrequency, tuning.grabDampingRatio, tuning.maximumGrabTorque, Time.fixedDeltaTime);
            PhysicsMath.Limit(heldBody, tuning.maximumThrowSpeed, tuning.maximumThrowSpin);
        }

        public bool TryGrab()
        {
            if (holding || !limb.operational) return false;
            int count = Physics.OverlapSphereNonAlloc(limb.wrist.position, tuning.grabRadius, candidates,
                tuning.grabbableLayers, QueryTriggerInteraction.Ignore);
            Rigidbody nearest = null; float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++) {
                Collider c = candidates[i]; Rigidbody body = c ? c.attachedRigidbody : null;
                if (!body || body.isKinematic || body == limb.wrist || body == limb.chassis
                    || body.mass > tuning.maximumGrabMass) continue;
                float d = (c.ClosestPoint(limb.wrist.position) - limb.wrist.position).sqrMagnitude;
                if (d < distance) { distance = d; nearest = body; }
            }
            if (!nearest) return false;
            heldBody = nearest;
            tether = heldBody.gameObject.AddComponent<ConfigurableJoint>();
            tether.connectedBody = limb.wrist;
            tether.autoConfigureConnectedAnchor = false;
            tether.anchor = heldBody.transform.InverseTransformPoint(limb.wrist.position);
            tether.connectedAnchor = Vector3.zero;
            tether.xMotion = tether.yMotion = tether.zMotion = ConfigurableJointMotion.Free;
            tether.angularXMotion = tether.angularYMotion = tether.angularZMotion = ConfigurableJointMotion.Free;
            tether.rotationDriveMode = RotationDriveMode.Slerp;
            tether.enableCollision = false;
            if (!owners.TryGetValue(heldBody, out HashSet<TetherGrip> set)) {
                set = new HashSet<TetherGrip>(); owners.Add(heldBody, set);
            }
            set.Add(this);
            objectHistory.Clear();
            objectHistory.Add(Time.fixedTimeAsDouble, heldBody.position, heldBody.rotation);
            GripChanged?.Invoke(true);
            return true;
        }

        public void Release(bool throwObject)
        {
            Rigidbody released = heldBody;
            // A destroyed Unity object still has a managed identity in the ownership map.
            if (ReferenceEquals(released, null)) return;
            if (tether) {
                tether.xDrive = tether.yDrive = tether.zDrive = tether.slerpDrive = default;
                tether.connectedBody = null;
                Destroy(tether);
            }
            bool lastOwner = true;
            if (owners.TryGetValue(released, out HashSet<TetherGrip> set)) {
                set.Remove(this); lastOwner = set.Count == 0;
                if (lastOwner) owners.Remove(released);
            }
            Vector3 v, w;
            bool estimated = tuning.throwUsesFastestHistory
                ? objectHistory.EstimatePeak(tuning.throwHistorySeconds, out v, out w)
                : objectHistory.Estimate(tuning.throwHistorySeconds, false, out v, out w);
            if (released && lastOwner && throwObject && estimated) {
                // Actual object history already includes chassis motion and physical lag.
                released.linearVelocity = PhysicsMath.Bounded(v * tuning.throwBoost, tuning.maximumThrowSpeed);
                released.angularVelocity = PhysicsMath.Bounded(w, tuning.maximumThrowSpin);
            }
            if (released) PhysicsMath.Limit(released, tuning.maximumThrowSpeed, tuning.maximumThrowSpin);
            heldBody = null; tether = null; objectHistory.Clear(); anchorError = 0f;
            nextGrabTime = Time.time + .15f;
            GripChanged?.Invoke(false);
        }

        private void OnDisable() { Release(false); }
        private void OnJointBreak(float force) { Release(false); }
    }
}
