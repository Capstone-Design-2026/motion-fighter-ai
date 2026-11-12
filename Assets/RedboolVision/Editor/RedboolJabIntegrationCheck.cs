using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Compare visible authored arm extension on the actual v32/v33 rigs. This does not
// enlarge hitboxes or tune camera recognition. Contact/gameplay tests run separately.
public static class RedboolJabIntegrationCheck
{
    [Serializable] public class Pose {
        public string version, clip; public float length, peakTime, elbowDegrees, wristForward,
            wristFromShoulder, armLength, extensionRatio, height;
    }
    [Serializable] public class Report { public Pose before, after, pvpOpponent, pvpExtended; public bool passed, onlyElbowCurveChanged, onlyJabOverrideChanged; public string scope; }
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("REDBOOL_JAB_CHECK_OUTPUT") ?? "Logs/JabIntegration";
        Directory.CreateDirectory(output);
        try {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(35,-25,0);
            var report=new Report {
                before=Measure("v32", "Assets/RedboolVision/Editor/Fixtures/Hero02_V32.prefab",output),
                after=Measure("v33", "Assets/Characters/Hero02/Hero02_Visual.prefab",output),
                pvpOpponent=Measure("pvp_p2", "Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_Visual.prefab",output),
                pvpExtended=Measure("pvp_p2_extended", "Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_Visual.prefab",output,RedboolPvpJabBake.Output),
                onlyElbowCurveChanged=CheckCurves(), onlyJabOverrideChanged=CheckController(),
                scope="Sampled visible rig extension within the existing .12-.215 second contact window; no hitbox inflation. Live blending/contact tested by HeroJabPlayCheck."
            };
            report.passed=report.onlyElbowCurveChanged && report.onlyJabOverrideChanged && report.after.clip=="Hero_JAB" && report.after.elbowDegrees>=165f && report.after.extensionRatio>=.96f && report.pvpExtended.elbowDegrees>=168f && report.pvpExtended.elbowDegrees<=174f && report.pvpExtended.wristForward>=report.pvpOpponent.wristForward;
            File.WriteAllText(Path.Combine(output,"comparison.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[REDBOOL JAB] "+JsonUtility.ToJson(report));
            EditorApplication.Exit(report.passed?0:1);
        } catch(Exception e) {File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static bool CheckCurves()
    {
        var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(RedboolPvpJabBake.Original);
        var revised=AssetDatabase.LoadAssetAtPath<AnimationClip>(RedboolPvpJabBake.Output);
        var bindings=AnimationUtility.GetCurveBindings(original);
        if(bindings.Length!=AnimationUtility.GetCurveBindings(revised).Length || Math.Abs(original.length-revised.length)>.0001f)return false;
        int changed=0;
        foreach(var b in bindings) {
            var a=AnimationUtility.GetEditorCurve(original,b);var c=AnimationUtility.GetEditorCurve(revised,b);
            bool equal=c!=null && a.keys.SequenceEqual(c.keys) && a.preWrapMode==c.preWrapMode && a.postWrapMode==c.postWrapMode;
            if(!equal){changed++;if(b.propertyName!="Left Forearm Stretch")return false;}
        }
        return changed==1;
    }
    static bool CheckController()
    {
        var original=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>("Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_UnifiedBoxing.overrideController");
        var revised=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>("Assets/Resources/RedboolPvpCheolmun.overrideController");
        if(original.runtimeAnimatorController!=revised.runtimeAnimatorController)return false;
        var before=new List<KeyValuePair<AnimationClip,AnimationClip>>();var after=new List<KeyValuePair<AnimationClip,AnimationClip>>();original.GetOverrides(before);revised.GetOverrides(after);
        if(before.Count!=after.Count)return false;
        int changed=0;
        foreach(var pair in before){var target=after.Single(p=>p.Key==pair.Key).Value;if(target==pair.Value)continue;changed++;if(pair.Value.name!="05_Cheolmun_JAB" || target.name!="RedboolPvpCheolmun_JAB")return false;}
        return changed==1;
    }
    static Pose Measure(string version,string path,string output,string clipPath=null)
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var anim=model.GetComponentsInChildren<Animator>(true).First(a=>a.avatar&&a.avatar.isHuman);
        model.transform.position=Vector3.zero;model.transform.rotation=Quaternion.identity;
        var clip=clipPath==null?anim.runtimeAnimatorController.animationClips.First(c=>c&&c.name.EndsWith("_JAB")):AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        anim.applyRootMotion=false;anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())r.forceMatrixRecalculationPerRender=true;
        // Match Fighter3DView's runtime neutral tint rather than a prefab's unset property value.
        var tint=new MaterialPropertyBlock();tint.SetColor("_BaseColor",Color.white);tint.SetColor("_Color",Color.white);
        foreach(var r in model.GetComponentsInChildren<Renderer>())r.SetPropertyBlock(tint);
        var graph=PlayableGraph.Create("JabComparison");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var play=AnimationClipPlayable.Create(graph,clip);play.SetApplyFootIK(false);play.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(graph,"Jab",anim).SetSourcePlayable(play);graph.Play();
        var shoulder=anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);var elbow=anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var wrist=anim.GetBoneTransform(HumanBodyBones.LeftHand);var head=anim.GetBoneTransform(HumanBodyBones.Head);
        var foot=anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var best=new Pose{version=version,clip=clip.name,length=clip.length,wristForward=float.NegativeInfinity};
        for(int i=0;i<=180;i++) {
            float t=.12f+(.215f-.12f)*i/180f;play.SetTime(t);graph.Evaluate(0f);
            float forward=Vector3.Dot(wrist.position-model.transform.position,model.transform.forward);
            if(forward<=best.wristForward)continue;
            best.peakTime=t;best.wristForward=forward;
            best.elbowDegrees=Vector3.Angle(shoulder.position-elbow.position,wrist.position-elbow.position);
            best.armLength=Vector3.Distance(shoulder.position,elbow.position)+Vector3.Distance(elbow.position,wrist.position);
            best.extensionRatio=Vector3.Distance(shoulder.position,wrist.position)/best.armLength;
            best.wristFromShoulder=Vector3.Dot(wrist.position-shoulder.position,model.transform.forward);
            best.height=head.position.y-foot.position.y;
        }
        play.SetTime(best.peakTime);graph.Evaluate(0f);
        var camera=new GameObject("Comparison camera").AddComponent<Camera>();camera.orthographic=true;
        camera.orthographicSize=1.1f;camera.nearClipPlane=.01f;camera.farClipPlane=30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.27f,.30f);
        var rt=new RenderTexture(1000,1000,24);camera.targetTexture=rt;
        foreach(var view in new[]{("side",new Vector3(5,1,.1f)),("quarter",new Vector3(4,1,4))}) {
            camera.transform.position=view.Item2;camera.transform.LookAt(new Vector3(0,.95f,.18f));
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=null;
            File.WriteAllBytes(Path.Combine(output,version+"_"+view.Item1+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
        }
        graph.Destroy();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(model);
        return best;
    }
}
