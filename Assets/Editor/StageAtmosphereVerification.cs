using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Explicit playmode checks/captures of saved scenes, with isolated progression.</summary>
[InitializeOnLoad]
public static class StageAtmosphereVerification
{
    const string Active="StageAtmosphereVerification.Active", SaveKey="VisionStriker.Run";
    static readonly string Out=StageAtmosphereInstaller.Output;
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator flow;static int frame=-1;
    static Camera cam;static Battle3DCamera rig;static SelectiveSceneryAtmosphere effect;
    static PlayerController player;static EnemyAI enemy;static Canvas canvas;
    static StageAtmosphereVerification(){if(SessionState.GetBool(Active,false))Subscribe();}
    public static void Run()
    {
        foreach(string d in new[]{"stills","validation","frames"})Directory.CreateDirectory(Out+"/"+d);
        SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
        File.WriteAllText(Out+"/save_before.json",PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.OpenScene(StageAtmosphereInstaller.ScenePath(0));
        SessionState.SetBool(Active,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;}
    static void Log(string m,string s,LogType t){if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
    static void Tick()
    {
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish();}catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Check(bool ok,string label){checks.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(Out+"/checks.txt",checks);}
    static void Finish()
    {
        if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();Time.timeScale=1;Time.captureFramerate=0;
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Original player save restored");
        File.WriteAllText(Out+"/save_after.json",PlayerPrefs.GetString(SaveKey,""));
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllLines(Out+"/errors.txt",errors);bool ok=errors.Count==0&&checks.All(s=>s.StartsWith("PASS"));
        File.WriteAllText(Out+"/done.txt",ok?"PASS":"FAIL");EditorApplication.Exit(ok?0:1);
    }
    static IEnumerator Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null;}
    static void IsolateRun(string id)
    {
        var run=RunState.Instance;var test=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,pendingNodeId=id,currentNodeId="cave",tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};
        test.map=MapGenerator.Generate(run.Config.map,run.Config,test.seed);typeof(RunState).GetProperty("Data").SetValue(run,test);
    }
    static void Setup(string id)
    {
        player=Object.FindAnyObjectByType<PlayerController>();enemy=Object.FindAnyObjectByType<EnemyAI>();player.enabled=false;enemy.enabled=false;
        foreach(var f in Object.FindObjectsByType<Fighter>()){f.CancelAttack();f.IsBlocking=false;f.InputLocked=false;}
        player.GetComponent<Fighter>().Facing=1;enemy.GetComponent<Fighter>().Facing=-1;
        foreach(var tuple in new[]{(player.GetComponent<Rigidbody2D>(),-2.1f),(enemy.GetComponent<Rigidbody2D>(),2.1f)})
        {var rb=tuple.Item1;rb.interpolation=RigidbodyInterpolation2D.None;rb.position=new Vector2(tuple.Item2,rb.position.y);rb.transform.position=new Vector3(tuple.Item2,rb.position.y,0);rb.linearVelocity=Vector2.zero;}
        Physics2D.SyncTransforms();
        cam=Camera.main;rig=cam.GetComponent<Battle3DCamera>();rig.allowMouseOrbit=false;effect=cam.GetComponent<SelectiveSceneryAtmosphere>();
        Check(effect&&effect.enabled,id+": saved camera treatment loaded");
        var targets=effect.surfaces.Select(s=>s.renderer).ToHashSet();
        foreach(var view in new[]{player.GetComponent<Fighter3DView>(),enemy.GetComponent<Fighter3DView>()})
            Check(view.visualRoot.GetComponentsInChildren<Renderer>(true).All(r=>!targets.Contains(r)),id+": fighter untouched");
        Check(effect.surfaces.All(s=>(s.renderer.renderingLayerMask&SelectiveSceneryAtmosphere.Layer(s.kind))!=0),id+": runtime target layers installed");
        Check(targets.All(r=>!StageAtmosphereInstaller.Hierarchy(r.transform).Contains("GateDoors")),id+": detailed doors protected");
        Check(targets.All(r=>r.sharedMaterials.All(m=>m&&!ShaderUtil.ShaderHasError(m.shader))),id+": treated materials valid");
        Check(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset").rendererFeatures.OfType<SelectiveSceneryFeature>().Any(),id+": renderer feature installed");
        canvas=Object.FindAnyObjectByType<CombatHUD>().GetComponentInChildren<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=cam.nearClipPlane+.1f;
        canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
    }
    static void Capture(string path,int width=1600,int height=900)
    {
        var rt=new RenderTexture(width,height,24);rt.Create();var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
        cam.aspect=(float)width/height;cam.targetTexture=rt;Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});
        RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});
        RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
        File.WriteAllBytes(path,path.EndsWith(".png")?tex.EncodeToPNG():tex.EncodeToJPG(92));
        RenderTexture.active=null;cam.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(tex);
    }
    static void View(string key)
    {
        canvas.gameObject.SetActive(true);Capture(Out+"/stills/"+key+".png");
        // Freeze dynamic transparent scenery only for the internal pixel test; final captures keep it.
        var hidden=Object.FindObjectsByType<Renderer>().Where(r=>r.enabled&&(r is ParticleSystemRenderer||r.sharedMaterials.Any(m=>m&&m.renderQueue>=2500))).ToArray();
        foreach(var r in hidden)r.enabled=false;canvas.gameObject.SetActive(false);
        if(effect.HasTargets)
        {
            Capture(Out+"/validation/"+key+"_on.png");
            effect.showMask=true;Capture(Out+"/validation/"+key+"_mask.png");effect.showMask=false;
            var treated=effect.surfaces.Select(s=>s.renderer.sharedMaterials).ToArray();
            for(int j=0;j<effect.surfaces.Length;j++)effect.surfaces[j].renderer.sharedMaterials=effect.surfaces[j].originalMaterials;
            effect.enabled=false;Capture(Out+"/validation/"+key+"_off.png");effect.enabled=true;
            for(int j=0;j<effect.surfaces.Length;j++)effect.surfaces[j].renderer.sharedMaterials=treated[j];
            Check(effect.surfaces.All(s=>(s.renderer.renderingLayerMask&SelectiveSceneryAtmosphere.Layer(s.kind))!=0),key+": effect disable/enable restores layers");
        }
        foreach(var r in hidden)if(r)r.enabled=true;canvas.gameObject.SetActive(true);
    }
    static IEnumerator Tests()
    {
        yield return null;yield return null;
        for(int i=0;i<StageAtmosphereInstaller.Ids.Length;i++)
        {
            string id=StageAtmosphereInstaller.Ids[i];IsolateRun(id);SceneRoutes.LoadBattle();for(var w=Wait(.7f);w.MoveNext();)yield return null;
            Check(SceneManager.GetActiveScene().name==StageAtmosphereInstaller.Scenes[i],id+": correct battle scene routed");Setup(id);
            rig.SetOrbit(0,8,true);for(var w=Wait(.25f);w.MoveNext();)yield return null;rig.enabled=false;
            View(id+"_front");
            rig.SetOrbit(35,10,true);yield return null;View(id+"_angle");
            rig.SetOrbit(180,8,true);yield return null;View(id+"_back");
            if(id=="entrance")
            {
                var env=GameObject.Find("Story Stage · entrance").transform;
                var facade=env.Find("Fortress wall - continuous entrance facade");
                Check(facade&&facade.GetComponentsInChildren<MeshFilter>().All(m=>AssetDatabase.Contains(m.sharedMesh)),"Entrance chamfered facade meshes persisted");
                Check(facade.GetComponentsInChildren<Collider2D>().Length==0,"Entrance has no new combat colliders");
            }
            if(new[]{"entrance","final"}.Contains(id)&&!Environment.GetCommandLineArgs().Contains("-stillsOnly"))
            {
                Time.captureFramerate=24;Directory.CreateDirectory(Out+"/frames/"+id);
                for(int k=0;k<120;k++)
                {rig.SetOrbit(Mathf.Sin(k/119f*Mathf.PI*2)*38,8,true);yield return null;Capture(Out+"/frames/"+id+"/"+k.ToString("D4")+".jpg",1280,720);}
                Time.captureFramerate=0;
            }
        }
        Check(errors.Count==0,"No runtime/rendering errors across eleven stages");
    }
}
