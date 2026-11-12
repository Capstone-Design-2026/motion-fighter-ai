using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

// A PvP-only copy. Torso, shoulder, footwork and the NPC's original assets stay unchanged.
public static class RedboolPvpJabBake
{
    public const string Original="Assets/Characters/StoryOpponents/05_Cheolmun/Animation/05_Cheolmun_JAB.anim";
    public const string Output="Assets/Resources/RedboolPvpCheolmun_JAB.anim";
    public static void Run()
    {
        try {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Original);
            var binding=AnimationUtility.GetCurveBindings(source).Single(b=>b.propertyName=="Left Forearm Stretch");
            var curve=AnimationUtility.GetEditorCurve(source,binding);
            float peak=curve.Evaluate(.2f),lo=peak,hi=1f;
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_Visual.prefab"));
            var anim=model.GetComponentsInChildren<Animator>(true).First(a=>a.avatar&&a.avatar.isHuman);
            var clip=Object.Instantiate(source);clip.name="RedboolPvpCheolmun_JAB";
            float angle=0;
            for(int i=0;i<18;i++) {
                float target=(lo+hi)*.5f;
                AnimationUtility.SetEditorCurve(clip,binding,Extend(curve,peak,target));
                angle=Sample(anim,clip,.2f);
                if(angle<170f)lo=target;else hi=target;
            }
            float final=(lo+hi)*.5f;
            AnimationUtility.SetEditorCurve(clip,binding,Extend(curve,peak,final));
            angle=Sample(anim,clip,.2f);
            if(angle<169.5f||angle>171f)throw new Exception("Elbow target failed: "+angle);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
            if(existing){EditorUtility.CopySerialized(clip,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(clip);}else AssetDatabase.CreateAsset(clip,Output);
            var controller=Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>("Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_UnifiedBoxing.overrideController"));
            controller.name="RedboolPvpCheolmun";
            var mappings=new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip,AnimationClip>>();controller.GetOverrides(mappings);
            int replaced=0;
            for(int i=0;i<mappings.Count;i++)if(mappings[i].Value && mappings[i].Value.name=="05_Cheolmun_JAB") {
                mappings[i]=new System.Collections.Generic.KeyValuePair<AnimationClip,AnimationClip>(mappings[i].Key,AssetDatabase.LoadAssetAtPath<AnimationClip>(Output));replaced++;
            }
            if(replaced!=1)throw new Exception("Expected exactly one Cheolmun jab mapping: "+replaced);
            controller.ApplyOverrides(mappings);
            const string controllerPath="Assets/Resources/RedboolPvpCheolmun.overrideController";
            var previous=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(controllerPath);
            if(previous){EditorUtility.CopySerialized(controller,previous);EditorUtility.SetDirty(previous);Object.DestroyImmediate(controller);}else AssetDatabase.CreateAsset(controller,controllerPath);
            AssetDatabase.SaveAssets();
            string report=Environment.GetEnvironmentVariable("REDBOOL_JAB_CHECK_OUTPUT")??"Logs/PvpJab";Directory.CreateDirectory(report);
            File.WriteAllText(Path.Combine(report,"bake.txt"),"Only Left Forearm Stretch changed\nOriginal muscle peak="+peak+"\nTarget="+final+"\nElbow="+angle+"\n");
            Object.DestroyImmediate(model);
            Debug.Log("[PVP JAB BAKE] elbow="+angle+" muscle="+final);
            EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static AnimationCurve Extend(AnimationCurve original,float peak,float target)
    {
        float delta=target-peak,span=peak+.3f;
        var keys=original.keys;
        for(int i=0;i<keys.Length;i++) {
            var k=keys[i];float u=Mathf.Clamp01((k.value+.3f)/span);
            float derivative=1f+delta*6f*u*(1f-u)/span;
            k.value+=delta*u*u*(3f-2f*u);k.inTangent*=derivative;k.outTangent*=derivative;keys[i]=k;
        }
        return new AnimationCurve(keys){preWrapMode=original.preWrapMode,postWrapMode=original.postWrapMode};
    }
    static float Sample(Animator anim,AnimationClip clip,float t)
    {
        var graph=PlayableGraph.Create("PvpElbowBake");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try {
            var play=AnimationClipPlayable.Create(graph,clip);play.SetApplyFootIK(false);play.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph,"Jab",anim).SetSourcePlayable(play);graph.Play();play.SetTime(t);graph.Evaluate(0f);
            var a=anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);var b=anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);var c=anim.GetBoneTransform(HumanBodyBones.LeftHand);
            return Vector3.Angle(a.position-b.position,c.position-b.position);
        }finally{graph.Destroy();}
    }
}
