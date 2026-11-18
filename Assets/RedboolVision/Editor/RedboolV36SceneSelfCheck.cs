using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>Asset-only v36 scene integration audit. Does not open/save scenes or start a camera.</summary>
public static class RedboolV36SceneSelfCheck
{
    const string LegacyGuid = "69f640a55f4836b46945dd0f59650707";
    static readonly string[] RemovedIds = {"1555233377","1555233378","1555233379","1555233380","1555233381","1557763670","1557763671","1557763672","879232032","879232033","879232034","879232035"};
    static readonly List<string> checks = new List<string>();
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);checks.Add(message);}
    public static void Run()
    {
        checks.Clear();int enabled=0,combat=0;var scripts=new HashSet<string>();
        foreach(var scene in EditorBuildSettings.scenes)
        {
            if(!scene.enabled)continue;enabled++;
            Check(File.Exists(scene.path),"Enabled scene exists: "+scene.path);
            string text=File.ReadAllText(scene.path);
            Check(!text.Contains(LegacyGuid)&&!Regex.IsMatch(text,@"^  m_Name: Webcam(UI|Manager)$",RegexOptions.Multiline),"No removed legacy webcam listener/preview: "+scene.path);
            foreach(Match match in Regex.Matches(text,@"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}"))scripts.Add(match.Groups[1].Value);
            if(scene.path=="Assets/Scenes/GameScene.unity"||scene.path=="Assets/Scenes/ScoutPass.unity"
                ||scene.path.StartsWith("Assets/Scenes/Hybrid/",StringComparison.Ordinal)
                ||scene.path.StartsWith("Assets/Scenes/StoryStages/",StringComparison.Ordinal)
                ||scene.path.StartsWith("Assets/Scenes/PvpStages/",StringComparison.Ordinal))
            {
                combat++;
                foreach(string id in RemovedIds)Check(!Regex.IsMatch(text,@"fileID: "+id+@"\b"),"Removed webcam object has no dangling local reference: "+Path.GetFileName(scene.path)+"/"+id);
            }
        }
        Check(enabled==28&&combat==24,"All 28 enabled scenes (BootScene intro + 27) retain 24 integrated combat arenas");
        foreach(string guid in scripts)
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            Check(!string.IsNullOrEmpty(path)&&AssetDatabase.LoadAssetAtPath<MonoScript>(path)!=null,"Every enabled scene script GUID resolves: "+guid);
        }
        foreach(string path in Directory.GetFiles("Assets","*.prefab",SearchOption.AllDirectories))
            Check(!File.ReadAllText(path).Contains(LegacyGuid),"Prefab has no removed webcam script: "+path);
        Check(!File.Exists("Assets/Scripts/WebCam/UDPWebcamReceiver.cs"),"Legacy UDP webcam implementation is removed");
        Check(File.Exists("Assets/RedboolVision/RedboolCameraRuntime.cs")&&File.Exists("Assets/RedboolVision/RedboolDemoReceiver.cs"),"Integrated camera lifecycle and guarded receiver remain present");
        string result="PASS "+checks.Count+" v36 scene checks\n"+string.Join("\n",checks.ToArray());
        string output=Environment.GetEnvironmentVariable("REDBOOL_V36_SCENE_CHECK_OUTPUT");
        if(!string.IsNullOrEmpty(output))File.WriteAllText(output,result);
        Debug.Log(result);
    }
}
