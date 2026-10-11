using System;
using UnityEngine;

// Visual bones follow the two physical hands. Fingers use analog controller grip.
[DefaultExecutionOrder(200)]
public sealed class ScoutArms : MonoBehaviour
{
    [Serializable]
    public sealed class Arm
    {
        public Transform upperArm;
        public Transform forearm;
        public Transform hand;
        public Quaternion handRotationOffset = Quaternion.identity;
        public Transform[] fingers;
        public Quaternion[] openFingerRotations;
        public Vector3 bend;
        [NonSerialized] public float curl;
    }

    public VrRigController rig;
    public Arm left = new Arm();
    public Arm right = new Arm();
    public float fingerCurlDegrees = 65f;
    public float fingerSpeed = 8f;

    private void LateUpdate()
    {
        if (rig == null)
        {
            return;
        }

        Pose(left, rig.leftHand, -1f);
        Pose(right, rig.rightHand, 1f);
    }

    private void Pose(Arm arm, VrPhysicsHand target, float side)
    {
        if (arm.upperArm == null || arm.forearm == null || arm.hand == null || target == null)
        {
            return;
        }

        arm.upperArm.position = rig.ShoulderPosition(side);
        float upperLength = Vector3.Distance(arm.upperArm.position, arm.forearm.position);
        float lowerLength = Vector3.Distance(arm.forearm.position, arm.hand.position);
        Vector3 hint = arm.upperArm.position + rig.Facing * new Vector3(side * 2f, -1f, -1f);
        Vector3 elbow;
        Vector3 end;
        if (MechLab.TwoBoneIK.Solve(arm.upperArm.position, target.transform.position, hint,
            upperLength, lowerLength, ref arm.bend, out elbow, out end))
        {
            arm.upperArm.rotation = Quaternion.FromToRotation(
                arm.forearm.position - arm.upperArm.position, elbow - arm.upperArm.position) * arm.upperArm.rotation;
            arm.forearm.rotation = Quaternion.FromToRotation(
                arm.hand.position - arm.forearm.position, end - arm.forearm.position) * arm.forearm.rotation;
            arm.hand.rotation = target.transform.rotation * arm.handRotationOffset;
        }

        arm.curl = Mathf.MoveTowards(arm.curl, target.Grip, Time.deltaTime * fingerSpeed);
        for (int i = 0; i < arm.fingers.Length; i++)
        {
            float angle = fingerCurlDegrees;
            if (arm.fingers[i].name.Contains("thumb"))
            {
                angle *= 0.6f;
            }

            arm.fingers[i].localRotation = arm.openFingerRotations[i] * Quaternion.AngleAxis(angle * arm.curl, Vector3.right);
        }
    }
}
