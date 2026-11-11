using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
[InitializeOnLoad]
public static class HeroFaceVerification {
 const string Flag="HeroFaceVerification.Active";
 static IEnumerator flow; static int frame=-1; static double deadline;
 static readonly List<string> checks=new List<string>();
 static PlayerController player; static Fighter3DView view; static Fighter fighter; static UdpActionReceiver receiver;
 static string Out=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/HeroFace"));
 static HeroFaceVerification(){if(SessionState.GetBool(Flag,false)) Subscribe();}
 public static void Run(){Directory.CreateDirectory(Out); EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity"); SessionState.SetBool(Flag,true); Subscribe(); EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();}
 static void Subscribe(){deadline=EditorApplication.timeSinceStartup+240; EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Runtime test timeout"); if(!Application.isPlaying||EditorApplication.isCompiling)return; Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate(); if(Time.frameCount==frame)return;frame=Time.frameCount; if(flow==null)flow=Tests();if(!flow.MoveNext())Finish(true);}catch(Exception e){checks.Add(e.ToString());Finish(false);}}
 static void Finish(bool ok){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;checks.Add(ok?"COMPLETE PASS":"FAIL");File.WriteAllLines(Out+"/checks.txt",checks);EditorApplication.Exit(ok?0:1);}
 static void Check(bool ok,string s){checks.Add((ok?"PASS ":"FAIL ")+s);File.WriteAllLines(Out+"/checks.txt",checks);if(!ok)throw new Exception(s);}
 static IEnumerator Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null;}
 static void Send(string action,string move="NEUTRAL"){using(var udp=new UdpClient()){var data=Encoding.UTF8.GetBytes("{\"action\":\""+action+"\",\"movement\":\""+move+"\",\"confidence\":1.0,\"movement_strength\":1.0}");udp.Send(data,data.Length,"127.0.0.1",receiver.port);}}
 static void Shot(string label){var cam=Camera.main; if(!cam)return;var rt=new RenderTexture(1280,720,24);RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Out+"/"+label+".png",image.EncodeToPNG());RenderTexture.active=previous;Object.Destroy(image);rt.Release();Object.Destroy(rt);}
 static IEnumerator Tests(){
  for(var w=Wait(1);w.MoveNext();)yield return null;
  player=Object.FindAnyObjectByType<PlayerController>(); view=player.GetComponent<Fighter3DView>();fighter=player.GetComponent<Fighter>(); receiver=Object.FindAnyObjectByType<UdpActionReceiver>();
  Check(view.visualRoot.name.StartsWith("Hero02"),"Hero02 is playable visual");Check(view.modelAnimator.isHuman&&view.modelAnimator.avatar.isValid,"Humanoid avatar valid");Check(fighter.Contacts.Ready,"All attack and hurt-volume bones resolved");Check(receiver!=null&&receiver.isActiveAndEnabled,"UDP recognition receiver enabled");
  var enemy=Object.FindAnyObjectByType<EnemyAI>();enemy.enabled=false;enemy.GetComponent<Health>().Configure("Verification",10000);player.GetComponent<Health>().Configure("Hero",10000);player.useKeyboard=false;
  Shot("idle");
  string[] actions={"JAB","CROSS","HIGHKICK","LOWKICK"};
  foreach(string action in actions){Send("NONE");for(var w=Wait(.2f);w.MoveNext();)yield return null;Send(action);for(var w=Wait(.12f);w.MoveNext();)yield return null;Check(receiver.playerController==player,"UDP targets hero player");Check(view.CurrentState==action,"UDP "+action+" starts existing animation");for(var w=Wait(.23f);w.MoveNext();)yield return null;Shot(action);for(var w=Wait(2);w.MoveNext();)yield return null;Check(view.CurrentState=="IDLE","Recovers after "+action);}
  foreach(string action in new[]{"BLOCK","LOW_BLOCK"}){if(action=="LOW_BLOCK")enemy.GetComponent<Fighter>().DeclareIntent(CombatMove.LowKick); Send(action);for(var w=Wait(.4f);w.MoveNext();)yield return null;Check(view.CurrentState.StartsWith(action=="BLOCK"?"GUARD":"GUARD_LOWKICK"),"UDP "+action+" drives guard");Shot(action);for(var w=Wait(3);w.MoveNext();)yield return null;enemy.GetComponent<Fighter>().ClearIntent();}
  float x=player.transform.position.x; Send("NONE","LEFT"); for(var w=Wait(.35f);w.MoveNext();)yield return null; Check(player.transform.position.x<x-.02f,"UDP shoulder LEFT moves hero"); Shot("movement"); Send("NONE"); Check(view.modelAnimator.GetCurrentAnimatorStateInfo(0).length>0,"Animator remains live"); for(var w=Wait(.3f);w.MoveNext();)yield return null; foreach(string attack in actions){var pr=player.GetComponent<Rigidbody2D>();var er=enemy.GetComponent<Rigidbody2D>();pr.position=new Vector2(-.45f,pr.position.y);er.position=new Vector2(.45f,er.position.y);pr.linearVelocity=er.linearVelocity=Vector2.zero;Physics2D.SyncTransforms();Send("NONE");for(var w=Wait(.25f);w.MoveNext();)yield return null;int hp=enemy.GetComponent<Health>().CurrentHealth;Send(attack);for(var w=Wait(1.8f);w.MoveNext();)yield return null;Check(enemy.GetComponent<Health>().CurrentHealth<hp,"Hero "+attack+" causes real contact damage");}
 }
}


