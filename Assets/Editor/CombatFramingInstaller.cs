using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class CombatFramingInstaller
{
    public static string Out {get {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-framingReport");return i>=0?a[i+1]:Path.Combine(Application.temporaryCachePath,"CombatFraming");}}
    public static readonly string[] Nodes={"scout","river","ridge","old_ferry","gate","quarry","east","camp","rescue","entrance","final","game"};
    public static string ScenePath(int i)=>i==0?"Assets/Scenes/ScoutPass.unity":i==11?"Assets/Scenes/GameScene.unity":"Assets/Scenes/StoryStages/"+StoryStagesBuilder.Scenes[i-1]+".unity";
    public static void ApplyToCamera(Camera camera)
    {
        var rig=camera.GetComponent<Battle3DCamera>();if(!rig)return;
        rig.minimumDistance=rig.orbitArena?4.6f:5.35f;
        rig.focusHeight=1.2f;rig.horizontalSafeArea=.9f;rig.verticalSafeArea=.8f;rig.combatPitch=7;
        rig.player=Object.FindAnyObjectByType<PlayerController>()?.transform;rig.enemy=Object.FindAnyObjectByType<EnemyAI>()?.transform;
        rig.SetOrbit(0,7,true);EditorUtility.SetDirty(rig);EditorUtility.SetDirty(camera.transform);
    }
    public static void Apply()
    {
        Directory.CreateDirectory(Out+"/Backup/Scenes");var records=new List<string>();
        try
        {
            for(int i=0;i<Nodes.Length;i++)
            {
                string path=ScenePath(i),backup=Out+"/Backup/Scenes/"+Path.GetFileName(path);if(!File.Exists(backup))File.Copy(path,backup);
                var scene=EditorSceneManager.OpenScene(path);var camera=Camera.main;
                if(!camera||!camera.GetComponent<Battle3DCamera>())throw new Exception("Missing combat camera "+path);
                ApplyToCamera(camera);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                var rig=camera.GetComponent<Battle3DCamera>();records.Add(Nodes[i]+" distance="+rig.minimumDistance+" focus="+rig.focusHeight+" fov="+camera.fieldOfView);
            }
            AssetDatabase.SaveAssets();File.WriteAllLines(Out+"/applied.txt",records);
            if(Environment.GetCommandLineArgs().Contains("-verifyFraming"))CombatFramingVerification.Run();else EditorApplication.Exit(0);
        }catch(Exception e){File.WriteAllText(Out+"/apply_error.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
