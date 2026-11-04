using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Full-screen story presentation. Independent of the map footer and of scaled combat time.</summary>
public class StoryCutscene : MonoBehaviour {
    public static bool IsOpen { get; private set; }
    bool next,skip;
    StoryVoice voice;
    StoryPortrait speechLeft, speechRight;
    void LateUpdate() {
        if(speechLeft!=null){speechLeft.Speaking=speechLeft.Highlighted&&voice!=null&&voice.IsPlaying;speechLeft.VoiceAmplitude=speechLeft.Speaking?voice.Level:0;}
        if(speechRight!=null){speechRight.Speaking=speechRight.Highlighted&&voice!=null&&voice.IsPlaying;speechRight.VoiceAmplitude=speechRight.Speaking?voice.Level:0;}
    }
    public static StoryCutsceneDefinition Load(string key) {
        return Resources.Load<StoryCutsceneDefinition>("Story/Cutscenes/"+key);
    }
    public static string Transcript(string key) {
        var data=Load(key); if(data==null||data.lines==null)return "";
        var text=new StringBuilder();
        foreach(var line in data.lines)text.AppendLine((string.IsNullOrEmpty(line.speaker)?"":line.speaker+": ")+line.text);
        return text.ToString().TrimEnd();
    }
    public static IEnumerator Play(RunState run,string key) {
        if(run==null||!run.IsStoryMap||run.HasSeenStory(key))yield break;
        var data=Load(key);if(data==null||data.lines==null||data.lines.Length==0)yield break;
        if(IsOpen)yield break;
        IsOpen=true;
        var canvas=UIFactory.CreateCanvas("StoryCutsceneCanvas",null,200);
        var presenter=canvas.gameObject.AddComponent<StoryCutscene>();
        try { yield return presenter.Present(data); run.CompleteStory(key); }
        finally { IsOpen=false; if(canvas!=null)Destroy(canvas.gameObject); }
    }
    void OnDestroy(){IsOpen=false;}
    void Update(){if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Return))next=true;}
    IEnumerator Present(StoryCutsceneDefinition data) {
        if(!string.IsNullOrEmpty(data.videoFileName)) {
            var video=gameObject.AddComponent<StoryVideo>();
            yield return video.Present(data);
            int resumeLine=video.CurrentLineIndex;
            bool completed=video.Completed;
            Destroy(video);
            if(completed)yield break;
            yield return PresentDialogue(data,resumeLine);
            yield break;
        }
        yield return PresentDialogue(data);
    }
    IEnumerator PresentDialogue(StoryCutsceneDefinition data,int startLine=0) {
        bool manualVideo=!string.IsNullOrEmpty(data.videoFileName);
        next=skip=false;
        voice=gameObject.AddComponent<StoryVoice>();
        var group=gameObject.AddComponent<CanvasGroup>();group.alpha=0;
        var blocker=UIFactory.CreateImage("InputBlocker",transform,Color.black,true);UIFactory.Stretch(blocker.rectTransform);
        var frame=UIFactory.CreateRect("DialogueFrame",transform);frame.gameObject.AddComponent<CinematicFrame>().Fit();
        if(data.background!=null){
            var bg=UIFactory.CreateImage("SceneBackground",frame,Color.white);bg.sprite=data.background;bg.preserveAspect=true;UIFactory.Stretch(bg.rectTransform);
        } else if(data.backgroundTexture!=null){
            var rect=UIFactory.CreateRect("SceneBackground",frame);
            var bg=rect.gameObject.AddComponent<RawImage>();bg.texture=data.backgroundTexture;bg.raycastTarget=false;
            var uv=data.backgroundUv;if(uv.width<=0||uv.height<=0)uv=new Rect(0,0,1,1);bg.uvRect=uv;
            var fit=rect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio=data.backgroundTexture.width*uv.width/(data.backgroundTexture.height*uv.height);
            var motion=rect.gameObject.AddComponent<StorySceneMotion>();motion.background=bg;motion.view=uv;
        } else {var bg=WuxiaBackdrop.Create(frame,null,true);bg.animate=true;}
        var tint=UIFactory.CreateImage("CinematicShade",frame,new Color(.015f,.025f,.03f,.42f));UIFactory.Stretch(tint.rectTransform);
        var left=CreatePortrait("LeftActor",frame,120);var right=CreatePortrait("RightActor",frame,1080);
        var leftLegacy=LegacyPortrait("LeftLegacy",frame,120);var rightLegacy=LegacyPortrait("RightLegacy",frame,1080);
        speechLeft=left;speechRight=right;
        var inspection=StoryInspection.Create(frame);
        var air=UIFactory.CreateRect("SceneAtmosphere",frame);UIFactory.Stretch(air);air.gameObject.AddComponent<StoryAtmosphere>();
        var top=UIFactory.CreateImage("LocationBand",frame,new Color(.02f,.025f,.025f,.88f));Box(top.rectTransform,0,1000,1920,80);
        var place=WuxiaTheme.Text("Location",top.transform,data.location,27,TextAnchor.MiddleLeft,WuxiaTheme.Gold);UIFactory.Stretch(place.rectTransform,90,0,240,0);
        if(!manualVideo){
            var skipButton=UIFactory.CreateButton("SkipStory",top.transform,"건너뛰기",23,Color.clear,()=>skip=true);Box(skipButton.GetComponent<RectTransform>(),1640,12,190,56);
        }
        var panel=UIFactory.CreateImage("DialoguePanel",frame,new Color(.02f,.025f,.025f,.97f),true);Box(panel.rectTransform,0,0,1920,260);
        var rule=UIFactory.CreateImage("GoldRule",panel.transform,new Color(.65f,.52f,.32f,.75f));Box(rule.rectTransform,90,258,1740,2);
        var speaker=WuxiaTheme.Text("Speaker",panel.transform,"",30,TextAnchor.MiddleLeft,WuxiaTheme.Gold);Box(speaker.rectTransform,110,192,1500,44);
        var body=WuxiaTheme.Text("Dialogue",panel.transform,"",34,TextAnchor.UpperLeft,WuxiaTheme.Paper);Box(body.rectTransform,110,70,1670,112);
        body.horizontalOverflow=HorizontalWrapMode.Wrap;body.verticalOverflow=VerticalWrapMode.Truncate;body.supportRichText=false;
        body.resizeTextForBestFit=true;body.resizeTextMinSize=26;body.resizeTextMaxSize=34;
        var advance=UIFactory.CreateButton("NextStory",panel.transform,"다음  ·  Space",24,Color.clear,()=>next=true);Box(advance.GetComponent<RectTransform>(),1590,12,220,48);
        var counter=WuxiaTheme.Text("LineCount",panel.transform,"",20,TextAnchor.MiddleLeft,new Color(.6f,.58f,.52f));Box(counter.rectTransform,110,15,200,40);
        for(float t=0;t<.2f;t+=Time.unscaledDeltaTime){group.alpha=t/.2f;yield return null;}group.alpha=1;
        for(int lineIndex=startLine;lineIndex<data.lines.Length;lineIndex++){
            var line=data.lines[lineIndex];if(skip)break;next=false;
            speaker.text=line.speaker;body.text="";counter.text=(lineIndex+1)+" / "+data.lines.Length;
            inspection.Show(line);
            bool examining=line.evidence!=StoryEvidence.None;
            left.SetActor(examining?null:line.leftActor);right.SetActor(examining?null:line.rightActor);
            left.BeginBeat();right.BeginBeat();
            bool hasSpeaker=!string.IsNullOrEmpty(line.speaker);
            left.Highlighted=hasSpeaker&&!line.speakerOnRight&&line.leftActor!=null&&line.speaker==line.leftActor.displayName;
            right.Highlighted=hasSpeaker&&line.speakerOnRight&&line.rightActor!=null&&line.speaker==line.rightActor.displayName;
            SetLegacy(leftLegacy,line.leftPortrait,!examining&&line.leftActor==null,left.Highlighted);
            SetLegacy(rightLegacy,line.rightPortrait,!examining&&line.rightActor==null,right.Highlighted);
            body.text=line.text??"";
            voice.Play(line);
            // One press advances the whole line, interrupting speech immediately.
            yield return null;
            while(!next&&!skip) yield return null;
            voice.Stop();
            left.Speaking=right.Speaking=false;
            left.VoiceAmplitude=right.VoiceAmplitude=0;
        }
        voice.Stop();
        left.Speaking=right.Speaking=false;
        for(float t=0;t<.18f;t+=Time.unscaledDeltaTime){group.alpha=1-t/.18f;yield return null;}
    }
    static void Box(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);}
    static StoryPortrait CreatePortrait(string name,Transform parent,float x){var rect=UIFactory.CreateRect(name,parent);Box(rect,x,260,720,725);return rect.gameObject.AddComponent<StoryPortrait>();}
    static Image LegacyPortrait(string name,Transform parent,float x){var img=UIFactory.CreateImage(name,parent,Color.white);img.preserveAspect=true;Box(img.rectTransform,x,260,720,725);return img;}
    static void SetLegacy(Image img,Sprite sprite,bool visible,bool active){img.sprite=sprite;img.enabled=visible&&sprite!=null;img.color=active?Color.white:new Color(.65f,.65f,.65f,1);}
}
