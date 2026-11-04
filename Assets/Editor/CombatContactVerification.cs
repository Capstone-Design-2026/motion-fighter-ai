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
public static class CombatContactVerification
{
 const string Active="CombatContactVerification.Active",SaveKey="VisionStriker.Run";
 static string Out=>CombatContactProbe.Out;
 static IEnumerator flow;static int lastFrame=-1;static Fighter[] fs;static Health[] hs;static Fighter3DView[] vs;static Rigidbody2D[] bs;static Vector3[] initial;
 static readonly List<string> checks=new List<string>(),errors=new List<string>(),trials=new List<string>();
 static int hits;static bool guarded;static CombatBodyPart part;static Vector3 point;static string shot,key;static Fighter attacking;
 static RenderTexture rt;static Texture2D image;static Camera cam;static bool resetAfterHit;
 static CombatContactVerification(){if(SessionState.GetBool(Active,false))Subscribe();}
 public static void Run(){
  Directory.CreateDirectory(Out+"/stills");SessionState.SetBool(Active+".Had",PlayerPrefs.HasKey(SaveKey));SessionState.SetString(Active+".Save",PlayerPrefs.GetString(SaveKey,""));File.WriteAllText(Out+"/save_before.json",PlayerPrefs.GetString(SaveKey,""));
  EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");SessionState.SetBool(Active,true);Subscribe();EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
 }
 static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;}
 static void Log(string m,string s,LogType t){if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!s.Contains("IvanMurzak"))errors.Add(m+"\n"+s);}
 static void Tick(){
  if(!Application.isPlaying||EditorApplication.isCompiling)return;
  Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
  try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish();}catch(Exception e){errors.Add(e.ToString());Finish();}
 }
 static void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Out+"/checks.txt",checks);}
 static void Finish(){
  HitImpact.ReleaseTimeControl();Time.timeScale=1;Time.captureFramerate=0;
  if(SessionState.GetBool(Active+".Had",false))PlayerPrefs.SetString(SaveKey,SessionState.GetString(Active+".Save",""));else PlayerPrefs.DeleteKey(SaveKey);PlayerPrefs.Save();
  File.WriteAllText(Out+"/save_after.json",PlayerPrefs.GetString(SaveKey,""));Check(PlayerPrefs.GetString(SaveKey,"")==SessionState.GetString(Active+".Save",""),"Original progress restored");
  File.WriteAllLines(Out+"/errors.txt",errors);File.WriteAllLines(Out+"/trials.csv",trials);File.WriteAllText(Out+"/done.txt",errors.Count==0&&checks.All(c=>c.StartsWith("PASS"))?"PASS":"FAIL");
  SessionState.SetBool(Active,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;EditorApplication.Exit(errors.Count==0&&checks.All(c=>c.StartsWith("PASS"))?0:1);
 }
 static IEnumerator Wait(float t){float end=Time.time+t;while(Time.time<end)yield return null;}
 static IEnumerator Real(float t){float end=Time.realtimeSinceStartup+t;while(Time.realtimeSinceStartup<end)yield return null;}
 static void Isolate(string id){var run=RunState.Instance;var data=new RunData{seed=684,playerHealth=100,playerMaxHealth=100,currentNodeId="cave",pendingNodeId=id,tutorialCompleted=true,tutorialVersion=1,storyPresentationVersion=1};data.map=MapGenerator.Generate(run.Config.map,run.Config,data.seed);typeof(RunState).GetProperty("Data").SetValue(run,data);}
 static void Setup(){
  var p=Object.FindAnyObjectByType<PlayerController>();var e=Object.FindAnyObjectByType<EnemyAI>();p.enabled=e.enabled=false;
  foreach(var u in Object.FindObjectsByType<UdpActionReceiver>())u.enabled=false;
  foreach(var b in Object.FindObjectsByType<BattleSceneBridge>())Object.Destroy(b);
  foreach(var b in Object.FindObjectsByType<Pushbox>())b.enabled=false;
  fs=new[]{p.GetComponent<Fighter>(),e.GetComponent<Fighter>()};hs=fs.Select(f=>f.GetComponent<Health>()).ToArray();vs=fs.Select(f=>f.GetComponent<Fighter3DView>()).ToArray();bs=fs.Select(f=>f.GetComponent<Rigidbody2D>()).ToArray();initial=fs.Select(f=>f.transform.position).ToArray();
  foreach(var f in fs){f.ContactResolved+=OnContact;var so=new SerializedObject(f);so.FindProperty("useHitImpact").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();}
  foreach(var b in bs){b.linearVelocity=Vector2.zero;b.constraints=RigidbodyConstraints2D.FreezeAll;b.interpolation=RigidbodyInterpolation2D.None;}
  cam=Camera.main;var rig=cam.GetComponent<Battle3DCamera>();rig.allowMouseOrbit=false;rig.automaticCombatFraming=false;rig.SetOrbit(0,7,true);
  if(!rt){rt=new RenderTexture(1280,720,24);rt.Create();image=new Texture2D(1280,720,TextureFormat.RGB24,false);}cam.targetTexture=rt;cam.aspect=16f/9;
  var canvas=Object.FindAnyObjectByType<CombatHUD>().GetComponentInChildren<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=cam.nearClipPlane+.1f;canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled=false;canvas.scaleFactor=1280f/1920;canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
 }
 static void OnContact(Fighter target,Vector3 at,CombatBodyPart body,bool block){
  hits++;point=at;guarded=block;part=body;
  if(!string.IsNullOrEmpty(shot)){Capture(shot);shot=null;}
  if(resetAfterHit)target.Health.ResetHealth();
 }
 static void Reset(int attacker,float distance,int facing=1,int guard=0){
  attacking=fs[attacker];hits=0;guarded=false;resetAfterHit=false;shot=null;
  for(int i=0;i<2;i++){hs[i].ResetHealth();hs[i].DamageLocked=false;fs[i].InputLocked=false;fs[i].IsBlocking=fs[i].IsCrouching=false;fs[i].Contacts.ResetHistory();vs[i].enabled=true;}
  fs[attacker].Facing=facing;fs[1-attacker].Facing=-facing;fs[1-attacker].IsBlocking=guard!=0;fs[1-attacker].IsCrouching=guard==2;
  Position(attacker,distance,facing);
 }
 static void Position(int attacker,float distance,int facing=1){
  for(int i=0;i<2;i++){var v=initial[i];v.x=(i==attacker?-.5f:.5f)*distance*facing;v.z=0;bs[i].position=v;bs[i].linearVelocity=Vector2.zero;fs[i].transform.position=v;}Physics2D.SyncTransforms();
 }
 static IEnumerator Trial(int actor,CombatMove move,float distance,int facing,int guard,int expected,string label,bool capture=false){
  Reset(actor,distance,facing,guard);for(var w=Wait(.34f);w.MoveNext();)yield return null;
  int hp=hs[1-actor].CurrentHealth;if(capture)shot=label;
  Check(attacking.TryAttack(move),label+" attack accepted");
  for(var w=Wait(Fighter3DView.Duration(move)+.12f);w.MoveNext();)yield return null;
  int damage=hp-hs[1-actor].CurrentHealth;
  trials.Add($"{key},{actor},{move},{distance:F2},{facing},{guard},{Time.captureFramerate},{hits},{damage},{part}");
  Check(hits<=1,label+" at most one contact");if(expected>=0)Check(hits==expected,label+" hit count "+hits);
  if(expected==1){bool shouldGuard=guard!=0&&(move==CombatMove.Jab||move==CombatMove.Straight||move==CombatMove.LowKick&&guard==2||move==CombatMove.HighKick&&guard==1);Check(guarded==shouldGuard,label+" correct guard rule");Check(damage==(shouldGuard?fs[1-actor].GuardedDamage(attacking.AttackDamage):attacking.AttackDamage),label+" correct damage "+damage);}
  if(expected==0)Check(damage==0,label+" no damage");
  Check(!attacking.IsAttacking&&vs[actor].CurrentState=="IDLE",label+" recovers to idle");shot=null;
 }
 [Serializable]class Overlay {public string name;public float[] contact;public List<Shape> attack=new List<Shape>(),body=new List<Shape>();}
 [Serializable]class Shape {public float[] a,b;public float radius;public string part;}
 static float[] Screen(Vector3 p){var v=cam.WorldToViewportPoint(p);return new[]{v.x*1280,(1-v.y)*720};}
 static Shape ShapeOf(CombatContactGeometry.Capsule c,string part){var a=Screen(c.a);var b=Screen(c.a+Vector3.up*c.radius);return new Shape{a=a,b=Screen(c.b),radius=Mathf.Abs(a[1]-b[1]),part=part};}
 static void Capture(string name){
  Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Out+"/stills/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;
  var overlay=new Overlay{name=name,contact=Screen(point)};overlay.attack.Add(ShapeOf(attacking.Contacts.strikes[(int)attacking.CurrentMove],"attack"));var defender=fs.First(f=>f!=attacking);
  for(int i=0;i<7;i++)overlay.body.Add(ShapeOf(defender.Contacts.hurt[i],defender.Contacts.parts[i].ToString()));File.WriteAllText(Out+"/stills/"+name+".json",JsonUtility.ToJson(overlay));
 }
 static IEnumerator Tests(){
  yield return null;trials.Add("stage,actor,move,distance,facing,guard,fps,contacts,damage,part");
  var a0=new CombatContactGeometry.Capsule(new Vector3(-1,0),new Vector3(-1,0),.1f);var a1=new CombatContactGeometry.Capsule(new Vector3(1,0),new Vector3(1,0),.1f);var b=new CombatContactGeometry.Capsule(Vector3.zero,Vector3.zero,.1f);
  Check(CombatContactGeometry.Sweep(a0,a1,b,b,0,1,out float t,out var p)&&Mathf.Abs(t-.4f)<.002f,"Sweep catches contact between two non-overlapping endpoints");
  Check(!CombatContactGeometry.Sweep(a0,a1,b,b,0,.2f,out _,out _),"Sweep excludes windup/recovery outside active interval");
  var b0=new CombatContactGeometry.Capsule(new Vector3(1,0),new Vector3(1,0),.1f);var b1=new CombatContactGeometry.Capsule(new Vector3(-1,0),new Vector3(-1,0),.1f);
  Check(CombatContactGeometry.Sweep(a0,a1,b0,b1,0,1,out t,out _)&&Mathf.Abs(t-.45f)<.002f,"Sweep handles moving opponent and moving fist");
  foreach(string node in new[]{"scout","east","camp","rescue","entrance","final"}){
   key=node;Isolate(node);SceneRoutes.LoadBattle();for(var w=Real(.6f);w.MoveNext();)yield return null;Setup();Time.timeScale=1;Time.captureFramerate=60;
   Check(fs.All(f=>f.Contacts.Ready),key+" both rigs provide anatomical contacts");
   foreach(int actor in node=="scout"?new[]{0,1}:new[]{1})foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))foreach(int face in new[]{1,-1}){
    string label=key+"_"+actor+"_"+move+"_"+face;
    for(var w=Trial(actor,move,.9f,face,0,1,label,node=="scout"&&face==1||node=="final"&&face==1);w.MoveNext();)yield return null;
   }
   foreach(CombatMove move in Enum.GetValues(typeof(CombatMove))){for(var w=Trial(1,move,2.2f,1,0,0,key+"_far_"+move);w.MoveNext();)yield return null;}
   if(node=="scout"||node=="final"){
    foreach(int fps in new[]{15,30}){Time.captureFramerate=fps;foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))for(var w=Trial(1,move,.9f,-1,0,1,key+"_fps"+fps+"_"+move);w.MoveNext();)yield return null;}Time.captureFramerate=60;
   }
  }
  // Shared guard matrix and active-window edge cases in the last installed arena.
  foreach(int guard in new[]{1,2})foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))for(var w=Trial(0,move,.9f,1,guard,1,"guard"+guard+"_"+move,true);w.MoveNext();)yield return null;
  foreach(CombatMove move in Enum.GetValues(typeof(CombatMove))){
   Reset(0,.9f);for(var w=Wait(.2f);w.MoveNext();)yield return null;attacking.TryAttack(move);for(var w=Wait(.06f);w.MoveNext();)yield return null;hs[0].TakeDamage(1,Vector2.zero);for(var w=Wait(1.3f);w.MoveNext();)yield return null;Check(hits==0&&!attacking.IsAttacking,"Interrupt cancels "+move);
   Reset(0,2.2f);for(var w=Wait(.2f);w.MoveNext();)yield return null;attacking.TryAttack(move);for(var w=Wait(CombatContactWorld.Window(move).y+.07f);w.MoveNext();)yield return null;Position(0,.9f);for(var w=Wait(1f);w.MoveNext();)yield return null;Check(hits==0,"Recovery cannot hit "+move);
   Reset(0,.9f);for(var w=Wait(.2f);w.MoveNext();)yield return null;attacking.TryAttack(move);for(var w=Wait(.04f);w.MoveNext();)yield return null;Position(0,2.2f);for(var w=Wait(1.3f);w.MoveNext();)yield return null;Check(hits==0,"Leaves during windup: whiff "+move);
   Reset(0,2.2f);for(var w=Wait(.2f);w.MoveNext();)yield return null;attacking.TryAttack(move);for(var w=Wait(CombatContactWorld.Window(move).x+.015f);w.MoveNext();)yield return null;Position(0,.9f);for(var w=Wait(1.3f);w.MoveNext();)yield return null;Check(hits==1,"Enters during active window: hit "+move);
   Reset(0,.9f);for(var w=Wait(.2f);w.MoveNext();)yield return null;resetAfterHit=true;attacking.TryAttack(move);for(var w=Wait(1.4f);w.MoveNext();)yield return null;Check(hits==1,"No duplicate even if target invulnerability resets "+move);resetAfterHit=false;
  }
  Check(Resources.Load<AudioClip>("Audio/CombatContact/Punch")&&Resources.Load<AudioClip>("Audio/CombatContact/Kick")&&Resources.Load<AudioClip>("Audio/CombatContact/Guard"),"Contact sound clips imported");
 }
}
