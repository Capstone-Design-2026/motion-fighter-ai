using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual scene fighters, production visual replacement and real Animators.
// No physical camera, room, calibration bypass in the game, or saved scene edits.
[InitializeOnLoad]
public static class RedboolGuardResumeCheck
{
    const string Active="RedboolGuardResumeCheck.Active", SaveKey="VisionStriker.Run";
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly List<string> checks=new List<string>();
    static IEnumerator flow; static int frame=-1;
    static string Output=>Environment.GetEnvironmentVariable("REDBOOL_GUARD_RESUME_CHECK_OUTPUT");
    [Serializable] sealed class Result { public bool passed;public string error;public string[] checks;public bool physical_camera_used=false,online_room_used=false,frame_time_benchmark=false; }
    static RedboolGuardResumeCheck(){if(SessionState.GetBool(Active,false))Subscribe();}
    public static void Run()
    {
        if(!Application.isBatchMode||string.IsNullOrEmpty(Output)||Array.IndexOf(Environment.GetCommandLineArgs(),"--redbool-external-camera")<0)
            throw new Exception("Batch output and external-camera flag required");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
        SessionState.SetFloat(Active+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        SessionState.SetBool(Active,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Active+".Start",0)>240){Finish("Timeout");return;}
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish(null);}catch(Exception e){Finish(e.ToString());}
    }
    static void Check(bool ok,string label)
    {checks.Add((ok?"PASS ":"FAIL ")+label);File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);if(!ok)throw new Exception(label);}
    static IEnumerator Wait(float seconds){float until=Time.time+seconds;while(Time.time<until)yield return null;}
    static void Late(Fighter3DView view)=>typeof(Fighter3DView).GetMethod("LateUpdate",Private).Invoke(view,null);
    static float Started(Fighter3DView view)=>(float)typeof(Fighter3DView).GetField("stateStarted",Private).GetValue(view);
    static bool AnimatorTargets(Animator a,string state)
    {int hash=Animator.StringToHash(state);return a.GetCurrentAnimatorStateInfo(0).shortNameHash==hash||(a.IsInTransition(0)&&a.GetNextAnimatorStateInfo(0).shortNameHash==hash);}
    static void Isolate(Fighter f,float x)
    {
        f.CancelAttack();f.ReleaseGuard();f.InputLocked=false;f.SetVisionPreparationLock(false);f.Health.ClearStun();
        var body=f.GetComponent<Rigidbody2D>();if(body!=null){body.simulated=false;body.linearVelocity=Vector2.zero;}
        var p=f.transform.position;p.x=x;f.transform.position=p;
        f.GetComponent<Fighter3DView>().ResetPresentation();
    }
    static IEnumerator Exercise(Fighter f,Fighter other,string label)
    {
        var view=f.GetComponent<Fighter3DView>();var anim=view.modelAnimator;
        Check(anim!=null&&anim.avatar!=null&&anim.avatar.isValid&&anim.isHuman,label+" real humanoid animator");
        int hp=f.Health.CurrentHealth,otherHp=other.Health.CurrentHealth,serial=f.AttackSerial;
        foreach(bool low in new[]{false,true})
        {
            f.CancelAttack();f.ReleaseGuard();view.ResetPresentation();other.DeclareIntent(low?CombatMove.LowKick:CombatMove.Jab);
            string pose=low?"GUARD_LOWKICK":"GUARD";
            Check(f.SetVisionGuard(true),label+" "+pose+" actual guard accepts fresh input");
            for(var w=Wait(.4f);w.MoveNext();)yield return null;
            Check(f.IsGuarding&&view.CurrentState==pose+"_HOLD"&&AnimatorTargets(anim,pose+"_HOLD"),label+" "+pose+" enters actual hold");
            f.SetVisionGuard(false);
            Check(!f.IsGuarding&&!f.IsVisionGuardHeld,label+" "+pose+" false input releases gameplay immediately");
            Late(view);Check(view.CurrentState==pose+"_RELEASE",label+" "+pose+" visual release starts immediately");
            for(var w=Wait(.075f);w.MoveNext();)yield return null;
            Check(Time.time-Started(view)<.252f,label+" "+pose+" native fixture remains inside release");
            var hand=anim.GetBoneTransform(HumanBodyBones.LeftHand);var before=hand.localRotation;
            Check(f.SetVisionGuard(true),label+" "+pose+" fresh guard restores actual permission");
            Late(view);
            Check(view.CurrentState==pose+"_HOLD",label+" "+pose+" resumes hold without restarting entry");
            Check(Quaternion.Angle(before,hand.localRotation)<.01f,label+" "+pose+" CrossFade schedules without directly snapping bones");
            float started=Started(view);
            for(var w=Wait(.20f);w.MoveNext();)yield return null;
            Check(AnimatorTargets(anim,pose+"_HOLD")&&view.CurrentState==pose+"_HOLD",label+" "+pose+" actual animator completes resume target");
            Check(Started(view)==started&&anim.speed==1f,label+" "+pose+" held updates do not restart or freeze animator");
            Check(f.AttackSerial==serial&&f.Health.CurrentHealth==hp&&other.Health.CurrentHealth==otherHp,label+" "+pose+" resume emits no attack or damage");
            f.SetVisionGuard(false);Late(view);
            for(var w=Wait(.30f);w.MoveNext();)yield return null;
            Check(view.CurrentState=="IDLE"&&!f.IsGuarding,label+" "+pose+" absent new input completes release");
            Check(f.SetVisionGuard(true),label+" "+pose+" fresh input after long release accepted");Late(view);
            Check(view.CurrentState==pose,label+" "+pose+" completed release retains ordinary entry");
            f.SetVisionGuard(false);other.ClearIntent();
            for(var w=Wait(.35f);w.MoveNext();)yield return null;
        }
        int interrupted=0;Action onInterrupt=()=>interrupted++;f.AttackInterruptedByDamage+=onInterrupt;
        try
        {
            Check(f.TryAttack(CombatMove.Jab),label+" actual normal attack begins after release");float start=f.AttackStarted;
            Check(!f.SetVisionGuard(true)&&f.IsAttacking&&f.AttackStarted==start,label+" guard cannot cancel active attack");Late(view);
            Check(view.CurrentState=="JAB"&&!f.IsGuarding&&interrupted==0,label+" attack presentation and interruption count preserved");
            f.CancelAttack();f.ClearIntent();f.SetVisionGuard(false);view.ResetPresentation();
        }
        finally{f.AttackInterruptedByDamage-=onInterrupt;}
        Check(f.Health.CurrentHealth==hp&&other.Health.CurrentHealth==otherHp,label+" complete check leaves both health values unchanged");
    }
    static IEnumerator Tests()
    {
        yield return null;yield return null;
        while(Object.FindAnyObjectByType<RedboolRenderWarmup>()!=null)yield return null;
        foreach(var r in Object.FindObjectsByType<RedboolDemoReceiver>(FindObjectsSortMode.None))r.enabled=false;
        foreach(var r in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None))r.enabled=false;
        foreach(var g in Object.FindObjectsByType<RedboolBattlePreparation>(FindObjectsSortMode.None))g.enabled=false;
        foreach(var b in Object.FindObjectsByType<BattleSceneBridge>(FindObjectsSortMode.None))Object.Destroy(b);
        var player=Object.FindAnyObjectByType<PlayerController>();var enemy=Object.FindAnyObjectByType<EnemyAI>();
        Check(player!=null&&enemy!=null,"Actual ScoutPass player and scout exist");player.enabled=false;enemy.enabled=false;
        foreach(var ai in Object.FindObjectsByType<FighterAI>(FindObjectsSortMode.None))ai.enabled=false;
        var hero=player.GetComponent<Fighter>();var scout=enemy.GetComponent<Fighter>();
        foreach(var s in Object.FindObjectsByType<SpecialMove>(FindObjectsSortMode.None))s.enabled=false;
        foreach(var s in Object.FindObjectsByType<EnemySpecial>(FindObjectsSortMode.None))s.enabled=false;
        Time.timeScale=1;Isolate(hero,-10);Isolate(scout,10);yield return null;
        for(var e=Exercise(hero,scout,"Story ScoutPass Hero02");e.MoveNext();)yield return null;
        for(var e=Exercise(scout,hero,"Story ScoutPass scout");e.MoveNext();)yield return null;
        scout.gameObject.SetActive(false);
        // Same native player-clone and visual-replacement operations as PvP setup,
        // without launching a room, receivers, tutorial or match controller.
        var second=Object.Instantiate(player.gameObject).GetComponent<PlayerController>();second.enabled=false;
        var p2=second.GetComponent<Fighter>();var p2view=p2.GetComponent<Fighter3DView>();
        var visual=RedboolPvpRoster.Visual(RedboolPvpRoster.DefaultSecond);
        Check(visual!=null&&p2view.ApplyVisual(visual),"Actual default PvP second avatar applies through production method");
        RedboolPvpJab.Apply(p2view);Isolate(hero,-10);Isolate(p2,10);yield return null;
        for(var e=Exercise(hero,p2,"PvP P1 Hero02 avatar");e.MoveNext();)yield return null;
        for(var e=Exercise(p2,hero,"PvP P2 Cheolmun avatar");e.MoveNext();)yield return null;
        Object.Destroy(second.gameObject);
        Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Campaign preference unchanged before restoration");
        Check(!RedboolPvp.Instance&&Object.FindAnyObjectByType<RedboolViewLink>()==null,"No online room or view peer created");
    }
    static void Finish(string error)
    {
        SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Time.timeScale=1;
        if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"result.json"),JsonUtility.ToJson(new Result{passed=error==null,error=error,checks=checks.ToArray()},true));
        EditorApplication.Exit(error==null?0:1);
    }
}
