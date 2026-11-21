using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Real receiver callbacks and real tutorial classes, without sockets, cameras or saves.</summary>
public static class RedboolPracticeDispatchSelfCheck
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    const long Ms=1000000;
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Field(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static object Read(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    static void Property(object target,string name,object value)=>target.GetType().GetProperty(name).SetValue(target,value);
    static RedboolPracticeInput Input(string action,long source,long now,int generation=1,RedboolPracticeInputKind kind=RedboolPracticeInputKind.State)
        =>new RedboolPracticeInput(kind,action,1,"NEUTRAL",0,"dispatch",generation,source,source,now,source+150*Ms);
    static void Dispatch(RedboolDemoReceiver receiver,RedboolPracticeInput input)
        =>typeof(RedboolDemoReceiver).GetMethod("NotifyPractice",Private).Invoke(receiver,new object[]{input.Action,input.Confidence,input.Movement,input.Strength,input});
    static void ArmReceiver(RedboolDemoReceiver receiver,long source)
    {
        Property(receiver,"StreamId","dispatch");Property(receiver,"CalibrationGeneration",1);Property(receiver,"BlockActive",true);
        Field(receiver,"sourceCaptureNs",source);Field(receiver,"rawSourceCaptureNs",source);Field(receiver,"deadline",source+150*Ms);
        Field(receiver,"hasState",true);Field(receiver,"lastCalibrated",true);Field(receiver,"controlling",true);
        Field(receiver,"tutorialInputValid",true);Field(receiver,"targetMove",1f);Field(receiver,"currentMove",1f);
    }
    public static void Run()
    {
        checks=0;var previousRun=RunState.Instance;
        var root=new GameObject("REDBOOL offline practice dispatch fixture");root.SetActive(false);
        Action<RedboolPracticeInput> caveHandler=null,typed=null;
        Action<string,float,string,float> legacy=null;
        try
        {
            var run=root.AddComponent<RunState>();Property(run,"Data",new RunData{tutorialStep=4});
            var cave=root.AddComponent<CaveTutorial>();Field(cave,"run",run);Field(cave,"demoUntil",-1000f);
            foreach(string name in new[]{"signal","status","progress"})
            {var label=new GameObject(name,typeof(RectTransform));label.SetActive(false);label.transform.SetParent(root.transform);Field(cave,name,label.AddComponent<Text>());}
            caveHandler=(Action<RedboolPracticeInput>)Delegate.CreateDelegate(typeof(Action<RedboolPracticeInput>),cave,typeof(CaveTutorial).GetMethod("OnVisionInput",Private));
            int oldCalls=0,newCalls=0;legacy=(a,c,m,s)=>oldCalls++;typed=s=>newCalls++;
            RedboolDemoReceiver.TutorialInput+=caveHandler;RedboolDemoReceiver.TutorialInput+=typed;RedboolDemoReceiver.TutorialAction+=legacy;
            var receiver=root.AddComponent<RedboolDemoReceiver>();
            Check((bool)typeof(RedboolDemoReceiver).GetProperty("Practicing",Private).GetValue(receiver),"Receiver detects typed story practice without a player");
            long now=RedboolPracticeContinuity.ClockNs(),source=now-150*Ms;
            Dispatch(receiver,Input("IDLE",source,now-41*Ms));ArmReceiver(receiver,source);
            typeof(RedboolDemoReceiver).GetMethod("Safe",Private).Invoke(receiver,new object[]{true});
            Check(!receiver.LiveReady&&!receiver.BlockActive&&(float)Read(receiver,"targetMove")==0&&(float)Read(receiver,"currentMove")==0,
                "Expired receiver releases live guard and both movement values even while tutorial memory is paused");
            Dispatch(receiver,Input("BLOCK",source+90*Ms,RedboolPracticeContinuity.ClockNs()));
            Check(cave.SuccessfulAttempts==1,"Real typed receiver dispatch preserves only story block arming across a short lease gap");
            Check(oldCalls==3&&newCalls==3,"Compatibility listener and typed listener each see one callback; story credit occurs once");
            Dispatch(receiver,Input("UNAVAILABLE",source+90*Ms,RedboolPracticeContinuity.ClockNs(),2,RedboolPracticeInputKind.Reset));
            Dispatch(receiver,Input("BLOCK",source+90*Ms,RedboolPracticeContinuity.ClockNs(),2));
            Check(cave.SuccessfulAttempts==1,"Calibration reset cannot credit another held story block");
            RedboolDemoReceiver.TutorialInput-=caveHandler;RedboolDemoReceiver.TutorialInput-=typed;RedboolDemoReceiver.TutorialAction-=legacy;
            caveHandler=null;typed=null;legacy=null;

            receiver.PvpOwned=true;var practice=new RedboolPractice();for(int i=0;i<4;i++)practice.Skip();
            receiver.PracticeInput+=practice.ObserveInput;int pvpOld=0,pvpNew=0;
            receiver.PracticeAction+=(a,c,m,s)=>pvpOld++;receiver.PracticeInput+=s=>pvpNew++;
            Check((bool)typeof(RedboolDemoReceiver).GetProperty("Practicing",Private).GetValue(receiver),"Receiver detects typed PvP practice independently of story listeners");
            now=RedboolPracticeContinuity.ClockNs();source=now-150*Ms;
            Dispatch(receiver,Input("IDLE",source,now-41*Ms));ArmReceiver(receiver,source);
            typeof(RedboolDemoReceiver).GetMethod("Safe",Private).Invoke(receiver,new object[]{true});
            Dispatch(receiver,Input("BLOCK",source+90*Ms,RedboolPracticeContinuity.ClockNs()));
            Check(practice.Hits==1&&pvpOld==3&&pvpNew==3,"Real receiver delivers one PvP attempt despite a compatibility listener and an output-lease gap");
            receiver.ClearInput();Dispatch(receiver,Input("BLOCK",source+90*Ms,RedboolPracticeContinuity.ClockNs()));
            Check(practice.Hits==1,"Explicit receiver disconnect removes private tutorial arming");
            Check(!receiver.Bound&&!root.activeInHierarchy,"Dispatch fixture never opened a socket or activated game components");
        }
        finally
        {
            if(caveHandler!=null)RedboolDemoReceiver.TutorialInput-=caveHandler;
            if(typed!=null)RedboolDemoReceiver.TutorialInput-=typed;
            if(legacy!=null)RedboolDemoReceiver.TutorialAction-=legacy;
            Object.DestroyImmediate(root);
        }
        Check(RunState.Instance==previousRun,"Story singleton and campaign save ownership remain unchanged");
        Debug.Log("REDBOOL PRACTICE DISPATCH PASS "+checks+" checks; no sockets, cameras, scene changes or campaign writes");
    }
}
