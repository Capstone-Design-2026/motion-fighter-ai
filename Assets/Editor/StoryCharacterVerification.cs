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
public static class StoryCharacterVerification
{
    const string Flag="StoryCharacterVerification.Active", SaveKey="VisionStriker.Run";
    static string Out=>StoryCharacterInstaller.Out;
    static IEnumerator flow;static int last=-1,number;static bool recording;static string key;
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static Camera cam;static Canvas canvas;static Fighter3DView view;static Fighter enemyF,playerF;static Health eh,ph;static Rigidbody2D eb,pb;
    static RenderTexture captureTarget;static Texture2D captureImage;
    static StoryCharacterVerification(){if(SessionState.GetBool(Flag,false))Subscribe();}
    public static void Run()
    {
        Directory.CreateDirectory(Out+"/stills");Directory.CreateDirectory(Out+"/frames");
        SessionState.SetBool(Flag+".HadSave",PlayerPrefs.HasKey(SaveKey));SessionState.SetString(Flag+".Save",PlayerPrefs.GetString(SaveKey,""));
        File.WriteAllText(Out+"/save_before.json",PlayerPrefs.GetString(SaveKey,""));
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");SessionState.SetBool(Flag,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;}
    static void Log(string m,string s,LogType t){if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
    static void Tick()
    {
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(Time.frameCount==last)return;last=Time.frameCount;
        try{if(flow==null)flow=Tests();bool next=flow.MoveNext();if(recording&&cam)Capture(Out+"/frames/"+key+"/"+(number++).ToString("D5")+".jpg");if(!next)Finish();}
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Out+"/checks.txt",checks);}
    static void Finish()
    {
        recording=false;if(SessionState.GetBool(Flag+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Flag+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();Time.timeScale=1;Time.captureFramerate=0;HitImpact.ReleaseTimeControl();
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Flag+".Save",""),"Original progress restored");File.WriteAllText(Out+"/save_after.json",PlayerPrefs.GetString(SaveKey,""));
        File.WriteAllLines(Out+"/runtime_errors.txt",errors);File.WriteAllText(Out+"/verification_done.txt",errors.Count==0&&checks.All(x=>x.StartsWith("PASS"))?"PASS":"FAIL");
        SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;EditorApplication.Exit(errors.Count==0&&checks.All(x=>x.StartsWith("PASS"))?0:1);
    }
    static IEnumerator Wait(float duration){float t=Time.time+duration;while(Time.time<t)yield return null;}
    static IEnumerator WaitReal(float duration){float t=Time.realtimeSinceStartup+duration;while(Time.realtimeSinceStartup<t)yield return null;}
    static void Isolate(string id)
    {
        var run=RunState.Instance;var data=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,pendingNodeId=id,currentNodeId="cave",tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};
        data.map=MapGenerator.Generate(run.Config.map,run.Config,data.seed);typeof(RunState).GetProperty("Data").SetValue(run,data);
    }
    static void Position(float left,float right)
    {
        foreach(var pair in new[]{(pb,left),(eb,right)}){pair.Item1.position=new Vector2(pair.Item2,pair.Item1.position.y);pair.Item1.linearVelocity=Vector2.zero;pair.Item1.interpolation=RigidbodyInterpolation2D.None;}
        Physics2D.SyncTransforms();
    }
    static void Capture(string path,int width=1280,int height=720)
    {
        if(!captureTarget){captureTarget=new RenderTexture(width,height,24);captureTarget.Create();captureImage=new Texture2D(width,height,TextureFormat.RGB24,false);}
        var rt=captureTarget;var tex=captureImage;
        cam.aspect=(float)width/height;cam.targetTexture=rt;
        if(canvas){var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();if(scaler)scaler.enabled=false;canvas.scaleFactor=width/1920f;}
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});
        var old=RenderTexture.active;RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes(path,path.EndsWith("png")?tex.EncodeToPNG():tex.EncodeToJPG(88));
        RenderTexture.active=old;
    }
    static void Pose(string name)
    {
        bool finite=true;float extent=0;var mesh=new Mesh();
        foreach(var skin in view.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh);var matrix=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
            foreach(var v in mesh.vertices){var p=matrix.MultiplyPoint3x4(v);finite&=!float.IsNaN(p.x)&&!float.IsInfinity(p.y);extent=Mathf.Max(extent,Vector3.Distance(p,view.visualRoot.position));}
        }
        Object.Destroy(mesh);Check(finite&&extent<3.2f,key+" "+name+" finite skin, no exploded vertices; extent="+extent.ToString("F2"));
        Capture(Out+"/stills/"+key+"_"+name+".png");
    }
    static IEnumerator Tests()
    {
        yield return null;
        var config=RunState.Instance.Config;var original=AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Scout);
        Check(config.GetEnemyVisual("scout")==original,"scout keeps the original scout visual");
        var selected=StoryCharacterInstaller.Selected;
        foreach(int i in selected)Check(config.GetEnemyVisual(StoryCharacterInstaller.Nodes[i])==AssetDatabase.LoadAssetAtPath<GameObject>(StoryCharacterInstaller.PrefabPath(i)),StoryCharacterInstaller.Nodes[i]+" configured with "+StoryCharacterInstaller.Keys[i]);
        foreach(int i in selected)
        {
            string id=StoryCharacterInstaller.Nodes[i];key=StoryCharacterInstaller.Keys[i];Isolate(id);SceneRoutes.LoadBattle();
            for(var w=WaitReal(1);w.MoveNext();)yield return null;
            Time.timeScale=1;Time.captureFramerate=24;
            var player=Object.FindAnyObjectByType<PlayerController>();var enemy=Object.FindAnyObjectByType<EnemyAI>();player.enabled=enemy.enabled=false;
            foreach(var udp in Object.FindObjectsByType<UdpActionReceiver>())udp.enabled=false;
            view=enemy.GetComponent<Fighter3DView>();enemyF=enemy.GetComponent<Fighter>();playerF=player.GetComponent<Fighter>();eh=enemy.GetComponent<Health>();ph=player.GetComponent<Health>();eb=enemy.GetComponent<Rigidbody2D>();pb=player.GetComponent<Rigidbody2D>();
            enemyF.CancelAttack();playerF.CancelAttack();eh.ResetHealth();ph.ResetHealth();enemyF.Facing=-1;playerF.Facing=1;
            foreach(var f in new[]{enemyF,playerF}){f.InputLocked=false;f.IsBlocking=f.IsCrouching=false;var so=new SerializedObject(f);so.FindProperty("useHitImpact").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();}
            var bridge=Object.FindAnyObjectByType<BattleSceneBridge>();var bridgeSo=new SerializedObject(bridge);bridgeSo.FindProperty("useSlowMotion").boolValue=false;bridgeSo.ApplyModifiedPropertiesWithoutUndo();
            cam=Camera.main;var rig=cam.GetComponent<Battle3DCamera>();rig.allowMouseOrbit=false;rig.automaticCombatFraming=false;rig.SetOrbit(0,8,true);
            canvas=Object.FindAnyObjectByType<CombatHUD>().GetComponentInChildren<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=cam.nearClipPlane+.1f;canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
            Position(-1.6f,1.6f);
            Check(view.visualRoot.name==key+"_Visual",key+" correct stage visual instantiated: "+SceneManager.GetActiveScene().name);
            Check(view.modelAnimator.isHuman&&view.modelAnimator.avatar.isValid,key+" valid humanoid");
            var controller=view.modelAnimator.runtimeAnimatorController as AnimatorOverrideController;
            Check(controller&&controller.runtimeAnimatorController==AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(UnifiedBoxingInstaller.Folder+"/Unified_Combat.controller"),key+" shared combat state machine");
            Check(view.modelAnimator.GetBoneTransform(HumanBodyBones.RightIndexDistal)!=null,key+" mapped fist fingers");
            Check(view.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().All(s=>s.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap")&&!ShaderUtil.ShaderHasError(m.shader))),key+" clothing texture/material valid");
            for(var w=Wait(.7f);w.MoveNext();)yield return null;
            Directory.CreateDirectory(Out+"/frames/"+key);number=0;recording=true;Pose("IDLE");
            for(var w=Wait(.8f);w.MoveNext();)yield return null;
            foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))
            {
                Check(enemyF.TryAttack(move),key+" starts "+move);
                for(var w=Wait(Fighter3DView.Impact(move));w.MoveNext();)yield return null;
                Pose(move.ToString());
                // A whiffed attack now adds whiffPunish recovery, so allow for it before checking idle.
                for(var w=Wait(Fighter3DView.Duration(move)-Fighter3DView.Impact(move)+.65f);w.MoveNext();)yield return null;
                Check(view.CurrentState=="IDLE"&&!enemyF.IsAttacking&&!view.modelAnimator.IsInTransition(0),key+" "+move+" recovers to idle");
            }
            // Held guard: stays up while the input is active, through a blocked hit, and ends with no delay on release.
            // In 3D the guard height is automatic: it goes low only when the opponent has telegraphed a low kick.
            foreach(bool low in new[]{false,true})
            {
                string state=low?"GUARD_LOWKICK":"GUARD";
                if(low)playerF.DeclareIntent(CombatMove.LowKick);
                Check(enemyF.TryGuard(low),key+" "+state+" cast");
                for(var w=Wait(.5f);w.MoveNext();)yield return null;Check(view.CurrentState==state+"_HOLD",key+" "+state+" holds");Pose(state);
                eh.TakeDamage(1,Vector2.zero,true);yield return null;Check(view.CurrentState==state+"_HIT",key+" "+state+" reacts");
                Check(enemyF.IsGuarding,key+" "+state+" stays up after the block while held");enemyF.ReleaseGuard();
                float guardUntil=Time.time+2.5f;while(Time.time<guardUntil&&view.CurrentState!="IDLE")yield return null;
                Check(view.CurrentState=="IDLE"&&!enemyF.IsGuarding,key+" "+state+" ends on release and returns to idle");
                if(low)playerF.ClearIntent();
                for(var w=Wait(.3f);w.MoveNext();)yield return null;
            }
            Check(enemyF.TryGuard(false),key+" guard cast for hold test");
            for(var w=Wait(2f);w.MoveNext();)yield return null;Check(enemyF.IsGuarding,key+" held guard has no time limit");
            enemyF.ReleaseGuard();Check(enemyF.CanAct,key+" released guard can act immediately");
            float releaseUntil=Time.time+3f;while(Time.time<releaseUntil&&view.CurrentState!="IDLE")yield return null;Check(view.CurrentState=="IDLE",key+" released guard returns to idle");
            for(var w=Wait(.6f);w.MoveNext();)yield return null;
            eh.TakeDamage(2,Vector2.zero);for(var w=Wait(.25f);w.MoveNext();)yield return null;Check(view.CurrentState=="HURT",key+" hurt reaction");Pose("HURT");
            for(var w=Wait(.65f);w.MoveNext();)yield return null;Check(view.CurrentState=="IDLE",key+" hurt recovers");
            foreach(int dir in new[]{-1,1})
            {
                string walk=dir<0?"WALK_FORWARD":"WALK_BACK";float end=Time.time+.8f,maxContact=0;bool seen=false;
                while(Time.time<end){eb.linearVelocity=new Vector2(dir*.7f,eb.linearVelocity.y);yield return null;seen|=view.CurrentState==walk;maxContact=Mathf.Max(maxContact,view.Walk.ContactError);}
                eb.linearVelocity=Vector2.zero;Check(seen,key+" "+walk+" follows real movement");Check(maxContact<.035f,key+" "+walk+" contact error="+maxContact.ToString("F4"));Pose(walk);
                for(var w=Wait(.65f);w.MoveNext();)yield return null;
            }
            recording=false;
            // Real hitboxes, damage and guard mitigation, not just animation playback.
            foreach(var p in Object.FindObjectsByType<Pushbox>())p.enabled=false;
            // 0.9 centre distance: the measured jab reach (CombatContacts doc) and inside the 3D AI attack range of 1.0.
            Position(-.45f,.45f);ph.ResetHealth();eh.ResetHealth();
            int hp=ph.CurrentHealth;Check(enemyF.TryAttack(CombatMove.Jab),key+" live jab accepted");
            for(var w=Wait(.85f);w.MoveNext();)yield return null;Check(ph.CurrentHealth<hp,key+" live attack damages player");
            for(var w=Wait(.6f);w.MoveNext();)yield return null;
            Position(-.45f,.45f);ph.ResetHealth();eh.ResetHealth();Check(enemyF.TryGuard(false),key+" live guard cast");
            bool guardedHit=false;Action<bool> onHit=g=>{if(g)guardedHit=true;};eh.OnDamaged+=onHit;
            for(var w=Wait(.4f);w.MoveNext();)yield return null;hp=eh.CurrentHealth;playerF.TryAttack(CombatMove.Jab);
            for(var w=Wait(.8f);w.MoveNext();)yield return null;eh.OnDamaged-=onHit;int chip=hp-eh.CurrentHealth;
            Check(guardedHit&&chip==0,key+" live guard blocks the player's jab (blocked hits deal 0): guarded="+guardedHit+" damage="+chip);
            enemyF.ReleaseGuard();
            for(var w=Wait(1.2f);w.MoveNext();)yield return null;
            // Retain actual stage AI for a brief autonomous approach check.
            Position(-2,2);ph.ResetHealth();eh.ResetHealth();float before=eb.position.x;enemy.enabled=true;
            for(var w=Wait(1.2f);w.MoveNext();)yield return null;enemy.enabled=false;enemyF.CancelAttack();Check(eb.position.x<before-.1f,key+" stage AI approaches with new visual");
            Position(-1.6f,1.6f);ph.ResetHealth();eh.ResetHealth();
            for(var w=Wait(.4f);w.MoveNext();)yield return null;recording=true;
            eh.TakeDamage(eh.MaxHealth+100,Vector2.zero);yield return null;Check(view.CurrentState=="KO",key+" KO plays");
            for(var w=Wait(.95f);w.MoveNext();)yield return null;if(cam&&view)Pose("KO");recording=false;
            float until=Time.realtimeSinceStartup+8;while(RunState.Instance.HasPendingBattle&&Time.realtimeSinceStartup<until)yield return null;
            Check(RunState.Instance.LastBattleWon&&!RunState.Instance.HasPendingBattle,key+" victory resolves progression");
            if(id!="final")Check(RunState.Instance.Data.clearedNodeIds.Contains(id),key+" stage marked cleared");else Check(RunState.Instance.Data.finished,key+" final battle marks run finished");
            Time.captureFramerate=0;
        }
    }
}
