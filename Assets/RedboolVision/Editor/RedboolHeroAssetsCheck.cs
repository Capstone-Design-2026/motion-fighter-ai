using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Read-only scene/rig checks for the upstream Hero02 visual integration.</summary>
public static class RedboolHeroAssetsCheck
{
    public static void Run()
    {
        if(!Application.isBatchMode) throw new Exception("Use the isolated batch verification project.");
        var lines=new List<string>(); int count=0;
        foreach(var scene in EditorBuildSettings.scenes.Where(s=>s.enabled))
        {
            EditorSceneManager.OpenScene(scene.path);
            foreach(var p in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var v=p.GetComponent<Fighter3DView>(); if(v==null)continue;
                if(v.visualRoot==null || !v.visualRoot.name.StartsWith("Hero02") || v.visualRoot.parent!=p.transform)
                    throw new Exception("Hero02 player visual missing: "+scene.path);
                var a=v.modelAnimator;
                if(a==null || a.avatar==null || !a.avatar.isValid || !a.avatar.isHuman || a.applyRootMotion)
                    throw new Exception("Invalid avatar/root motion: "+scene.path);
                if(a.runtimeAnimatorController==null || a.runtimeAnimatorController.name!="Unified_Combat"
                    || a.runtimeAnimatorController.animationClips.Any(c=>!c.humanMotion))
                    throw new Exception("Shared combat controller changed: "+scene.path);
                foreach(var bone in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot})
                    if(a.GetBoneTransform(bone)==null)throw new Exception("Missing gameplay bone "+bone+": "+scene.path);
                if(p.GetComponent<Fighter>()==null || p.GetComponent<Health>()==null || p.GetComponent<Rigidbody2D>()==null)
                    throw new Exception("Gameplay root changed: "+scene.path);
                foreach(var r in v.visualRoot.GetComponentsInChildren<Renderer>())
                    if(r.sharedMaterials.Any(m=>m==null || m.shader==null))throw new Exception("Missing visual material: "+scene.path);
                lines.Add("PASS "+scene.path+" Hero02 / shared animations / gameplay root / humanoid bones");count++;
            }
        }
        if(count!=12)throw new Exception("Expected 12 combat scene players, got "+count);
        string output=Environment.GetEnvironmentVariable("REDBOOL_MOTION_CHECK_OUTPUT");
        if(!String.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"hero-scenes.txt"),lines);}
    }
}
