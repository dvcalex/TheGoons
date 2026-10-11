using UnityEngine;

namespace MechLab
{
    [DefaultExecutionOrder(-180)]
    [RequireComponent(typeof(ServoLimb))]
    public sealed class PhysicalArmIK : MonoBehaviour
    {
        public Transform elbowHint;
        [Tooltip("Body-local preferred bend direction when no explicit hint is assigned.")]
        public Vector3 bendDirection = new Vector3(-1f, -.7f, -.3f);
        [Min(0f)]
        public float stiffness = 100f;
        [Min(0f)]
        public float damping = 16f;
        [Min(0f)]
        public float maximumTorque = 65f;

        public Vector3 solvedElbow
        {
            get;
            private set;
        }

        public float elbowError
        {
            get;
            private set;
        }

        private ServoLimb limb;
        private Rigidbody upper;
        private Rigidbody lower;
        private ConfigurableJoint shoulderJoint;
        private ConfigurableJoint elbowJoint;
        private ConfigurableJoint wristJoint;
        private float upperLength;
        private float lowerLength;
        private Vector3 previousBend;
        private float guideAuthority;

        private void Start()
        {
            limb = GetComponent<ServoLimb>();
            if (limb.segments == null || limb.segments.Length != 2)
            {
                enabled = false;
                return;
            }

            upper = limb.segments[0];
            lower = limb.segments[1];
            shoulderJoint = FindJoint(upper, limb.chassis);
            elbowJoint = FindJoint(lower, upper);
            wristJoint = FindJoint(limb.wrist, lower);
            if (!shoulderJoint || !elbowJoint || !wristJoint)
            {
                enabled = false;
                return;
            }

            upperLength = Vector3.Distance(
                upper.transform.TransformPoint(shoulderJoint.anchor),
                upper.transform.TransformPoint(elbowJoint.connectedAnchor)
            );
            lowerLength = Vector3.Distance(
                lower.transform.TransformPoint(elbowJoint.anchor),
                lower.transform.TransformPoint(wristJoint.connectedAnchor)
            );
        }

        private static ConfigurableJoint FindJoint(Rigidbody child, Rigidbody parent)
        {
            if (child)
            {
                foreach (var j in child.GetComponents<ConfigurableJoint>())
                {
                    if (j.connectedBody == parent)
                    {
                        return j;
                    }
                }
            }

            return null;
        }

        private void FixedUpdate()
        {
            if (!limb || !limb.operational || !upper || !shoulderJoint || !elbowJoint)
            {
                guideAuthority = 0f;
                return;
            }

            // Recover the hand first after tracking loss. Elbow correction must not fling
            // a hanging wrist through nearby props while its servo is catching up.
            float requestedAuthority = limb.authority * Mathf.InverseLerp(.25f, .06f, limb.constraintError);
            guideAuthority = Mathf.MoveTowards(guideAuthority, requestedAuthority, Time.fixedDeltaTime * 3f);
            Vector3 root = limb.chassis.transform.TransformPoint(shoulderJoint.connectedAnchor);
            Vector3 elbow = upper.transform.TransformPoint(elbowJoint.connectedAnchor);
            Vector3 hint = elbowHint ? elbowHint.position : root + limb.chassis.rotation * bendDirection;
            Vector3 desired;
            Vector3 end;
            if (!TwoBoneIK.Solve(
                root,
                limb.wrist.position,
                hint,
                upperLength,
                lowerLength,
                ref previousBend,
                out desired,
                out end
            ))
            {
                return;
            }

            solvedElbow = desired;
            elbowError = Vector3.Distance(elbow, desired);
            Vector3 axis = (limb.wrist.position - root).normalized;
            // Only steer swivel about shoulder-to-hand. The wrist retains physical control of reach.
            Vector3 currentBend = Vector3.ProjectOnPlane(elbow - root, axis);
            Vector3 desiredBend = Vector3.ProjectOnPlane(desired - root, axis);
            if (currentBend.sqrMagnitude < .0001f || desiredBend.sqrMagnitude < .0001f)
            {
                return;
            }

            float angle = Vector3.SignedAngle(currentBend, desiredBend, axis) * Mathf.Deg2Rad;
            Vector3 tangent = Vector3.Cross(axis, currentBend).normalized;
            float radius = Mathf.Max(.1f, currentBend.magnitude);
            float speed = Vector3.Dot(upper.GetPointVelocity(elbow) - limb.chassis.GetPointVelocity(root), tangent) / radius;
            Vector3 force = tangent
                * Mathf.Clamp(angle * stiffness - speed * damping, -maximumTorque, maximumTorque)
                / radius
                * guideAuthority;
            // Apply at the elbow, rather than spinning a capsule about its low-inertia long axis.
            // Endpoint reactions preserve net force; the chassis receives the motor reaction torque.
            upper.AddForceAtPosition(force * .5f, elbow, ForceMode.Force);
            lower.AddForceAtPosition(force * .5f, elbow, ForceMode.Force);
            limb.wrist.AddForceAtPosition(-force * .5f, limb.wrist.position, ForceMode.Force);
            limb.chassis.AddForceAtPosition(-force * .5f, root, ForceMode.Force);
            Vector3 moment = Vector3.Cross(elbow - root, force) - Vector3.Cross(limb.wrist.position - root, force * .5f);
            limb.chassis.AddTorque(-moment, ForceMode.Force);
        }

        private void OnDrawGizmosSelected()
        {
            var arm = GetComponent<ServoLimb>();
            if (!arm || !arm.shoulder)
            {
                return;
            }

            Gizmos.color = Color.green;
            Vector3 hint = elbowHint
                ? elbowHint.position
                : arm.shoulder.position
                    + (arm.chassis ? arm.chassis.rotation : Quaternion.identity) * bendDirection;
            Gizmos.DrawWireSphere(hint, .06f);
            Gizmos.DrawLine(arm.shoulder.position, hint);
            if (Application.isPlaying)
            {
                Gizmos.DrawWireSphere(solvedElbow, .04f);
            }
        }
    }
}
