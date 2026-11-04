using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Explicit batch review. Uses an isolated run and restores the previous save on completion.</summary>
[InitializeOnLoad]
public static class StoryStagesReview
{
    const string Active="StoryStagesReview.Active",SaveKey="VisionStriker.Run";
    static string Out=>StoryStagesBuilder.Output+"/review";
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator flow;static int frame=-1;
    static PlayerController player;static EnemyAI enemy;static Camera camera;static Battle3DCamera rig;static CombatHUD hud;static Canvas canvas;
    static StoryStagesReview(){if(SessionState.GetBool(Active,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
        File.WriteAllText(Out+"/save_backup.json",PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.OpenScene("Assets/Scenes/StoryStages/"+StoryStagesBuilder.Scenes[0]+".unity");
        SessionState.SetBool(Active,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Log(string m,string s,LogType t){if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
    static void Tick()
    {
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish();}
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Check(bool ok,string label){checks.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(Out+"/checks.txt",checks);}
    static void Finish()
    {
        if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();Time.captureFramerate=0;Time.timeScale=1;
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Original run save restored");
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllLines(Out+"/runtime_errors.txt",errors);
        bool ok=errors.Count==0&&checks.All(s=>s.StartsWith("PASS"));File.WriteAllText(Out+"/done.txt",ok?"PASS":"FAIL");EditorApplication.Exit(ok?0:1);
    }
    static IEnumerator Wait(float s){float end=Time.realtimeSinceStartup+s;while(Time.realtimeSinceStartup<end)yield return null;}
    static void TestRun(string id)
    {
        var run=RunState.Instance;
        var data=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,pendingNodeId=id,currentNodeId="cave",tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};
        data.map=MapGenerator.Generate(run.Config.map,run.Config,data.seed);
        typeof(RunState).GetProperty("Data").SetValue(run,data);
    }
    static void Find()
    {
        player=Object.FindAnyObjectByType<PlayerController>();enemy=Object.FindAnyObjectByType<EnemyAI>();player.enabled=false;enemy.enabled=false;
        foreach(var f in Object.FindObjectsByType<Fighter>()){f.CancelAttack();f.InputLocked=false;f.IsBlocking=false;}
        player.GetComponent<Fighter>().Facing=1;enemy.GetComponent<Fighter>().Facing=-1;
        camera=Camera.main;rig=camera.GetComponent<Battle3DCamera>();rig.allowMouseOrbit=false;
        hud=Object.FindAnyObjectByType<CombatHUD>();canvas=hud.GetComponentInChildren<Canvas>();canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
    }
    static void Position(float px,float ex)
    {
        foreach(var a in new[]{(player.GetComponent<Rigidbody2D>(),px),(enemy.GetComponent<Rigidbody2D>(),ex)})
        {a.Item1.interpolation=RigidbodyInterpolation2D.None;a.Item1.position=new Vector2(a.Item2,a.Item1.position.y);a.Item1.transform.position=new Vector3(a.Item2,a.Item1.position.y,0);a.Item1.linearVelocity=Vector2.zero;}
        Physics2D.SyncTransforms();
    }
    static IEnumerator Tests()
    {
        yield return null;yield return null;
        bool quick=Environment.GetCommandLineArgs().Contains("-stageQuick");
        for(int i=0;i<StoryStagesBuilder.Ids.Length;i++)
        {
            string id=StoryStagesBuilder.Ids[i],scene=StoryStagesBuilder.Scenes[i];
            if(Environment.GetCommandLineArgs().Contains("-stageWaterOnly")&&!new[]{"river","old_ferry","final"}.Contains(id))continue;
            if(Environment.GetCommandLineArgs().Contains("-stageDressingOnly")&&!new[]{"gate","quarry","camp","rescue","entrance","final"}.Contains(id))continue;
            TestRun(id);Check(SceneRoutes.PendingBattleScene==scene,id+": story node resolves correct scene");SceneRoutes.LoadBattle();
            for(var w=Wait(.8f);w.MoveNext();)yield return null;Find();
            Check(SceneManager.GetActiveScene().name==scene,id+": actual story route loaded scene");
            Check(!hud.AllowRestart,id+": story restart stays disabled");
            var view=enemy.GetComponent<Fighter3DView>();
            Check(view.modelAnimator&&view.modelAnimator.avatar.isHuman&&view.visualRoot.name.Contains("Scout"),id+": placeholder scout and humanoid animator active");
            var stage=RunState.Instance.PendingStage;
            Check(enemy.GetComponent<Health>().MaxHealth==stage.enemyMaxHealth,id+": original stage health preserved");
            var env=GameObject.Find("Story Stage · "+id);
            Check(env&&env.GetComponentsInChildren<Collider2D>(true).Length==0,id+": scenery adds no combat colliders");
            Check(RenderSettings.skybox.GetFloat("_CloudSpeed")>0,id+": animated cloud sky");
            Check(env.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).All(r=>r.sharedMaterials.All(m=>m&&!ShaderUtil.ShaderHasError(m.shader))),id+": no missing materials or shader errors");
            Position(-2.1f,2.1f);rig.SetOrbit(0,8,true);
            for(var w=Wait(.6f);w.MoveNext();)yield return null;
            for(var c=Capture(id+"_front",1600,900);c.MoveNext();)yield return null;
            rig.SetOrbit(180,8,true);for(var c=Capture(id+"_back",1600,900);c.MoveNext();)yield return null;
            rig.enabled=false;camera.transform.position=id=="final"?new Vector3(0,17,-34):new Vector3(0,15,-30);camera.transform.LookAt(id=="final"?new Vector3(0,9,24):new Vector3(0,4,18));
            canvas.gameObject.SetActive(false);for(var c=Capture(id+"_overview",1600,900);c.MoveNext();)yield return null;canvas.gameObject.SetActive(true);rig.enabled=true;
            if(new[]{"gate","rescue","entrance"}.Contains(id))
            {
                rig.enabled=false;canvas.gameObject.SetActive(false);
                float gz=id=="gate"?22:id=="rescue"?27:26;
                camera.transform.position=new Vector3(9,5,gz-18);camera.transform.LookAt(new Vector3(0,3.5f,gz));
                for(var c=Capture(id+"_gate_detail",1600,900);c.MoveNext();)yield return null;
                camera.transform.position=new Vector3(-10,6,gz+15);camera.transform.LookAt(new Vector3(0,3.5f,gz));
                for(var c=Capture(id+"_gate_reverse",1600,900);c.MoveNext();)yield return null;
                canvas.gameObject.SetActive(true);rig.enabled=true;
            }
            if(new[]{"river","old_ferry","final"}.Contains(id))
            {
                rig.enabled=false;canvas.gameObject.SetActive(false);
                camera.transform.position=id=="final"?new Vector3(0,.8f,20):new Vector3(-3,2.1f,7);
                camera.transform.LookAt(id=="final"?new Vector3(0,-.8f,34):new Vector3(-5,0,24));
                for(var c=Capture(id+"_water",1600,900);c.MoveNext();)yield return null;
                if(Environment.GetCommandLineArgs().Contains("-stageWaterMovies"))
                    for(var c=Movie(id+"_water",false);c.MoveNext();)yield return null;
                canvas.gameObject.SetActive(true);rig.enabled=true;
            }
            if(quick)
            {
            bool quickFramed=true,quickClear=true;
            foreach(float aspect in new[]{16f/9,4f/3,21f/9})foreach(float center in new[]{0f,-3f,3f})foreach(float angle in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
            {
                Position(center-2.1f,center+2.1f);camera.aspect=aspect;rig.SetOrbit(angle,8,true);yield return null;
                foreach(var a in new[]{player.transform,enemy.transform})foreach(float h in new[]{.05f,2.05f})
                {var v=camera.WorldToViewportPoint(a.position+Vector3.up*h);quickFramed&=v.z>.1f&&v.x>.015f&&v.x<.985f&&v.y>.015f&&v.y<.94f;}
                quickClear&=!Physics.CheckSphere(camera.transform.position,.2f,1<<2,QueryTriggerInteraction.Collide);
            }
            Check(quickFramed,id+": both fighters fit 72 camera / aspect / position combinations");Check(quickClear,id+": camera quickClears scenery in 72 combinations");

                Position(-2.1f,2.1f);camera.aspect=16f/9;
                if(Environment.GetCommandLineArgs().Contains("-stageCaptureMovie"))
                {rig.SetOrbit(0,8,true);for(var c=Movie(id);c.MoveNext();)yield return null;}
                continue;
            }
            bool framed=true,clear=true;
            foreach(float aspect in new[]{16f/9,4f/3,21f/9})foreach(float center in new[]{0f,-3f,3f})foreach(float angle in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
            {
                Position(center-2.1f,center+2.1f);camera.aspect=aspect;rig.SetOrbit(angle,8,true);yield return null;
                foreach(var a in new[]{player.transform,enemy.transform})foreach(float h in new[]{.05f,2.05f})
                {var v=camera.WorldToViewportPoint(a.position+Vector3.up*h);framed&=v.z>.1f&&v.x>.015f&&v.x<.985f&&v.y>.015f&&v.y<.94f;}
                clear&=!Physics.CheckSphere(camera.transform.position,.2f,1<<2,QueryTriggerInteraction.Collide);
            }
            Check(framed,id+": both fighters fit 72 camera / aspect / position combinations");Check(clear,id+": camera clears scenery in 72 combinations");
            Position(-5.3f,3);float end=Time.realtimeSinceStartup+.7f;while(Time.realtimeSinceStartup<end){player.GetComponent<Rigidbody2D>().linearVelocity=new Vector2(-4,0);yield return null;}
            Check(player.transform.position.x>-6.5f,id+": left arena boundary stops outward movement");
            Position(-3,5.3f);end=Time.realtimeSinceStartup+.7f;while(Time.realtimeSinceStartup<end){enemy.GetComponent<Rigidbody2D>().linearVelocity=new Vector2(4,0);yield return null;}
            Check(enemy.transform.position.x<6.5f,id+": right arena boundary stops outward movement");
            Position(-.72f,.72f);camera.aspect=16f/9;rig.SetOrbit(0,8,true);
            for(var w=Wait(.5f);w.MoveNext();)yield return null;
            int hp=enemy.GetComponent<Health>().CurrentHealth;
            Check(player.GetComponent<Fighter>().TryAttack(CombatMove.Straight),id+": common straight starts");
            for(var w=Wait(1.5f);w.MoveNext();)yield return null;
            Check(enemy.GetComponent<Health>().CurrentHealth<hp,id+": actual hit damages enemy on new floor");
            Position(-2.1f,2.1f);rig.SetOrbit(0,8,true);
            for(var c=Movie(id);c.MoveNext();)yield return null;
            enemy.GetComponent<Health>().TakeDamage(100000,Vector2.zero);
            if(id!="final")
            {
                for(var w=Wait(4.6f);w.MoveNext();)yield return null;
                Check(SceneManager.GetActiveScene().name==SceneRoutes.Map&&RunState.Instance.Data.clearedNodeIds.Contains(id),id+": victory clears its node and returns to map");
            }
            else
            {
                float until=Time.realtimeSinceStartup+20;
                while(SceneManager.GetActiveScene().name!=SceneRoutes.Ending&&Time.realtimeSinceStartup<until)
                {
                    foreach(var cut in Object.FindObjectsByType<StoryCutscene>())typeof(StoryCutscene).GetField("next",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(cut,true);
                    yield return null;
                }
                Check(SceneManager.GetActiveScene().name==SceneRoutes.Ending,"Final victory plays after-story and reaches ending");
            }
        }
        if(!quick)
        {
            TestRun("rescue");SceneRoutes.LoadBattle();for(var w=Wait(.8f);w.MoveNext();)yield return null;Find();
            player.GetComponent<Health>().TakeDamage(100000,Vector2.zero);
            for(var w=Wait(4.6f);w.MoveNext();)yield return null;
            Check(SceneManager.GetActiveScene().name==SceneRoutes.Start&&!RunState.Instance.HasRun,"Defeat resets run and returns to start");
        }
        Check(errors.Count==0,"No runtime exceptions across all ten stages");
    }
    static IEnumerator Capture(string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,24);rt.Create();camera.targetTexture=rt;camera.aspect=(float)width/height;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        yield return null;yield return null;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Out+"/"+name+".jpg",tex.EncodeToJPG(93));
        RenderTexture.active=null;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;Object.Destroy(tex);rt.Release();Object.Destroy(rt);
    }
    static IEnumerator Movie(string id,bool orbit=true)
    {
        var dir=Out+"/frames/"+id;Directory.CreateDirectory(dir);
        const int width=1280,height=720;var rt=new RenderTexture(width,height,24);rt.Create();camera.targetTexture=rt;camera.aspect=(float)width/height;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);Time.captureFramerate=24;
        for(int i=0;i<(orbit?120:60);i++)
        {
            if(orbit) { if(i<48)rig.SetOrbit(Mathf.Sin(i/48f*Mathf.PI)*14,8,true);else rig.SetOrbit((i-48)/72f*360,8,true); }
            yield return null;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
            File.WriteAllBytes(dir+"/"+i.ToString("D4")+".jpg",tex.EncodeToJPG(89));RenderTexture.active=null;
        }
        Time.captureFramerate=0;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;Object.Destroy(tex);rt.Release();Object.Destroy(rt);
    }
}
