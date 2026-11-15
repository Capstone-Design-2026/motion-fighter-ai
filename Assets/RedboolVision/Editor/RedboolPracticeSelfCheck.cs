using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public static class RedboolPracticeSelfCheck
{
    [Serializable] class Sample {public double t;public string action,movement;public float strength;}
    [Serializable] class Case {public int step;public double oldDelay;public Sample[] samples;}
    [Serializable] class Fixture {public Case[] cases;}
    static int checks;
    static void Check(bool value,string why) {checks++;if(!value)throw new Exception(why);}
    public static void Run()
    {
        var lines=new List<string>();
        var fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText("Tools/DualScreen/tests/practice_returns.json"));
        foreach(var c in fixture.cases)
        {
            double now=0;var p=new RedboolPractice(()=>now);
            while(p.Step<c.step)p.Skip();
            string motion=c.step==5||c.step==6?"BUSY":RedboolPractice.Actions[c.step];
            string move=c.step==5?"LEFT":c.step==6?"RIGHT":"NEUTRAL";
            for(int i=0;i<2;i++) {p.Observe("IDLE",1,"NEUTRAL",0);p.Observe(motion,1,move,.8f);}
            Check(p.Hits==2,"Replay seeded with two accepted real actions");
            foreach(var s in c.samples) {now=s.t;p.Tick();p.Observe(s.action,1,s.movement,s.strength);if(p.Step>c.step)break;}
            Check(p.Step>c.step,"Recorded return advances "+c.step);
            Check(now<=c.oldDelay+.1,"No slower recorded return "+c.step);
            lines.Add(RedboolPractice.Actions[c.step]+" old="+c.oldDelay.ToString("F2")+"s new="+now.ToString("F2")+"s");
        }
        double t=0;var block=new RedboolPractice(()=>t);for(int i=0;i<4;i++)block.Skip();
        block.Observe("IDLE",1,"NEUTRAL",0);
        for(int i=0;i<100;i++){t+=.03;block.Observe("BLOCK",1,"NEUTRAL",0);block.Tick();}
        Check(block.Hits==1,"A held block cannot count twice");
        block.Observe("BUSY",1,"NEUTRAL",0);block.Observe("BLOCK",1,"NEUTRAL",0);
        t+=1;block.Tick();Check(block.Step==4,"Second held block requires actual release");
        block.Observe("UNAVAILABLE",1,"NEUTRAL",0);t+=1;block.Tick();Check(block.Step==4,"Lost camera cannot fake block release");
        block.Observe("BUSY",1,"NEUTRAL",0);t+=.16;block.Observe("BUSY",1,"NEUTRAL",0);
        Check(block.Step==5,"Block release works while attack FSM is PREPARING");
        lines.Insert(0,"PASS "+checks+" checks; recorded return timing only, not new recognition accuracy");
        string output=Environment.GetEnvironmentVariable("REDBOOL_PRACTICE_CHECK_OUTPUT");
        if(!String.IsNullOrEmpty(output)) File.WriteAllLines(output,lines);Debug.Log(String.Join("\n",lines));
    }
}
