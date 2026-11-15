using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Read-only validation of every enabled combat scene and all configured routes.
public static class RedboolHeroAssetsCheck
{
    public static void Run()
    {
        if(!Application.isBatchMode) throw new Exception("Use batch verification.");
        var lines=new List<string>();
        var enabled=EditorBuildSettings.scenes.Where(s=>s.enabled).ToArray();
        var menus=new HashSet<string>{"StartScene","MapScene","EndingScene"};
        var combat=enabled.Where(s=>!menus.Contains(Path.GetFileNameWithoutExtension(s.path))).ToArray();
        var names=new HashSet<string>(combat.Select(s=>Path.GetFileNameWithoutExtension(s.path)));
        var config=Resources.Load<RunConfig>("RunConfig");
        if(config==null)throw new Exception("Missing RunConfig");
        foreach(var route in config.nodeBattleScenes)
            if(!names.Contains(route.sceneName))throw new Exception("Unbuilt story route: "+route.sceneName);
        foreach(var route in new[]{RedboolPvp.ArenaScene,RedboolDuelTest.ArenaScene})
            if(!names.Contains(route))throw new Exception("Unbuilt demo route: "+route);
        foreach(var scene in combat)
        {
            EditorSceneManager.OpenScene(scene.path);
            var players=Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            if(players.Length!=1)throw new Exception("Expected one authored player in "+scene.path+", got "+players.Length);
            var p=players[0];var v=p.GetComponent<Fighter3DView>();
            if(v==null || v.visualRoot==null || !v.visualRoot.name.StartsWith("Hero02") || v.visualRoot.parent!=p.transform)
                throw new Exception("Hero02 player visual missing: "+scene.path);
            var a=v.modelAnimator;
            if(a==null || a.avatar==null || !a.avatar.isValid || !a.avatar.isHuman || a.applyRootMotion)
                throw new Exception("Invalid avatar/root motion: "+scene.path);
            var controller=a.runtimeAnimatorController;
            if(controller==null || controller.animationClips.Any(c=>c==null || !c.humanMotion))
                throw new Exception("Invalid humanoid combat controller: "+scene.path);
            foreach(var suffix in new[]{"JAB","CROSS","HIGHKICK","LOWKICK"})
                if(!controller.animationClips.Any(c=>c.name.EndsWith(suffix)))throw new Exception("Missing action "+suffix+": "+scene.path);
            foreach(var bone in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot})
                if(a.GetBoneTransform(bone)==null)throw new Exception("Missing gameplay bone "+bone+": "+scene.path);
            if(p.GetComponent<Fighter>()==null || p.GetComponent<Health>()==null || p.GetComponent<Rigidbody2D>()==null)
                throw new Exception("Gameplay root changed: "+scene.path);
            foreach(var r in v.visualRoot.GetComponentsInChildren<Renderer>(true))
                if(r.sharedMaterials.Any(m=>m==null || m.shader==null))throw new Exception("Missing visual material: "+scene.path);
            foreach(var r in v.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(r.sharedMesh==null)throw new Exception("Missing skin mesh: "+scene.path);
            var surface=v.visualRoot.GetComponentInChildren<HeroPunchSurfaceVisibility>(true);
            if(surface==null || surface.animator!=a || surface.original==null || surface.original.Length==0
                || surface.original.Any(r=>r==null) || surface.jabSurface==null || surface.straightSurface==null
                || surface.jabSurface.sharedMesh==surface.straightSurface.sharedMesh)
                throw new Exception("Missing attack-specific skin correction: "+scene.path);
            surface.ApplyRemoteSurface(1);
            if(!surface.jabSurface.enabled || surface.straightSurface.enabled || surface.original.Any(r=>r.enabled))throw new Exception("Jab surface switch failed");
            surface.ApplyRemoteSurface(2);
            if(surface.jabSurface.enabled || !surface.straightSurface.enabled || surface.original.Any(r=>r.enabled))throw new Exception("Straight surface switch failed");
            surface.ApplyRemoteSurface(0);
            if(surface.jabSurface.enabled || surface.straightSurface.enabled || surface.original.Any(r=>!r.enabled))throw new Exception("Neutral surface switch failed");
            lines.Add("PASS "+scene.path+" Hero02 / humanoid actions / bones / materials / corrective meshes / gameplay root");
        }
        lines.Add("PASS all "+combat.Length+" combat scenes; "+enabled.Length+" enabled scenes; all story and solo/PvP routes built");
        string output=Environment.GetEnvironmentVariable("REDBOOL_MOTION_CHECK_OUTPUT");
        if(!String.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"hero-scenes.txt"),lines);}
        Debug.Log(lines[lines.Count-1]);
    }
}
