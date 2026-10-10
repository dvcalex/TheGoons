using UnityEngine;

namespace MechLab
{
    [CreateAssetMenu(menuName = "Mech Lab/Attachment tuning")]
    public sealed class AttachmentTuning : ScriptableObject
    {
        public float addedMass = 0f;
        public float damageMultiplier = 1f;
        public float impulseMultiplier = 1f;
        public float authorityMultiplier = 1f;
        public float radiusMultiplier = 1f;
    }
}
