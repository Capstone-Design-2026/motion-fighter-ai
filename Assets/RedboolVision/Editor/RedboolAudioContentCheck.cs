using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Read-only reference/decode metadata checks for the late v34 music update.
// No Play mode, audio playback, webcam, room connection or asset saves.
public static class RedboolAudioContentCheck
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string reason) { if(!value)throw new Exception(reason);checks.Add("PASS "+reason); }
    static AudioClip Clip(string name)
    {
        string path="Assets/Sound/BGM/"+name+".mp3";
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        Check(clip!=null && clip.samples>0 && clip.channels>0 && clip.frequency>0 && clip.length>1f,
            name+" imports with valid audio sample metadata");
        Check(clip.loadState!=AudioDataLoadState.Failed,name+" audio data is not failed");
        return clip;
    }
    static AudioClip[] Playlist(BattleBgm bgm)
    {
        var clips=new SerializedObject(bgm).FindProperty("clips");
        return Enumerable.Range(0,clips.arraySize).Select(i=>(AudioClip)clips.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
    }
    public static void Run()
    {
        if(!Application.isBatchMode)throw new Exception("Use isolated batch verification");
        checks.Clear();
        var fight1=Clip("BGM_fight_newVersion1");var fight2=Clip("BGM_fight_newVersion2");
        var pvp=Clip("BGM_PVP_120BPM");var boss=Clip("BGM_BossFight_newVersion");var ending=Clip("BGM_Ending_newVersion");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BattleBgm.prefab");
        Check(prefab!=null&&prefab.GetComponent<BattleBgm>()!=null,"Shared battle music prefab remains valid");
        var playlist=Playlist(prefab.GetComponent<BattleBgm>());
        Check(playlist.Length==4 && playlist.All(c=>c!=null) && playlist.Contains(fight1) && playlist.Contains(fight2),"Shared battle playlist contains four valid clips including both approved new tracks");
        foreach(string removed in new[]{"90ae9bc7508466a4a9cd075a6aeea4e3","45998c2c42b1b2f4d8a8381be381639e"})
            Check(!playlist.Any(c=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c))==removed),"Removed celt track is absent from battle playlist "+removed);
        Check(Mathf.Abs(new SerializedObject(prefab.GetComponent<BattleBgm>()).FindProperty("volume").floatValue-.4f)<.001f,"Battle music volume remains0.4");
        var config=Resources.Load<RunConfig>("RunConfig");
        Check(config!=null&&config.bossBattleMusic==boss&&config.endingMusic==ending,"Boss and ending reference the approved replacement tracks");
        Check(config.heroMovement==HeroMovementStyle.CommonWalk&&Mathf.Abs(config.heroWalkSpeed-2.61f)<.001f,"Validated camera-player movement selection and speed are preserved");
        var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).ToArray();
        var names=new HashSet<string>(scenes.Select(s=>Path.GetFileNameWithoutExtension(s.path)));
        Check(RedboolPvp.ArenaScene=="UndergroundPit"&&RedboolDuelTest.ArenaScene=="ScoutPass"&&names.Contains(RedboolPvp.ArenaScene)&&names.Contains(RedboolDuelTest.ArenaScene),"PvP and solo routes remain correct and built");
        Check(config.nodeBattleScenes.All(s=>names.Contains(s.sceneName)),"All configured story music routes remain built");
        EditorSceneManager.OpenScene("Assets/Scenes/PvpStages/UndergroundPit.unity");
        var bgms=Object.FindObjectsByType<BattleBgm>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Check(bgms.Length==1,"PvP scene has exactly one battle music source");
        var arenaPlaylist=Playlist(bgms[0]);
        Check(arenaPlaylist.Length==1&&arenaPlaylist[0]==pvp,"UndergroundPit uses only the dedicated PvP track");
        var fighters=Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Check(fighters.Length==2,"Authored arena still has two fighter roots before PvP replacement");
        foreach(var fighter in fighters)
        {
            var data=new SerializedObject(fighter);
            Check(data.FindProperty("guardedPunchDamage").intValue==2&&data.FindProperty("guardedKickDamage").intValue==5,"Serialized chip damage matches current rules for "+fighter.name);
        }
        int extraLights=Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Count(c=>c!=null&&c.GetType().FullName=="UnityEngine.Rendering.Universal.UniversalAdditionalLightData");
        Check(extraLights>=14,"Authored URP light metadata is present");
        string output=Environment.GetEnvironmentVariable("REDBOOL_AUDIO_CHECK_OUTPUT");
        string report="PASS "+checks.Count+" audio/content checks\n"+string.Join("\n",checks)+"\nScope: imported references and sample metadata only; no playback or measured audio/graphics performance.\n";
        if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,report);}
        Debug.Log(report);
    }
}
