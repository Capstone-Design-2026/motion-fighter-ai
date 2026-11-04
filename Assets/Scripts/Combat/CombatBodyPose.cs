using UnityEngine;
using Capsule=CombatContactGeometry.Capsule;

public enum CombatBodyPart { Head, Chest, Abdomen, Leg, Guard }

/// <summary>Read-only skeleton snapshots; never changes physical pushboxes or animations.</summary>
public sealed class CombatBodyPose
{
    readonly Fighter fighter; Fighter3DView view; Animator animator;
    Transform head,neck,chest,hips;
    readonly Transform[] hands=new Transform[2],elbows=new Transform[2],thighs=new Transform[2],knees=new Transform[2],feet=new Transform[2],toes=new Transform[2];
    public readonly Capsule[] strikes=new Capsule[4],previousStrikes=new Capsule[4];
    public readonly Capsule[] hurt=new Capsule[9],previousHurt=new Capsule[9];
    public readonly CombatBodyPart[] parts={CombatBodyPart.Head,CombatBodyPart.Chest,CombatBodyPart.Abdomen,CombatBodyPart.Leg,CombatBodyPart.Leg,CombatBodyPart.Leg,CombatBodyPart.Leg,CombatBodyPart.Guard,CombatBodyPart.Guard};
    bool captured;Vector3 lastRoot;int lastFacing;float scale=1;
    public CombatBodyPose(Fighter owner){fighter=owner;}
    public bool Ready
    {
        get {
            if(!view)view=fighter.GetComponent<Fighter3DView>();
            var next=view?view.modelAnimator:null;
            if(!next||!next.isHuman||!next.avatar||!next.avatar.isValid)return false;
            if(animator!=next){
                animator=next;captured=false;
                head=Bone(HumanBodyBones.Head);neck=Bone(HumanBodyBones.Neck);chest=Bone(HumanBodyBones.Chest);hips=Bone(HumanBodyBones.Hips);
                for(int i=0;i<2;i++){
                    hands[i]=Bone(i==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                    elbows[i]=Bone(i==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                    thighs[i]=Bone(i==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                    knees[i]=Bone(i==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                    feet[i]=Bone(i==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                    toes[i]=Bone(i==0?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
                }
                if(head&&hips)scale=Mathf.Clamp(Vector3.Distance(head.position,hips.position)/.65f,.85f,1.15f);
            }
            return head&&chest&&hips&&hands[0]&&hands[1]&&elbows[0]&&elbows[1]&&thighs[0]&&thighs[1]&&knees[0]&&knees[1]&&feet[0]&&feet[1];
        }
    }
    Transform Bone(HumanBodyBones bone)=>animator.GetBoneTransform(bone);
    public void ResetHistory(){captured=false;}
    public bool Capture()
    {
        if(!Ready)return false;
        for(int i=0;i<4;i++)previousStrikes[i]=strikes[i];
        for(int i=0;i<hurt.Length;i++)previousHurt[i]=hurt[i];
        var h=head.position;var n=neck?neck.position:Vector3.Lerp(chest.position,h,.75f);
        hurt[0]=new Capsule(h,h+head.up*.105f*scale,.115f*scale);
        hurt[1]=new Capsule(chest.position,n,.18f*scale);
        hurt[2]=new Capsule(hips.position,Vector3.Lerp(hips.position,chest.position,.8f),.17f*scale);
        for(int i=0;i<2;i++){
            var knuckle=hands[i].position+(hands[i].position-elbows[i].position).normalized*.045f*scale;
            strikes[i]=new Capsule(knuckle,knuckle,.095f*scale);
            hurt[3+i*2]=new Capsule(thighs[i].position,knees[i].position,.115f*scale);
            hurt[4+i*2]=new Capsule(knees[i].position,feet[i].position,.085f*scale);
            hurt[7+i]=new Capsule(elbows[i].position,knuckle,.095f*scale);
        }
        // Both approved kick clips strike with the right leg (measured at 120 Hz).
        var end=toes[1]?toes[1].position:feet[1].position;
        strikes[2]=strikes[3]=new Capsule(Vector3.Lerp(knees[1].position,feet[1].position,.3f),end,.085f*scale);
        if(!captured||(fighter.transform.position-lastRoot).sqrMagnitude>1f||lastFacing!=fighter.Facing){
            for(int i=0;i<4;i++)previousStrikes[i]=strikes[i];
            for(int i=0;i<hurt.Length;i++)previousHurt[i]=hurt[i];
        }
        lastRoot=fighter.transform.position;lastFacing=fighter.Facing;captured=true;return true;
    }
    public static bool Allows(CombatMove move,CombatBodyPart part,bool guarded)
    {
        if(part==CombatBodyPart.Guard)return guarded&&move!=CombatMove.LowKick;
        if(move==CombatMove.LowKick)return part==CombatBodyPart.Leg;
        if(move==CombatMove.HighKick)return part==CombatBodyPart.Head||part==CombatBodyPart.Chest;
        return part==CombatBodyPart.Head||part==CombatBodyPart.Chest||part==CombatBodyPart.Abdomen;
    }
}
