using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug=UnityEngine.Debug;

/// <summary>Explicit development-only packaged lifecycle fixture. Synthetic camera only.</summary>
public sealed class RedboolCameraNativeCheck : MonoBehaviour
{
    [Serializable] sealed class Report {public bool passed;public string error;public List<string> checks;public List<int> children;}
    readonly List<string> checks=new List<string>();
    readonly List<int> children=new List<int>();
    string directory;bool finished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"--redbool-camera-check")<0)return;
        if(!Debug.isDebugBuild||Application.isEditor||Application.isBatchMode||Array.IndexOf(args,"--redbool-camera-synthetic")<0)
        {Debug.LogError("Camera check requires a non-batch development player and --redbool-camera-synthetic; no camera started.");Application.Quit(2);return;}
        var root=new GameObject("REDBOOL Synthetic Camera Check");DontDestroyOnLoad(root);root.AddComponent<RedboolCameraNativeCheck>();
    }
    IEnumerator Start()
    {
        directory=Environment.GetEnvironmentVariable("REDBOOL_CAMERA_CHECK_OUTPUT");
        if(string.IsNullOrEmpty(directory))directory=Path.Combine(Application.persistentDataPath,"SyntheticCameraCheck");
        Directory.CreateDirectory(directory);
        var flow=Flow();
        while(true)
        {
            bool more=false;Exception failure=null;
            try {more=flow.MoveNext();}catch(Exception error){failure=error;}
            if(failure!=null){Finish(false,failure.ToString());yield break;}
            if(!more){Finish(true,null);yield break;}
            yield return flow.Current;
        }
    }
    void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);Debug.Log("[CAMERA CHECK] "+label);}
    static void Deadline(double end,string label) {if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Timed out: "+label);}
    static Button ButtonNamed(string name)
    {
        foreach(var b in FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(b.name==name)return b;
        return null;
    }
    static bool Alive(int id)
    {
        try {using(var p=Process.GetProcessById(id))return !p.HasExited;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    IEnumerator Flow()
    {
        double end=Time.realtimeSinceStartupAsDouble+30;
        while(ButtonNamed("PvpButton")==null||RedboolCameraRuntime.Instance==null)
        {Deadline(end,"menu and integrated runtime");yield return null;}
        var runtime=RedboolCameraRuntime.Instance;
        Check(runtime.Synthetic&&RedboolCameraRuntime.Packaged,"Bundled runtime explicitly synthetic");
        Check(!runtime.Running,"Menu does not open camera child");
        var menu=FindAnyObjectByType<StartSceneUI>();var lobby=menu.transform.Find("StartCanvas/TwoScreenMenu");
        Check(lobby!=null&&!lobby.gameObject.activeSelf,"Lobby hidden by default");
        ButtonNamed("PvpButton").onClick.Invoke();yield return null;
        Check(lobby.gameObject.activeSelf&&!RedboolPvp.Active,"PvP menu opens without pairing");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"01_pvp_lobby.png"));
        yield return new WaitForSecondsRealtime(.3f);
        ButtonNamed("Host").onClick.Invoke();
        end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Fresh||runtime.Status==null||runtime.Status.phase!="test"||!runtime.PreviewFresh)
        {Deadline(end,"host synthetic status and preview");yield return null;}
        Check(RedboolPvp.Active&&!RedboolPvp.IsClient&&RedboolPvp.RequireView,"HostDual starts same-arena two-screen mode");
        Check(runtime.Status.synthetic&&!runtime.Status.camera_connected,"Synthetic backend reports physical camera closed");
        int first=runtime.ChildId;children.Add(first);Check(first>0,"Host owns one child PID");
        Check((string)typeof(RedboolCameraRuntime).GetField("role",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)=="P1","Host camera uses P1 role");
        long sequence=runtime.Status.preview_sequence;
        int beforeControls=runtime.Status.control_count;
        runtime.Calibrate();end=Time.realtimeSinceStartupAsDouble+15;
        while(runtime.Status==null||runtime.Status.last_command!="calibrate"||runtime.Status.control_count<=beforeControls)
        {Deadline(end,"calibration control round trip");yield return null;}
        Check(runtime.Status.last_command=="calibrate"&&runtime.Status.control_count==beforeControls+1,"Local calibration command received once");
        end=Time.realtimeSinceStartupAsDouble+10;
        while(runtime.Status.preview_sequence<=sequence)
        {Deadline(end,"continuing local preview");yield return null;}
        Check(runtime.PreviewFresh,"Preview continues after calibration request");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"02_pvp_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        RedboolPvp.Instance.Leave(true);end=Time.realtimeSinceStartupAsDouble+15;
        while(SceneManager.GetActiveScene().name!=SceneRoutes.Start||runtime.Running||Alive(first)||ButtonNamed("DuelTestButton")==null)
        {Deadline(end,"return to menu stops owned child");yield return null;}
        Check(!runtime.Running&&!Alive(first),"Returning to menu stops old child PID");
        ButtonNamed("DuelTestButton").onClick.Invoke();end=Time.realtimeSinceStartupAsDouble+60;
        while(!runtime.Running||runtime.ChildId==first||!runtime.Fresh||runtime.Status==null||runtime.Status.phase!="test"||!runtime.PreviewFresh)
        {Deadline(end,"story duel synthetic status and preview");yield return null;}
        int second=runtime.ChildId;children.Add(second);
        Check(second>0&&second!=first&&!RedboolPvp.Active,"Story duel starts independent owned child");
        Check((string)typeof(RedboolCameraRuntime).GetField("role",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)=="DEV01","Story camera uses DEV01 role");
        Check(runtime.Synthetic&&runtime.Status.synthetic&&!runtime.Status.camera_connected,"Story path remains synthetic without physical camera");
        end=Time.realtimeSinceStartupAsDouble+10;
        while(!runtime.PreviewFresh) {Deadline(end,"first story preview JPEG");yield return null;}
        Check(runtime.PreviewFresh,"Story preview arrives from the new child");
        // A Windows child must not retain a scene receiver's socket. Keep the
        // same DEV01 child alive while the old scene closes and reopens 5065.
        var beforeReceiver=FindAnyObjectByType<RedboolDemoReceiver>();
        Check(beforeReceiver!=null&&beforeReceiver.isActiveAndEnabled,"Story input receiver opens before scene transition");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        yield return null;
        end=Time.realtimeSinceStartupAsDouble+15;
        while(FindAnyObjectByType<RedboolBattlePreparation>()==null||!runtime.PreviewFresh)
        {Deadline(end,"scene transition with persistent camera child");yield return null;}
        var afterReceiver=FindAnyObjectByType<RedboolDemoReceiver>();
        Check(afterReceiver!=null&&afterReceiver!=beforeReceiver&&afterReceiver.isActiveAndEnabled&&
            !afterReceiver.Status.StartsWith("SOCKET ERROR"),"New scene reopens input port while camera child stays alive");
        Check(runtime.ChildId==second&&Alive(second),"Scene change retains the same camera process");
        yield return new WaitForSecondsRealtime(.2f);
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"03_story_camera.png"));
        yield return new WaitForSecondsRealtime(.3f);
        var stop=(IEnumerator)typeof(RedboolCameraRuntime).GetMethod("StopOwned",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(runtime,null);
        runtime.StartCoroutine(stop);
        Check(runtime.ChildId==0&&typeof(RedboolCameraRuntime).GetField("stoppingChild",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime)!=null,"Shutdown retains detached child while coroutine yields");
        Destroy(runtime.gameObject);yield return null;
        end=Time.realtimeSinceStartupAsDouble+10;
        while(Alive(second)) {Deadline(end,"destroyed runtime stops detached child");yield return null;}
        Check(runtime==null&&!Alive(second),"Destroying runtime during shutdown stops child while game stays alive");
    }
    void Finish(bool passed,string error)
    {
        if(finished)return;finished=true;
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonUtility.ToJson(new Report {passed=passed,error=error,checks=checks,children=children},true));
        Debug.Log("[CAMERA CHECK] "+(passed?"PASS":"FAIL "+error));
        Application.Quit(passed?0:1);
    }
}
