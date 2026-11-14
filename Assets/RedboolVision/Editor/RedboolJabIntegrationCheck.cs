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

// Measure the installed clips on their actual humanoid rigs, within each clip's
// physical contact window. Immutable previous clips prevent GUID reuse from
// silently changing the baseline. No range/hitbox parameters are modified.
public static class RedboolJabIntegrationCheck
{
    [Serializable] public class Pose {
        public string version, clip, move; public float length, peakTime, elbowDegrees, wristForward,
            wristFromShoulder, armLength, extensionRatio, height, windowStart, windowEnd;
    }
    [Serializable] public class Report { public Pose[] samples; public bool passed; public string scope; }
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("REDBOOL_JAB_CHECK_OUTPUT") ?? "Logs/JabIntegration";
        Directory.CreateDirectory(output);
        try {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(35,-25,0);
            const string hero="Assets/Characters/Hero02/Hero02_Visual.prefab";
            const string oldHero="Assets/RedboolVision/Editor/Fixtures/Hero02_V33.prefab";
            const string p2="Assets/Characters/StoryOpponents/05_Cheolmun/05_Cheolmun_Visual.prefab";
            var poses=new[]{
                Measure("hero_v33_jab",oldHero,output,CombatMove.Jab,"Assets/RedboolVision/Editor/Fixtures/HeroJab_V33.anim"),
                Measure("hero_v34_jab",hero,output,CombatMove.Jab),
                Measure("hero_v33_straight",oldHero,output,CombatMove.Straight,"Assets/RedboolVision/Editor/Fixtures/HeroCross_V33.anim",new Vector2(.295f,.43f)),
                Measure("hero_v34_straight",hero,output,CombatMove.Straight),
                Measure("p2_v33_jab",p2,output,CombatMove.Jab,"Assets/RedboolVision/Editor/Fixtures/CheolmunJab_V33.anim"),
                Measure("p2_upstream_jab",p2,output,CombatMove.Jab,RedboolPvpJabBake.Original),
                Measure("p2_selected_jab",p2,output,CombatMove.Jab,RedboolPvpJabBake.Selected),
                Measure("p2_upstream_straight",p2,output,CombatMove.Straight)
            };
            var report=new Report {samples=poses,scope="Sampled authored humanoid geometry and corrective skin on actual rigs within physical contact windows. No hitbox inflation; blended animation, actual contact/whiff and held guard tested separately by HeroJabPlayCheck.",
                passed=poses.All(p=>p.armLength>0 && p.wristFromShoulder>0 && p.elbowDegrees>90f && p.extensionRatio>.7f)
                    && poses[1].elbowDegrees>=165f && poses[1].extensionRatio>=.96f
                    && poses[3].elbowDegrees>=155f && poses[3].extensionRatio>=.92f
                    && poses[6].elbowDegrees>=165f && poses[6].extensionRatio>=.96f
                    && Math.Abs(poses[6].length-poses[5].length)<.001f};
            File.WriteAllText(Path.Combine(output,"comparison.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[REDBOOL JAB] "+JsonUtility.ToJson(report));
            EditorApplication.Exit(report.passed?0:1);
        } catch(Exception e) {File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static Pose Measure(string version,string path,string output,CombatMove move,string clipPath=null,Vector2? oldWindow=null)
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var anim=model.GetComponentsInChildren<Animator>(true).First(a=>a.avatar&&a.avatar.isHuman);
        model.transform.position=Vector3.zero;model.transform.rotation=Quaternion.identity;
        var clip=clipPath==null?anim.runtimeAnimatorController.animationClips.First(c=>c&&c.name.EndsWith(move==CombatMove.Jab?"_JAB":"_CROSS")):AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        anim.applyRootMotion=false;anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())r.forceMatrixRecalculationPerRender=true;
        // Match Fighter3DView's runtime neutral tint rather than a prefab's unset property value.
        var tint=new MaterialPropertyBlock();tint.SetColor("_BaseColor",Color.white);tint.SetColor("_Color",Color.white);
        foreach(var r in model.GetComponentsInChildren<Renderer>())r.SetPropertyBlock(tint);
        var graph=PlayableGraph.Create("JabComparison");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var play=AnimationClipPlayable.Create(graph,clip);play.SetApplyFootIK(false);play.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(graph,"Jab",anim).SetSourcePlayable(play);graph.Play();
        var shoulder=anim.GetBoneTransform(move==CombatMove.Jab?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
        var elbow=anim.GetBoneTransform(move==CombatMove.Jab?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
        var wrist=anim.GetBoneTransform(move==CombatMove.Jab?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var head=anim.GetBoneTransform(HumanBodyBones.Head);
        var foot=anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        var window=oldWindow??CombatContactWorld.Window(move);
        var best=new Pose{version=version,move=move.ToString(),clip=clip.name,length=clip.length,windowStart=window.x,windowEnd=window.y,wristForward=float.NegativeInfinity};
        for(int i=0;i<=180;i++) {
            float t=Mathf.Lerp(window.x,window.y,i/180f);play.SetTime(t);graph.Evaluate(0f);
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
        foreach(var surface in model.GetComponentsInChildren<HeroPunchSurfaceVisibility>(true))surface.ApplyRemoteSurface(move==CombatMove.Jab?(byte)1:(byte)2);
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
