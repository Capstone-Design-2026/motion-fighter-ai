using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class HeroFaceInstaller
{
    const string Folder = "Assets/Characters/Hero02";
    const string Model = Folder + "/Hero02_Rigged_LOD0.fbx";
    const string Prefab = Folder + "/Hero02_Visual.prefab";
    static string Report => Path.Combine(Application.dataPath, "../Logs/HeroFaceIntegration.txt");
    [MenuItem("Tools/Motion Fighter/Apply Hero Face")]
    public static void Apply()
    {
        var report = new List<string>();
        try
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            var desc = importer.humanDescription;
            var names = new[] { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            desc.human = names.Select(n => new HumanBone { boneName=n, humanName=n, limit=new HumanLimit { useDefaultValues=true } }).ToArray();
            importer.humanDescription = desc;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman) throw new Exception("Hero02 Humanoid Avatar invalid");
            report.Add("PASS valid Humanoid avatar, 17 mapped body bones");
            foreach (string path in Directory.GetFiles(Folder + "/Textures", "*.png"))
            {
                var t = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                t.textureType = path.Contains("_Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.sRGBTexture = path.Contains("_BaseColor");
                t.alphaSource = TextureImporterAlphaSource.FromInput;
                t.maxTextureSize = 2048;
                t.SaveAndReimport();
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new Exception("URP Lit missing");
            string materialPath=Folder+"/Hero02.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material=new Material(shader); AssetDatabase.CreateAsset(material,materialPath); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Hero02_BaseColor.png"));
            material.SetColor("_BaseColor",Color.white);
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Hero02_Normal.png"));
            material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/Hero02_MetallicSmoothness.png"));
            material.SetFloat("_Metallic",1); material.SetFloat("_Smoothness",1);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name="Hero02_Visual";
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
            var animator=root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
            animator.avatar=avatar;
            animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(UnifiedBoxingInstaller.Folder+"/Unified_Combat.controller");
            if (!animator.runtimeAnimatorController) throw new Exception("Existing boxing controller missing");
            animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            // Check that each existing combat state can animate this new avatar.
            animator.Rebind();
            foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
            {
                if (!clip.humanMotion) throw new Exception("Non-humanoid clip: "+clip.name);
                var graph=PlayableGraph.Create("Hero face retarget check");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var play=AnimationClipPlayable.Create(graph,clip);
                AnimationPlayableOutput.Create(graph,"Hero",animator).SetSourcePlayable(play);
                graph.Play(); play.SetTime(clip.length*.4); graph.Evaluate(0);
                foreach(var b in new[]{HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightFoot})
                { var p=animator.GetBoneTransform(b).position; if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)) throw new Exception("Invalid retarget pose "+clip.name); }
                graph.Destroy(); report.Add("PASS retarget "+clip.name);
            }
            UnityEngine.Object.DestroyImmediate(root);
            // Save an unposed instance, preserving the imported rest pose.
            root=(GameObject)PrefabUtility.InstantiatePrefab(model); root.name="Hero02_Visual";
            animator=root.GetComponent<Animator>(); animator.avatar=avatar;
            animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(UnifiedBoxingInstaller.Folder+"/Unified_Combat.controller");
            animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var r in root.GetComponentsInChildren<Renderer>()) r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Prefab); UnityEngine.Object.DestroyImmediate(root);
            foreach(var scenePath in EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path))
            {
                var scene=EditorSceneManager.OpenScene(scenePath);
                bool changed=false;
                foreach(var player in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                {
                    var view=player.GetComponent<Fighter3DView>();
                    if(!view) { report.Add("SKIP legacy 2D player: "+scenePath); continue; }
                    var old=view.visualRoot;
                    if(!old || old==player.transform) throw new Exception("Unsafe visual root: "+scenePath);
                    var replacement=(GameObject)PrefabUtility.InstantiatePrefab(prefab,player.transform);
                    replacement.transform.localPosition=old.localPosition;
                    replacement.transform.localRotation=old.localRotation;
                    view.visualRoot=replacement.transform; view.modelAnimator=replacement.GetComponent<Animator>();
                    view.fighterTint=Color.white;
                    // Gameplay root, input receiver, hitboxes and clip references remain intact.
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                    EditorUtility.SetDirty(view); changed=true;
                    report.Add("PASS player visual assigned: "+scenePath);
                }
                if(changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            report.Add("COMPLETE"); File.WriteAllLines(Report,report);
            if(Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch(Exception e) { report.Add(e.ToString()); File.WriteAllLines(Report,report); Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
    }
}
