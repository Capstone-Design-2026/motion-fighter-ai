using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class RedboolPvpPlayCheck
{
 const string Active="RedboolPvpPlayCheck.Active", SaveKey="VisionStriker.Run";
 static System.Diagnostics.Process guest;
 static string GuestBinary=>Environment.GetEnvironmentVariable("REDBOOL_DUAL_CLIENT");
 static void StartGuest(){
  var info=new System.Diagnostics.ProcessStartInfo(GuestBinary,"--redbool-pvp-client 127.0.0.1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \""+Path.Combine(Output,"guest.log")+"\"") {UseShellExecute=false};
  info.EnvironmentVariables["REDBOOL_VIEW_TRACE_FILE"]=Path.Combine(Output,"guest-"+DateTime.UtcNow.Ticks+".jsonl");
  info.EnvironmentVariables["REDBOOL_VIEW_CAPTURE_DIR"]=Output;
  guest=System.Diagnostics.Process.Start(info);
 }
 static void StopGuest(){if(guest!=null){if(!guest.HasExited){guest.Kill();guest.WaitForExit(5000);}guest.Dispose();guest=null;}}
 static IEnumerator flow;static int frame=-1;static readonly UdpClient[] clients=new UdpClient[2];
 static readonly long[] sequences=new long[2];static readonly double[] nextSync=new double[2];
 static readonly bool[] calibrated={true,true},block={false,false},send={true,true};
 static readonly string[] phases={"READY","READY"};
 static readonly string[] tracking={"VALID","VALID"};
 static readonly string[] movement={"NEUTRAL","NEUTRAL"};static readonly int[] generation={1,1};
 static readonly Dictionary<string,Ack> replies=new Dictionary<string,Ack>();
 static readonly List<string> checks=new List<string>();
 static string Output=>Environment.GetEnvironmentVariable("REDBOOL_PVP_CHECK_OUTPUT");
 static RedboolPvp Game=>RedboolPvp.Instance;
 static long Now{get{long t=System.Diagnostics.Stopwatch.GetTimestamp(),f=System.Diagnostics.Stopwatch.Frequency;return t/f*1000000000L+(long)((t%f)*(1000000000.0/f));}}
 [Serializable] class Packet {
  public int version=2,ttl_ms=150,calibration_version=1,calibration_generation;
  public string kind,stream_id,actor_id,capture_epoch,sync_id="pvp-check",event_id,side,gesture,fsm_state="READY",tracking="VALID",movement="NEUTRAL",personal_status="BASELINE V26";
  public long sequence,sent_ns,receiver_offset_ns,clock_uncertainty_ns,event_expires_ns;
  public bool calibrated=true,block_active;public float movement_strength;
 }
 [Serializable] class Ack{public string event_id,status,player_id,tutorial_action,tutorial_phase;}
 static RedboolPvpPlayCheck(){if(SessionState.GetBool(Active,false))Subscribe();}
 public static void Run(){
  if(!Application.isBatchMode||String.IsNullOrEmpty(Output))throw new Exception("Isolated batch output required");
  Directory.CreateDirectory(Output);SessionState.SetBool(Active+".HadSave",PlayerPrefs.HasKey(SaveKey));SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));
  SessionState.SetFloat(Active+".Start",(float)EditorApplication.timeSinceStartup);
  EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");SessionState.SetBool(Active,true);Subscribe();
  EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
 }
 static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 static void Tick(){
  if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Active+".Start",0)>180){Finish("Timeout");return;}
  if(!Application.isPlaying||EditorApplication.isCompiling)return;EditorApplication.isPaused=false;
  EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
  try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish(null);}catch(Exception e){Finish(e.ToString());}
 }
 static void Check(bool ok,string message){checks.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);if(!ok)throw new Exception(message);}
 static void Send(int slot,Packet p){p.actor_id=p.actor_id??"P"+(slot+1);p.stream_id="pvp-test-"+slot;p.capture_epoch=p.stream_id;p.sequence=++sequences[slot];p.sent_ns=Now;p.calibration_generation=generation[slot];var b=Encoding.UTF8.GetBytes(JsonUtility.ToJson(p));clients[slot].Send(b,b.Length);}
 static void Drain(){for(int i=0;i<2;i++)while(clients[i]!=null&&clients[i].Available>0){var peer=new IPEndPoint(IPAddress.Any,0);var a=JsonUtility.FromJson<Ack>(Encoding.UTF8.GetString(clients[i].Receive(ref peer)));if(!String.IsNullOrEmpty(a.event_id))replies[a.event_id]=a;}}
 static IEnumerator Pump(float seconds){double end=Time.realtimeSinceStartupAsDouble+seconds;do{
  for(int i=0;i<2;i++)if(send[i]){if(Time.realtimeSinceStartupAsDouble>=nextSync[i]){Send(i,new Packet{kind="SYNC"});nextSync[i]=Time.realtimeSinceStartupAsDouble+1;}
   Send(i,new Packet{kind="STATE",fsm_state=phases[i],tracking=tracking[i],calibrated=calibrated[i],block_active=block[i],movement=movement[i],movement_strength=movement[i]=="NEUTRAL"?0:.8f});}
  Drain();yield return null;
 }while(Time.realtimeSinceStartupAsDouble<end);Drain();}
 static void Gesture(int slot,string gesture,string id,bool expired=false,string actor=null){Send(slot,new Packet{kind="GESTURE",gesture=gesture,event_id=id,side=gesture=="SPECIAL"?"BOTH":gesture.StartsWith("LEFT")?"LEFT":"RIGHT",event_expires_ns=Now+(expired?-1:150000000L),actor_id=actor});}
 static IEnumerator Practice(int first=0,int last=1){
  string[] gestures={"LEFT_PUNCH","RIGHT_PUNCH","LEFT_RAISE","RIGHT_RAISE"};
  for(int step=0;step<8;step++){
   for(var w=Pump(.4f);w.MoveNext();)yield return null;
   for(int attempt=0;attempt<2;attempt++){
    for(int i=first;i<=last;i++){
     if(step<4)Gesture(i,gestures[step],"practice-"+generation[i]+"-"+i+"-"+step+"-"+attempt);
     else if(step==4)block[i]=true;
     else if(step<7)movement[i]=step==5?"LEFT":"RIGHT";
     else Gesture(i,"SPECIAL","practice-x-"+generation[i]+"-"+i+"-"+attempt);
    }
    for(var w=Pump(.15f);w.MoveNext();)yield return null;
    for(int i=first;i<=last;i++){block[i]=false;movement[i]="NEUTRAL";}
    for(var w=Pump(.5f);w.MoveNext();)yield return null;
   }
   for(int i=first;i<=last;i++) {
    string detail="";
    if(step<4||step==7)for(int attempt=0;attempt<2;attempt++) {
     string id=(step==7?"practice-x-":"practice-")+generation[i]+"-"+i+"-"+(step==7?"":step+"-")+attempt;
     detail+=" ack"+attempt+"="+(replies.TryGetValue(id,out var response)?response.status:"MISSING");
    }
    Check(Game.Practices[i].Step==step+1,"P"+(i+1)+" practice step "+(step+1)+" independently completed"+detail);
   }
  }
 }
 static void Shot(string name){
  var camera=Camera.main;var rt=new RenderTexture(1280,720,24);
  var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);var overlays=new List<Canvas>();
  foreach(var c in canvases)if(c.renderMode==RenderMode.ScreenSpaceOverlay){overlays.Add(c);c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
  UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});
  var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
  File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());RenderTexture.active=old;Object.Destroy(image);rt.Release();Object.Destroy(rt);
  foreach(var c in overlays){c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}
 }
 static IEnumerator Tests(){
  yield return null;yield return null;
  Check(GameObject.Find("PvpButton")!=null,"Menu exposes PvP entry");
  string save=PlayerPrefs.GetString(SaveKey,"");
  if(!String.IsNullOrEmpty(GuestBinary)){RedboolPvp.HostDual();StartGuest();}else RedboolPvp.Begin();yield return null;yield return null;yield return null;
  Check(SceneManager.GetActiveScene().name==RedboolPvp.Arena,"Both humans use the PvP arena ("+RedboolPvp.Arena+")");
  Check(Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length==2,"Exactly two human controllers");
  Check(Object.FindAnyObjectByType<EnemyAI>()==null,"No active AI opponent in PvP");
  Check(!Object.FindAnyObjectByType<BattleSceneBridge>().enabled,"Campaign bridge disabled before Start");
  Check(Game.Players[0].GetComponent<Fighter3DView>().modelAnimator.isHuman && Game.Players[1].GetComponent<Fighter3DView>().modelAnimator.isHuman,"Both use humanoid character animations");
  Check(Game.Players[1].GetComponent<Fighter3DView>().visualRoot.name.Contains("Cheolmun"),"2P uses authored Cheolmun visual");
  Check(Game.Players[1].GetComponent<Fighter3DView>().modelAnimator.runtimeAnimatorController.name=="RedboolPvpCheolmun","2P uses only the targeted jab extension controller");
  Check(GameObject.Find("PvpSpecialGauge0")!=null &&GameObject.Find("PvpSpecialGauge1")!=null,"Both participants have original-style charge bars");
  Check(GameObject.Find("PvpHealthBar0")!=null &&GameObject.Find("PvpHealthBar1")!=null,"Both participants use story-style health bars");
  for(int i=0;i<2;i++) Check(!new SerializedObject(Game.Players[i].GetComponent<SpecialMove>()).FindProperty("chargeWhenHit").boolValue,"P"+(i+1)+" scene/clone keeps incoming-hit charging disabled");
  Check(Game.Receivers[0].port==5065&&Game.Receivers[1].port==5066,"Separate sockets assigned P1 and P2");
  for(int i=0;i<2;i++){clients[i]=new UdpClient();clients[i].Connect(IPAddress.Loopback,5065+i);}
  for(var w=Pump(.6f);w.MoveNext();)yield return null;
  if(guest!=null) {
   // Starting a native guest can compile shaders for several seconds. Wait for
   // its warmup/complete-snapshot ACKs before emitting the short practice poses.
   // This does not extend any packet lease or bypass an actual practice step.
   double readyDeadline=Time.realtimeSinceStartupAsDouble+12,stableSince=-1;
   bool stable=false;
   while(Time.realtimeSinceStartupAsDouble<readyDeadline) {
    if(Game.ViewLink.PeerReady) {
     if(stableSince<0) stableSince=Time.realtimeSinceStartupAsDouble;
     if(Time.realtimeSinceStartupAsDouble-stableSince>=.5) {stable=true;break;}
    } else stableSince=-1;
    for(var w=Pump(.05f);w.MoveNext();)yield return null;
   }
   Check(stable,"Guest completes startup and acknowledges snapshots stably before short practice fixtures");
  }
  Check(Game.BothLive&&!Game.CanFight,"Both inputs live; tutorial still holds fight");Shot("pvp_practice");
  // Screenshot rendering blocks this editor-only publisher; refresh the input
  // state before sending an event, as the separate camera process normally does.
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Gesture(0,"LEFT_PUNCH","duplicate");for(var w=Pump(.12f);w.MoveNext();)yield return null;
  Gesture(0,"LEFT_PUNCH","duplicate");for(var w=Pump(.12f);w.MoveNext();)yield return null;
  Check(Game.Practices[0].Hits==1&&Game.Practices[1].Hits==0,"Event deduplication and player isolation: hits="+Game.Practices[0].Hits+" other="+Game.Practices[1].Hits+" ack="+(replies.TryGetValue("duplicate",out var duplicateAck)?duplicateAck.status:"MISSING"));
  // Reset initial test sample through the actual recalibration generation.
  Game.SkipPractice(1,"stale-run",0);Check(Game.Practices[1].Step==0,"Stale skip request cannot advance a tutorial");
  Game.SkipPractice(1,Game.Practices[1].Run,0);Check(Game.Practices[1].Step==1 && !Game.CanFight,"Explicit skip keeps battle gated and does not calibrate");
  generation[1]++;generation[0]++;for(var w=Pump(.2f);w.MoveNext();)yield return null;
  for(var w=Practice();w.MoveNext();)yield return null;
  phases[0]=phases[1]="PREPARING";block[0]=true;
  for(var w=Pump(1.3f);w.MoveNext();)yield return null;
  Check(Game.CanFight,"Both complete + fresh cameras opens one shared round without a second strict arm READY gate");
  phases[0]=phases[1]="READY";block[0]=false;for(var w=Pump(.15f);w.MoveNext();)yield return null;
  if(guest!=null) {
   Check(Game.ViewLink.PeerReady,"Real second game acknowledges complete snapshots");
   StopGuest();for(var w=Pump(.4f);w.MoveNext();)yield return null;
   Check(!Game.CanFight,"Second screen exit pauses both players");float stopped=Game.Remaining;
   Gesture(0,"LEFT_PUNCH","view-disconnected");for(var w=Pump(.2f);w.MoveNext();)yield return null;
   Check(Game.Remaining==stopped && replies["view-disconnected"].status=="INPUT_LOCKED","Missing display freezes timer and rejects attacks");
   StartGuest();double reconnectDeadline=Time.realtimeSinceStartupAsDouble+12;
   while(!Game.CanFight &&Time.realtimeSinceStartupAsDouble<reconnectDeadline) {for(var w=Pump(.1f);w.MoveNext();)yield return null;}
   Check(Game.CanFight &&Game.ViewLink.PeerReady,"Restarted second game rejoins current match without replaying attacks");
  }Shot("pvp_ready");
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  for(int i=0;i<2;i++)Check(!Game.Players[i].GetComponent<Fighter>().VisionPreparationLocked,"P"+(i+1)+" unlocked");
  Gesture(0,"LEFT_PUNCH","p1-jab");Gesture(1,"RIGHT_PUNCH","p2-straight");
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(replies["p1-jab"].status=="APPLIED"&&replies["p1-jab"].player_id=="P1","P1 attack applies to P1");
  Check(replies["p2-straight"].status=="APPLIED"&&replies["p2-straight"].player_id=="P2","P2 attack applies to P2");
  int count=Game.Receivers[0].AttacksApplied;
  Gesture(0,"LEFT_PUNCH","wrong-player",actor:"P2");Gesture(0,"LEFT_PUNCH","expired",true);
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(!replies.ContainsKey("wrong-player")&&replies["expired"].status=="EXPIRED"&&Game.Receivers[0].AttacksApplied==count,"Wrong actor and expired input cannot attack");
  for(var w=Pump(1.2f);w.MoveNext();)yield return null;
  block[0]=true;for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(Game.Players[0].GetComponent<Fighter>().IsVisionGuardHeld&&!Game.Players[1].GetComponent<Fighter>().IsVisionGuardHeld,"Guard belongs only to its player");
  block[0]=false;for(var w=Pump(.08f);w.MoveNext();)yield return null;Gesture(0,"RIGHT_PUNCH","block-followup");
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(replies["block-followup"].status=="APPLIED","Held guard releases into attack");
  // Field v30: an expired held input was cancelling the entire round about once
  // per second. Presence grace must never extend that actor's actual input lease.
  for(var w=Pump(1.2f);w.MoveNext();)yield return null;
  block[1]=true;movement[1]="RIGHT";for(var w=Pump(.10f);w.MoveNext();)yield return null;
  Check(Game.Players[1].GetComponent<Fighter>().IsVisionGuardHeld,"P2 guard is held before transport expiry");
  send[1]=false;double expiryWait=Time.realtimeSinceStartupAsDouble+.4;
  while(Game.Receivers[1].LiveReady&&Time.realtimeSinceStartupAsDouble<expiryWait)
   {for(var w=Pump(.01f);w.MoveNext();)yield return null;}
  Check(!Game.Receivers[1].LiveReady&&Game.Receivers[1].PresenceReady&&Game.CanFight,"Expired controls retain only bounded match presence during a short transport gap");
  // EditorApplication.update may observe the stopwatch crossing the deadline
  // before this frame's MonoBehaviour.Update. Allow that update to release controls.
  for(var w=Pump(.02f);w.MoveNext();)yield return null;
  float staleMove=(float)typeof(PlayerController).GetField("externalMoveX",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(Game.Players[1]);
  Check(!Game.Players[1].GetComponent<Fighter>().IsVisionGuardHeld&&!Game.Receivers[1].BlockActive&&staleMove==0&&Game.Receivers[1].PresenceReady&&Game.CanFight,"Expired camera releases guard and movement on the next game update within transport grace: held="+Game.Players[1].GetComponent<Fighter>().IsVisionGuardHeld+" block="+Game.Receivers[1].BlockActive+" move="+staleMove+" present="+Game.Receivers[1].PresenceReady);
  Gesture(1,"LEFT_PUNCH","expired-presence-attack");for(var w=Pump(.02f);w.MoveNext();)yield return null;
  Check(replies["expired-presence-attack"].status=="INPUT_LOCKED"&&Game.CanFight,"Presence grace cannot authorize a new attack from an expired actor");
  send[1]=true;block[1]=false;movement[1]="NEUTRAL";for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(Game.CanFight&&Game.BothLive,"Fresh state restores controls without pausing or another countdown");
  Gesture(1,"RIGHT_RAISE","gap-active-kick");for(var w=Pump(.04f);w.MoveNext();)yield return null;
  Check(replies["gap-active-kick"].status=="APPLIED"&&Game.Players[1].GetComponent<Fighter>().IsAttacking,"Kick begins before short transport gap");
  send[1]=false;expiryWait=Time.realtimeSinceStartupAsDouble+.4;
  while(Game.Receivers[1].LiveReady&&Time.realtimeSinceStartupAsDouble<expiryWait)
   {for(var w=Pump(.01f);w.MoveNext();)yield return null;}
  for(var w=Pump(.02f);w.MoveNext();)yield return null;
  Check(Game.CanFight&&!Game.Receivers[1].LiveReady&&Game.Players[1].GetComponent<Fighter>().IsAttacking&&!Game.Players[1].GetComponent<Fighter>().VisionPreparationLocked,"Short transport expiry does not cancel an already committed attack");
  send[1]=true;for(var w=Pump(1.2f);w.MoveNext();)yield return null;
  tracking[1]="LOST";for(var w=Pump(.05f);w.MoveNext();)yield return null;
  Check(!Game.Receivers[1].PresenceReady&&!Game.CanFight,"Known tracking loss revokes presence immediately rather than using transport grace");
  tracking[1]="VALID";for(var w=Pump(.3f);w.MoveNext();)yield return null;
  Check(Game.CanFight,"Fresh tracking recovers after a brief explicit loss");
  tracking[1]="STOPPED";for(var w=Pump(.05f);w.MoveNext();)yield return null;
  Check(!Game.Receivers[1].PresenceReady&&!Game.CanFight,"Explicit camera stop revokes presence immediately");
  tracking[1]="VALID";for(var w=Pump(.3f);w.MoveNext();)yield return null;
  send[1]=false;for(var w=Pump(.3f);w.MoveNext();)yield return null;
  Check(!Game.CanFight&&Game.Players[0].GetComponent<Fighter>().VisionPreparationLocked&&Game.Players[1].GetComponent<Fighter>().VisionPreparationLocked,"One missing camera pauses both fighters");
  float time=Game.Remaining;Gesture(0,"LEFT_PUNCH","paused-attack");for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(replies["paused-attack"].status=="INPUT_LOCKED"&&Game.Remaining==time,"Pause rejects attacks and freezes timer");
  send[1]=true;for(var w=Pump(1.5f);w.MoveNext();)yield return null;
  Check(Game.CanFight,"Reconnect resumes without replaying old attacks");
  for(int i=0;i<2;i++)Game.Players[i].GetComponent<SpecialMove>().AddCharge(5);
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  for(int i=0;i<2;i++) {var gauge=GameObject.Find("PvpSpecialGauge"+i).GetComponent<WuxiaCombatBar>();Check(gauge.Ready &&gauge.FillRatio==1,"P"+(i+1)+" charge bar fills and signals ready");}
  Shot("pvp_special_ready");
  for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Gesture(0,"SPECIAL","special-p1");Gesture(1,"SPECIAL","special-p2");for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(replies["special-p1"].status=="APPLIED"&&replies["special-p2"].status=="APPLIED","Both X specials start without global cut-in ownership conflict");
  Check(Time.timeScale>.9f,"PvP specials do not impose competing global slow motion");
  for(var w=Pump(2);w.MoveNext();)yield return null;
  Check(!Game.Players[0].GetComponent<SpecialMove>().IsPlaying&&!Game.Players[1].GetComponent<SpecialMove>().IsPlaying,"Both specials finish and release control");
  Gesture(0,"LEFT_RAISE","special-followup");for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(replies["special-followup"].status=="APPLIED","Attack after X special applies");
  generation[1]++;calibrated[1]=false;for(var w=Pump(.2f);w.MoveNext();)yield return null;
  Check(Game.Practices[1].Step==0&&Game.Practices[0].Complete&&!Game.CanFight,"Only replaced player's tutorial resets; both pause");
  calibrated[1]=true;for(var w=Practice(1,1);w.MoveNext();)yield return null;
  for(var w=Pump(1.3f);w.MoveNext();)yield return null;
  Game.Players[0].GetComponent<Health>().SetCurrentHealth(0);Game.Players[1].GetComponent<Health>().SetCurrentHealth(0);
  for(var w=Pump(.1f);w.MoveNext();)yield return null;
  Check(Game.Resolved&&Game.Result=="무승부","Simultaneous zero HP resolves as draw");
  Check(GameObject.Find("PvpRoundResult")!=null &&GameObject.Find("RoundResultText").GetComponent<UnityEngine.UI.Text>().text.Contains("K.O."),"Round ending has a central KO explanation instead of looking disconnected");
  Game.Rematch();for(var w=Pump(1.3f);w.MoveNext();)yield return null;
  Check(Game.CanFight&&Game.Players[0].GetComponent<Health>().CurrentHealth==100&&Game.Players[1].GetComponent<Health>().CurrentHealth==100,"Rematch restores both HP without another calibration/tutorial");
  Check(GameObject.Find("PvpRoundResult")==null,"Result overlay clears after rematch");
  float x0=Game.Players[0].transform.position.x,x1=Game.Players[1].transform.position.x;
  movement[0]="LEFT";movement[1]="RIGHT";for(var w=Pump(.5f);w.MoveNext();)yield return null;
  Check(Mathf.Abs(Game.Players[0].transform.position.x-x0)>.1f&&Mathf.Abs(Game.Players[1].transform.position.x-x1)>.1f,"Both movement channels drive their own real character");
  movement[0]=movement[1]="NEUTRAL";for(var w=Pump(.5f);w.MoveNext();)yield return null;
  Check(Mathf.Abs(Game.Players[0].GetComponent<Rigidbody2D>().linearVelocity.x)<.05f&&Mathf.Abs(Game.Players[1].GetComponent<Rigidbody2D>().linearVelocity.x)<.05f,"Returning to center stops both characters");
  var pa=Game.Players[0];var pb=Game.Players[1];
  pa.transform.position=new Vector3(-.45f,pa.transform.position.y,0);pb.transform.position=new Vector3(.45f,pb.transform.position.y,0);
  pa.GetComponent<Rigidbody2D>().position=pa.transform.position;pb.GetComponent<Rigidbody2D>().position=pb.transform.position;
  pa.GetComponent<Rigidbody2D>().linearVelocity=pb.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;
  Physics2D.SyncTransforms();for(var w=Pump(.3f);w.MoveNext();)yield return null;
  Gesture(1,"LEFT_PUNCH","p2-real-contact");for(var w=Pump(.9f);w.MoveNext();)yield return null;
  Check(pa.GetComponent<Health>().CurrentHealth<100,"P2 real animated contact damages P1: "+replies["p2-real-contact"].status+" positions "+pa.transform.position+" / "+pb.transform.position);
  CheckCharge(0,0);CheckCharge(1,1);
  Check(GameObject.Find("PvpHealthBar0").GetComponent<WuxiaCombatBar>().FillRatio<1,"Damage lowers the real health bar");Shot("pvp_damage_health_bar");
  pa.GetComponent<Health>().ResetHealth();pb.GetComponent<Health>().ResetHealth();
  pa.transform.position=new Vector3(-.45f,pa.transform.position.y,0);pb.transform.position=new Vector3(.45f,pb.transform.position.y,0);pa.GetComponent<Rigidbody2D>().position=pa.transform.position;pb.GetComponent<Rigidbody2D>().position=pb.transform.position;
  pa.GetComponent<Rigidbody2D>().linearVelocity=pb.GetComponent<Rigidbody2D>().linearVelocity=Vector2.zero;Physics2D.SyncTransforms();
  for(var w=Pump(.3f);w.MoveNext();)yield return null;
  Gesture(0,"LEFT_PUNCH","p1-real-contact");for(var w=Pump(.9f);w.MoveNext();)yield return null;
  Check(pb.GetComponent<Health>().CurrentHealth<100,"P1 real animated contact damages P2");
  CheckCharge(0,1);CheckCharge(1,1);
  pa.GetComponent<Health>().SetCurrentHealth(60);pb.GetComponent<Health>().SetCurrentHealth(80);
  typeof(RedboolPvp).GetProperty("Remaining").SetValue(Game,0f);
  for(var w=Pump(.1f);w.MoveNext();)yield return null;
  Check(Game.Resolved&&Game.Result=="2P 승리"&&GameObject.Find("RoundResultText").GetComponent<UnityEngine.UI.Text>().text.Contains("시간 종료"),"Timeout with both fighters alive explains round end, not a network disconnection");
  Game.Rematch();for(var w=Pump(1.3f);w.MoveNext();)yield return null;
  Check(Game.CanFight&&Game.Practices[0].Complete&&Game.Practices[1].Complete,"Timed-out round restarts without repeating tutorial");
  RunState.Instance.Save();RunState.Instance.ClearSave();
  Check(PlayerPrefs.GetString(SaveKey,"")==save,"PvP save and clear-save calls are guarded");
  Game.Leave(true);yield return null;yield return null;
  Check(!RedboolPvp.Active&&SceneManager.GetActiveScene().name==SceneRoutes.Start&&Time.timeScale==1,"Exit restores menu, clock and story mode");
 }
 static void CheckCharge(int slot,int expected){
  var meter=Game.Players[slot].GetComponent<SpecialMove>();
  Check(meter.Charge==expected,"P"+(slot+1)+" charges only its own landed normal attack: "+meter.Charge);
  var bar=GameObject.Find("PvpSpecialGauge"+slot).GetComponent<WuxiaCombatBar>();
  Check(Mathf.Abs(bar.FillRatio-(float)expected/meter.HitsToCharge)<.001f,"P"+(slot+1)+" live charge bar agrees with contact count");
  if(Game.ViewLink!=null){
   var capture=typeof(RedboolViewLink).GetMethod("CaptureActor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   var actor=(RedboolViewActor)capture.Invoke(Game.ViewLink,new object[]{slot});
   var packet=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=(byte)(slot+2),session=Guid.NewGuid(),nonce=Guid.NewGuid(),seq=1,time=1,actor=actor});
   Check(RedboolViewProtocol.TryDecode(packet,out var decoded)&&decoded.actor.charge==expected&&decoded.actor.maxCharge==meter.HitsToCharge,"P"+(slot+1)+" host snapshot carries the same charge to the second screen");
  }
 }
 static void Finish(string failure){
  StopGuest();SessionState.SetBool(Active,false);EditorApplication.update-=Tick;foreach(var c in clients)c?.Close();
  if(SessionState.GetBool(Active+".HadSave",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);PlayerPrefs.Save();
  File.WriteAllText(Path.Combine(Output,"result.txt"),(failure==null?"PASS\n":"FAIL\n"+failure+"\n")+checks.Count+" checks");EditorApplication.Exit(failure==null?0:1);
 }
}
