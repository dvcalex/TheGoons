using UnityEngine;

namespace MechLab
{
    public enum ReactionState { Ready, Staggered, Fallen, Recovering, Disabled }
    public sealed class ReactionBody : MonoBehaviour
    {
        public MechTuning tuning;
        public Rigidbody[] bodies;
        public ConfigurableJoint[] drivenJoints;
        public ReactionState state { get; private set; }
        public float health { get; private set; }
        private float timer;
        private JointDrive[] originalDrive;
        private void Start()
        {
            health = tuning.targetHealth;
            originalDrive = new JointDrive[drivenJoints == null ? 0 : drivenJoints.Length];
            for (int i = 0; i < originalDrive.Length; i++) if (drivenJoints[i]) originalDrive[i] = drivenJoints[i].slerpDrive;
        }
        public void Accept(StrikePacket strike)
        {
            health = Mathf.Max(0f, health - strike.damage);
            if (health <= 0f) { state = ReactionState.Disabled; timer = float.PositiveInfinity; }
            else if (strike.quality >= tuning.knockdownThreshold) { state = ReactionState.Fallen; timer = tuning.knockdownSeconds; }
            else if (strike.quality >= tuning.staggerThreshold && state != ReactionState.Fallen) {
                state = ReactionState.Staggered; timer = tuning.staggerSeconds;
            }
        }
        private void FixedUpdate()
        {
            if (!tuning || originalDrive == null) return;
            if (state != ReactionState.Disabled) {
                timer = Mathf.Max(0f, timer - Time.fixedDeltaTime);
                if (timer == 0f && (state == ReactionState.Fallen || state == ReactionState.Staggered)) {
                    state = ReactionState.Recovering; timer = tuning.staggerSeconds;
                } else if (timer == 0f && state == ReactionState.Recovering) state = ReactionState.Ready;
            }
            float fraction = state == ReactionState.Ready ? 1f : state == ReactionState.Recovering
                ? 1f - timer / Mathf.Max(tuning.staggerSeconds, 0.01f) : 0f;
            for (int i = 0; i < originalDrive.Length; i++) if (drivenJoints[i]) {
                JointDrive drive = originalDrive[i];
                drive.positionSpring *= fraction; drive.positionDamper *= fraction;
                drive.maximumForce *= fraction; drivenJoints[i].slerpDrive = drive;
            }
            if (bodies != null) foreach (Rigidbody body in bodies)
                PhysicsMath.Limit(body, tuning.enemyMaximumSpeed, tuning.enemyMaximumSpin);
        }
    }
}
