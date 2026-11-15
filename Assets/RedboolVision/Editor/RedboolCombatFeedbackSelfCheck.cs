using System;
using System.Collections.Generic;
using System.IO;
#if !REDBOOL_FEEDBACK_STANDALONE
using UnityEngine;
#endif

/// <summary>Presentation-only regressions. No camera, socket or input replay is started.</summary>
public static class RedboolCombatFeedbackSelfCheck
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);checks.Add(message);}
    static RedboolCombatFeedbackContext Ready() {
        return new RedboolCombatFeedbackContext {Available=true,Calibrated=true,TrackingValid=true,CanAttack=true};
    }
    public static void Run()
    {
        checks.Clear();CheckStates();
#if !REDBOOL_FEEDBACK_STANDALONE
        CheckProtocolAndLocalSlot();CheckDamageCancellationEdge();CheckOperatorReturn();
#endif
        string result="PASS "+checks.Count+" combat feedback checks\n"+string.Join("\n",checks.ToArray());
        string path=Environment.GetEnvironmentVariable("REDBOOL_COMBAT_FEEDBACK_CHECK_OUTPUT");
        if(!string.IsNullOrEmpty(path))File.WriteAllText(path,result);
#if REDBOOL_FEEDBACK_STANDALONE
        Console.WriteLine(result);
#else
        Debug.Log(result);
#endif
    }
#if REDBOOL_FEEDBACK_STANDALONE
    public static void Main() {Run();}
