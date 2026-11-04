using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opt-in voice QA. Opens an unsaved empty scene in batch mode; never saves project scenes.</summary>
[InitializeOnLoad]
public static class StoryVoiceVerification
{
    static readonly string Output = Environment.GetEnvironmentVariable("VISION_VOICE_QA") ?? Path.Combine(Path.GetTempPath(),"VisionStrikerVoiceQA");
    static List<string> results = new List<string>();
    static StoryCutsceneDefinition[] scenes;
    static Canvas canvas;
    static Camera camera;
    static StoryCutscene presenter;
    static StoryVoice voice;
    static int sceneIndex, lineIndex, state;
    static double since, started;
    static bool startedPlay;
    static StoryVideo video;
    static StoryCutsceneDefinition prologue;
    static HashSet<int> videoSeen = new HashSet<int>();
    static StoryDialogueLine[] all;
    static int audioIndex;
    static StoryVoiceVerification() { EditorApplication.update += Tick; }
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        results.Clear();
        var loaded = Resources.LoadAll<StoryCutsceneDefinition>("Story/Cutscenes");
        Check(loaded.Length==38,"38 cutscenes imported");
        if(loaded.SelectMany(x=>x.lines).All(l=>l.voice==null))
        {
            Check(loaded.SelectMany(x=>x.lines).All(l=>l.voiceEnvelope==null||l.voiceEnvelope.Length==0),"Silent build has no old envelope");
            Check(loaded.SelectMany(x=>x.lines).All(l=>l.videoSpeechTrack==null),"Silent build has no old speech track");
            File.WriteAllLines(Path.Combine(Output,"import_checks.txt"),results);
            Debug.Log("[VoiceQA] Current story intentionally has no recorded voices. Silent build checks passed.");
            if(Application.isBatchMode)EditorApplication.Exit(0);
            return;
        }
        int count=0;
        foreach(var data in loaded) foreach(var line in data.lines)
        {
            count++;
            Check(line.voice!=null,data.name+" voice reference");
            Check(line.voiceEnvelope!=null && line.voiceEnvelope.Length>0,data.name+" envelope exists");
            if(line.voice!=null && line.voiceEnvelope!=null)
                Check(Mathf.Abs(line.voiceEnvelope.Length/30f-line.voice.length)<.2f,data.name+" envelope duration matches clip");
            Check(line.voiceEnvelope==null||line.voiceEnvelope.All(x=>!float.IsNaN(x)&&x>=0&&x<=1),"finite envelope");
            if(line.videoSpeechTrack!=null){var t=JsonUtility.FromJson<StorySpeakingImage.Track>(line.videoSpeechTrack.text);Check(t.entry.Length>0&&t.hold.Length>0,"both movie tracks");}
        }
        Check(count==103,"103 voiced lines");
        File.WriteAllLines(Path.Combine(Output,"import_checks.txt"),results);
        SessionState.SetBool("VisionVoiceQA",true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.isPaused=false; EditorApplication.delayCall += () => { EditorApplication.EnterPlaymode(); };
    }
    static void Check(bool good,string label)
    {
        results.Add((good?"PASS ":"FAIL ")+label);
        if(!good)throw new Exception(label);
    }
    static void Next(int s){state=s;since=EditorApplication.timeSinceStartup;}
    static void Tick()
    {
        if(!SessionState.GetBool("VisionVoiceQA",false)||!EditorApplication.isPlaying||!Application.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            if(!startedPlay)
            {
                EditorApplication.isPaused=false; Application.runInBackground=true; startedPlay=true;started=EditorApplication.timeSinceStartup;
                camera=new GameObject("VoiceQACamera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
                camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.tag="MainCamera";
                camera.transform.position=new Vector3(0,0,-10);
                scenes=Resources.LoadAll<StoryCutsceneDefinition>("Story/Cutscenes").OrderBy(x=>x.name).ToArray();
                all=scenes.SelectMany(x=>x.lines).ToArray();
                voice=new GameObject("VoicePlaybackCheck").AddComponent<StoryVoice>();
                Time.timeScale=0;
                voice.Play(all[0]);Next(0);
            }
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup-started>300)throw new Exception("QA timeout");
            double age=EditorApplication.timeSinceStartup-since;
            if(state==0)
            {
                if(age<.22)return;
                var source=voice.GetComponent<AudioSource>();
                Check(source!=null&&source.clip==all[audioIndex].voice,"playback clip "+audioIndex);
                Check(source.ignoreListenerPause&&source.spatialBlend==0,"2D pause-independent source");
                // Batch mode may use a null audio device. Record status without claiming audible QA.
                results.Add("AUDIO_STATE "+audioIndex+" playing="+voice.IsPlaying+" time="+source.time);
                voice.Stop();Check(!voice.IsPlaying&&voice.Level==0&&source.clip==null,"stop clears voice/envelope");
                if(++audioIndex<all.Length){voice.Play(all[audioIndex]);Next(0);return;}
                UnityEngine.Object.Destroy(voice.gameObject);Time.timeScale=1;sceneIndex=0;ShowScene();Next(1);
            }
            else if(state==1)
            {
                if(age<.28)return;
                var voices=canvas.GetComponentsInChildren<AudioSource>();
                if ((voices.Length!=1 || voices[0].clip!=scenes[sceneIndex].lines[lineIndex].voice) && age<8) return;
                results.Add("DIALOGUE_STATE age="+age+" sources="+voices.Length+" dt="+Time.unscaledDeltaTime);
                Check(voices.Length==1&&voices[0].clip==scenes[sceneIndex].lines[lineIndex].voice,"dialogue line reference "+scenes[sceneIndex].name+"/"+lineIndex);
                if(lineIndex==0)Shot(scenes[sceneIndex].name);
                typeof(StoryCutscene).GetField("next",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(presenter,true);
                if(++lineIndex<scenes[sceneIndex].lines.Length){Next(1);return;}
                Next(2);
            }
            else if(state==2)
            {
                if(age<.25)return;
                Check(!canvas.GetComponentsInChildren<StoryVoice>().Any(x=>x.IsPlaying),"dialogue finished silent");
                UnityEngine.Object.Destroy(canvas.gameObject);
                if(++sceneIndex<scenes.Length){ShowScene();Next(1);return;}
                canvas=NewCanvas();video=canvas.gameObject.AddComponent<StoryVideo>();prologue=Resources.Load<StoryCutsceneDefinition>("Story/Cutscenes/cave_prologue");
                video.StartCoroutine(video.Present(prologue));Next(3);
            }
            else if(state==3)
            {
                if(video.Completed){Check(videoSeen.Count==20,"all 20 movie beats voiced");Finish();return;}
                if(!video.IsWaitingForInput)return;
                int k=video.CurrentLineIndex;
                var vs=canvas.GetComponentInChildren<StoryVoice>();
                if(!videoSeen.Contains(k))
                {
                    Check(vs!=null&&vs.GetComponent<AudioSource>().clip==prologue.lines[k].voice,"movie voice "+k);
                    videoSeen.Add(k);since=EditorApplication.timeSinceStartup;return;
                }
                if(age<.5)return;
                // Let one movie reach its hold to prove voice survives entry->loop.
                if(k==0 && !video.IsHolding)return;
                if(k==0)Check(vs.GetComponent<AudioSource>().clip==prologue.lines[k].voice,"hold keeps same voice");
                Shot("movie_"+(k+1).ToString("D2"));video.RequestAdvance();Check(!vs.IsPlaying,"movie advance interrupts voice");
            }
        }
        catch(Exception ex){results.Add("FAIL "+ex);Finish(1);}
    }
    static Canvas NewCanvas(){var c=UIFactory.CreateCanvas("VoiceQA",null,200);c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;return c;}
    static void ShowScene()
    {
        canvas=NewCanvas();presenter=canvas.gameObject.AddComponent<StoryCutscene>();lineIndex=0;
        var routine=(IEnumerator)typeof(StoryCutscene).GetMethod("PresentDialogue",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{scenes[sceneIndex],0});
        presenter.StartCoroutine(routine);
    }
    static void Shot(string name)
    {
        Canvas.ForceUpdateCanvases();var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;camera.Render();
        var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(1280,720,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,720),0,0);t.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+".png"),t.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.Destroy(t);rt.Release();UnityEngine.Object.Destroy(rt);
    }
    static void Finish(int code=0)
    {
        File.WriteAllLines(Path.Combine(Output,"runtime_checks.txt"),results);
        SessionState.SetBool("VisionVoiceQA",false);Time.timeScale=1;EditorApplication.Exit(code);
    }
}
