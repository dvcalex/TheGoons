using System;
using UnityEngine;

namespace MechLab
{
    public sealed class ImpactResponseSolver : MonoBehaviour
    {
        public MechTuning tuning;
        public ContactStrikeSensor sensor;
        public event Action<StrikePacket> Resolved;

        private void OnEnable()
        {
            if (sensor)
            {
                sensor.Strike += Resolve;
            }
        }

        private void OnDisable()
        {
            if (sensor)
            {
                sensor.Strike -= Resolve;
            }
        }

        private void Resolve(StrikePacket packet)
        {
            if (!tuning || !PhysicsMath.Finite(packet.closingSpeed) || !PhysicsMath.Finite(packet.effectiveMass))
            {
                return;
            }

            AttachmentTuning item = packet.source.attachment;
            float multiplier = item ? Mathf.Max(0f, item.impulseMultiplier) : 1f;
            float desired = packet.effectiveMass * packet.closingSpeed * tuning.impulseScale * multiplier;
            // Unity already supplied a contact impulse. Add only a bounded exaggeration budget.
            packet.addedImpulse = Mathf.Clamp(desired - packet.solverImpulse, 0f, tuning.maximumExtraImpulse);
            if (packet.receiver && !packet.receiver.isKinematic)
            {
                packet.addedImpulse = Mathf.Min(packet.addedImpulse, packet.receiver.mass * tuning.maximumTargetDeltaSpeed);
                packet.receiver.AddForceAtPosition(packet.direction * packet.addedImpulse, packet.point, ForceMode.Impulse);
                packet.source.wrist.AddForceAtPosition(-packet.direction * packet.addedImpulse, packet.point, ForceMode.Impulse);
            }
            else
            {
                packet.addedImpulse = 0f;
            }

            packet.damage = tuning.fullStrikeDamage
                * Mathf.Pow(Mathf.Clamp01(packet.quality), tuning.damagePower)
                * (item ? Mathf.Max(0f, item.damageMultiplier) : 1f);
            if (packet.receiver)
            {
                ReactionBody reaction = packet.receiver.GetComponentInParent<ReactionBody>();
                if (reaction)
                {
                    reaction.Accept(packet);
                }
            }

            Resolved?.Invoke(packet);
        }
    }
}