#endif
    static void CheckStates()
    {
        var model=new RedboolCombatFeedback();var state=Ready();
        Check(model.Resolve(state,10)==RedboolCombatFeedbackCode.Ready,"Fresh idle is ready");
        string[] actions={"JAB","STRAIGHT","HIGH_KICK","LOW_KICK","SPECIAL"};
        var expected=new[]{RedboolCombatFeedbackCode.JabStarted,RedboolCombatFeedbackCode.StraightStarted,
            RedboolCombatFeedbackCode.HighKickStarted,RedboolCombatFeedbackCode.LowKickStarted,RedboolCombatFeedbackCode.SpecialStarted};
        for(int i=0;i<actions.Length;i++) {
            model.Acknowledge("APPLIED",actions[i],10);
            Check(model.Resolve(state,10.1)==expected[i],"Accepted "+actions[i]+" reports a start, not a hit");
            Check(model.Resolve(state,11.2)==RedboolCombatFeedbackCode.Ready,"Accepted notice expires: "+actions[i]);
        }
        model.Acknowledge("APPLIED","JAB",10);state.TrackingValid=false;
        Check(model.Resolve(state,10.1)==RedboolCombatFeedbackCode.TrackingLost,"Lost tracking immediately masks accepted notice");
        state.InputDelayed=true;
        Check(model.Resolve(state,10.1)==RedboolCombatFeedbackCode.InputDelayed,"Expired/slow input differs from missing joints");
        state=Ready();state.Calibrated=false;
        Check(model.Resolve(state,10.1)==RedboolCombatFeedbackCode.Preparing,"Recalibration masks old accepted notice");
        state=Ready();state.Preparing=true;
        Check(model.Resolve(state,10.1)==RedboolCombatFeedbackCode.Preparing,"Authoritative preparation blocks success text");
        state=Ready();state.Dead=true;
        Check(model.Resolve(state,10.1)==RedboolCombatFeedbackCode.RoundOver,"Round end is not an input miss");
        state=Ready();model.AttackInterrupted(10.2);state.HitStunned=true;state.CanAttack=false;
        Check(model.Resolve(state,10.3)==RedboolCombatFeedbackCode.Interrupted,"Actual interruption overrides accepted attack");
        Check(model.Resolve(state,11.4)==RedboolCombatFeedbackCode.HitStunned,"Remaining hitstun is explained after notice ends");
        state=Ready();
        Check(model.Resolve(state,11.4)==RedboolCombatFeedbackCode.Ready,"No extra UI wait after stun ends");
        model.AttackInterrupted(12);model.Acknowledge("APPLIED","STRAIGHT",12.5);
        Check(model.Resolve(state,12.6)==RedboolCombatFeedbackCode.StraightStarted,"A real subsequent attack clears stale interruption notice");
        model.Reset();model.Acknowledge("GAME_BUSY","JAB",20);state=Ready();state.Attacking=true;state.CanAttack=false;
        Check(model.Resolve(state,20.1)==RedboolCombatFeedbackCode.Attacking,"Busy active attack is not tracking failure");
        state.Recovering=true;
        Check(model.Resolve(state,20.1)==RedboolCombatFeedbackCode.Recovering,"Recovery identified separately from attack startup");
        state=Ready();state.Guarding=true;state.CanAttack=false;
        Check(model.Resolve(state,20.1)==RedboolCombatFeedbackCode.ReleaseGuard,"Rejected attack while blocking explains release");
        state=Ready();
        Check(model.Resolve(state,20.2)==RedboolCombatFeedbackCode.Ready,"Recovered state does not keep a stale busy warning");
        model.Acknowledge("NOT_CHARGED","SPECIAL",30);
        Check(model.Resolve(state,30.1)==RedboolCombatFeedbackCode.NotCharged,"Uncharged X is explicitly recognized");
        state.HitStunned=true;
        Check(model.Resolve(state,30.1)==RedboolCombatFeedbackCode.HitStunned,"Current hitstun takes priority over charge notice");
        state=Ready();model.Reset();state.Practicing=true;state.Preparing=true;
        Check(model.Resolve(state,40)==RedboolCombatFeedbackCode.None,"Tutorial idle does not show combat-ready claim");
        model.Acknowledge("TUTORIAL_APPLIED","JAB",40);
        Check(model.Resolve(state,40.1)==RedboolCombatFeedbackCode.TutorialRecognized,"Tutorial recognition never claims combat start");
        model.Reset();state=Ready();state.SpecialActive=true;state.CanAttack=false;
        Check(model.Resolve(state,50)==RedboolCombatFeedbackCode.SpecialActive,"Special animation is not a recognition lock");
        model.Acknowledge("BAD_EVENT","JAB",50);
        Check(model.Resolve(state,50.1)==RedboolCombatFeedbackCode.SpecialActive,"Malformed input creates no false success notice");
        model.Reset();state=Ready();state.Available=false;
        Check(model.Resolve(state,60)==RedboolCombatFeedbackCode.None,"Missing receiver has no fabricated feedback");
        for(byte i=0;i<=(byte)RedboolCombatFeedbackCode.ReleaseGuard;i++) {
            string label=RedboolCombatFeedback.Text((RedboolCombatFeedbackCode)i);
            Check(!label.Contains("명중")&&!label.Contains("타격 성공"),"Feedback never equates accepted command with landed hit: "+i);
        }
        Check(!RedboolCombatFeedback.Valid(255),"Unknown remote feedback code rejected");
    }
