using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

public static class CombatContactProbe
{
 public static string Out {get{var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-contactReport");return i>=0?a[i+1]:Path.Combine(Application.temporaryCachePath,"HitContacts");}}
 public static void Run()
 {
  Directory.CreateDirectory(Out);var lines=new List<string>{"actor,move,time,bone,x,y,z"};
  try {
   EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
   var source=Object.FindAnyObjectByType<PlayerController>().GetComponent<Fighter3DView>();
   var paths=new[]{AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(source.visualRoot.gameObject)),UnifiedBoxingInstaller.Scout}.Concat(Enumerable.Range(0,StoryCharacterInstaller.Keys.Length).Select(StoryCharacterInstaller.PrefabPath)).ToArray();
   var names=new[]{"player","scout"}.Concat(StoryCharacterInstaller.Keys.Select(k=>k.Substring(3).ToLowerInvariant())).ToArray();
   for(int k=0;k<paths.Length;k++){
    var root=Object.Instantiate(k==0?source.visualRoot.gameObject:AssetDatabase.LoadAssetAtPath<GameObject>(paths[k]));
    root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,90,0));
    foreach(var m in root.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(m);
    var animator=root.GetComponentInChildren<Animator>();var clips=animator.runtimeAnimatorController.animationClips;
    animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.Rebind();
    foreach(string move in new[]{"IDLE","JAB","CROSS","HIGHKICK","LOWKICK"}){
     var clip=clips.First(c=>c.name.EndsWith("_"+move));
     var graph=PlayableGraph.Create("Contact measurement");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
     var play=AnimationClipPlayable.Create(graph,clip);play.SetApplyFootIK(false);AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(play);graph.Play();
     for(int j=0;j<=Mathf.CeilToInt(clip.length*120);j++){
      float t=Mathf.Min(j/120f,clip.length-.00001f);play.SetTime(t);graph.Evaluate(0);
      foreach(var bone in new[]{HumanBodyBones.Head,HumanBodyBones.Neck,HumanBodyBones.Chest,HumanBodyBones.Hips,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}){
       var b=animator.GetBoneTransform(bone);if(!b)continue;var v=b.position;
       lines.Add($"{names[k]},{move},{t:F4},{bone},{v.x:F4},{v.y:F4},{v.z:F4}");
      }
     }
     graph.Destroy();
    }
    Object.DestroyImmediate(root);
   }
   File.WriteAllLines(Out+"/poses.csv",lines);File.WriteAllText(Out+"/probe_done.txt","PASS");EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText(Out+"/probe_error.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
 }
}
