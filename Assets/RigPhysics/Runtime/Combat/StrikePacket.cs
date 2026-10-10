using UnityEngine;

namespace MechLab
{
    public struct StrikePacket
    {
        public ServoLimb source;
        public Rigidbody receiver;
        public Vector3 point;
        public Vector3 direction;
        public float closingSpeed;
        public float quality;
        public float effectiveMass;
        public float solverImpulse;
        public float addedImpulse;
        public float damage;
    }
}
