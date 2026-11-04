using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Explicit editor-only verification. Test run data is isolated and the prior save is restored.
[InitializeOnLoad]
public static class ScoutPassVerification
{
    const string Active="ScoutPassVerification.Active",SaveKey="VisionStriker.Run";
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator flow;static int lastFrame=-1;
    static PlayerController player;static EnemyAI enemy;static BattleSceneBridge battle;static CombatHUD hud;
    static Camera camera;static Canvas canvas;
    static string Out { get {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-scoutReport");return i>=0?a[i+1]:Application.temporaryCachePath;} }
    static ScoutPassVerification(){if(SessionState.GetBool(Active,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
        File.WriteAllText(Path.Combine(Out,"save_backup.json"),PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.OpenScene(ScoutPassBuilder.ScenePath);
        SessionState.SetBool(Active,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Log(string m,string s,LogType t)
    {if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
    static void Tick()
    {
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try {if(flow==null)flow=Tests();if(!flow.MoveNext())Finish();}catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Finish()
    {
        if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();Time.timeScale=1;
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Original player save restored");
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        File.WriteAllLines(Path.Combine(Out,"runtime_errors.txt"),errors);
        bool ok=errors.Count==0&&checks.All(c=>c.StartsWith("PASS"));File.WriteAllText(Path.Combine(Out,"verification_done.txt"),ok?"PASS":"FAIL");
        EditorApplication.Exit(ok?0:1);
    }
    static void Check(bool ok,string label){checks.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(Path.Combine(Out,"checks.txt"),checks);}
    static IEnumerator Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)yield return null;}
    static void Find()
    {
        player=Object.FindAnyObjectByType<PlayerController>();enemy=Object.FindAnyObjectByType<EnemyAI>();battle=Object.FindAnyObjectByType<BattleSceneBridge>();
        player.enabled=false;enemy.enabled=false;battle.enabled=false;
        foreach(var f in Object.FindObjectsByType<Fighter>()){f.CancelAttack();f.InputLocked=false;f.IsBlocking=false;}
        player.GetComponent<Fighter>().Facing=1;enemy.GetComponent<Fighter>().Facing=-1;
        hud=Object.FindAnyObjectByType<CombatHUD>();canvas=hud.GetComponentInChildren<Canvas>();camera=Camera.main;
        canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
    }
    static void Position(float px,float ex)
    {
        foreach(var pair in new[]{(player.GetComponent<Rigidbody2D>(),px),(enemy.GetComponent<Rigidbody2D>(),ex)})
        {pair.Item1.interpolation=RigidbodyInterpolation2D.None;pair.Item1.position=new Vector2(pair.Item2,pair.Item1.position.y);pair.Item1.transform.position=new Vector3(pair.Item2,pair.Item1.position.y,0);pair.Item1.linearVelocity=Vector2.zero;}
        Physics2D.SyncTransforms();
    }
    static void TestRun()
    {
        var run=RunState.Instance;var data=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,pendingNodeId="scout",currentNodeId="cave",tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};
        data.map=MapGenerator.Generate(run.Config.map,run.Config,data.seed);
        typeof(RunState).GetProperty("Data").SetValue(run,data);
    }
    static IEnumerator Tests()
    {
        yield return null;yield return null;Find();
        var dressing=GameObject.Find("Scout Pass · 청계역 감시 길목/Checkpoint Life");
        if(dressing)
        {
            Check(dressing.GetComponentsInChildren<Collider2D>().Length==0,"Checkpoint props do not alter 2D combat collision");
            var particles=dressing.GetComponentsInChildren<ParticleSystem>();
            Check(particles.Length==2&&particles.Sum(p=>p.main.maxParticles)<=88,"Brazier smoke and embers stay within 88 particles");
            Check(particles.All(p=>p.isPlaying),"Brazier smoke and embers animate during gameplay");
            Check(dressing.GetComponentsInChildren<Light>().All(l=>l.shadows==LightShadows.None),"Decorative fire adds no shadow-casting lights");
        }
        for(var test=SilentDialogueChecks();test.MoveNext();)yield return null;
        for(var test=OrbitChecks();test.MoveNext();)yield return null;
        for(var test=CombatCameraChecks();test.MoveNext();)yield return null;
        if(Environment.GetCommandLineArgs().Contains("-scoutPreview"))
        {

            for(var w=Wait(1);w.MoveNext();)yield return null;
            for(var c=Capture("01_gameplay",1920,1080);c.MoveNext();)yield return null;
            for(var c=Environment.GetCommandLineArgs().Contains("-scoutCombatCamera")?CombatCameraMovie():Movie();c.MoveNext();)yield return null;
            Position(-5.7f,3.3f);for(var w=Wait(.8f);w.MoveNext();)yield return null;
            for(var c=Capture("02_left_boundary",1920,1080);c.MoveNext();)yield return null;
            Position(-3.3f,5.7f);for(var w=Wait(.8f);w.MoveNext();)yield return null;
            for(var c=Capture("03_right_boundary",1920,1080);c.MoveNext();)yield return null;
            foreach(var shader in Object.FindObjectsByType<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.shader).Distinct())
                Check(!ShaderUtil.ShaderHasError(shader),"Final shader compiles: "+shader.name);
            Check(errors.Count==0,"Final scene renders and animates without runtime errors");yield break;
        }
        Check(GameObject.Find("Arena Environment 3D")==null,"Old arena replaced only in ScoutPass");
        Check(GameObject.Find("Ground")?.GetComponent<Collider2D>()!=null,"Original fighting floor collider retained");
        Check(GameObject.Find("ArenaWall_Left")!=null&&GameObject.Find("ArenaWall_Right")!=null,"Both arena boundary walls retained");
        Check(enemy.GetComponent<Fighter3DView>().visualRoot.name.Contains("Scout"),"Scout visual and animator present in standalone scene");
        Check(hud!=null&&canvas.transform.Find("DuelHeader/RoundTimer/Seconds").GetComponent<Text>().text=="120","Applied HP / charge / 120-second HUD present");
        Check(Object.FindObjectsByType<LODGroup>().Length>=60,"Repeated props use distance LODs");
        var materials=Object.FindObjectsByType<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
        foreach(var s in materials.Select(m=>m.shader).Distinct())Check(!ShaderUtil.ShaderHasError(s),"Shader compiles: "+s.name);
        foreach(var n in new[]{"LowBoulder","CliffRock","BentPine","TimberFence"})
            Check(materials.Any(m=>m.name.StartsWith(n)&&m.GetTexture("_BaseMap")!=null),n+" texture assigned");
        var flag=Object.FindAnyObjectByType<ScoutPassWind>();Vector3[] before=flag.GetComponent<MeshFilter>().mesh.vertices;
        for(var w=Wait(1);w.MoveNext();)yield return null;
        Check(before.Where((v,i)=>Vector3.Distance(v,flag.GetComponent<MeshFilter>().mesh.vertices[i])>.002f).Any(),"Cloth visibly deforms over time");
        Check(Mathf.Abs(player.transform.position.y-enemy.transform.position.y)<.08f,"Both fighters stand on same flat plane");
        for(var c=Capture("01_gameplay",1920,1080);c.MoveNext();)yield return null;
        // Validate actual movement/collision near each end, not only wall existence.
        Position(-5.3f,3);
        float end=Time.realtimeSinceStartup+1.0f;
        while(Time.realtimeSinceStartup<end){var rb=player.GetComponent<Rigidbody2D>();rb.linearVelocity=new Vector2(-4,rb.linearVelocity.y);yield return null;}
        Check(player.transform.position.x>-6.5f,"Left wall stops outward movement");player.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
        for(var w=Wait(.7f);w.MoveNext();)yield return null;
        for(var c=Capture("02_left_boundary",1920,1080);c.MoveNext();)yield return null;
        Position(-3,5.3f);end=Time.realtimeSinceStartup+1;
        while(Time.realtimeSinceStartup<end){var rb=enemy.GetComponent<Rigidbody2D>();rb.linearVelocity=new Vector2(4,rb.linearVelocity.y);yield return null;}
        Check(enemy.transform.position.x<6.5f,"Right wall stops outward movement");enemy.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
        for(var w=Wait(.7f);w.MoveNext();)yield return null;
        for(var c=Capture("03_right_boundary",1920,1080);c.MoveNext();)yield return null;
        Position(-.72f,.72f);for(var w=Wait(.7f);w.MoveNext();)yield return null;
        int health=enemy.GetComponent<Health>().CurrentHealth;Check(player.GetComponent<Fighter>().TryAttack(CombatMove.Straight),"Player can start shared straight animation");
        for(var w=Wait(1.5f);w.MoveNext();)yield return null;
        Check(enemy.GetComponent<Health>().CurrentHealth<health,"Actual attack hits scout on new floor");
        for(var c=Capture("04_close_combat",1920,1080);c.MoveNext();)yield return null;
        Position(-2.4f,2.4f);for(var w=Wait(.8f);w.MoveNext();)yield return null;
        for(var c=Capture("05_ultrawide",2560,1080);c.MoveNext();)yield return null;
        for(var c=Capture("06_classic",1440,1080);c.MoveNext();)yield return null;
        var config=RunState.Instance.Config;
        Check(config.GetBattleScene("scout")=="ScoutPass"&&config.GetBattleScene("unknown")==SceneRoutes.Battle,"Scout-only scene override and default fallback");
        Check(Application.CanStreamedLevelBeLoaded("ScoutPass"),"ScoutPass included in build scene list");
        TestRun();Check(SceneRoutes.PendingBattleScene=="ScoutPass","Saved pending scout node selects new scene");SceneRoutes.LoadBattle();
        for(var w=Wait(1);w.MoveNext();)yield return null;Find();
        Check(SceneManager.GetActiveScene().name=="ScoutPass","Real story battle scene route loads ScoutPass");
        Check(!hud.AllowRestart,"Story mode keeps restart disabled");
        Check(enemy.GetComponent<Health>().DisplayName.Contains("척후병"),"Story stage config and scout override initialize");
        // Death callbacks remain live while the timer is disabled; finish and check both route outcomes.
        enemy.GetComponent<Health>().TakeDamage(100000,Vector2.zero);
        for(var w=Wait(.35f);w.MoveNext();)yield return null;
        for(var c=Capture("07_victory",1920,1080);c.MoveNext();)yield return null;
        for(var w=Wait(5);w.MoveNext();)yield return null;
        Check(SceneManager.GetActiveScene().name==SceneRoutes.Map&&RunState.Instance.Data.clearedNodeIds.Contains("scout"),"Scout victory clears node and returns to map");
        TestRun();SceneRoutes.LoadBattle();for(var w=Wait(.7f);w.MoveNext();)yield return null;Find();
        player.GetComponent<Health>().TakeDamage(100000,Vector2.zero);for(var w=Wait(5);w.MoveNext();)yield return null;
        Check(SceneManager.GetActiveScene().name==SceneRoutes.Start&&!RunState.Instance.HasRun,"Defeat resets run and returns to start");
        Check(errors.Count==0,"No runtime exceptions during map / battle / outcome flow");
    }
    static IEnumerator SilentDialogueChecks()
    {
        if(!Environment.GetCommandLineArgs().Contains("-scoutClouds"))yield break;
        var scenes=Resources.LoadAll<StoryCutsceneDefinition>("Story/Cutscenes");
        var lines=scenes.SelectMany(s=>s.lines).ToArray();
        Check(scenes.Length==38&&lines.Length==103,"All 38 cutscenes / 103 dialogue lines retained");
        Check(lines.All(l=>l.voice==null),"Every story voice reference removed");
        Check(lines.All(l=>l.voiceEnvelope==null||l.voiceEnvelope.Length==0),"Every old voice envelope cleared");
        Check(lines.All(l=>l.videoSpeechTrack==null),"Old speech tracking detached");
        var go=new GameObject("SilentDialogueProbe");var voice=go.AddComponent<StoryVoice>();
        foreach(var line in lines)voice.Play(line);
        Check(!voice.IsPlaying&&voice.Level==0&&go.GetComponent<AudioSource>()==null,"Unvoiced lines create no voice AudioSource or lip amplitude");Object.Destroy(go);
        var c=UIFactory.CreateCanvas("SilentDialogueCanvas",null,300);
        var presenter=c.gameObject.AddComponent<StoryCutscene>();
        var data=scenes.First(s=>s.name=="cave_visit");
        var routine=(IEnumerator)typeof(StoryCutscene).GetMethod("PresentDialogue",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(presenter,new object[]{data,0});
        presenter.StartCoroutine(routine);
        for(var w=Wait(.6f);w.MoveNext();)yield return null;
        Check(c.GetComponentsInChildren<Text>().Any(t=>t.text==data.lines[0].text),"Silent cutscene still shows first subtitle");
        Check(c.GetComponentsInChildren<AudioSource>().Length==0,"Actual subtitle presenter remains silent");
        typeof(StoryCutscene).GetField("next",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(presenter,true);
        yield return null;yield return null;yield return null;
        Check(c.GetComponentsInChildren<Text>().Any(t=>t.text==data.lines[1].text),"Space advance path moves to next subtitle without audio");
        Object.Destroy(c.gameObject);yield return null;
        var mat=RenderSettings.skybox;
        Check(mat!=null&&mat.shader.name=="VisionStriker/Scout 360 Sky"&&mat.GetTexture("_CloudTex")!=null,"360 sky and separately drifting clouds assigned");
        Check(mat.GetFloat("_CloudSpeed")>0,"Cloud scroll speed is active");
    }
    static IEnumerator OrbitChecks()
    {
        if(!Environment.GetCommandLineArgs().Contains("-scoutOrbit"))yield break;
        var rig=camera.GetComponent<Battle3DCamera>();
        Check(rig.orbitArena,"360 camera enabled only for ScoutPass");
        Check(GameObject.Find("DistantMountainBackdrop")==null,"Old flat backdrop removed from rotating stage");
        Check(GameObject.Find("Arena360Ground")!=null&&GameObject.Find("MountainRing2")!=null,"Full ground and all three closed mountain rings present");
        Check(!ShaderUtil.ShaderHasError(RenderSettings.skybox.shader),"Animated 360 cloud sky compiles");
        rig.allowMouseOrbit=false;
        Position(-2.1f,2.1f);yield return null;
        bool framed=true,clear=true;var failures=new List<string>();
        foreach(float aspect in new[]{16f/9,4f/3,21f/9})
        foreach(float center in new[]{0f,-3f,3f})
        foreach(float angle in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
        {
            Position(center-2.1f,center+2.1f);camera.aspect=aspect;rig.SetOrbit(angle,8,true);yield return null;
            foreach(var actor in new[]{player.transform,enemy.transform})
            foreach(float h in new[]{.05f,2.05f})
            {Vector3 v=camera.WorldToViewportPoint(actor.position+Vector3.up*h);bool fits=v.z>.1f&&v.x>.02f&&v.x<.98f&&v.y>.02f&&v.y<.92f;framed&=fits;if(!fits)failures.Add($"aspect={aspect} center={center} angle={angle} actor={actor.name} point={h} world={actor.position} view={v} cam={camera.transform.position}");}
            clear&=!Physics.CheckSphere(camera.transform.position,.2f,1<<2,QueryTriggerInteraction.Collide);
        }
        File.WriteAllLines(Path.Combine(Out,"framing_details.txt"),failures);
        Check(framed,"Both fighters fit at 8 angles, 3 screen ratios and 3 arena positions (72 cases)");
        Check(clear,"72 orbit poses remain outside prop camera hulls");
        Position(-2.1f,2.1f);camera.aspect=16f/9;yield return null;yield return null;
        foreach(float angle in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
        {
            rig.SetOrbit(angle,8,true);
            for(var c=Capture("orbit_"+angle.ToString("000"),1920,1080);c.MoveNext();)yield return null;
        }
        rig.enabled=false;canvas.enabled=false;
        camera.transform.position=new Vector3(0,90,-105);camera.transform.LookAt(new Vector3(0,0,25));
        for(var c=Capture("arena_overview",1920,1080);c.MoveNext();)yield return null;
        canvas.enabled=true;rig.enabled=true;
        foreach(string n in new[]{"MountainRing0","MountainRing1","MountainRing2"})
        {var r=GameObject.Find(n).GetComponent<Renderer>();Check(!float.IsNaN(r.bounds.extents.y)&&r.bounds.extents.y>8,"Valid 3D mountain geometry: "+n);}
        rig.SetOrbit(45,8,true);float fov=camera.fieldOfView;
        HitImpact.PlayZoom(Vector2.up,.9f,.1f,.15f,.1f);yield return null;
        Check(HitImpact.CameraEffectActive,"Finishing camera effect owns pose while orbit is suspended");
        for(var w=Wait(.8f);w.MoveNext();)yield return null;
        Check(!HitImpact.CameraEffectActive&&Mathf.Abs(camera.fieldOfView-fov)<.01f,"Finishing zoom restores FOV and releases camera");
        // Exercise the common movement path used by vision and keyboard inputs.
        bool originalExternal=player.externalControl;player.externalControl=true;
        var movement=typeof(PlayerController).GetField("externalMoveX",BindingFlags.NonPublic|BindingFlags.Instance);
        var handle=typeof(PlayerController).GetMethod("HandleKeyInput",BindingFlags.NonPublic|BindingFlags.Instance);
        movement.SetValue(player,1f);rig.SetOrbit(180,8,true);handle.Invoke(player,null);
        Check(player.GetComponent<Rigidbody2D>().linearVelocity.x<0,"Rightward input remains screen-right from the reverse camera");
        rig.SetOrbit(0,8,true);handle.Invoke(player,null);
        Check(player.GetComponent<Rigidbody2D>().linearVelocity.x>0,"Rightward input remains screen-right from the default camera");
        movement.SetValue(player,0f);handle.Invoke(player,null);player.externalControl=originalExternal;
        rig.ResetOrbit();rig.SetOrbit(0,8,true);rig.allowMouseOrbit=true;
    }
    static bool ActorsFramed()
    {
        foreach(var actor in new[]{player.transform,enemy.transform})
        foreach(float h in new[]{.05f,2.05f})
        {
            Vector3 v=camera.WorldToViewportPoint(actor.position+Vector3.up*h);
            if(v.z<=.1f||v.x<=.02f||v.x>=.98f||v.y<=.02f||v.y>=.92f)return false;
        }
        return true;
    }
    static IEnumerator CombatCameraChecks()
    {
        if(!Environment.GetCommandLineArgs().Contains("-scoutCombatCamera"))yield break;
        var rig=camera.GetComponent<Battle3DCamera>();
        Check(rig.automaticCombatFraming,"Movement-driven combat framing serialized in ScoutPass");
        rig.allowMouseOrbit=false;Position(-1.2f,1.2f);rig.SetOrbit(0,8,true);rig.ResetOrbit();
        for(var w=Wait(1.5f);w.MoveNext();)yield return null;
        Vector3 still=camera.transform.position;Quaternion stillRotation=camera.transform.rotation;
        for(var w=Wait(.6f);w.MoveNext();)yield return null;
        Check(Vector3.Distance(still,camera.transform.position)<.01f&&Quaternion.Angle(stillRotation,camera.transform.rotation)<.1f,
            "Idle footwork does not spin or shake the camera");
        bool framing=true,input=true,limits=true;
        foreach(float aspect in new[]{16f/9,4f/3,21f/9})
        foreach(var pose in new[]{new Vector2(-5,-3),new Vector2(-5,5),new Vector2(3,5)})
        {
            camera.aspect=aspect;Position(pose.x,pose.y);
            for(var w=Wait(1.2f);w.MoveNext();)yield return null;
            framing&=ActorsFramed();input&=rig.ScreenHorizontalToWorld(1)>0;
            limits&=Mathf.Abs(rig.OrbitYaw)<=rig.maximumMovementYaw+.05f;
        }
        Check(framing,"Auto camera frames close / distant / cornered fighters at three aspect ratios");
        Check(input&&limits,"Automatic orbit stays within 14 degrees and never reverses screen input");
        camera.aspect=16f/9;
        Position(-4.4f,-2.2f);for(var w=Wait(1.5f);w.MoveNext();)yield return null;
        float leftYaw=rig.OrbitYaw;for(var c=Capture("auto_left",1280,720);c.MoveNext();)yield return null;
        Position(2.2f,4.4f);for(var w=Wait(1.5f);w.MoveNext();)yield return null;
        float rightYaw=rig.OrbitYaw;for(var c=Capture("auto_right",1280,720);c.MoveNext();)yield return null;
        Check(leftYaw<-8&&rightYaw>8,"Left and right combat locations produce opposite natural camera angles");

        Position(-3,-.8f);rig.SetOrbit(-6,8,true);rig.ResetOrbit();
        float speed=player.moveSpeed;bool keyboard=player.useKeyboard;
        player.useKeyboard=false;player.enabled=true;
        float travelSpeed=speed*(player.GetComponent<Fighter3DView>()!=null?Fighter3DView.BoxingTravelScale:1f);
        float previousYaw=rig.OrbitYaw,maxStep=0,minYaw=100,maxYaw=-100;
        bool movingFramed=true,clear=true;int samples=0;
        foreach(float direction in new[]{1f,-1f})
        {
            // Exercise a useful travel distance with the current production step speed.
            float startX=player.transform.position.x,end=Time.time+8f;
            while(Mathf.Abs(player.transform.position.x-startX)<3.6f&&Time.time<end)
            {
                player.ExternalMovement(direction);
                var rb=enemy.GetComponent<Rigidbody2D>();rb.linearVelocity=new Vector2(direction*travelSpeed*(direction<0?.8f:1),rb.linearVelocity.y);
                yield return null;
                maxStep=Mathf.Max(maxStep,Mathf.Abs(Mathf.DeltaAngle(previousYaw,rig.OrbitYaw)));previousYaw=rig.OrbitYaw;
                minYaw=Mathf.Min(minYaw,rig.OrbitYaw);maxYaw=Mathf.Max(maxYaw,rig.OrbitYaw);
                movingFramed&=ActorsFramed();clear&=!Physics.CheckSphere(camera.transform.position,.2f,1<<2,QueryTriggerInteraction.Collide);samples++;
            }
        }
        player.ClearExternalInput();player.enabled=false;player.moveSpeed=speed;player.useKeyboard=keyboard;
        player.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;enemy.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
        Check(maxYaw-minYaw>10&&samples>50,"Real movement input drives automatic rotation without mouse input");
        Check(maxStep<2.1f,"Direction changes stay below 2.1 degrees per camera step");
        Check(movingFramed&&clear,"Moving fighters stay framed and camera stays clear of props");
        File.WriteAllText(Path.Combine(Out,"combat_camera_metrics.txt"),$"samples={samples}\ntravelSpeed={travelSpeed}\nminYaw={minYaw}\nmaxYaw={maxYaw}\nmaxFrameYawStep={maxStep}\n");

        Position(-1,1);for(var w=Wait(1.2f);w.MoveNext();)yield return null;
        float close=Vector3.Distance(camera.transform.position,(player.transform.position+enemy.transform.position)*.5f+Vector3.up*1.05f);
        Position(-5,5);for(var w=Wait(1.2f);w.MoveNext();)yield return null;
        float far=Vector3.Distance(camera.transform.position,(player.transform.position+enemy.transform.position)*.5f+Vector3.up*1.05f);
        Check(far>close+1,"Camera pulls back for separated fighters without FOV pumping");
        Position(-1,1);for(var w=Wait(1.2f);w.MoveNext();)yield return null;
        rig.SetOrbit(45,18,true);Vector3 resetPose=camera.transform.position;rig.ResetOrbit();
        Check(Vector3.Distance(resetPose,camera.transform.position)==0&&rig.OrbitYaw>40,"Home reset starts a blend instead of cutting to a new angle");
        for(var w=Wait(2.5f);w.MoveNext();)yield return null;
        Check(Mathf.Abs(rig.OrbitYaw)<1&&!rig.IsManualView,"Manual view smoothly returns to neutral combat framing");
        rig.SetOrbit(35,8,true);
        for(var w=Wait(rig.manualReturnDelay+2.2f);w.MoveNext();)yield return null;
        Check(!rig.IsManualView&&Mathf.Abs(rig.OrbitYaw)<1,"Released mouse orbit automatically returns after the delay");
        Time.timeScale=0;Vector3 pausePose=camera.transform.position;Quaternion pauseRot=camera.transform.rotation;Position(1,3);
        for(var w=Wait(.25f);w.MoveNext();)yield return null;
        Check(camera.transform.position==pausePose&&camera.transform.rotation==pauseRot,"Pause freezes automatic tracking");Time.timeScale=1;
        player.GetComponent<Fighter>().InputLocked=true;Vector3 lockedPose=camera.transform.position;
        for(var w=Wait(.25f);w.MoveNext();)yield return null;
        Check(camera.transform.position==lockedPose,"KO input lock prevents camera tracking from fighting the finish");
        player.GetComponent<Fighter>().InputLocked=false;
        Position(-1,1);for(var w=Wait(1);w.MoveNext();)yield return null;
        Quaternion orderPose=camera.transform.rotation;Position(1,-1);
        for(var w=Wait(.3f);w.MoveNext();)yield return null;
        Check(Quaternion.Angle(orderPose,camera.transform.rotation)<10,"Swapped fighter order does not flip the camera 180 degrees");
        Position(-2.1f,2.1f);rig.ResetOrbit();rig.allowMouseOrbit=true;
        for(var w=Wait(1);w.MoveNext();)yield return null;
    }
    // A repeatable movement/attack scenario. The production camera remains fully automatic.
    static IEnumerator CombatCameraMovie()
    {
        string dir=Path.Combine(Out,"frames");Directory.CreateDirectory(dir);
        const int width=1280,height=720,fps=24,frames=576;
        var rig=camera.GetComponent<Battle3DCamera>();rig.ResetOrbit();rig.allowMouseOrbit=false;
        Position(-3,-.8f);for(var w=Wait(1);w.MoveNext();)yield return null;
        var rt=new RenderTexture(width,height,24);rt.Create();camera.targetTexture=rt;camera.aspect=(float)width/height;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);Time.captureFramerate=fps;
        float speed=player.moveSpeed;bool keyboard=player.useKeyboard;player.useKeyboard=false;player.enabled=true;
        float travelSpeed=speed*(player.GetComponent<Fighter3DView>()!=null?Fighter3DView.BoxingTravelScale:1f);
        var prb=player.GetComponent<Rigidbody2D>();var erb=enemy.GetComponent<Rigidbody2D>();
        var ph=player.GetComponent<Health>();var eh=enemy.GetComponent<Health>();ph.ResetHealth();eh.ResetHealth();
        var pf=player.GetComponent<Fighter>();var ef=enemy.GetComponent<Fighter>();
        var metrics=new List<string>{"frame,playerX,enemyX,yaw,manual"};
        for(int i=0;i<frames;i++)
        {
            float t=i/(float)fps,axis=0,enemySpeed=0;
            if(t>=1.5f&&t<5){axis=1;enemySpeed=travelSpeed;}
            else if(t>=6&&t<10.5f){axis=-1;enemySpeed=-travelSpeed*.8f;}
            else if(t>=11.5f&&t<14.5f){axis=-1;enemySpeed=travelSpeed*.8f;}
            else if(t>=15&&t<20)
            {
                float gap=enemy.transform.position.x-player.transform.position.x;
                if(gap>1.5f){axis=1;enemySpeed=-travelSpeed;}
            }
            player.ExternalMovement(axis);if(!eh.IsHitStunned)erb.linearVelocity=new Vector2(enemySpeed,erb.linearVelocity.y);
            if(i==480)pf.TryAttack(CombatMove.Straight);
            if(i==522){pf.IsBlocking=true;player.ExternalAction("BLOCK");ef.TryAttack(CombatMove.Jab);}
            if(i==555)player.ExternalAction("IDLE");
            yield return null;
            metrics.Add($"{i},{player.transform.position.x:F3},{enemy.transform.position.x:F3},{rig.OrbitYaw:F3},{rig.IsManualView}");
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(dir,i.ToString("D4")+".jpg"),tex.EncodeToJPG(90));RenderTexture.active=null;
        }
        File.WriteAllLines(Path.Combine(Out,"preview_camera_metrics.csv"),metrics);
        Check(metrics.Skip(1).All(m=>m.EndsWith("False")),"Gameplay preview uses no manual orbit or scripted camera rotation");
        Check(eh.CurrentHealth<eh.MaxHealth,"Gameplay preview includes actual straight hit");
        player.ClearExternalInput();player.enabled=false;player.moveSpeed=speed;player.useKeyboard=keyboard;prb.linearVelocity=erb.linearVelocity=Vector2.zero;
        Time.captureFramerate=0;HitImpact.ReleaseTimeControl();camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        Object.Destroy(tex);rt.Release();Object.Destroy(rt);yield return null;
    }

    static IEnumerator Movie()
    {
        string dir=Path.Combine(Out,"frames");Directory.CreateDirectory(dir);
        const int width=1280,height=720;var rt=new RenderTexture(width,height,24);rt.Create();
        camera.targetTexture=rt;camera.aspect=(float)width/height;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);Time.captureFramerate=24;
        for(int i=0;i<(Environment.GetCommandLineArgs().Contains("-scoutOrbit")?576:240);i++)
        {
            if(Environment.GetCommandLineArgs().Contains("-scoutOrbit"))
            {float a=Mathf.Clamp01((i-96)/384f)*360;camera.GetComponent<Battle3DCamera>().SetOrbit(a,8,true);}
            yield return null;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(dir,i.ToString("D4")+".jpg"),tex.EncodeToJPG(92));RenderTexture.active=null;
        }
        Time.captureFramerate=0;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        Object.Destroy(tex);rt.Release();Object.Destroy(rt);yield return null;
    }
    static IEnumerator Capture(string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,24);rt.Create();camera.targetTexture=rt;camera.aspect=(float)width/height;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        yield return null;yield return null;yield return null;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Out,name+".png"),tex.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;Object.Destroy(tex);rt.Release();Object.Destroy(rt);
        yield return null;
    }
}
