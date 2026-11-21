using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

/// <summary>Imports the approved story opponents 02-11 (01 is the scout); preserves the shared combat state machine.</summary>
public static class StoryCharacterInstaller
{
    public const string Root="Assets/Characters/StoryOpponents";
    public static readonly string[] Keys={"02_ValleyAmbusher","03_HiredBandit","04_FerryRaider","05_Cheolmun","06_QuarryElite","07_RidgeElite","08_Tracker","09_Warden","10_Heukgyeon","11_Mujin"};
    public static readonly string[] Nodes={"river","ridge","old_ferry","gate","quarry","east","camp","rescue","entrance","final"};
    public static readonly string[] Scenes={"RiverAmbush","WindRidge","OldFerry","SealedGate","HiddenQuarry","EasternRidge","SearchCamp","PrisonStockade","BlackCloudEntrance","SluiceFortress"};
    public static readonly string[] Names={"계곡 잠복병","고용 산적","옛 나루 약탈자","철문","채석장 정예병","능선 정예병","추포대 추적자","옥졸장","흑견","무진"};
    public static readonly float[] Heights={1.80f,1.92f,1.72f,1.98f,1.86f,1.78f,1.85f,1.94f,1.89f,2f};
    public static string Out {get{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-characterReport");return i>=0?args[i+1]:Path.Combine(Application.temporaryCachePath,"StoryCharacters");}}
    /// <summary>Indices to install/finalize/verify. "-characterKeys 02_ValleyAmbusher,03_HiredBandit" limits the run; default is every character.</summary>
    public static int[] Selected {get{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-characterKeys");var all=Enumerable.Range(0,Keys.Length);if(i<0||i+1>=args.Length)return all.ToArray();var want=args[i+1].Split(',');return all.Where(k=>want.Contains(Keys[k])).ToArray();}}
    public static string PrefabPath(int i)=>Root+"/"+Keys[i]+"/"+Keys[i]+"_Visual.prefab";
    public static void ApplyConfiguredVisual(string nodeId)
    {
        if(!Nodes.Contains(nodeId))return;
        var config=AssetDatabase.LoadAssetAtPath<RunConfig>("Assets/Resources/RunConfig.asset");
        var prefab=config.GetEnemyVisual(nodeId);if(!prefab||!AssetDatabase.GetAssetPath(prefab).StartsWith(Root+"/"))return;
        var enemy=Object.FindAnyObjectByType<EnemyAI>();if(!enemy)return;
        var view=enemy.GetComponent<Fighter3DView>();if(!view)return;
        var old=view.visualRoot;var pos=old?old.localPosition:Vector3.zero;var rot=old?old.localRotation:Quaternion.identity;
        var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,enemy.transform);root.transform.localPosition=pos;root.transform.localRotation=rot;
        view.visualRoot=root.transform;view.modelAnimator=root.GetComponentInChildren<Animator>();view.fighterTint=Color.white;
        if(old)Object.DestroyImmediate(old.gameObject);EditorUtility.SetDirty(view);
    }
    public static void FinalizeScenes()
    {
        Directory.CreateDirectory(Out+"/Backup/Scenes");
        try
        {
            AssetDatabase.Refresh();
            var notes=new List<string>();
            foreach(int i in Selected)
            {
                string path="Assets/Scenes/StoryStages/"+Scenes[i]+".unity", backup=Out+"/Backup/Scenes/"+Scenes[i]+".unity";
                if(!File.Exists(backup))File.Copy(path,backup);
                var scene=EditorSceneManager.OpenScene(path);ApplyConfiguredVisual(Nodes[i]);
                var view=Object.FindAnyObjectByType<EnemyAI>().GetComponent<Fighter3DView>();
                if(!view.modelAnimator.isHuman||!view.modelAnimator.avatar.isValid||view.visualRoot.name!=Keys[i]+"_Visual")throw new Exception("Scene visual invalid "+Scenes[i]);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);notes.Add(Scenes[i]+": "+view.visualRoot.name);
            }
            AssetDatabase.SaveAssets();File.WriteAllLines(Out+"/scene_installation.txt",notes);EditorApplication.Exit(0);
        }catch(Exception e){File.WriteAllText(Out+"/scene_error.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static T Save<T>(T asset,string path) where T:Object
    {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(asset,path);return asset;}
    public static void Apply()
    {
        Directory.CreateDirectory(Out);
        try
        {
            AssetDatabase.Refresh();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var report=new List<string>();var config=AssetDatabase.LoadAssetAtPath<RunConfig>("Assets/Resources/RunConfig.asset");
            var shared=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(UnifiedBoxingInstaller.Folder+"/Unified_Combat.controller");
            var scoutController=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(UnifiedBoxingInstaller.Folder+"/Unified_Scout.overrideController");
            var sourcePairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();scoutController.GetOverrides(sourcePairs);
            foreach(int i in Selected)
            {
                string folder=Root+"/"+Keys[i], fbx=folder+"/"+Keys[i]+".fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
                importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.optimizeGameObjects=false;importer.isReadable=false;
                importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);var modelAnimator=model.GetComponent<Animator>();
                if(!modelAnimator||!modelAnimator.avatar||!modelAnimator.avatar.isHuman||!modelAnimator.avatar.isValid)throw new Exception(Keys[i]+" invalid humanoid");
                // Explicit finger mapping is needed for a closed boxing fist, not just major joints.
                var desc=importer.humanDescription;var map=desc.human.ToList();var bones=model.GetComponentsInChildren<Transform>().Select(t=>t.name).ToHashSet();
                foreach(string side in new[]{"Left","Right"})foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})for(int j=0;j<3;j++)
                {
                    string human=side+" "+finger+" "+new[]{"Proximal","Intermediate","Distal"}[j],bone="mixamorig:"+side+"Hand"+(finger=="Little"?"Pinky":finger)+(j+1);
                    if(bones.Contains(bone)){map.RemoveAll(b=>b.humanName==human);map.Add(new HumanBone{humanName=human,boneName=bone,limit=new HumanLimit{useDefaultValues=true}});}
                }
                desc.human=map.ToArray();importer.humanDescription=desc;importer.SaveAndReimport();model=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                foreach(string file in Directory.GetFiles(folder,"*.png"))
                {
                    string path=file.Replace('\\','/');var tex=(TextureImporter)AssetImporter.GetAtPath(path);
                    tex.maxTextureSize=2048;tex.mipmapEnabled=true;tex.textureCompression=TextureImporterCompression.CompressedHQ;
                    if(path.Contains("Normal")){tex.textureType=TextureImporterType.NormalMap;tex.sRGBTexture=false;}
                    else tex.sRGBTexture=path.Contains("BaseColor");tex.SaveAndReimport();
                }
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Keys[i]+"_Clothing"};
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+Keys[i]+"_BaseColor.png"));material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.24f);material.SetFloat("_Metallic",.02f);
                var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+Keys[i]+"_Normal.png");if(normal){material.SetTexture("_BumpMap",normal);material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",.65f);}
                material=Save(material,folder+"/"+Keys[i]+"_Clothing.mat");
                var root=new GameObject(Keys[i]+"_Visual");var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);instance.transform.SetParent(root.transform,false);instance.name=Names[i]+" Model";
                Bounds bounds=new Bounds();bool first=true;foreach(var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>()){skin.sharedMaterials=Enumerable.Repeat(material,skin.sharedMaterials.Length).ToArray();skin.updateWhenOffscreen=true;if(first){bounds=skin.bounds;first=false;}else bounds.Encapsulate(skin.bounds);}
                instance.transform.localScale=Vector3.one*(Heights[i]/bounds.size.y);
                var actor=instance.GetComponent<Animator>();actor.applyRootMotion=false;actor.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                actor.runtimeAnimatorController=shared;PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(i));Object.DestroyImmediate(root);
                var overrides=new AnimatorOverrideController(shared){name=Keys[i]+"_UnifiedBoxing"};
                var calibrated=Calibrate(PrefabPath(i),folder,sourcePairs,report);
                overrides.ApplyOverrides(calibrated);var saved=Save(overrides,folder+"/"+Keys[i]+"_UnifiedBoxing.overrideController");
                root=PrefabUtility.LoadPrefabContents(PrefabPath(i));root.GetComponentInChildren<Animator>().runtimeAnimatorController=saved;PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(i));PrefabUtility.UnloadPrefabContents(root);
                config.nodeEnemyVisuals.RemoveAll(n=>n!=null&&n.nodeId==Nodes[i]);config.nodeEnemyVisuals.Add(new NodeEnemyVisual{nodeId=Nodes[i],visualPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i))});
                report.Add(Keys[i]+": "+Nodes[i]+" / "+Names[i]+" / valid humanoid / height="+Heights[i]);
                File.WriteAllLines(Out+"/installation.txt",report);AssetDatabase.SaveAssets();
            }
            EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();
            File.WriteAllText(Out+"/install_done.txt","PASS");
            if(Environment.GetCommandLineArgs().Contains("-verifyCharacters"))StoryCharacterVerification.Run();else EditorApplication.Exit(0);
        }catch(Exception e){File.WriteAllText(Out+"/install_error.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static List<KeyValuePair<AnimationClip,AnimationClip>> Calibrate(string path,string folder,List<KeyValuePair<AnimationClip,AnimationClip>> pairs,List<string> report)
    {
        UnifiedBoxingInstaller.EnsureFolder(folder+"/Animation");
        var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var reference=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Scout));
        var ta=target.GetComponentInChildren<Animator>();var ra=reference.GetComponentInChildren<Animator>();
        foreach(var a in new[]{ta,ra}){a.runtimeAnimatorController=null;a.applyRootMotion=false;a.Rebind();}
        var result=new List<KeyValuePair<AnimationClip,AnimationClip>>();
        var binding=EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y");
        foreach(var pair in pairs)
        {
            var source=pair.Value?pair.Value:pair.Key;if(!source.humanMotion)throw new Exception("Nonhuman clip "+source.name);
            var clip=Object.Instantiate(source);clip.name=Path.GetFileName(folder)+"_"+pair.Key.name.Replace("Unified_Common_","").Replace("(Clone)","");
            var graph=PlayableGraph.Create("Story opponent floor calibration");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var tp=AnimationClipPlayable.Create(graph,source);var rp=AnimationClipPlayable.Create(graph,source);tp.SetApplyFootIK(false);rp.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph,"Target",ta).SetSourcePlayable(tp);AnimationPlayableOutput.Create(graph,"Reference",ra).SetSourcePlayable(rp);graph.Play();
            var y=AnimationUtility.GetEditorCurve(source,binding);if(y==null)throw new Exception("Root curve missing "+source.name);
            int n=Mathf.Max(2,Mathf.CeilToInt(source.length*60)+1);var values=new Keyframe[n];float maximum=0;
            for(int f=0;f<n;f++)
            {
                float t=source.length*f/(n-1);tp.SetTime(Mathf.Min(t,source.length-.00001f));rp.SetTime(Mathf.Min(t,source.length-.00001f));graph.Evaluate(0);
                float delta=UnifiedBoxingInstaller.SkinFloor(reference)-UnifiedBoxingInstaller.SkinFloor(target);maximum=Mathf.Max(maximum,Mathf.Abs(delta));
                values[f]=new Keyframe(t,y.Evaluate(t)+delta/ta.humanScale);
            }
            graph.Destroy();var curve=new AnimationCurve(values);
            for(int k=0;k<n;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);}
            AnimationUtility.SetEditorCurve(clip,binding,curve);
            clip=Save(clip,folder+"/Animation/"+clip.name+".anim");result.Add(new KeyValuePair<AnimationClip,AnimationClip>(pair.Key,clip));
            report.Add(clip.name+" length="+source.length.ToString("F3")+" max contact correction="+maximum.ToString("F4"));
        }
        Object.DestroyImmediate(target);Object.DestroyImmediate(reference);return result;
    }
}