#if !REDBOOL_FEEDBACK_STANDALONE
    static RedboolViewActor Actor(byte feedback)
    {
        var actor=new RedboolViewActor {scale=Vector3.one,rotation=Quaternion.identity,maxHp=100,hp=100,feedback=feedback};
        for(int i=0;i<actor.bones.Length;i++)actor.bones[i]=Quaternion.identity;
        return actor;
    }
    static void CheckProtocolAndLocalSlot()
    {
        for(byte kind=2;kind<=3;kind++)for(byte code=0;code<=(byte)RedboolCombatFeedbackCode.ReleaseGuard;code++) {
            var packet=new RedboolViewPacket {kind=kind,session=Guid.NewGuid(),nonce=Guid.NewGuid(),seq=1,time=1,actor=Actor(code)};
            byte[] bytes=RedboolViewProtocol.Encode(packet);
            Check(bytes.Length<RedboolViewProtocol.MaxBytes,"Actor "+kind+" stays below MTU budget: "+bytes.Length+" bytes");
            Check(RedboolViewProtocol.TryDecode(bytes,out var result)&&result.actor.feedback==code,"Both authoritative actors preserve feedback "+kind+":"+code);
            bytes[4]=(byte)(RedboolViewProtocol.Version-1);
            Check(!RedboolViewProtocol.TryDecode(bytes,out _),"Pre-feedback peer version is rejected explicitly");
        }
        var invalid=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=2,seq=1,time=1,actor=Actor(255)});
        Check(!RedboolViewProtocol.TryDecode(invalid,out _),"Unknown on-wire feedback code is rejected");
        var frame=new RedboolViewFrame {world=new RedboolViewWorld()};
        frame.actors[0]=Actor((byte)RedboolCombatFeedbackCode.JabStarted);
        frame.actors[1]=Actor((byte)RedboolCombatFeedbackCode.NotCharged);
        Check(RedboolCameraPanel.SelectFeedback(true,frame,true,RedboolCombatFeedbackCode.Ready)==RedboolCombatFeedbackCode.NotCharged,
            "Guest shows P2 authority, never P1 or a local simulated state");
        Check(RedboolCameraPanel.SelectFeedback(false,frame,true,RedboolCombatFeedbackCode.JabStarted)==RedboolCombatFeedbackCode.JabStarted,
            "Host/story uses its own receiver");
        Check(RedboolCameraPanel.SelectFeedback(true,frame,false,RedboolCombatFeedbackCode.JabStarted)==RedboolCombatFeedbackCode.ViewLost,
            "Lost view does not preserve previous success");
        frame.world.resolved=true;
        Check(RedboolCameraPanel.SelectFeedback(true,frame,true,RedboolCombatFeedbackCode.Ready)==RedboolCombatFeedbackCode.RoundOver,
            "Guest winner and loser both see ended round");
    }
    static void CheckDamageCancellationEdge()
    {
        var go=new GameObject("FeedbackCancellationFixture");
        try {
            var fighter=go.AddComponent<Fighter>();
            int count=0;fighter.AttackInterruptedByDamage+=()=>count++;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var attack=typeof(Fighter).GetField("<IsAttacking>k__BackingField",flags);
            var interrupt=typeof(Fighter).GetMethod("InterruptAttack",flags);
            attack.SetValue(fighter,true);interrupt.Invoke(fighter,new object[]{false});
            Check(!fighter.IsAttacking&&count==1,"Actual damage interruption cancels then emits exactly one presentation edge");
            interrupt.Invoke(fighter,new object[]{false});
            Check(count==1,"Damage while already idle does not claim interrupted attack");
            attack.SetValue(fighter,true);fighter.CancelAttack();
            Check(count==1&&!fighter.IsAttacking,"Ordinary completion/preparation cancellation is not labelled a hit");
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    static void CheckOperatorReturn()
    {
        bool oldOperator=RedboolDemoSettings.OperatorMode,oldKeyboard=RedboolBattlePreparation.KeyboardTest;
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        try {
            typeof(RedboolBattlePreparation).GetMethod("InstallHook",flags).Invoke(null,null);
            RedboolDemoSettings.OperatorMode=true;RedboolBattlePreparation.SetKeyboardTest(true);
            Check(RedboolBattlePreparation.KeyboardTest,"Operator can explicitly select keyboard rehearsal");
            RedboolDemoSettings.OperatorMode=false;
            Check(!RedboolBattlePreparation.KeyboardTest,"Operator OFF immediately returns battle to camera mode");
            RedboolBattlePreparation.SetKeyboardTest(true);
            RedboolDemoSettings.OperatorMode=false;
            Check(RedboolBattlePreparation.KeyboardTest,"False-to-false setting does not override direct/command-line fixture mode");
            typeof(RedboolBattlePreparation).GetMethod("OnSettingsChanged",flags).Invoke(null,null);
            Check(RedboolBattlePreparation.KeyboardTest,"Unrelated setting notifications retain explicit fixture mode");
        }
        finally {RedboolDemoSettings.OperatorMode=oldOperator;RedboolBattlePreparation.SetKeyboardTest(oldKeyboard);}
    }
#endif
}
