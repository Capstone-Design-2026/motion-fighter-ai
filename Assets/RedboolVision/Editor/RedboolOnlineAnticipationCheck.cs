using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Actual Animator evaluation on transform-only clones; no scene/game/camera/network starts.</summary>
public static class RedboolOnlineAnticipationCheck
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Gestures={"LEFT_PUNCH","RIGHT_PUNCH","LEFT_RAISE","RIGHT_RAISE"};
    static readonly string[] States={"JAB","CROSS","HIGHKICK","LOWKICK"};
    static int checks;
    [Serializable] sealed class SampleRow
    {
        public string rig,state,asset;
        public int changedBones,measuredSamples,isolatedRendererCurves;
        public List<string> ignoredBindingTargets=new List<string>();
        public float largestBoneAngleDegrees;
        public double bindCpuMs,cpuP50Ms,cpuP95Ms,cpuMaxMs;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public int checks;
        public string scope="Editor-only production sampler on Hero02, Scout, Cheolmun and Mujin. Actual sampling occurs on separate transform-only rigs without renderer/gameplay components. Known renderer curves have no target there. Exact source preservation and a 2ms sampling budget are checked. No gameplay, camera, network or rendering. CPU samples exclude whole-game frame cost and physical display latency.";
        public string error;
        public List<SampleRow> samples=new List<SampleRow>();
    }
    sealed class Pose
    {
        public Transform[] nodes;
        public Vector3[] positions,scales;
        public Quaternion[] rotations;
        public bool[] active;
        public Pose(Transform root)
        {
            nodes=root.GetComponentsInChildren<Transform>(true);
            positions=new Vector3[nodes.Length];scales=new Vector3[nodes.Length];rotations=new Quaternion[nodes.Length];
            active=new bool[nodes.Length];
            for(int i=0;i<nodes.Length;i++) {positions[i]=nodes[i].localPosition;scales[i]=nodes[i].localScale;rotations[i]=nodes[i].localRotation;active[i]=nodes[i].gameObject.activeSelf;}
        }
        public bool Exact()
        {
            for(int i=0;i<nodes.Length;i++)
                if(nodes[i]==null||!nodes[i].localPosition.Equals(positions[i])||!nodes[i].localScale.Equals(scales[i])||!nodes[i].localRotation.Equals(rotations[i])||nodes[i].gameObject.activeSelf!=active[i])return false;
            return true;
        }
    }
    static void Check(bool condition,string message)
    {
        checks++;if(!condition)throw new InvalidOperationException("Online anticipation: "+message);
    }
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run this check outside Play Mode.");
        checks=0;var report=new Report();
        try
        {
            CheckNewestAuthority();
            var config=AssetDatabase.LoadAssetAtPath<RunConfig>("Assets/Resources/RunConfig.asset");
            Check(config!=null,"RunConfig exists");
            var hero=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Hero02/Hero02_Visual.prefab");
            CheckRig(hero,"Hero02",null,report);
            foreach(string node in new[]{"scout","gate","final"})
                CheckRig(config.GetEnemyVisual(node),node,node=="gate"?Resources.Load<RuntimeAnimatorController>("RedboolPvpCheolmun"):null,report);
            report.passed=true;
        }
        catch(Exception error)
        {
            report.error=error.ToString();throw;
        }
        finally
        {
            report.checks=checks;
            string json=JsonUtility.ToJson(report,true);
            string path=Environment.GetEnvironmentVariable("REDBOOL_ANTICIPATION_CHECK_OUTPUT");
            if(!string.IsNullOrEmpty(path))
            {
                string parent=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(parent);
                File.WriteAllText(path,json);
            }
            Debug.Log((report.passed?"PASS ":"FAIL ")+checks+" actual anticipation sampler checks\n"+json);
        }
    }
    static void CheckNewestAuthority()
    {
        // No live game singleton or scene is created. These are the real protocol
        // data objects, exercising the same gate used before each overlay write.
        var shown=new RedboolViewActor {hp=100,live=true,character=1};
        var world=new RedboolViewWorld {round=2,fighting=true};
        var newest=new RedboolViewFrame {world=new RedboolViewWorld {round=2,fighting=true}};
        newest.actors[0]=new RedboolViewActor();newest.actors[1]=new RedboolViewActor {hp=100,live=true,character=1};
        Func<bool> allowed=()=>RedboolAnticipationAuthority.Allows(true,shown,world,newest);
        Check(allowed(),"fresh same-round authority allows presentation");
        Check(!RedboolAnticipationAuthority.Allows(false,shown,world,newest),"stale display cancels");
        Check(!RedboolAnticipationAuthority.Allows(true,shown,world,null),"missing authority cancels");
        foreach(byte gate in new[]{(byte)8,(byte)9})
        {
            newest.world.gate=gate;Check(!allowed(),"Latest READY/FIGHT cancels anticipation before interpolation");newest.world.gate=0;
            world.gate=gate;Check(!allowed(),"Displayed READY/FIGHT cannot show an early local attack");world.gate=0;
        }
        newest.world.paused=true;Check(!allowed(),"latest pause cancels while display still fighting");newest.world.paused=false;
        newest.world.resolved=true;Check(!allowed(),"latest resolution cancels older display");newest.world.resolved=false;
        newest.world.fighting=false;Check(!allowed(),"latest stopped fight cancels");newest.world.fighting=true;
        newest.world.round++;Check(!allowed(),"latest round cancels older round");newest.world.round--;
        newest.world.selectionRun=Guid.NewGuid();Check(!allowed(),"latest selection run cancels older run");newest.world.selectionRun=Guid.Empty;
        newest.actors[1].character++;Check(!allowed(),"latest avatar cancels older rig");newest.actors[1].character--;
        newest.actors[1].hp=99;Check(!allowed(),"new damage cancels before interpolation reaches it");newest.actors[1].hp=100;
        newest.actors[1].hp=0;Check(!allowed(),"new death cancels");newest.actors[1].hp=100;
        newest.actors[1].live=false;Check(!allowed(),"new tracking loss cancels");newest.actors[1].live=true;
        newest.actors[1].phase=1;Check(!allowed(),"new special phase cancels");newest.actors[1].phase=0;
        newest.actors[1].specialFlags=1;Check(!allowed(),"new enemy special cancels");newest.actors[1].specialFlags=0;
        newest.actors[1].attackSerial++;Check(allowed(),"attack serial change is handled by bounded handoff policy");
        shown.live=false;Check(!allowed(),"invalid displayed actor also cancels");shown.live=true;
        var saved=newest.actors[1];newest.actors[1]=null;Check(!allowed(),"incomplete newest snapshot cancels");newest.actors[1]=saved;
        Check(allowed()&&shown.hp==100&&world.round==2&&!world.paused,"authority gate does not mutate displayed state");
    }
    static void CheckRig(GameObject source,string name,RuntimeAnimatorController replacement,Report report)
    {
        Check(source!=null,name+" visual prefab exists");
        var sourceAnimator=source.GetComponentInChildren<Animator>(true);
        Check(sourceAnimator!=null&&sourceAnimator.avatar!=null,name+" real avatar exists");
        var controller=replacement!=null?replacement:sourceAnimator.runtimeAnimatorController;
        Check(controller!=null,name+" real controller exists");
        VerifyControllerCallbacks(controller,name);
        bool dirtyPrefab=EditorUtility.IsDirty(source),dirtyController=EditorUtility.IsDirty(controller),dirtyAvatar=EditorUtility.IsDirty(sourceAnimator.avatar);
        GameObject fixture=null,helperRoot=null;
        RedboolOnlineAnticipation helper=null;
        try
        {
            // Copy only the authored transform tree. No prefab MonoBehaviour can
            // receive Awake/OnEnable and no renderer, audio or collider is created.
            var map=new Dictionary<Transform,Transform>();
            fixture=CloneTransforms(source.transform,null,map);fixture.SetActive(false);
            var animator=map[sourceAnimator.transform].gameObject.AddComponent<Animator>();
            animator.enabled=false;animator.avatar=sourceAnimator.avatar;animator.runtimeAnimatorController=controller;
            animator.fireEvents=false;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            fixture.SetActive(true);animator.enabled=true;animator.Rebind();
            animator.Play(Animator.StringToHash(animator.GetLayerName(0)+".IDLE"),0,0);animator.Update(0);animator.enabled=false;
            Check(animator.isInitialized&&animator.isHuman,name+" real humanoid initialized");
            var bones=new Transform[(int)HumanBodyBones.LastBone];
            for(int i=0;i<bones.Length;i++)bones[i]=animator.GetBoneTransform((HumanBodyBones)i);
            Check(bones[(int)HumanBodyBones.LeftHand]!=null&&bones[(int)HumanBodyBones.RightHand]!=null,name+" hands resolved");
            helperRoot=new GameObject("Anticipation sampler check");helperRoot.SetActive(false);
            helper=helperRoot.AddComponent<RedboolOnlineAnticipation>();
            var beforeBind=new Pose(fixture.transform);
            var bindState=animator.GetCurrentAnimatorStateInfo(0);
            typeof(RedboolOnlineAnticipation).GetMethod("Bind",Private).Invoke(helper,new object[]{animator,bones});
            Check((bool)typeof(RedboolOnlineAnticipation).GetField("safeRig",Private).GetValue(helper),name+" accepted by production rig guards");
            Check(beforeBind.Exact()&&!animator.enabled&&!animator.fireEvents&&animator.speed==1&&!animator.applyRootMotion
                &&animator.runtimeAnimatorController==controller&&animator.GetCurrentAnimatorStateInfo(0).fullPathHash==bindState.fullPathHash
                &&animator.GetCurrentAnimatorStateInfo(0).normalizedTime==bindState.normalizedTime,name+" creating sampler never evaluates or changes the real rig");
            var isolatedRoot=(GameObject)typeof(RedboolOnlineAnticipation).GetField("samplerRoot",Private).GetValue(helper);
            var isolatedAnimator=(Animator)typeof(RedboolOnlineAnticipation).GetField("sampler",Private).GetValue(helper);
            Check(isolatedRoot!=null&&isolatedAnimator!=null&&isolatedAnimator!=animator,name+" samples on a separate Animator");
            foreach(var component in isolatedRoot.GetComponentsInChildren<Component>(true))
                Check(component is Transform||component==isolatedAnimator,name+" isolated rig has only transforms and its one Animator; no gameplay/renderer/event-receiver component");
            Check(isolatedAnimator.GetBehaviours<StateMachineBehaviour>().Length==0&&!isolatedAnimator.enabled&&!isolatedAnimator.fireEvents
                &&!isolatedAnimator.applyRootMotion,name+" isolated evaluation has no behaviours/events/root-motion/automatic update");
            var isolatedBones=(Transform[])typeof(RedboolOnlineAnticipation).GetField("sampleBones",Private).GetValue(helper);
            for(int i=0;i<bones.Length;i++)if(bones[i]!=null)
                Check(isolatedBones[i]!=null&&isolatedBones[i]!=bones[i]&&isolatedBones[i].IsChildOf(isolatedRoot.transform),name+" forecast bone belongs exclusively to the isolated rig");
            Check(fixture.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&fixture.GetComponentsInChildren<Collider>(true).Length==0
                &&fixture.GetComponentsInChildren<Collider2D>(true).Length==0&&fixture.GetComponentsInChildren<AudioSource>(true).Length==0
                &&fixture.GetComponentsInChildren<Health>(true).Length==0&&fixture.GetComponentsInChildren<Fighter>(true).Length==0,name+" fixture contains no gameplay or audio components");
            var sample=typeof(RedboolOnlineAnticipation).GetMethod("Sample",Private);
            animator.fireEvents=true;animator.speed=.75f;
            for(int move=0;move<States.Length;move++)
            {
                var clip=FindClip(controller,States[move]);
                Check(clip!=null,name+" "+States[move]+" actual clip resolved");
                bool dirtyClip=EditorUtility.IsDirty(clip);
                int rendererCurves=0;var ignoredBindingTargets=new List<string>();
                foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if(binding.type==typeof(Transform)||binding.type==typeof(Animator))continue;
                    string description=binding.type.FullName+" "+binding.path+" "+binding.propertyName;
                    // Hero authored punches have SkinnedMeshRenderer blendshape
                    // curves. These are precisely why the live rig must never be
                    // sampled. Accept only this inspected type, with no target on
                    // the isolated rig; unknown side-effect bindings still fail.
                    Check(binding.type==typeof(SkinnedMeshRenderer)&&binding.propertyName.StartsWith("blendShape.",StringComparison.Ordinal),
                        name+" "+States[move]+" has an unreviewed animation binding: "+description);
                    var target=string.IsNullOrEmpty(binding.path)?isolatedAnimator.transform:isolatedAnimator.transform.Find(binding.path);
                    Check(target==null||target.GetComponent(binding.type)==null,name+" "+States[move]+" side-effect binding has no target: "+description);
                    rendererCurves++;
                    string targetDescription=binding.type.FullName+" "+binding.path+" blendShape.*";
                    if(!ignoredBindingTargets.Contains(targetDescription))ignoredBindingTargets.Add(targetDescription);
                }
                Check(AnimationUtility.GetObjectReferenceCurveBindings(clip).Length==0,name+" "+States[move]+" has no object-reference side effects");
                helper.Policy.Cancel();Check(helper.Policy.Begin(name+"/"+move,Gestures[move],0,7),name+" "+States[move]+" policy starts");
                var before=new Pose(fixture.transform);
                var priorState=animator.GetCurrentAnimatorStateInfo(0);
                Check((bool)sample.Invoke(helper,new object[]{.1f}),name+" "+States[move]+" isolated disabled Animator samples within budget (last "+helper.LastSampleCpuMs.ToString("F3")+" ms)");
                var predicted=(Quaternion[])typeof(RedboolOnlineAnticipation).GetField("forecast",Private).GetValue(helper);
                float largest=0;int changed=0;
                for(int i=0;i<bones.Length;i++)if(bones[i]!=null)
                {float angle=Quaternion.Angle(predicted[i],bones[i].localRotation);largest=Mathf.Max(largest,angle);if(angle>.05f)changed++;}
                Check(changed>0&&largest>.05f,name+" "+States[move]+" produces a changed pose at 100ms");
                Check(before.Exact(),name+" "+States[move]+" restores every transform exactly");
                Check(!animator.enabled&&animator.fireEvents&&animator.speed==.75f&&!animator.applyRootMotion
                    &&animator.cullingMode==AnimatorCullingMode.AlwaysAnimate&&animator.runtimeAnimatorController==controller,name+" "+States[move]+" preserves Animator flags/controller");
                var restored=animator.GetCurrentAnimatorStateInfo(0);
                Check(restored.fullPathHash==priorState.fullPathHash&&Mathf.Abs(restored.normalizedTime-priorState.normalizedTime)<.00001f,name+" "+States[move]+" restores Animator state/time");
                Check(!isolatedAnimator.enabled&&!isolatedAnimator.fireEvents&&!isolatedAnimator.applyRootMotion
                    &&isolatedAnimator.GetCurrentAnimatorStateInfo(0).IsName(States[move]),name+" requested attack was evaluated only on the disabled isolated Animator");
                for(int i=0;i<5;i++)Check((bool)sample.Invoke(helper,new object[]{.08f}),name+" warmup sample");
                var timing=new double[30];
                for(int i=0;i<timing.Length;i++)
                {Check((bool)sample.Invoke(helper,new object[]{.01f+i*.004f}),name+" measured sample");timing[i]=helper.LastSampleCpuMs;}
                Array.Sort(timing);
                Check(before.Exact(),name+" "+States[move]+" repeated samples leave no transform drift");
                Check(helper.SampleFailures==0&&helper.SampleBudgetStops==0&&timing[29]<=RedboolOnlineAnticipation.SampleBudgetMs
                    &&!animator.enabled&&animator.fireEvents&&!isolatedAnimator.enabled&&!isolatedAnimator.fireEvents,
                    name+" repeated samples stay within CPU budget and preserve separate event/automatic-animation flags");
                Check(EditorUtility.IsDirty(clip)==dirtyClip,name+" clip asset was not modified");
                report.samples.Add(new SampleRow {rig=name,state=States[move],asset=AssetDatabase.GetAssetPath(source),changedBones=changed,
                    largestBoneAngleDegrees=largest,measuredSamples=timing.Length,isolatedRendererCurves=rendererCurves,ignoredBindingTargets=ignoredBindingTargets,
                    bindCpuMs=helper.LastBindCpuMs,cpuP50Ms=(timing[14]+timing[15])*.5,cpuP95Ms=timing[28],cpuMaxMs=timing[29]});
            }
            Check(EditorUtility.IsDirty(source)==dirtyPrefab&&EditorUtility.IsDirty(controller)==dirtyController&&EditorUtility.IsDirty(sourceAnimator.avatar)==dirtyAvatar,name+" prefab/controller/avatar assets were not modified");
        }
        finally
        {
            // This helper deliberately stayed inactive; explicitly release its
            // independent clone without relying on Unity's OnDestroy scheduling.
            if(helper!=null)typeof(RedboolOnlineAnticipation).GetMethod("ReleaseSampler",Private).Invoke(helper,null);
            if(helperRoot!=null)Object.DestroyImmediate(helperRoot);
            if(fixture!=null)Object.DestroyImmediate(fixture);
        }
    }
    static GameObject CloneTransforms(Transform source,Transform parent,Dictionary<Transform,Transform> map)
    {
        var copy=new GameObject(source.name);copy.SetActive(false);copy.transform.SetParent(parent,false);
        copy.transform.localPosition=source.localPosition;copy.transform.localRotation=source.localRotation;copy.transform.localScale=source.localScale;
        map[source]=copy.transform;
        for(int i=0;i<source.childCount;i++)CloneTransforms(source.GetChild(i),copy.transform,map);
        if(parent!=null)copy.SetActive(source.gameObject.activeSelf);
        return copy;
    }
    static AnimationClip FindClip(RuntimeAnimatorController runtime,string stateName)
    {
        if(runtime is AnimatorOverrideController replacement)
        {
            var original=FindClip(replacement.runtimeAnimatorController,stateName);
            var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();replacement.GetOverrides(overrides);
            foreach(var entry in overrides)if(entry.Key==original)return entry.Value!=null?entry.Value:original;
            return original;
        }
        var controller=runtime as AnimatorController;
        return controller!=null&&controller.layers.Length>0?FindClip(controller.layers[0].stateMachine,stateName):null;
    }
    static AnimationClip FindClip(AnimatorStateMachine machine,string stateName)
    {
        foreach(var state in machine.states)if(state.state.name==stateName)return state.state.motion as AnimationClip;
        foreach(var child in machine.stateMachines) {var found=FindClip(child.stateMachine,stateName);if(found!=null)return found;}
        return null;
    }
    static void VerifyControllerCallbacks(RuntimeAnimatorController runtime,string name)
    {
        while(runtime is AnimatorOverrideController replacement)runtime=replacement.runtimeAnimatorController;
        var controller=runtime as AnimatorController;
        Check(controller!=null,name+" controller can be inspected before evaluation");
        foreach(var layer in controller.layers)VerifyStateMachine(layer.stateMachine,name);
    }
    static void VerifyStateMachine(AnimatorStateMachine machine,string name)
    {
        Check(machine.behaviours.Length==0,name+" state machine has no callback behaviours");
        foreach(var state in machine.states)Check(state.state.behaviours.Length==0,name+" state has no callback behaviours");
        foreach(var child in machine.stateMachines)VerifyStateMachine(child.stateMachine,name);
    }
}
