using UnityEngine;

namespace MechLab
{
    // Every number below is an independent starting value, not a recovered game setting.
    [CreateAssetMenu(menuName = "Mech Lab/Simulation tuning")]
    public sealed class MechTuning : ScriptableObject
    {
        [Header("Simulation")]
        public float stepSeconds = 1f / 90f;
        public int solverPasses = 12;
        public int velocityPasses = 4;
        public float lostPoseGrace = 0.10f;
        public float trackingJumpDistance = 0.45f;
        public float reacquireSeconds = 0.20f;
        public float poseHistorySeconds = 0.09f;
        public float maximumTrackingSpeed = 12f;
        public float maximumTrackingSpin = 30f;
        public float gripEngage = 0.75f;
        public float gripDisengage = 0.55f;
        public float handPositionScale = 1.5f;
        public Vector3 trackingPivot = new Vector3(0f, 1.4f, 0f);
        public Vector3 shoulderFromFloor = new Vector3(0.55f, 1.45f, 0f);

        [Header("Physical arm")]
        public float limbReach = 1.3f;
        public float wristMass = 8f;
        public float segmentMass = 6f;
        public float segmentLength = 0.65f;
        public float segmentRadius = 0.09f;
        public float handRadius = 0.14f;
        public float positionFrequency = 7f;
        public float positionDampingRatio = 1f;
        public float rotationFrequency = 8f;
        public float rotationDampingRatio = 1f;
        public float maximumServoForce = 3500f;
        public float maximumServoTorque = 800f;
        [Range(0f, 1f)] public float armGravitySupport = 1f;
        public float maximumArmSpeed = 12f;
        public float maximumArmSpin = 25f;
        public float armLinearDrag = 0.1f;
        public float armAngularDrag = 0.8f;
        public float targetSpeed = 10f;
        public float targetSpinDegrees = 1000f;
        public float limbLimitSpring = 4000f;
        public float limbLimitDamping = 120f;
        public float shoulderAngleLimit = 90f;
        public float elbowAngleLimit = 135f;
        public float wristAngleLimit = 80f;

        [Header("Chassis")]
        [Tooltip("Hold the pilot chassis orientation against arm/contact torque. Explicit cockpit turns still work. Disable for free-body experiments.")]
        public bool lockBodyRotation = true;
        public float chassisMass = 160f;
        public float bodyRadius = 0.45f;
        public float bodyHeight = 1.7f;
        public float bodyLinearDrag = 0.2f;
        public float bodyAngularDrag = 2f;
        public float uprightFrequency = 2f;
        public float uprightDampingRatio = 1f;
        public float maximumUprightTorque = 1400f;
        public float maximumBodySpeed = 7f;
        public float maximumBodySpin = 3f;

        [Header("Surface pulling")]
        public LayerMask environmentLayers = 1 << 8;
        public LayerMask hittableLayers = (1 << 8) | (1 << 10);
        public LayerMask grabbableLayers = 1 << 10;
        public float pullVelocityGain = 1.5f;
        public float anchorPositionGain = 3f;
        [Range(0f, 1f)] public float anchorControlBlend = 0.75f;
        public bool projectPullToSurface = true;
        [Tooltip("Rotate projected slope/wall strokes onto the ground plane, following the recovered single-player anchor path.")]
        public bool rotateSurfacePullToGround = true;
        public float pullResponseSeconds = 0.12f;
        public float maximumPullAcceleration = 14f;
        public float maximumPullForce = 2300f;
        public float maximumPullSpeed = 5f;
        public float anchorBreakDistance = 0.65f;
        public bool verticalPulling = false;

        [Header("Strikes")]
        public float strikeHistorySeconds = 0.06f;
        public float minimumStrikeSpeed = 1.2f;
        public float fullStrikeSpeed = 7f;
        public float minimumSwingTravel = 0.08f;
        public float rearmSpeed = 0.7f;
        public float separationSeconds = 0.06f;
        public float strikeCooldown = 0.22f;
        public float strikePower = 1.6f;
        public float maximumEffectiveMass = 45f;
        public float impulseScale = 0.7f;
        public float maximumExtraImpulse = 100f;
        public float maximumTargetDeltaSpeed = 6f;
        public float fullStrikeDamage = 25f;
        public float damagePower = 1.2f;
        public float recoverySeconds = 0.11f;
        public float impactDriveFraction = 0.28f;

        [Header("Grab")]
        public float grabRadius = 0.23f;
        public float maximumGrabMass = 50f;
        public float grabFrequency = 9f;
        public float grabDampingRatio = 1f;
        public float maximumGrabForce = 2500f;
        public float maximumGrabTorque = 900f;
        public float grabBreakError = 0.9f;
        public float maximumThrowSpeed = 10f;
        public float maximumThrowSpin = 20f;
        public float throwBoost = 1f;
        public bool throwUsesFastestHistory = true;
        public float throwHistorySeconds = 0.1f;

        [Header("Enemy and feedback")]
        public float targetHealth = 100f;
        public float staggerThreshold = 0.4f;
        public float knockdownThreshold = 0.8f;
        public float staggerSeconds = 0.5f;
        public float knockdownSeconds = 1.5f;
        public float enemyMaximumSpeed = 10f;
        public float enemyMaximumSpin = 15f;
        public float maximumHapticAmplitude = 0.85f;
        public float hapticSeconds = 0.055f;
        public float audioGain = 0.65f;
        public float placeholderToneHz = 75f;
        public float placeholderAudioDecay = 25f;
        public float placeholderAudioSeconds = 0.2f;
        public float flashSeconds = 0.06f;
        public float telemetryHz = 30f;
        public float demoStrokeMeters = 0.15f;
        public float demoStrokeHz = 0.3f;
        public Vector3 demoLeftPose = new Vector3(-0.3f, 1.25f, 0.6f);
        public Vector3 demoRightPose = new Vector3(0.3f, 1.25f, 0.6f);
        public float demoPropMass = 5f;
        public float demoPropMassStep = 10f;
        public float demoEnemyBaseMass = 20f;
        public float demoEnemyTorsoMass = 15f;
        public float demoEnemyMotorInertia = 3f;
        public float demoEnemyMotorHz = 3f;
        public float demoEnemyMotorTorque = 200f;
    }
}
