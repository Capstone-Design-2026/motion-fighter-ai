using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class RedboolTutorialSetup {
    const string Marker="// REDBOOL raised-kicks-v3";
    static string Digest(string text) {
        using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.Replace("\r\n","\n")))).Replace("-","").ToLowerInvariant();
    }
    [MenuItem("Tools/REDBOOL/Connect Cave Tutorial")]
    private static void Connect() {
        if(EditorApplication.isPlaying) { Debug.LogWarning("Stop Play Mode first.");return; }
        string[] names={"Scripts/Meta/CaveTutorial.cs","Scripts/UI/TrainingPoseGraphic.cs"};
        string[] texts=new string[2];
        for(int i=0;i<2;i++) {
            string path=Path.Combine(Application.dataPath,names[i]);
            if(!File.Exists(path)) { Debug.LogError("Missing "+names[i]+". No files changed.");return; }
            texts[i]=File.ReadAllText(path);
            if(texts[i].StartsWith(Marker,StringComparison.Ordinal)) continue;
            string hash=Digest(texts[i]);
            bool known=i==0 ? hash=="caccd5c542ef37050783afa7dfdae1b52e76b780b9fa222b746f22c170278e58" || hash=="790c5dae74dfb41d650d95f4931f25068e0892882d26fb41b9c4bf6801bf4cbe" : hash=="2a074c8d649101e06397eac3132964d2032f2ef9bc4fdf629c0157d04a483d47";
            if(!known) { Debug.LogError(names[i]+" differs from reviewed source. No files changed; review the newer team version first.");return; }
        }
        for(int i=0;i<2;i++) {
            if(texts[i].StartsWith(Marker,StringComparison.Ordinal)) continue;
            string path=Path.Combine(Application.dataPath,names[i]);
            string backup=Path.Combine(Application.dataPath,"../Library/RedboolVisionBackup/v3/"+Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if(!File.Exists(backup)) File.Copy(path,backup);
            File.WriteAllText(path,Marker+"\n"+(i==0?PatchCave(texts[i]):PatchGraphic(texts[i])));
        }
        AssetDatabase.Refresh();
        Debug.Log("Tutorial text and pose demonstration now use RIGHT_RAISE for LOW_KICK. Verify all seven tutorial steps in Play Mode.");
    }
    static string PatchCave(string text) {
        if(!text.Contains("RedboolDemoReceiver.TutorialAction")) {
        text=text.Replace("UdpActionReceiver.TutorialAction","RedboolDemoReceiver.TutorialAction");
        text=text.Replace("FindAnyObjectByType<UdpActionReceiver>()","FindAnyObjectByType<RedboolDemoReceiver>()");
        text=text.Replace("AddComponent<UdpActionReceiver>()","AddComponent<RedboolDemoReceiver>()");
            text=text.Replace("if(keyboard||exit)return;lastPacket=Time.unscaledTime;","if(keyboard||exit)return;lastPacket=Time.unscaledTime;if(action==\"UNAVAILABLE\"){armed=false;return;}");
        }
        text=text.Replace("왼손을 아래로 내리세요","오른손을 위로 들어 올리세요");
        text=text.Replace("왼손을 허리 쪽으로 내려보세요.","오른손을 어깨보다 높이 들어보세요.");
        text=text.Replace("case \"LEFT_HAND_DOWN\":","case \"RIGHT_RAISE\":case \"RIGHT_HAND_UP\":");
        text=text.Replace("case \"LEFT_HAND_UP\":","case \"LEFT_RAISE\":case \"LEFT_HAND_UP\":");
        return text;
    }
    static string PatchGraphic(string text) {
        text=text.Replace("if(step==3){le=Vector2.Lerp(le,new Vector2(.69f,.49f),t);lh=Vector2.Lerp(lh,new Vector2(.69f,.31f),t);}","if(step==3){re=Vector2.Lerp(re,new Vector2(.28f,.84f),t);rh=Vector2.Lerp(rh,new Vector2(.31f,.98f),t);}");
        text=text.Replace(":step!=1;",":(step!=1&&step!=3);");
        text=text.Replace("(step==1||step==4)","(step==1||step==3||step==4)");
        return text;
    }
}
