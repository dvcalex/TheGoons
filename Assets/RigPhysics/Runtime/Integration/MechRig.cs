using UnityEngine;
using UnityEngine.Events;

namespace MechLab
{
    // A small Inspector entry point for the baked rig; physics stays in the existing components.
    [DefaultExecutionOrder(-500)]
    public sealed class MechRig : MonoBehaviour
    {
        public MechTuning tuning;
        public HullMotor hull;
        public Transform trackingFloor;
        public PilotPoseStream leftHand;
        public PilotPoseStream rightHand;
        public ImpactResponseSolver[] impacts;
        [Tooltip("Raised once for each resolved strike. The value is proposed damage; connect your effects here.")]
        public UnityEvent<float> onStrike = new UnityEvent<float>();

        private void Awake()
        {
            // Suppress only this rig's internal collisions, preserving the game's layer matrix
            // and collisions between separate mech instances.
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    var a = colliders[i].attachedRigidbody;
                    var b = colliders[j].attachedRigidbody;
                    bool fists = a && b && a != b && a.GetComponent<ServoLimb>() && b.GetComponent<ServoLimb>();
                    Physics.IgnoreCollision(colliders[i], colliders[j], !fists);
                }
        }

        private void OnEnable()
        {
            if (impacts == null) return;
            foreach (ImpactResponseSolver impact in impacts) if (impact) impact.Resolved += Strike;
        }

        private void OnDisable()
        {
            if (impacts == null) return;
            foreach (ImpactResponseSolver impact in impacts) if (impact) impact.Resolved -= Strike;
        }

        private void Strike(StrikePacket packet) => onStrike.Invoke(packet.damage);

        public void UseExternalInput(bool external)
        {
            leftHand.useScriptedInput = rightHand.useScriptedInput = external;
        }
    }
}
