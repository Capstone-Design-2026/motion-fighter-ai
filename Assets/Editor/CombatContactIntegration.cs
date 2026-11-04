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
public static class CombatContactIntegration
{
 const string Active="CombatContactIntegration.Active",SaveKey="VisionStriker.Run";
 static string Out=>CombatContactProbe.Out+"/integration";
 static IEnumerator flow;static int lastFrame=-1;static Fighter[] fs;static Health[] hs;static Fighter3DView[] vs;static Rigidbody2D[] bs;static Vector3[] initial;
 static readonly List<string> checks=new List<string>(),errors=new List<string>(),trials=new List<string>();
 static int hits;static bool guarded;static CombatBodyPart part;static Vector3 point;static string shot,key;static Fighter attacking;
 static RenderTexture rt;static Texture2D image;static Camera cam;static bool resetAfterHit;
 static CombatContactIntegration(){if(SessionState.GetBool(Active,false))Subscribe();}
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
 static void Setup(bool preserveBridge=false){
  var p=Object.FindAnyObjectByType<PlayerController>();var e=Object.FindAnyObjectByType<EnemyAI>();p.enabled=e.enabled=false;
  foreach(var u in Object.FindObjectsByType<UdpActionReceiver>())u.enabled=false;
  if(!preserveBridge)foreach(var b in Object.FindObjectsByType<BattleSceneBridge>())Object.Destroy(b);
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
  foreach(string node in new[]{"scout","east","camp","rescue","entrance","final"}){
   key=node;Isolate(node);SceneRoutes.LoadBattle();for(var w=Real(.6f);w.MoveNext();)yield return null;Setup();Time.timeScale=1;Time.captureFramerate=60;
   foreach(int actor in node=="scout"?new[]{0,1}:new[]{1})foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))
    for(var w=Trial(actor,move,1f,-1,0,1,node+"_AIreach_"+actor+"_"+move);w.MoveNext();)yield return null;
   if(node=="scout"){
    foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))foreach(float distance in new[]{1.2f,1.4f,1.6f})
     for(var w=Trial(0,move,distance,1,0,-1,"range_"+move+"_"+distance);w.MoveNext();)yield return null;
    Reset(0,.9f);for(var w=Wait(.3f);w.MoveNext();)yield return null;fs[1].Facing=1;fs[1].IsBlocking=true;
    for(var w=Wait(.3f);w.MoveNext();)yield return null;attacking.TryAttack(CombatMove.Jab);for(var w=Wait(.8f);w.MoveNext();)yield return null;Check(hits==1&&!guarded,"Back-facing guard cannot block");
    Reset(0,.9f);for(var w=Wait(.3f);w.MoveNext();)yield return null;attacking.TryAttack(CombatMove.Straight);for(var w=Wait(.08f);w.MoveNext();)yield return null;
    Time.timeScale=0;for(var w=Real(.3f);w.MoveNext();)yield return null;Check(hits==0,"Pause during startup produces no contact");Time.timeScale=1;
    for(var w=Wait(1f);w.MoveNext();)yield return null;Check(hits==1,"Paused attack resumes without duplicate or lost contact");
   }
   // Actual EnemyAI + physical pushboxes, with no player inputs or artificial attack calls.
   Reset(1,3.2f,-1);foreach(var b in bs)b.constraints=RigidbodyConstraints2D.FreezeRotation;
   foreach(var push in Object.FindObjectsByType<Pushbox>())push.enabled=true;
   var enemy=Object.FindAnyObjectByType<EnemyAI>();UnityEngine.Random.InitState(392);enemy.enabled=true;
   int initialHp=hs[0].CurrentHealth;float deadline=Time.time+10;while(Time.time<deadline&&hs[0].CurrentHealth==initialHp)yield return null;
   Check(hs[0].CurrentHealth<initialHp,node+" autonomous AI approaches and connects");
   Check(Mathf.Abs(fs[0].transform.position.x-fs[1].transform.position.x)>=.70f,node+" physical fighter separation retained");enemy.enabled=false;
  }
  // Real death -> bridge -> map/start/ending. Do not call ResolveBattle directly.
  foreach(string outcome in new[]{"win","lose","boss"}){
   key=outcome=="boss"?"final":"scout";Isolate(key);SceneRoutes.LoadBattle();for(var w=Real(.6f);w.MoveNext();)yield return null;Setup(true);Time.captureFramerate=60;
   int actor=outcome=="lose"?1:0;Reset(actor,.9f,actor==0?1:-1);hs[1-actor].SetCurrentHealth(1);
   foreach(var f in fs){var so=new SerializedObject(f);so.FindProperty("useHitImpact").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();}
   for(var w=Wait(.3f);w.MoveNext();)yield return null;attacking.TryAttack(CombatMove.Jab);
   float until=Time.realtimeSinceStartup+4;while(!hs[1-actor].IsDead&&Time.realtimeSinceStartup<until)yield return null;
   Check(hs[1-actor].IsDead&&hits==1,outcome+" actual contact causes KO");
   Check(fs.All(f=>f.InputLocked)&&hs.All(h=>h.DamageLocked),outcome+" locks both fighters after KO");
   Check(!attacking.TryAttack(CombatMove.Jab),outcome+" cannot attack after KO");
   for(var w=Real(.12f);w.MoveNext();)yield return null;Check(Time.timeScale>0&&Time.timeScale<1,outcome+" KO slows without freezing");
   string expected=outcome=="win"?SceneRoutes.Map:outcome=="lose"?SceneRoutes.Start:SceneRoutes.Ending;
   until=Time.realtimeSinceStartup+12;bool sawEnding=false;
   while(SceneManager.GetActiveScene().name!=expected&&Time.realtimeSinceStartup<until){
    if(outcome=="boss"&&StoryCutscene.IsOpen){sawEnding=true;foreach(var b in Object.FindObjectsByType<UnityEngine.UI.Button>())if(b.name=="NextStory")b.onClick.Invoke();}
    yield return null;
   }
   Check(SceneManager.GetActiveScene().name==expected,outcome+" routes to "+expected);
   Check(Mathf.Abs(Time.timeScale-1)<.001f,outcome+" restores normal time");
   if(outcome=="lose")Check(!RunState.Instance.HasRun,"Loss resets the run");
   if(outcome=="win")Check(RunState.Instance.Data.clearedNodeIds.Contains("scout"),"Victory records cleared stage");
   if(outcome=="boss")Check(sawEnding&&RunState.Instance.Data.finished,"Boss plays ending dialogue then credits");
  }
 }
}
