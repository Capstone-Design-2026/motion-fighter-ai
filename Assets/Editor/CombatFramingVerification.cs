using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class CombatFramingVerification
{
    const string Active="CombatFramingVerification.Active",SaveKey="VisionStriker.Run";
    static string Out=>CombatFramingInstaller.Out;
    static IEnumerator flow;static int lastFrame=-1,number;static bool recording;static string key;
    static readonly List<string> checks=new List<string>(),errors=new List<string>(),metrics=new List<string>();
    static Camera cam;static Battle3DCamera rig;static Canvas hud;static Fighter3DView[] views;static Fighter[] fighters;static Health[] health;static Rigidbody2D[] bodies;
    static RenderTexture target;static Texture2D image;static Mesh mesh;
    static CombatFramingVerification(){if(SessionState.GetBool(Active,false))Subscribe();}
    public static void Run()
    {
        foreach(string d in new[]{"stills","frames"})Directory.CreateDirectory(Out+"/"+d);
        SessionState.SetBool(Active+".HasSave",PlayerPrefs.HasKey(SaveKey));SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));File.WriteAllText(Out+"/save_before.json",PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.OpenScene(CombatFramingInstaller.ScenePath(0));SessionState.SetBool(Active,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;}
    static void Log(string m,string s,LogType t){if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
    static void Tick()
    {
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(flow==null)flow=Tests();bool next=flow.MoveNext();if(recording&&cam)Capture(Out+"/frames/"+key+"/"+(number++).ToString("D5")+".jpg");if(!next)Finish();}
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Check(bool ok,string label){checks.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(Out+"/checks.txt",checks);}
    static void Finish()
    {
        recording=false;HitImpact.ReleaseTimeControl();Time.timeScale=1;Time.captureFramerate=0;
        if(SessionState.GetBool(Active+".HasSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);PlayerPrefs.Save();
        File.WriteAllText(Out+"/save_after.json",PlayerPrefs.GetString(SaveKey,""));Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Player save restored");
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllLines(Out+"/errors.txt",errors);File.WriteAllLines(Out+"/metrics.csv",metrics);bool ok=errors.Count==0&&checks.All(c=>c.StartsWith("PASS"));File.WriteAllText(Out+"/done.txt",ok?"PASS":"FAIL");EditorApplication.Exit(ok?0:1);
    }
    static IEnumerator Wait(float t){float end=Time.time+t;while(Time.time<end)yield return null;}
    static IEnumerator RealWait(float t){float end=Time.realtimeSinceStartup+t;while(Time.realtimeSinceStartup<end)yield return null;}
    static void Isolate(string id)
    {
        var run=RunState.Instance;var data=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,currentNodeId="cave",pendingNodeId=id=="game"?"":id,tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};
        data.map=MapGenerator.Generate(run.Config.map,run.Config,data.seed);typeof(RunState).GetProperty("Data").SetValue(run,data);
    }
    static void Setup()
    {
        var p=Object.FindAnyObjectByType<PlayerController>();var e=Object.FindAnyObjectByType<EnemyAI>();p.enabled=e.enabled=false;
        foreach(var u in Object.FindObjectsByType<UdpActionReceiver>())u.enabled=false;
        foreach(var bridge in Object.FindObjectsByType<BattleSceneBridge>())Object.Destroy(bridge);
        views=new[]{p.GetComponent<Fighter3DView>(),e.GetComponent<Fighter3DView>()};fighters=views.Select(v=>v.GetComponent<Fighter>()).ToArray();health=views.Select(v=>v.GetComponent<Health>()).ToArray();bodies=views.Select(v=>v.GetComponent<Rigidbody2D>()).ToArray();
        for(int i=0;i<2;i++){fighters[i].CancelAttack();fighters[i].InputLocked=false;fighters[i].IsBlocking=fighters[i].IsCrouching=false;fighters[i].Facing=i==0?1:-1;health[i].DamageLocked=false;health[i].ResetHealth();bodies[i].linearVelocity=Vector2.zero;bodies[i].interpolation=RigidbodyInterpolation2D.None;}
        cam=Camera.main;rig=cam.GetComponent<Battle3DCamera>();rig.allowMouseOrbit=false;rig.ResetOrbit();
        if(!target){target=new RenderTexture(1280,720,24);target.Create();image=new Texture2D(1280,720,TextureFormat.RGB24,false);mesh=new Mesh();}
        cam.targetTexture=target;cam.aspect=16f/9;
        hud=Object.FindAnyObjectByType<CombatHUD>().GetComponentInChildren<Canvas>();hud.renderMode=RenderMode.ScreenSpaceCamera;hud.worldCamera=cam;hud.planeDistance=cam.nearClipPlane+.1f;hud.GetComponent<UnityEngine.UI.CanvasScaler>().enabled=false;hud.scaleFactor=1280f/1920f;hud.transform.Find("KeyGuide")?.gameObject.SetActive(false);
    }
    static void Position(float left,float right)
    {
        bodies[0].position=new Vector2(left,bodies[0].position.y);bodies[1].position=new Vector2(right,bodies[1].position.y);
        foreach(var b in bodies){b.linearVelocity=Vector2.zero;b.transform.position=new Vector3(b.position.x,b.position.y,0);}Physics2D.SyncTransforms();
    }
    static Bounds Pixels(Fighter3DView view)
    {
        Bounds result=default;bool first=true;
        foreach(var skin in view.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh);var matrix=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
            foreach(var v in mesh.vertices){var p=cam.WorldToViewportPoint(matrix.MultiplyPoint3x4(v));if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);}
        }
        return result;
    }
    static bool Visible()
    {
        return views.All(v=>{var b=Pixels(v);return b.min.z>cam.nearClipPlane&&b.min.x>=.015f&&b.max.x<=.985f&&b.min.y>=.025f&&b.max.y<=.89f;});
    }
    static float Height()=>views.Average(v=>Pixels(v).size.y);
    static void Capture(string path)
    {
        cam.targetTexture=target;Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=target});RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=target});
        var old=RenderTexture.active;RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(path,path.EndsWith("png")?image.EncodeToPNG():image.EncodeToJPG(90));RenderTexture.active=old;
    }
    // Exact former framing equations, evaluated with the same actors/pose/FOV for comparison.
    static void PreviousFrame(bool arena)
    {
        var p=rig.player;var e=rig.enemy;float fov=cam.fieldOfView;
        Vector3 center=(p.position+e.position)*.5f,focus;Vector3 position;
        if(!arena)
        {
            float distance=Mathf.Clamp((Mathf.Abs(p.position.x-e.position.x)+3)/(2*Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*cam.aspect),6.3f,12);
            float h=1.05f+Mathf.Max(p.position.y,e.position.y)*.3f;focus=new Vector3(center.x,h,0);position=new Vector3(center.x,h+distance*.17f,-distance);
        }
        else
        {
            focus=new Vector3(center.x,Mathf.Max(p.position.y,e.position.y)+1.05f,center.z);
            Vector3 back=Vector3.back*Mathf.Cos(8*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(8*Mathf.Deg2Rad),right=Vector3.right,up=Vector3.Cross(-back,right);
            float tanV=Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*.78f,tanH=tanV*cam.aspect,d=5.4f;
            foreach(var actor in new[]{p,e})for(int x=-1;x<=1;x+=2)for(int y=0;y<=1;y++)for(int z=-1;z<=1;z+=2)
            {var q=actor.position+new Vector3(x*.65f,y==0?-.08f:2.15f,z*.45f)-focus;d=Mathf.Max(d,Mathf.Abs(Vector3.Dot(q,right))/tanH+Vector3.Dot(q,back),Mathf.Abs(Vector3.Dot(q,up))/tanV+Vector3.Dot(q,back));}
            position=focus+back*d;
        }
        cam.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position));
    }
    static IEnumerator Tests()
    {
        yield return null;metrics.Add("stage,before_height,after_height,enlargement_percent");
        for(int stage=0;stage<CombatFramingInstaller.Nodes.Length;stage++)
        {
            key=CombatFramingInstaller.Nodes[stage];Isolate(key);
            if(key=="game")SceneManager.LoadScene("GameScene");else SceneRoutes.LoadBattle();
            for(var w=RealWait(.6f);w.MoveNext();)yield return null;
            Setup();Time.captureFramerate=24;Time.timeScale=1;Position(-1.6f,1.6f);
            for(var w=Wait(.7f);w.MoveNext();)yield return null;
            Check(Mathf.Abs(rig.focusHeight-CombatFramingInstaller.FocusHeight)<.001f&&Mathf.Abs(rig.minimumDistance-(rig.orbitArena?CombatFramingInstaller.OrbitDistance:CombatFramingInstaller.SideDistance))<.001f,key+" saved close framing settings");
            foreach(var v in views){v.enabled=false;v.modelAnimator.Play("IDLE",0,0);v.modelAnimator.Update(0);v.modelAnimator.speed=0;}
            rig.enabled=false;PreviousFrame(rig.orbitArena);float before=Height();Capture(Out+"/stills/"+key+"_before.png");
            rig.enabled=true;rig.SetOrbit(0,7,true);rig.ResetOrbit();for(var w=Wait(.5f);w.MoveNext();)yield return null;
            float after=Height(),gain=after/before;metrics.Add(key+","+before.ToString("F4")+","+after.ToString("F4")+","+((gain-1)*100).ToString("F1"));
            Check(gain>=1.14f&&gain<=1.25f,key+" normal-view enlargement "+((gain-1)*100).ToString("F1")+"%");Check(Visible(),key+" normal view full bodies");Capture(Out+"/stills/"+key+"_after.png");
            foreach(var v in views){v.modelAnimator.speed=1;v.enabled=true;}
            bool demo=key=="scout"||key=="east"||key=="final";if(demo){Directory.CreateDirectory(Out+"/frames/"+key);number=0;recording=true;}
            bool visible=true;float smooth=0;Vector3 last=cam.transform.position;
            for(int f=0;f<36;f++){Position(Mathf.Lerp(-1.6f,-.7f,(f+1)/36f),Mathf.Lerp(1.6f,.7f,(f+1)/36f));yield return null;visible&=Visible();smooth=Mathf.Max(smooth,Vector3.Distance(last,cam.transform.position));last=cam.transform.position;}
            Check(visible&&smooth<.4f,key+" approach stays visible and smooth; max step="+smooth.ToString("F3"));Capture(Out+"/stills/"+key+"_close.png");
            // Avoid interrupting the animated target during the camera safety check.
            health[0].DamageLocked=true;fighters[1].TryAttack(CombatMove.HighKick);visible=true;
            for(int f=0;f<36;f++){yield return null;visible&=Visible();if(f==12)Capture(Out+"/stills/"+key+"_kick.png");}
            Check(visible,key+" full high-kick silhouette retained");health[0].DamageLocked=false;
            visible=true;last=cam.transform.position;smooth=0;
            for(int f=0;f<72;f++){Position(Mathf.Lerp(-.7f,-4.2f,(f+1)/72f),Mathf.Lerp(.7f,4.2f,(f+1)/72f));yield return null;visible&=Visible();smooth=Mathf.Max(smooth,Vector3.Distance(last,cam.transform.position));last=cam.transform.position;}
            Check(visible&&smooth<.4f,key+" separation stays visible and smooth; max step="+smooth.ToString("F3"));Capture(Out+"/stills/"+key+"_wide.png");recording=false;
            foreach(float aspect in new[]{4f/3,16f/10,21f/9}){cam.aspect=aspect;rig.SetOrbit(0,7,true);yield return null;Check(Visible(),key+" aspect "+aspect.ToString("F2")+" safety");}cam.aspect=16f/9;
            if(rig.orbitArena)foreach(float angle in new[]{0f,90f,180f,270f}){rig.SetOrbit(angle,7,true);yield return null;Check(Visible(),key+" orbit "+angle+" safety");}
            Position(-.65f,.65f);rig.SetOrbit(0,7,true);rig.ResetOrbit();for(var w=Wait(.7f);w.MoveNext();)yield return null;
            float originalFov=cam.fieldOfView;HitImpact.PlayZoom(new Vector2(.65f,1),.75f,.12f,.15f,.15f);visible=true;
            float limit=Time.realtimeSinceStartup+3;while((HitImpact.CameraEffectActive||Time.realtimeSinceStartup<limit-2.5f)&&Time.realtimeSinceStartup<limit){yield return null;visible&=Visible();}
            Check(visible&&!HitImpact.CameraEffectActive&&Mathf.Abs(cam.fieldOfView-originalFov)<.01f,key+" special zoom safety and FOV restoration");
            health[0].ResetHealth();health[1].ResetHealth();health[1].SetCurrentHealth(1);fighters[1].IsBlocking=false;fighters[1].CancelAttack();fighters[0].InputLocked=false;
            for(var w=Wait(.35f);w.MoveNext();)yield return null;recording=demo;Check(fighters[0].TryAttack(CombatMove.Jab),key+" starts finishing attack");visible=true;bool sawKO=false,sawZoom=false;
            limit=Time.realtimeSinceStartup+4;float start=Time.time;
            while(Time.realtimeSinceStartup<limit&&(Time.time<start+1.8f||HitImpact.CameraEffectActive)){yield return null;visible&=Visible();sawKO|=views[1].CurrentState=="KO";sawZoom|=HitImpact.CameraEffectActive;}
            Check(visible&&sawKO&&sawZoom,key+" actual finishing zoom and KO stay framed");Check(Mathf.Abs(cam.fieldOfView-originalFov)<.01f,key+" KO restores FOV");Capture(Out+"/stills/"+key+"_ko.png");recording=false;
            Time.captureFramerate=0;
        }
    }
}
