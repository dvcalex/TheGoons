using UnityEngine;

namespace MechLab
{
    // Visual bones only. Run after Animator; follow the physical wrist, never the raw controller.
    [DefaultExecutionOrder(200)]
    public sealed class HumanoidArmIK : MonoBehaviour
    {
        public Animator animator;
        public bool leftArm = true;
        public Transform upperArm, forearm, hand;
        public Transform physicalHandTarget, elbowHint, bodyFrame;
        public Vector3 handRotationOffset;
        [Range(0f,1f)] public float weight = 1f;
        private Vector3 previousBend;

        [ContextMenu("Bind humanoid arm bones")]
        public void BindHumanoid()
        {
            if(!animator) animator=GetComponentInParent<Animator>();
            if(!animator || !animator.isHuman || !animator.avatar || !animator.avatar.isValid) return;
            upperArm=animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            forearm=animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            hand=animator.GetBoneTransform(leftArm ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if(!bodyFrame) bodyFrame=animator.transform;
        }
        private void Awake() { if(!upperArm || !forearm || !hand) BindHumanoid(); }
        private void LateUpdate() { SolvePose(); }
        public bool SolvePose()
        {
            if(!upperArm || !forearm || !hand || !physicalHandTarget || weight <= 0f) return false;
            if(!forearm.IsChildOf(upperArm) || !hand.IsChildOf(forearm)) return false;
            // Reject physical bones: transform IK must never teleport dynamic collision bodies.
            if(Dynamic(upperArm) || Dynamic(forearm) || Dynamic(hand)) return false;
            float a=Vector3.Distance(upperArm.position,forearm.position), b=Vector3.Distance(forearm.position,hand.position);
            Vector3 hint=elbowHint ? elbowHint.position : upperArm.position
                +(bodyFrame ? bodyFrame.rotation : transform.rotation)*new Vector3(leftArm ? -1f : 1f,-.7f,-.3f);
            Vector3 elbow,end;
            if(!TwoBoneIK.Solve(upperArm.position,physicalHandTarget.position,hint,a,b,ref previousBend,out elbow,out end)) return false;
            upperArm.rotation=Quaternion.Slerp(upperArm.rotation,Quaternion.FromToRotation(forearm.position-upperArm.position,
                elbow-upperArm.position)*upperArm.rotation,weight);
            forearm.rotation=Quaternion.Slerp(forearm.rotation,Quaternion.FromToRotation(hand.position-forearm.position,
                end-forearm.position)*forearm.rotation,weight);
            hand.rotation=Quaternion.Slerp(hand.rotation,physicalHandTarget.rotation*Quaternion.Euler(handRotationOffset),weight);
            return true;
        }
        private static bool Dynamic(Transform bone) { var rb=bone.GetComponent<Rigidbody>(); return rb && !rb.isKinematic; }
    }
}
