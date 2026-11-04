using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

// Bakes the approved SoftHop and ShortStep studies into six real Humanoid clips.
// Reference poses are immutable; rebuilding must never add the corrections twice.
public static class BoxingLocomotionBaker
{
 const string BaseFolder=UnifiedBoxingInstaller.Folder+"/LocomotionReference";
 static string Out=>UnifiedBoxingInstaller.Out;
 const float Period=25f/60f,Cycle=.66f,Distance=.24f;
 sealed class Actor
 {
  public string key; public GameObject root; public Animator animator;
  public PlayableGraph graph; public AnimationClipPlayable play;
  public Transform hips,spine,chest,head;
  public Transform[] thighs=new Transform[2],knees=new Transform[2],feet=new Transform[2],toes=new Transform[2];
  public Transform[] arms=new Transform[2],elbows=new Transform[2],hands=new Transform[2];
  public Quaternion[] footRot=new Quaternion[2];
  public List<Vector3>[] soleVertices={new List<Vector3>(),new List<Vector3>()};
  public SkinnedMeshRenderer[] skins; public Mesh[] meshes; public Transform[] displays;
  public Vector3 hipBase; public float size,reachError,minGap=100,minSole=100,maxPlantError;
  public Vector3[] worldFoot=new Vector3[2],lastFoot=new Vector3[2];
  public bool[] contact=new bool[2],lastContact=new bool[2];
  public float plantSlip,maxAir,minHip=100,maxHip=-100; public int metricFrames,airFrames; public Vector3[] toeAnchor=new Vector3[2];
 }
 static Actor Create(string key,string path,int layer)
 {
  var a=new Actor{key=key,root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path))};
  a.root.name=key+" - preview only";
  foreach(var m in a.root.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(m);
  foreach(var t in a.root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
  a.animator=a.root.GetComponentInChildren<Animator>();
  a.animator.runtimeAnimatorController=null; a.animator.applyRootMotion=false;
  a.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; a.animator.Rebind();
  var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseFolder+"/"+key+"_Reference.anim");
  if(!idle||!a.animator.isHuman)throw new Exception("Missing Humanoid guard for "+key);
  a.graph=PlayableGraph.Create("Preview breathing and step "+key); a.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
  a.play=AnimationClipPlayable.Create(a.graph,idle); a.play.SetApplyFootIK(false);
  AnimationPlayableOutput.Create(a.graph,"Pose",a.animator).SetSourcePlayable(a.play); a.graph.Play(); a.play.SetTime(0); a.graph.Evaluate(0);
  a.hips=a.animator.GetBoneTransform(HumanBodyBones.Hips); a.spine=a.animator.GetBoneTransform(HumanBodyBones.Spine);
  a.chest=a.animator.GetBoneTransform(HumanBodyBones.Chest); a.head=a.animator.GetBoneTransform(HumanBodyBones.Head);
  a.size=a.head.position.y/1.56f; a.size=Mathf.Clamp(a.size,.85f,1.2f);
  a.hipBase=a.hips.position+Vector3.up*(.055f*a.size); a.hipBase.x=0;
  for(int i=0;i<2;i++)
  {
   a.thighs[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
   a.knees[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
   a.feet[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
   a.toes[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
   a.arms[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
   a.elbows[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
   a.hands[i]=a.animator.GetBoneTransform(i==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
   // Retain a slight toe-out on the rear foot; flatten only the reference sole.
   Vector3 toe=a.toes[i].position-a.feet[i].position;
   Vector3 flat=new Vector3(i==0?-.018f:.055f,-.035f,.17f);
   a.feet[i].rotation=Quaternion.FromToRotation(toe,flat)*a.feet[i].rotation;
   a.footRot[i]=a.feet[i].rotation;
  }
  a.skins=a.root.GetComponentsInChildren<SkinnedMeshRenderer>();
  var probe=new Mesh();
  foreach(var skin in a.skins)
  {
   skin.BakeMesh(probe); var matrix=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
   foreach(var v in probe.vertices)
   {
    var w=matrix.MultiplyPoint3x4(v);
    for(int i=0;i<2;i++)if(w.y<a.feet[i].position.y+.035f*a.size&&(w-a.feet[i].position).sqrMagnitude<Mathf.Pow(.27f*a.size,2))
     a.soleVertices[i].Add(Quaternion.Inverse(a.footRot[i])*(w-a.feet[i].position));
   }
  }
  Object.DestroyImmediate(probe);
  if(a.soleVertices.Any(v=>v.Count<10))throw new Exception("Not enough sole calibration vertices for "+key);
  for(int i=0;i<2;i++)
  {
   float bottom=a.soleVertices[i].Min(v=>(a.footRot[i]*v).y);
   a.toeAnchor[i]=a.soleVertices[i].Where(v=>(a.footRot[i]*v).y<bottom+.025f*a.size).OrderByDescending(v=>(a.footRot[i]*v).z).First();
  }
  return a;
 }
 static float Ease(float u){u=Mathf.Clamp01(u); return u*u*u*(u*(u*6-15)+10);}
 static float Bell(float u){return u>0&&u<1?Mathf.Pow(Mathf.Sin(Mathf.PI*u),2):0;}
 static float Hermite(float a,float b,float va,float vb,float u,float duration)
 {
  u=Mathf.Clamp01(u);float u2=u*u,u3=u2*u;
  return (2*u3-3*u2+1)*a+(u3-2*u2+u)*duration*va+(-2*u3+3*u2)*b+(u3-u2)*duration*vb;
 }
 static float Body(float p)
 {
  p=Mathf.Repeat(p,1);
  if(p<.23f)return Hermite(-.003f,.009f,0,.246154f,p/.23f,.23f);
  if(p<.49f){float u=(p-.23f)/.26f;return .009f+.016f*4*u*(1-u);}
  if(p<.64f)return Hermite(.009f,-.004f,-.246154f,0,(p-.49f)/.15f,.15f);
  return Hermite(-.004f,-.003f,0,0,(p-.64f)/.36f,.36f);
 }
 static float Air(float p)
 {p=Mathf.Repeat(p,1);float u=(p-.23f)/.26f;return u>0&&u<1?.018f*4*u*(1-u):0;}
 static float Roll(float p)
 {
  p=Mathf.Repeat(p,1);float[] t={0,.12f,.23f,.36f,.49f,.64f,.80f,1};float[] v={4,5,9,6,5,3,4,4};
  for(int i=0;i<t.Length-1;i++)if(p<=t[i+1])return Mathf.Lerp(v[i],v[i+1],Ease((p-t[i])/(t[i+1]-t[i])));
  return 4;
 }
 static float FootHeight(Actor a,int side,Quaternion rotation)
 {
  float minimum=100;foreach(var v in a.soleVertices[side])minimum=Mathf.Min(minimum,(rotation*v).y);
  return -minimum+.002f;
 }
 static void BouncePose(Actor a,float realTime,float bounceTime,float weight)
 {
  a.root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);a.play.SetTime(0);a.graph.Evaluate(0);
  float phase=Mathf.Repeat(bounceTime/Period,1),breath=Mathf.Sin(realTime*2*Mathf.PI/5f);
  float rhythm=Mathf.Sin(bounceTime*Mathf.PI/Period),body=(Body(phase)+Body(phase-.10f))*.5f;
  int hop=Mathf.FloorToInt(bounceTime/Period+.00001f);int earlyFoot=hop%2;
  float weightShift=Mathf.Cos(bounceTime*Mathf.PI/Period);
  float velocity=(Body(phase+.002f)+Body(phase-.098f)-Body(phase-.002f)-Body(phase-.102f))/(.008f*Period);
  a.hips.position=a.hipBase+new Vector3(weightShift*.006f*weight*a.size,((.016f+body)*weight+breath*.0018f*(1-weight))*a.size,.012f*weightShift*weight*a.size);
  a.hips.rotation=Quaternion.AngleAxis(rhythm*.45f*weight,Vector3.up)*a.hips.rotation;
  a.spine.rotation=Quaternion.AngleAxis(1.4f+breath*.35f+Mathf.Clamp(-velocity,-1,1)*.22f*weight,Vector3.right)*a.spine.rotation;
  a.chest.rotation=Quaternion.AngleAxis(-breath*.5f,Vector3.right)*Quaternion.AngleAxis(-rhythm*.55f*weight,Vector3.up)*a.chest.rotation;
  a.head.rotation=Quaternion.AngleAxis(-Mathf.Clamp(-velocity,-1,1)*.22f*weight,Vector3.right)*a.head.rotation;
  for(int i=0;i<2;i++)
  {
   Quaternion handRotation=a.hands[i].rotation;Vector3 pole=a.elbows[i].position-a.arms[i].position;
   Vector3 handTarget=a.hands[i].position+new Vector3(0,-(a.key=="Common"?.065f:.025f)*a.size-Mathf.Clamp(velocity*.018f,-.005f,.005f)*weight+body*.18f*weight,.025f*a.size);
   Solve(a.arms[i],a.elbows[i],a.hands[i],handTarget,pole);a.hands[i].rotation=handRotation;
  }
  Vector3[] targets=new Vector3[2];Quaternion[] rotations=new Quaternion[2];float[] lift=new float[2];
  for(int i=0;i<2;i++)
  {
   // Alternate the early contact leg every hop; both still keep their fighting stance.
   float p=phase-(i==earlyFoot?0:.10f);lift[i]=Air(p)*a.size*weight;
   rotations[i]=Quaternion.AngleAxis(Roll(p)*weight,Vector3.right)*a.footRot[i];
   // Rock about the forefoot instead of dragging the contact point as the heel rises.
   Vector3 pivot=a.footRot[i]*a.toeAnchor[i]-rotations[i]*a.toeAnchor[i];
   targets[i]=new Vector3((i==0?-.105f:.105f)*a.size+pivot.x,
    FootHeight(a,i,rotations[i])+lift[i],a.hipBase.z+(i==0?.205f:-.205f)*a.size+pivot.z);
  }
  for(int i=0;i<2;i++)
  {
   float length=Vector3.Distance(a.thighs[i].position,a.knees[i].position)+Vector3.Distance(a.knees[i].position,a.feet[i].position);
   Vector3 d=a.thighs[i].position-targets[i];d.y=0;
   float maxHipY=targets[i].y+Mathf.Sqrt(Mathf.Max(.01f,Mathf.Pow(length*.977f,2)-d.sqrMagnitude));
   if(a.thighs[i].position.y>maxHipY)a.hips.position-=Vector3.up*(a.thighs[i].position.y-maxHipY);
  }
  for(int i=0;i<2;i++)
  {
   Solve(a.thighs[i],a.knees[i],a.feet[i],targets[i],new Vector3(i==0?-.06f:.06f,0,1));a.feet[i].rotation=rotations[i];
   a.reachError=Mathf.Max(a.reachError,Vector3.Distance(a.feet[i].position,targets[i]));
   a.worldFoot[i]=a.feet[i].position;a.contact[i]=lift[i]<.00001f;
   float sole=a.feet[i].position.y-FootHeight(a,i,rotations[i])+.002f;
   a.minSole=Mathf.Min(a.minSole,sole);a.maxAir=Mathf.Max(a.maxAir,sole-.002f);
   if(a.contact[i])a.maxPlantError=Mathf.Max(a.maxPlantError,Mathf.Abs(sole-.002f));
  }
  if(lift[0]>.004f&&lift[1]>.004f)a.airFrames++;
  a.minHip=Mathf.Min(a.minHip,a.hips.position.y);a.maxHip=Mathf.Max(a.maxHip,a.hips.position.y);
  a.minGap=Mathf.Min(a.minGap,a.feet[0].position.z-a.feet[1].position.z);
 }
 static void Solve(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 pole)
 {
  Vector3 start=upper.position,delta=target-start;
  float a=Vector3.Distance(start,lower.position),b=Vector3.Distance(lower.position,end.position);
  float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
  Vector3 axis=delta.normalized,bend=Vector3.ProjectOnPlane(pole,axis).normalized;
  float along=(a*a-b*b+d*d)/(2*d);
  Vector3 knee=start+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
  upper.rotation=Quaternion.FromToRotation(lower.position-start,knee-start)*upper.rotation;
  lower.rotation=Quaternion.FromToRotation(end.position-lower.position,target-lower.position)*lower.rotation;
 }
 struct Step
 {
  public float left,right,liftLeft,liftRight,travel,pulse,shift;
 }
 static Step Sample(float time,float direction)
 {
  if(time<=0)return new Step();
  float cycle=time/Cycle; int n=Mathf.FloorToInt(cycle+0.00001f); float phase=Mathf.Clamp01(cycle-n);
  float u=(phase-.035f)/.405f, v=(phase-.49f)/.405f;
  float first=Distance*(n+Ease(u))*direction,second=Distance*(n+Ease(v))*direction;
  bool forward=direction>0;
  return new Step
  {
   left=forward?first:second,right=forward?second:first,
   liftLeft=(forward?Bell(u):Bell(v))*.033f,
   liftRight=(forward?Bell(v):Bell(u))*.030f,
   travel=(first+second)*.5f,pulse=Bell(u)+Bell(v),
   shift=(Bell(u)-Bell(v))*(forward?1:-1)
  };
 }
 static void StepPose(Actor a,float realTime,float moveTime,float direction,float origin)
 {
  a.root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); a.play.SetTime(0); a.graph.Evaluate(0);
  Step s=Sample(moveTime,direction);
  float breath=Mathf.Sin(realTime*2*Mathf.PI/4.8f);
  float breathSlow=Mathf.Sin(realTime*2*Mathf.PI/9.6f);
  // Small compression on the supporting leg, then a quiet rebound. No airborne running cycle.
  a.hips.position=a.hipBase+new Vector3(s.shift*.0045f*a.size,(-.007f*s.pulse+.0018f*breath)*a.size,.002f*breathSlow*a.size);
  a.hips.rotation=Quaternion.AngleAxis(s.shift*.9f,Vector3.up)*a.hips.rotation;
  a.spine.rotation=Quaternion.AngleAxis(1.4f+breath*.55f+s.pulse*.65f,Vector3.right)*a.spine.rotation;
  a.chest.rotation=Quaternion.AngleAxis(-breath*.8f,Vector3.right)*Quaternion.AngleAxis(-s.shift*1.2f,Vector3.up)*a.chest.rotation;
  // The arms inherit the chest motion instead of swinging like a walking/running animation.
  a.head.rotation=Quaternion.AngleAxis(-breath*.2f-s.pulse*.4f,Vector3.right)*a.head.rotation;
  for(int i=0;i<2;i++)
  {
   // Relax the high guard just enough to leave the eyes visible; retain the source elbow plane.
   Quaternion handRotation=a.hands[i].rotation;
   Vector3 pole=a.elbows[i].position-a.arms[i].position;
   Vector3 handTarget=a.hands[i].position+new Vector3(0,-(a.key=="Common"?.065f:.025f)*a.size,.025f*a.size);
   Solve(a.arms[i],a.elbows[i],a.hands[i],handTarget,pole);
   a.hands[i].rotation=handRotation;
  }
  Vector3[] targets=new Vector3[2]; Quaternion[] rotations=new Quaternion[2];
  for(int i=0;i<2;i++)
  {
   float lift=i==0?s.liftLeft:s.liftRight,advance=i==0?s.left:s.right;
   // Keep the supporting sole in contact. Small toe clearance only during the swing.
   float roll=lift>0?-2.5f*(lift/.033f):0;
   rotations[i]=Quaternion.AngleAxis(roll,Vector3.right)*a.footRot[i];
   targets[i]=new Vector3((i==0?-.105f:.105f)*a.size,FootHeight(a,i,rotations[i])+lift*a.size,
     a.hipBase.z+(i==0?.205f:-.205f)*a.size+advance-s.travel);
  }
  // Keep knees soft and avoid either leg reaching an IK singularity.
  for(int i=0;i<2;i++)
  {
   float length=Vector3.Distance(a.thighs[i].position,a.knees[i].position)+Vector3.Distance(a.knees[i].position,a.feet[i].position);
   Vector3 d=a.thighs[i].position-targets[i]; d.y=0;
   float maxHipY=targets[i].y+Mathf.Sqrt(Mathf.Max(.01f,Mathf.Pow(length*.974f,2)-d.sqrMagnitude));
   if(a.thighs[i].position.y>maxHipY)a.hips.position-=Vector3.up*(a.thighs[i].position.y-maxHipY);
  }
  for(int i=0;i<2;i++)
  {
   Solve(a.thighs[i],a.knees[i],a.feet[i],targets[i],new Vector3(i==0?-.06f:.06f,0,1));
   a.feet[i].rotation=rotations[i];
   a.reachError=Mathf.Max(a.reachError,Vector3.Distance(a.feet[i].position,targets[i]));
   a.worldFoot[i]=a.feet[i].position+new Vector3(0,0,s.travel+origin);
   a.contact[i]=(i==0?s.liftLeft:s.liftRight)<.00001f;
   float sole=a.feet[i].position.y-FootHeight(a,i,rotations[i])+.002f;
   a.minSole=Mathf.Min(a.minSole,sole);
   if(a.contact[i])a.maxPlantError=Mathf.Max(a.maxPlantError,Mathf.Abs(sole-.002f));
  }
  a.minGap=Mathf.Min(a.minGap,a.feet[0].position.z-a.feet[1].position.z);
  
 }

 [MenuItem("VisionStriker/Animation/Rebuild approved boxing locomotion")]
 public static void Rebuild()
 {
  Directory.CreateDirectory(Out);
  var report=new List<string>();
  UnifiedBoxingInstaller.EnsureFolder(BaseFolder);
  foreach(var key in new[]{"Common","ScoutContact"})
  {
   string reference=BaseFolder+"/"+key+"_Reference.anim";
   if(!AssetDatabase.LoadAssetAtPath<AnimationClip>(reference))
   {
    var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(UnifiedBoxingInstaller.Folder+"/"+key+"/Unified_"+key+"_IDLE.anim");
    AssetDatabase.CreateAsset(Object.Instantiate(original),reference);
   }
   var a=Create(key,key=="Common"?UnifiedBoxingInstaller.Boxer:UnifiedBoxingInstaller.Scout,0);
   try
   {
    using(var handler=new HumanPoseHandler(a.animator.avatar,a.animator.transform))
    foreach(string motion in new[]{"IDLE","WALK_FORWARD","WALK_BACK"})
    {
     float length=motion=="IDLE"?5f:Cycle;int n=Mathf.RoundToInt(length*60);
     var names=HumanTrait.MuscleName.Concat(new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"}).ToArray();
     var keys=names.Select(x=>new Keyframe[n+1]).ToArray();
     var hips=new Vector3[n+1];var feet=new Vector3[n+1,2];var minima=new float[n+1];
     var pose=new HumanPose();
     for(int f=0;f<=n;f++)
     {
      float t=f*length/n;
      if(motion=="IDLE")BouncePose(a,t,t,1);else StepPose(a,0,t,motion=="WALK_FORWARD"?1:-1,0);
      handler.GetHumanPose(ref pose);hips[f]=a.hips.position;
      for(int i=0;i<2;i++)feet[f,i]=a.feet[i].position;
      minima[f]=Mathf.Min(a.feet[0].position.y-FootHeight(a,0,a.feet[0].rotation),a.feet[1].position.y-FootHeight(a,1,a.feet[1].rotation))+.002f;
      for(int m=0;m<HumanTrait.MuscleCount;m++)keys[m][f]=new Keyframe(t,pose.muscles[m]);
      int k=HumanTrait.MuscleCount;var p=pose.bodyPosition;var q=pose.bodyRotation;
      float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w};
      for(int m=0;m<7;m++)keys[k+m][f]=new Keyframe(t,values[m]);
     }
     var clip=new AnimationClip{name="Unified_"+key+"_"+motion,frameRate=60};
     for(int m=0;m<names.Length;m++)
     {
      keys[m][n].value=keys[m][0].value;
      SetCurve(clip,names[m],keys[m]);
     }
     var settings=AnimationUtility.GetAnimationClipSettings(clip);
     settings.loopTime=true;settings.loopBlend=true;
     // Bake the small vertical hop INTO the body pose. It must not become
     // discarded root motion when the combat code owns character translation.
     settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;settings.loopBlendOrientation=true;settings.startTime=0;settings.stopTime=length;
     settings.keepOriginalPositionY=true;settings.keepOriginalPositionXZ=true;settings.keepOriginalOrientation=true;
     settings.heightFromFeet=false;
     AnimationUtility.SetAnimationClipSettings(clip,settings);
     // Round-trip through Unity's Humanoid solver and keep the approved sole lift.
     SetClip(a,clip);
     var y=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y"));
     var groundKeys=new Keyframe[n+1];float worst=0;
     for(int f=0;f<=n;f++)
     {
      float t=f*length/n;a.play.SetTime(Mathf.Min(t,length-.00001f));a.graph.Evaluate(0);
      float actual=UnifiedBoxingInstaller.SkinFloor(a.root);
      groundKeys[f]=new Keyframe(t,y.Evaluate(t)+(minima[f]-actual)/a.animator.humanScale);
      for(int i=0;i<2;i++)worst=Mathf.Max(worst,Vector3.Distance(a.feet[i].position,feet[f,i]));
     }
     groundKeys[n].value=groundKeys[0].value;SetCurve(clip,"RootT.y",groundKeys);
     string path=UnifiedBoxingInstaller.Folder+"/"+key+"/"+clip.name+".anim";
     var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
     if(!old)throw new Exception("Expected existing clip: "+path);
     SetClip(a,AssetDatabase.LoadAssetAtPath<AnimationClip>(reference));
     EditorUtility.CopySerialized(clip,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(clip);
     report.Add(key+" "+motion+": "+length+"s, loop; retarget ankle difference before floor correction="+worst.ToString("F5"));
    }
   }
   finally{a.graph.Destroy();Object.DestroyImmediate(a.root);}
  }
  foreach(var key in new[]{"Common","ScoutContact"})
  {
   var reference=AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseFolder+"/"+key+"_Reference.anim");
   foreach(var binding in AnimationUtility.GetCurveBindings(reference))
   {
    float value=AnimationUtility.GetEditorCurve(reference,binding).Evaluate(0);
    AnimationUtility.SetEditorCurve(reference,binding,AnimationCurve.Constant(0,1,value));
   }
   var rs=AnimationUtility.GetAnimationClipSettings(reference);rs.stopTime=1;AnimationUtility.SetAnimationClipSettings(reference,rs);
   EditorUtility.SetDirty(reference);
  }
  AssetDatabase.SaveAssets();File.WriteAllLines(Path.Combine(Out,"bake_report.txt"),report);
 }
 public static void Batch()
 {
  try{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Rebuild();File.WriteAllText(Path.Combine(Out,"bake_done.txt"),"PASS");EditorApplication.Exit(0);}
  catch(Exception e){File.WriteAllText(Path.Combine(Out,"bake_error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
 }
 static void SetClip(Actor a,AnimationClip clip)
 {
  var output=(AnimationPlayableOutput)a.graph.GetOutput(0);
  a.play.Destroy();a.play=AnimationClipPlayable.Create(a.graph,clip);a.play.SetApplyFootIK(false);output.SetSourcePlayable(a.play);
 }
 static void SetCurve(AnimationClip clip,string name,Keyframe[] keys)
 {
  if(keys.Length>2 && keys.Max(k=>k.value)-keys.Min(k=>k.value)<.00001f)
   keys=new[]{new Keyframe(keys[0].time,keys[0].value),new Keyframe(keys[keys.Length-1].time,keys[0].value)};
  var curve=new AnimationCurve(keys);
  for(int i=0;i<keys.Length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);}
  AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),name),curve);
 }
}
