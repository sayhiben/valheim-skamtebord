using UnityEngine;

namespace Skamtebord;

// An additive Humanoid pose, applied after animation on owners and observers.
// Counterlean the pelvis/torso, then solve each leg back to its animated foot
// target. The planted foot stays on the board and the pushing foot keeps its clip.
internal sealed class SkateBalance
{
    private readonly Transform[] bones;
    private readonly Quaternion[] rotations;
    private readonly Vector3[] positions;
    private bool applied;
    private float amount;
    private float lastSupport = float.NegativeInfinity;

    internal SkateBalance(Animator animator)
    {
        if(!animator || !animator.isHuman) return;
        var ids=new[] {HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
        bones=new Transform[ids.Length];
        for(int i=0;i<ids.Length;i++)
        {
            bones[i]=animator.GetBoneTransform(ids[i]);
            if(!bones[i]) {bones=null;return;}
        }
        rotations=new Quaternion[bones.Length];positions=new Vector3[bones.Length];
    }

    internal void Restore()
    {
        if(!applied || bones==null) return;
        for(int i=0;i<bones.Length;i++) if(bones[i])
        {
            bones[i].localRotation=rotations[i];bones[i].localPosition=positions[i];
        }
        applied=false;
    }

    internal void Apply(Vector3 boardUp,bool supported,float strength)
    {
        if(bones==null) return;
        Restore();
        float tilt=Vector3.Angle(boardUp,Vector3.up);
        if(supported) lastSupport=Time.time;
        // Small bumps should not toggle the whole upper-body pose every tick.
        // Sustained flight still releases the balance for airborne poses.
        float target=supported || Time.time-lastSupport<.12f ? strength*(1f-Mathf.InverseLerp(50,80,tilt)) : 0;
        amount=Mathf.MoveTowards(amount,target,Time.deltaTime*8);
        if(amount<.001f || tilt<.05f) return;
        for(int i=0;i<bones.Length;i++)
        {
            rotations[i]=bones[i].localRotation;positions[i]=bones[i].localPosition;
        }
        applied=true;
        Vector3 left=bones[4].position,right=bones[7].position;
        Quaternion leftRotation=bones[4].rotation,rightRotation=bones[7].rotation;
        Quaternion correction=Quaternion.FromToRotation(boardUp,Vector3.up);
        // Leave a little natural slope lean; lower the pelvis to give the bent
        // knees room without stretching either leg or pulling the feet upward.
        bones[0].position-=Vector3.up*(Mathf.Clamp01(tilt/45f)*.09f*amount);
        bones[0].rotation=Quaternion.Slerp(Quaternion.identity,correction,.4f*amount)*bones[0].rotation;
        bones[1].rotation=Quaternion.Slerp(Quaternion.identity,correction,.5f*amount)*bones[1].rotation;
        SolveLeg(bones[2],bones[3],bones[4],left,leftRotation);
        SolveLeg(bones[5],bones[6],bones[7],right,rightRotation);
    }

    private static void SolveLeg(Transform thigh,Transform knee,Transform foot,Vector3 target,Quaternion footRotation)
    {
        Vector3 hip=thigh.position,axis=target-hip;
        float upper=Vector3.Distance(hip,knee.position),lower=Vector3.Distance(knee.position,foot.position);
        float distance=Mathf.Clamp(axis.magnitude,Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
        if(axis.sqrMagnitude<.00001f || upper<.001f || lower<.001f) return;
        axis.Normalize();
        Vector3 bend=Vector3.ProjectOnPlane(knee.position-hip,axis).normalized;
        if(bend.sqrMagnitude<.01f) bend=Vector3.ProjectOnPlane(thigh.forward,axis).normalized;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        float height=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        Vector3 kneeTarget=hip+axis*along+bend*height;
        thigh.rotation=Quaternion.FromToRotation(knee.position-hip,kneeTarget-hip)*thigh.rotation;
        knee.rotation=Quaternion.FromToRotation(foot.position-knee.position,target-knee.position)*knee.rotation;
        foot.rotation=footRotation;
    }
}
