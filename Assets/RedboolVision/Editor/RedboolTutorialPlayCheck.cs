using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Runs the real tutorial UI and UDP receiver, without a camera or a user's game.
[InitializeOnLoad]
public static class RedboolTutorialPlayCheck
{
 const string Active="RedboolTutorialPlayCheck.Active", SaveKey="VisionStriker.Run";
 static IEnumerator flow; static int lastFrame=-1; static UdpClient client;
 static readonly List<string> checks=new List<string>();
 static string Output=>Environment.GetEnvironmentVariable("REDBOOL_TUTORIAL_CHECK_DIR");
 static long sequence; static double nextSync;
 static string state="READY", tracking="VALID", movement="NEUTRAL"; static bool calibrated=true,block;
 static string stream="tutorial-integration-check",sync="tutorial-sync";
 static readonly Dictionary<string,string> replies=new Dictionary<string,string>();
 [Serializable] class Packet {
  public int version=2,ttl_ms=150,calibration_version=1;
  public string kind,stream_id=stream,actor_id="DEV01",capture_epoch=stream,sync_id=sync,event_id,side,gesture,fsm_state,tracking,movement;
  public long sequence,sent_ns,receiver_offset_ns,clock_uncertainty_ns,event_expires_ns;
  public bool calibrated,block_active;public float movement_strength;
 }
 [Serializable] class Ack {public string event_id,status;}
 static long Now {get {long t=System.Diagnostics.Stopwatch.GetTimestamp(),f=System.Diagnostics.Stopwatch.Frequency;return t/f*1000000000L+(long)((t%f)*(1000000000.0/f));}}
 static RedboolTutorialPlayCheck(){if(SessionState.GetBool(Active,false))Subscribe();}
 public static void Run(){
  if(!Application.isBatchMode||String.IsNullOrEmpty(Output))throw new Exception("Isolated batch run and output directory required");
  Directory.CreateDirectory(Output);
  SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
  SessionState.SetFloat(Active+".Start",(float)EditorApplication.timeSinceStartup);
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  SessionState.SetBool(Active,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
 }
 static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 static void Tick(){
  if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Active+".Start",0)>150){Finish("Timeout");return;}
  if(!Application.isPlaying||EditorApplication.isCompiling)return; EditorApplication.isPaused = false; // batch: ignore Error Pause from unrelated editor errors
  EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
  try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish(null);}catch(Exception e){Finish(e.ToString());}
 }
 static void Check(bool ok,string message){checks.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);if(!ok)throw new Exception(message);}
 static void Send(Packet p){p.sequence=++sequence;p.sent_ns=Now;var b=Encoding.UTF8.GetBytes(JsonUtility.ToJson(p));client.Send(b,b.Length);}
 static void Drain(){while(client.Available>0){var peer=new IPEndPoint(IPAddress.Any,0);var a=JsonUtility.FromJson<Ack>(Encoding.UTF8.GetString(client.Receive(ref peer)));if(!String.IsNullOrEmpty(a.event_id))replies[a.event_id]=a.status;}}
 static IEnumerator Pump(float seconds){
  double end=Time.realtimeSinceStartupAsDouble+seconds;
  do{
   if(Time.realtimeSinceStartupAsDouble>=nextSync){Send(new Packet{kind="SYNC"});nextSync=Time.realtimeSinceStartupAsDouble+1;}
   Send(new Packet{kind="STATE",calibrated=calibrated,tracking=tracking,fsm_state=state,block_active=block,movement=movement,movement_strength=movement=="NEUTRAL"?0:.8f});
   Drain();yield return null;
  }while(Time.realtimeSinceStartupAsDouble<end);Drain();
 }
 static void Gesture(string gesture,string id){Send(new Packet{kind="GESTURE",gesture=gesture,event_id=id,side=gesture=="SPECIAL"?"BOTH":gesture.StartsWith("LEFT")?"LEFT":"RIGHT",event_expires_ns=Now+150000000L});}
 static IEnumerator Tests(){
  yield return null;yield return null;
  var run=RunState.Instance;
  // Test progression has its own in-memory map; restore the original save on exit.
  var data=new RunData();data.map.nodes.Add(new MapNode{id="cave"});
  typeof(RunState).GetProperty("Data").SetValue(run,data);
  var go=new GameObject("TestReceiver");go.SetActive(false);var receiver=go.AddComponent<RedboolDemoReceiver>();receiver.port=15065;go.SetActive(true);
  client=new UdpClient();client.Connect(IPAddress.Loopback,15065);
  run.StartCoroutine(CaveTutorial.Play(run));yield return null;
  var tutorial=Object.FindAnyObjectByType<CaveTutorial>();Check(tutorial!=null,"Real tutorial created");
  for(var wait=Pump(3);wait.MoveNext();)yield return null;
  // The recorded failure: READY -> CANDIDATE -> ACTIVE -> one-shot punch.
  state="CANDIDATE";for(var wait=Pump(.15f);wait.MoveNext();)yield return null;
  state="ACTIVE";for(var wait=Pump(.08f);wait.MoveNext();)yield return null;
  Gesture("LEFT_PUNCH","first-jab");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1,"Candidate and ACTIVE do not erase first jab (recorded regression)");
  Gesture("LEFT_PUNCH","first-jab");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1,"Same UDP event ID counts once");
  state="RETURNING";for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Gesture("RIGHT_PUNCH","wrong-jab-step");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1,"Wrong technique does not complete step");
  calibrated=false;for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Gesture("LEFT_PUNCH","uncalibrated");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1&&replies["uncalibrated"]=="INPUT_LOCKED","Uncalibrated input cannot count");
  calibrated=true;tracking="LOST";for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Gesture("LEFT_PUNCH","tracking-lost");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1&&replies["tracking-lost"]=="INPUT_LOCKED","Lost tracking cannot count");
  tracking="VALID";for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Gesture("LEFT_PUNCH","second-jab");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==2,"New jab counts without bilateral READY guard");
  for(var wait=Pump(1.4f);wait.MoveNext();)yield return null;
  Check(tutorial.CurrentStep==1,"Jab advances to straight");
  Gesture("RIGHT_PUNCH","during-demo");for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==0,"Demonstration inputs do not count");
  for(int step=1;step<4;step++){
   for(var wait=Pump(3);wait.MoveNext();)yield return null;
   string gesture=step==1?"RIGHT_PUNCH":step==2?"LEFT_RAISE":"RIGHT_RAISE";
   for(int attempt=0;attempt<2;attempt++){
    state="CANDIDATE";for(var wait=Pump(.1f);wait.MoveNext();)yield return null;
    state="ACTIVE";for(var wait=Pump(.08f);wait.MoveNext();)yield return null;
    Gesture(gesture,"step"+step+"-"+attempt);for(var wait=Pump(.12f);wait.MoveNext();)yield return null;
    Check(tutorial.SuccessfulAttempts==attempt+1,gesture+" attempt "+(attempt+1));
    state="RETURNING";
   }
   for(var wait=Pump(1.4f);wait.MoveNext();)yield return null;
   Check(tutorial.CurrentStep==step+1,gesture+" advances");
  }
  state="RETURNING";for(var wait=Pump(3);wait.MoveNext();)yield return null;
  block=true;for(var wait=Pump(.4f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1,"Held block counts once");
  for(var wait=Pump(.4f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==1,"Continuing block does not count twice");
  block=false;for(var wait=Pump(.2f);wait.MoveNext();)yield return null;
  block=true;for(var wait=Pump(.15f);wait.MoveNext();)yield return null;
  Check(tutorial.SuccessfulAttempts==2,"Release then block counts twice without READY");
  block=false;for(var wait=Pump(1.4f);wait.MoveNext();)yield return null;
  Check(tutorial.CurrentStep==5,"Block advances to movement");
  for(int step=5;step<7;step++){
   movement="NEUTRAL";for(var wait=Pump(3);wait.MoveNext();)yield return null;
   movement=step==5?"LEFT":"RIGHT";for(var wait=Pump(.4f);wait.MoveNext();)yield return null;
   Check(tutorial.SuccessfulAttempts==1,"Move "+movement+" counts once while attack FSM is RETURNING");
   for(var wait=Pump(.3f);wait.MoveNext();)yield return null;
   Check(tutorial.SuccessfulAttempts==1,"Held movement does not count twice");
   // A punch event's NEUTRAL field must not masquerade as physical centering.
   Gesture("LEFT_PUNCH","during-move-"+step);for(var wait=Pump(.2f);wait.MoveNext();)yield return null;
   Check(tutorial.SuccessfulAttempts==1,"Attack cannot rearm held movement");
   movement="NEUTRAL";for(var wait=Pump(.2f);wait.MoveNext();)yield return null;
   movement=step==5?"LEFT":"RIGHT";for(var wait=Pump(.15f);wait.MoveNext();)yield return null;
   Check(tutorial.SuccessfulAttempts==2,"Center and move counts second attempt");
   movement="NEUTRAL";for(var wait=Pump(1.4f);wait.MoveNext();)yield return null;
  }
  Check(run.Data.tutorialCompleted&&run.Data.tutorialStep==7,"All seven steps completed through actual UDP");
 }
 static void Finish(string failure){
  SessionState.SetBool(Active,false);EditorApplication.update-=Tick;client?.Close();
  if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);
  PlayerPrefs.Save();
  File.WriteAllText(Path.Combine(Output,"result.txt"),(failure==null?"PASS\n":"FAIL\n"+failure+"\n")+checks.Count+" checks");
  EditorApplication.Exit(failure==null?0:1);
 }
}
