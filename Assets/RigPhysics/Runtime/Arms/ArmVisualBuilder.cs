using UnityEngine;

namespace MechLab
{
    // Explicit authoring helper: creates editable placeholder bones, never rebuilds on Play.
    public static class ArmVisualBuilder
    {
        public static HumanoidArmIK Create(ServoLimb limb)
        {
            var existing=limb.shoulder.GetComponentInChildren<HumanoidArmIK>(true);
            if(existing) return existing;
            var upperBody=limb.segments[0]; var lowerBody=limb.segments[1];
            ConfigurableJoint elbowJoint=null;
            foreach(var j in lowerBody.GetComponents<ConfigurableJoint>()) if(j.connectedBody==upperBody) elbowJoint=j;
            if(!elbowJoint) return null;
            Vector3 elbow=upperBody.transform.TransformPoint(elbowJoint.connectedAnchor);
            var upper=new GameObject("Upper arm bone").transform;
            upper.SetParent(limb.shoulder,false); upper.position=limb.shoulder.position;
            var lower=new GameObject("Forearm bone").transform; lower.SetParent(upper,true); lower.position=elbow;
            var hand=new GameObject("Hand bone").transform; hand.SetParent(lower,true); hand.position=limb.transform.position;
            Segment(upper,lower.position,upperBody.GetComponent<Renderer>().sharedMaterial);
            Segment(lower,hand.position,lowerBody.GetComponent<Renderer>().sharedMaterial);
            var rig=upper.gameObject.AddComponent<HumanoidArmIK>();
            rig.upperArm=upper; rig.forearm=lower; rig.hand=hand; rig.physicalHandTarget=limb.transform;
            rig.leftArm=limb.input.hand==UnityEngine.XR.XRNode.LeftHand; rig.bodyFrame=limb.chassis.transform;
            var guide=limb.GetComponent<PhysicalArmIK>(); if(guide) rig.elbowHint=guide.elbowHint;
            foreach(var segment in limb.segments) foreach(var r in segment.GetComponentsInChildren<Renderer>()) r.enabled=false;
            return rig;
        }
        private static void Segment(Transform bone, Vector3 end, Material material)
        {
            var visual=GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name="Placeholder limb mesh";
            var collider=visual.GetComponent<Collider>();
            if(Application.isPlaying) { collider.enabled=false; Object.Destroy(collider); } else Object.DestroyImmediate(collider);
            visual.transform.position=(bone.position+end)*.5f;
            visual.transform.rotation=Quaternion.FromToRotation(Vector3.up,end-bone.position);
            visual.transform.localScale=new Vector3(.18f,Vector3.Distance(bone.position,end)*.5f,.18f);
            visual.transform.SetParent(bone,true); visual.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
