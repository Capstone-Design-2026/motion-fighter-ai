using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Native render-preparation lifecycle only. Empty unsaved scene, no webcam or game sockets.</summary>
[InitializeOnLoad]
public static class RedboolWarmupLifecycleCheck
{
    const string Active="RedboolWarmupLifecycleCheck.Active",SaveKey="VisionStriker.Run";
    static readonly List<string> checks=new List<string>();
    static readonly List<GameObject> owned=new List<GameObject>();
    static IEnumerator flow; static int frame=-1;
    static Camera camera; static RenderTexture previous,replacement;
    static string Output=>Environment.GetEnvironmentVariable("REDBOOL_WARMUP_LIFECYCLE_CHECK_OUTPUT");
    static RedboolWarmupLifecycleCheck(){if(SessionState.GetBool(Active,false))Subscribe();}
    public static void Run()
    {
        if(!Application.isBatchMode||string.IsNullOrEmpty(Output))throw new Exception("Isolated batch output required");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Active,true);SessionState.SetFloat(Active+".Start",(float)EditorApplication.timeSinceStartup);
        Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Active+".Start",0)>180){Finish("Timeout");return;}
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish(null);}catch(Exception e){Finish(e.ToString());}
    }
    static void Check(bool ok,string text)
    {
        checks.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);
        if(!ok)throw new Exception(text);
    }
    static object Read(object o,string field)=>o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
    static GameObject Own(string name){var go=new GameObject(name);owned.Add(go);return go;}
    static RedboolRenderWarmup Make(out RedboolBattlePreparation gate)
    {
        gate=Own("Warmup preparation fixture").AddComponent<RedboolBattlePreparation>();gate.enabled=false;typeof(RedboolBattlePreparation).GetProperty("EffectsPreparing").SetValue(gate,true);
        var warm=Own("Warmup lifecycle fixture").AddComponent<RedboolRenderWarmup>();warm.preparation=gate;return warm;
    }
    static IEnumerator UntilTarget(RedboolRenderWarmup warm)
    {
        double end=Time.realtimeSinceStartupAsDouble+20;
        while(warm!=null&&Read(warm,"target")==null)
        {
            if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Offscreen target never opened");
            yield return null;
        }
        Check(warm!=null&&Read(warm,"target")!=null,"Actual Start coroutine opened its temporary render target");
    }
    static IEnumerator Tests()
    {
        yield return null;
        Application.runInBackground=true;
        camera=Own("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
        previous=new RenderTexture(16,16,0);previous.Create();replacement=new RenderTexture(16,16,0);replacement.Create();
        camera.targetTexture=previous;
        var warm=Make(out var gate);warm.enabled=false;
        Check(!gate.EffectsPreparing,"Disabling before Start immediately clears the story preparation flag");
        yield return null;yield return null;
        Check(warm==null,"Disabled before first yield cannot leave a non-null PvP warmup gate");
        Check(camera.targetTexture==previous,"Aborting before render setup leaves the camera target intact");

        warm=Make(out gate);
        for(var wait=UntilTarget(warm);wait.MoveNext();)yield return null;
        Check(camera.targetTexture!=(RenderTexture)previous,"Warmup owns a distinct temporary render target");
        warm.enabled=false;
        Check(!gate.EffectsPreparing&&camera.targetTexture==previous,"Component disable restores camera target and preparation immediately");
        camera.targetTexture=replacement;Call(warm,"Restore");
        Check(camera.targetTexture==replacement,"Repeated cleanup cannot overwrite a newer camera target owner");
        camera.targetTexture=previous;
        yield return null;yield return null;
        Check(warm==null,"Component disabled during offscreen frames is destroyed, unblocking the existing null gate");

        warm=Make(out gate);
        for(var wait=UntilTarget(warm);wait.MoveNext();)yield return null;
        warm.gameObject.SetActive(false);
        Check(!gate.EffectsPreparing&&camera.targetTexture==previous,"GameObject disable restores resources even though Unity stops its coroutine");
        yield return null;yield return null;
        Check(warm==null,"Inactive GameObject retains no abandoned warmup component");

        warm=Make(out gate);
        double end=Time.realtimeSinceStartupAsDouble+20;int start=Time.frameCount;
        while(warm!=null)
        {
            if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Healthy render preparation did not finish");
            yield return null;
        }
        Check(Time.frameCount-start>=6,"Healthy preparation still retains its offscreen and restored-backbuffer frames");
        Check(!gate.EffectsPreparing&&camera.targetTexture==previous,"Healthy completion restores its original target and clears preparation");
        foreach(var go in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            Check(go.name!="Effect warmup sample","No active temporary effect sample survives completion");

        camera.targetTexture=null;Object.Destroy(camera.gameObject);camera=null;yield return null;
        warm=Make(out gate);yield return null;yield return null;yield return null;
        Check(warm==null&&!gate.EffectsPreparing,"No-camera startup also completes without a permanent warmup gate");
    }
    static void Finish(string error)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;
        if(camera!=null)camera.targetTexture=null;
        foreach(var go in owned)if(go!=null)Object.Destroy(go);owned.Clear();
        if(previous!=null){previous.Release();Object.Destroy(previous);}
        if(replacement!=null){replacement.Release();Object.Destroy(replacement);}
        bool had=SessionState.GetBool(Active+".HadSave",false);string saved=SessionState.GetString(Active+".Save","");
        if(had)PlayerPrefs.SetString(SaveKey,saved);else PlayerPrefs.DeleteKey(SaveKey);PlayerPrefs.Save();
        if(PlayerPrefs.HasKey(SaveKey)!=had||(had&&PlayerPrefs.GetString(SaveKey)!=saved))error=(error??"")+" Save restoration failed";
        File.WriteAllText(Path.Combine(Output,"result.txt"),error==null?"PASS "+checks.Count:error);
        EditorApplication.Exit(error==null?0:1);
    }
}
