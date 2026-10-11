using System;
using System.Collections.Generic;
using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-150)]
    public sealed class ContactStrikeSensor : MonoBehaviour
    {
        public MechTuning tuning;
        public ServoLimb limb;
        public TetherGrip grip;
        public event Action<StrikePacket> Strike;

        public float latestQuality
        {
            get;
            private set;
        }

        public int registeredStrikes
        {
            get;
            private set;
        }

        public int contactEvents
        {
            get;
            private set;
        }
        public string lastDecision
        {
            get;
            private set;
        } = "no contact";
        public string lastCollider
        {
            get;
            private set;
        } = "none";

        public float lastContactTravel
        {
            get;
            private set;
        }

        public float bestClosingSpeed
        {
            get;
            private set;
        }

        public float lastCollisionSpeed
        {
            get;
            private set;
        }

        public float lastTargetBeforeSpeed
        {
            get;
            private set;
        }

        public float lastTargetAfterSpeed
        {
            get;
            private set;
        }

        private struct Motion
        {
            public double time;
            public Vector3 velocity;
            public Vector3 spin;
            public Vector3 center;
        }

        private readonly List<Motion> motion = new List<Motion>(32);
        private readonly HashSet<Collider> touching = new HashSet<Collider>();
        private readonly Dictionary<int, StrikePacket> pending = new Dictionary<int, StrikePacket>();
        private bool consumed;
        private double lastStrike = -100;
        private double separatedSince;
        private float travel;

        private void FixedUpdate()
        {
            if (!tuning || !limb || !limb.wrist)
            {
                return;
            }

            double now = Time.fixedTimeAsDouble;
            int oldCount = touching.Count;
            touching.RemoveWhere(c => !c || !c.enabled);
            if (oldCount > 0 && touching.Count == 0)
            {
                separatedSince = now;
            }

            // Pending contacts retain the pre-solver velocity captured at their callback.
            if (pending.Count > 0 && !consumed && now - lastStrike >= tuning.strikeCooldown)
            {
                StrikePacket best = default;
                float score = 0f;
                foreach (StrikePacket item in pending.Values)
                {
                    if (item.quality > score)
                    {
                        best = item;
                        score = item.quality;
                    }
                }

                if (score > 0f && limb.operational && !(grip && grip.holding))
                {
                    consumed = true;
                    lastStrike = now;
                    latestQuality = score;
                    registeredStrikes++;
                    lastDecision = "registered";
                    limb.DisruptAfterImpact(score);
                    Strike?.Invoke(best);
                }
            }

            pending.Clear();
            if (!limb.operational)
            {
                motion.Clear();
                travel = 0f;
                consumed = true;
                return;
            }

            float speed = limb.wrist.linearVelocity.magnitude;
            if (touching.Count == 0 && speed < tuning.rearmSpeed && now - separatedSince >= tuning.separationSeconds)
            {
                consumed = false;
                travel = 0f;
            }

            // Travel excludes chassis velocity to reject passive body translation as a fresh swing.
            travel += (limb.wrist.linearVelocity - limb.chassis.GetPointVelocity(limb.wrist.position)).magnitude * Time.fixedDeltaTime;
            travel = Mathf.Min(travel, tuning.limbReach * 4f);
            motion.Add(new Motion
            {
                time = now,
                velocity = limb.wrist.linearVelocity,
                spin = limb.wrist.angularVelocity,
                center = limb.wrist.worldCenterOfMass
            });
            while (motion.Count > 1 && now - motion[0].time > tuning.strikeHistorySeconds)
            {
                motion.RemoveAt(0);
            }
        }

        private void OnCollisionEnter(Collision hit)
        {
            touching.Add(hit.collider);
            contactEvents++;
            lastCollider = hit.collider.name;
            lastContactTravel = travel;
            lastCollisionSpeed = hit.relativeVelocity.magnitude;
            if (!tuning || !limb || !limb.wrist || !limb.operational)
            {
                lastDecision = "arm unavailable";
                return;
            }

            if (consumed)
            {
                lastDecision = "swing already consumed";
                return;
            }

            if (grip && grip.holding)
            {
                lastDecision = "holding an object";
                return;
            }

            if (motion.Count == 0)
            {
                lastDecision = "no velocity history";
                return;
            }

            if (travel < tuning.minimumSwingTravel)
            {
                lastDecision = "insufficient swing travel";
                return;
            }

            if ((tuning.hittableLayers.value & (1 << hit.gameObject.layer)) == 0)
            {
                lastDecision = "excluded layer";
                return;
            }

            Rigidbody target = hit.rigidbody;
            if (target == limb.chassis || target == limb.wrist)
            {
                lastDecision = "self contact";
                return;
            }

            ContactMotionSnapshot targetMotion = target ? target.GetComponent<ContactMotionSnapshot>() : null;
            StrikePacket best = default;
            for (int i = 0; i < hit.contactCount; i++)
            {
                ContactPoint contact = hit.GetContact(i);
                Vector3 direction = -contact.normal;
                Vector3 v = Vector3.zero;
                foreach (Motion sample in motion)
                {
                    v += sample.velocity + Vector3.Cross(sample.spin, contact.point - sample.center);
                }

                v /= motion.Count;
                Vector3 targetVelocity = Vector3.zero;
                // Never subtract a post-solver target velocity from an approach-history sample.
                // Dynamic and moving kinematic receivers require a current pre-step snapshot.
                if (target && (!targetMotion || !targetMotion.TryPointVelocity(contact.point, out targetVelocity)))
                {
                    lastDecision = "receiver needs current ContactMotionSnapshot";
                    return;
                }

                lastTargetBeforeSpeed = targetVelocity.magnitude;
                lastTargetAfterSpeed = target ? target.GetPointVelocity(contact.point).magnitude : 0f;
                Vector3 relative = v - targetVelocity;
                float closing = Mathf.Max(0f, Vector3.Dot(relative, direction));
                bestClosingSpeed = Mathf.Max(bestClosingSpeed, closing);
                float quality = Mathf.Pow(
                    Mathf.InverseLerp(
                        tuning.minimumStrikeSpeed,
                        tuning.fullStrikeSpeed,
                        Mathf.Min(closing, tuning.maximumArmSpeed + tuning.maximumBodySpeed)
                    ),
                    Mathf.Max(0.1f, tuning.strikePower)
                );
                if (quality <= best.quality)
                {
                    continue;
                }

                float handMass = PhysicsMath.EffectiveMass(limb.wrist, contact.point, direction, tuning.maximumEffectiveMass);
                float targetMass = PhysicsMath.EffectiveMass(target, contact.point, direction, tuning.maximumEffectiveMass);
                best = new StrikePacket
                {
                    source = limb,
                    receiver = target,
                    point = contact.point,
                    direction = direction,
                    closingSpeed = closing,
                    quality = quality,
                    effectiveMass = Mathf.Min(tuning.maximumEffectiveMass, PhysicsMath.PairMass(handMass, targetMass)),
                    solverImpulse = Mathf.Max(0f, Vector3.Dot(hit.impulse, contact.normal))
                };
            }

            if (best.quality <= 0f)
            {
                lastDecision = "insufficient normal closing speed";
                return;
            }

            lastDecision = "queued";
            int key = target ? target.GetInstanceID() : hit.collider.GetInstanceID();
            if (!pending.TryGetValue(key, out StrikePacket previous))
            {
                pending[key] = best;
            }
            else
            {
                best.solverImpulse += previous.solverImpulse;
                if (previous.quality > best.quality)
                {
                    previous.solverImpulse = best.solverImpulse;
                    best = previous;
                }

                pending[key] = best;
            }
        }

        public void ResetDiagnostics()
        {
            contactEvents = 0;
            lastDecision = "no contact";
            lastCollider = "none";
            lastContactTravel = bestClosingSpeed = lastCollisionSpeed = lastTargetBeforeSpeed = lastTargetAfterSpeed = 0f;
        }

        public string DiagnosticSummary()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "contacts={0}, collider={1}, decision={2}, travel={3:F3}m, best closing={4:F2}m/s, collision speed={5:F2}m/s, target before/after={6:F2}/{7:F2}m/s",
                contactEvents,
                lastCollider,
                lastDecision,
                lastContactTravel,
                bestClosingSpeed,
                lastCollisionSpeed,
                lastTargetBeforeSpeed,
                lastTargetAfterSpeed
            );
        }

        private void OnCollisionExit(Collision hit)
        {
            touching.Remove(hit.collider);
            if (touching.Count == 0)
            {
                separatedSince = Time.fixedTimeAsDouble;
            }
        }

        private void OnDisable()
        {
            touching.Clear();
            pending.Clear();
            motion.Clear();
            consumed = true;
        }
    }
}
