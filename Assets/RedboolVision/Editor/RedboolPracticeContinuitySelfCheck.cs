using System;
using UnityEngine;

public static class RedboolPracticeContinuitySelfCheck
{
    const long Ms=1000000;
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static RedboolPracticeInput Sample(long capture,long now,string action="IDLE",string movement="NEUTRAL",
        RedboolPracticeInputKind kind=RedboolPracticeInputKind.State,string stream="one",int generation=1,long offset=0)
        =>new RedboolPracticeInput(kind,action,1,movement,movement=="NEUTRAL"?0:1,stream,generation,
            capture*Ms,(capture+offset)*Ms,now*Ms,(capture+offset+150)*Ms);
    static RedboolPracticeInput Pause(long capture,long now)=>Sample(capture,now,"UNAVAILABLE","NEUTRAL",RedboolPracticeInputKind.Pause);
    public static void Run()
    {
        checks=0;var c=new RedboolPracticeContinuity();
        Check(c.Observe(Sample(1000,1109),true)==RedboolPracticeContinuity.Result.FreshReset,"First proven sample begins fresh memory");
        Check(c.Observe(Pause(1000,1150),false)==RedboolPracticeContinuity.Result.Paused&&c.RestNs==41*Ms,"Only the original41ms lease is counted before expiry");
        Check(!c.Fresh(1150*Ms)&&c.PrivateUntilNs==1220*Ms,"Public freshness expires at150ms while private deadline is fixed220ms");
        c.Observe(Pause(1000,1199),false);
        Check(c.RestNs==41*Ms&&c.PrivateUntilNs==1220*Ms,"Repeated stale notifications add neither time nor deadline");
        Check(c.Observe(Sample(1090,1199),true)==RedboolPracticeContinuity.Result.Fresh&&c.RestNs==41*Ms,"Fresh90ms successor resumes only the prior proven rest");
        c.Tick(1240*Ms);Check(c.RestNs==82*Ms&&!c.Fresh(1240*Ms),"Two41ms leases total82ms;49ms stale gap contributes zero");
        Check(c.Tick(1310*Ms)&&!c.Active&&c.RestNs==0,"No successor clears private evidence at source+220ms");

        c.Observe(Sample(1000,1109),true);c.Observe(Pause(1000,1150),false);
        Check(c.Observe(Sample(1000,1155,offset:20),true)==RedboolPracticeContinuity.Result.Paused
            &&!c.Fresh(1155*Ms)&&c.PrivateUntilNs==1220*Ms&&c.RestNs==41*Ms,
            "New clock offset cannot renew the expired lease of the same raw frame");
        c.Reset();c.Observe(Sample(1000,1109),true);c.Observe(Pause(1000,1150),false);
        Check(c.Observe(Sample(1101,1199,offset:-20),true)==RedboolPracticeContinuity.Result.FreshReset&&c.RestNs==0,
            "Raw101ms gap resets even if a new clock offset makes mapped spacing81ms");
        c.Reset();c.Observe(Sample(1000,1109),true);
        Check(c.Observe(Sample(999,1110,offset:20),true)==RedboolPracticeContinuity.Result.Reset&&!c.Active,"Out-of-order raw capture cannot restore private memory");
        foreach(var input in new[]{Sample(1000,1120,stream:"other"),Sample(1000,1120,generation:2)})
        {
            c.Reset();c.Observe(Sample(1000,1109),true);c.Tick(1120*Ms);
            Check(c.Observe(input,false)==RedboolPracticeContinuity.Result.FreshReset&&c.RestNs==0,"Person or calibration generation starts without old credit");
        }
        c.Reset();c.Observe(Sample(1000,1109),true);
        Check(c.Observe(Sample(1000,1150,"JAB",kind:RedboolPracticeInputKind.Event),false)==RedboolPracticeContinuity.Result.Reset&&!c.Active,
            "An expired event cannot credit or leave old rest memory behind");
        c.Observe(Sample(1000,1109),true);
        Check(c.Observe(Sample(1000,1110,kind:RedboolPracticeInputKind.Reset),false)==RedboolPracticeContinuity.Result.Reset&&!c.Active,
            "Genuine invalid input and disconnect hard-reset private timing");
        Check(c.Observe(Sample(0,1109),true)==RedboolPracticeContinuity.Result.Legacy&&!c.Active,"Unproven legacy samples never acquire gap tolerance");

        long presence=1250*Ms;
        for(long now=1150;now<1250;now+=10)
            Check(RedboolPracticeContinuity.PreservePresence(presence,now*Ms,true,true,1000*Ms,1000*Ms,1,1,"STALE","one","one")==presence,
                "Repeated same-frame STALE preserves but never extends original presence: "+now);
        Check(RedboolPracticeContinuity.PreservePresence(presence,1250*Ms,true,true,1000*Ms,1000*Ms,1,1,"STALE","one","one")==0,"Missing successor expires original presence");
        foreach(string tracking in new[]{"LOST","INVALID","STOPPED"})
            Check(RedboolPracticeContinuity.PreservePresence(presence,1150*Ms,true,true,1000*Ms,1000*Ms,1,1,tracking,"one","one")==0,"Genuine tracking loss clears presence: "+tracking);
        Check(RedboolPracticeContinuity.PreservePresence(presence,1150*Ms,true,true,1000*Ms,1090*Ms,1,1,"STALE","one","one")==0,"A different stale source does not inherit presence");
        Check(RedboolPracticeContinuity.PreservePresence(presence,1150*Ms,true,true,1000*Ms,1000*Ms,1,2,"STALE","one","one")==0,"Recalibration clears presence");
        Check(RedboolPracticeContinuity.PreservePresence(presence,1150*Ms,true,false,1000*Ms,1000*Ms,1,1,"STALE","one","one")==0,"Uncalibrated input clears presence");
        Check(RedboolPracticeContinuity.PreservePresence(presence,1150*Ms,true,true,1000*Ms,1000*Ms,1,1,"STALE","one","other")==0,"Another stream cannot borrow presence");

        long clock=0;var practice=new RedboolPractice(()=>clock/1e9,()=>clock);
        for(int i=0;i<4;i++)practice.Skip();
        long cap=1000;
        Action<string,string> fresh=(action,move)=>{clock=(cap+109)*Ms;practice.ObserveInput(Sample(cap,cap+109,action,move));};
        Action pause=()=>{clock=(cap+150)*Ms;practice.ObserveInput(Pause(cap,cap+150));practice.Tick();};
        fresh("IDLE","NEUTRAL");pause();cap+=90;fresh("BLOCK","NEUTRAL");
        Check(practice.Step==4&&practice.Hits==1,"025-like109ms processing/90ms capture can arm and count a fresh block across a lease gap");
        for(int i=0;i<6;i++){pause();cap+=90;fresh("BLOCK","NEUTRAL");}
        Check(practice.Hits==1,"Held block across gaps is still exactly one attempt");
        pause();cap+=90;fresh("IDLE","NEUTRAL");pause();cap+=90;fresh("BLOCK","NEUTRAL");
        Check(practice.Hits==2&&practice.Step==4,"Only actual release then fresh block supplies the second attempt");
        for(int i=0;i<4;i++){pause();cap+=90;fresh("IDLE","NEUTRAL");pause();Check(practice.Step==4,"Stale periods cannot advance the return step: "+i);}
        cap+=90;fresh("IDLE","NEUTRAL");Check(practice.Step==5,"150ms accumulated valid rest advances only on a fresh return sample");
        pause();cap+=90;fresh("BUSY","LEFT");Check(practice.Hits==0,"A completed step cannot leak its armed state into the next step");
        pause();cap+=90;fresh("IDLE","NEUTRAL");pause();cap+=90;fresh("BUSY","LEFT");
        Check(practice.Hits==1,"Fresh center then lean works with bounded short gaps");
        practice.Suspended=true;practice.Tick();practice.Suspended=false;
        cap+=90;fresh("BUSY","LEFT");Check(practice.Hits==1,"Selection suspension cannot carry private arming into resumed practice");

        var unknown=new RedboolPractice(()=>clock/1e9,()=>clock);for(int i=0;i<4;i++)unknown.Skip();
        clock=1109*Ms;unknown.ObserveInput(Sample(1000,1109));unknown.Observe("UNAVAILABLE",0,"NEUTRAL",0);
        clock=1199*Ms;unknown.ObserveInput(Sample(1090,1199,"BLOCK"));Check(unknown.Hits==0,"Legacy source-unknown unavailable still hard-resets arming");

        var old=new RedboolPractice(()=>clock/1e9,()=>clock);var current=new RedboolPractice(()=>clock/1e9,()=>clock);
        for(int i=0;i<4;i++){old.Skip();current.Skip();}cap=1000;
        foreach(string action in new[]{"IDLE","BLOCK","IDLE","BLOCK","IDLE","IDLE","IDLE","IDLE","IDLE","IDLE","IDLE"})
        {
            clock=(cap+5)*Ms;old.Observe(action,1,"NEUTRAL",0);current.ObserveInput(Sample(cap,cap+5,action));old.Tick();current.Tick();
            Check(old.Step==current.Step&&old.Hits==current.Hits,"Fast fresh block/return progression matches existing implementation: "+cap);cap+=33;
        }
        var discrete=new RedboolPractice(()=>clock/1e9,()=>clock);
        clock=1109*Ms;discrete.ObserveInput(Sample(1000,1109));discrete.ObserveInput(Sample(1000,1109,"JAB",kind:RedboolPracticeInputKind.Event));
        clock=1199*Ms;discrete.ObserveInput(Sample(1090,1199));discrete.ObserveInput(Sample(1090,1199,"JAB",kind:RedboolPracticeInputKind.Event));
        clock=1240*Ms;discrete.ObserveInput(Pause(1090,1240));clock=1450*Ms;discrete.Tick();
        Check(discrete.Step==1,"Two previously accepted discrete events retain their existing delayed completion despite later silence");
        clock=1510*Ms;discrete.ObserveInput(Sample(1500,1510,"STRAIGHT",kind:RedboolPracticeInputKind.Event));
        Check(discrete.Hits==1,"A fresh receiver-validated next-step event needs no extra STATE after tutorial advance");
        Debug.Log("REDBOOL PRACTICE CONTINUITY PASS "+checks+" checks; tutorial-only memory, no camera or game packets");
    }
}
