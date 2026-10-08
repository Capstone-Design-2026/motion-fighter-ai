using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Full-screen story presentation. Independent of the map footer and of scaled combat time.</summary>
public class StoryCutscene : MonoBehaviour {
    public static bool IsOpen { get; private set; }
    bool next,skip;
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
        var group=gameObject.AddComponent<CanvasGroup>();group.alpha=0;
        var root=UIFactory.CreateImage("InputBlocker",transform,new Color(.025f,.032f,.035f,1),true);UIFactory.Stretch(root.rectTransform);
        if(data.background!=null){var bg=UIFactory.CreateImage("SceneBackground",transform,Color.white);bg.sprite=data.background;bg.preserveAspect=true;UIFactory.Stretch(bg.rectTransform);}
        else if(data.backgroundTexture!=null){var rect=UIFactory.CreateRect("SceneBackground",transform);UIFactory.Stretch(rect);var bg=rect.gameObject.AddComponent<RawImage>();bg.texture=data.backgroundTexture;bg.raycastTarget=false;}
        else {var bg=WuxiaBackdrop.Create(transform,null,true);bg.animate=false;}
        var tint=UIFactory.CreateImage("CinematicShade",transform,new Color(.015f,.025f,.03f,.56f));UIFactory.Stretch(tint.rectTransform);
        var left=Portrait("LeftPortrait",new Vector2(.25f,.5f));var right=Portrait("RightPortrait",new Vector2(.75f,.5f));
        var top=UIFactory.CreateImage("LetterboxTop",transform,new Color(.02f,.025f,.025f,.96f));
        top.rectTransform.anchorMin=new Vector2(0,1);top.rectTransform.anchorMax=Vector2.one;top.rectTransform.pivot=new Vector2(.5f,1);top.rectTransform.sizeDelta=new Vector2(0,105);
        var place=WuxiaTheme.Text("Location",top.transform,data.location,28,TextAnchor.MiddleLeft,WuxiaTheme.Gold);UIFactory.Stretch(place.rectTransform,85,0,250,0);
        if(!manualVideo) {
        var skipButton=UIFactory.CreateButton("SkipStory",top.transform,"건너뛰기",23,Color.clear,()=>skip=true);
        UIFactory.Place(skipButton.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-65,-24),new Vector2(165,56));
        }
        var panel=UIFactory.CreateImage("DialoguePanel",transform,new Color(.02f,.025f,.025f,.97f),true);
        panel.rectTransform.anchorMin=Vector2.zero;panel.rectTransform.anchorMax=new Vector2(1,0);panel.rectTransform.pivot=new Vector2(.5f,0);panel.rectTransform.sizeDelta=new Vector2(0,300);
        var speaker=WuxiaTheme.Text("Speaker",panel.transform,"",28,TextAnchor.UpperLeft,WuxiaTheme.Gold);UIFactory.Place(speaker.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(160,-34),new Vector2(1000,45));
        var body=WuxiaTheme.Text("Dialogue",panel.transform,"",32,TextAnchor.UpperLeft,WuxiaTheme.Paper);UIFactory.Stretch(body.rectTransform,160,90,160,93);body.horizontalOverflow=HorizontalWrapMode.Wrap;body.supportRichText=false;
        var advance=UIFactory.CreateButton("NextStory",panel.transform,"다음  ·  Space",24,Color.clear,()=>next=true);
        UIFactory.Place(advance.GetComponent<RectTransform>(),new Vector2(1,0),new Vector2(1,0),new Vector2(-110,20),new Vector2(180,54));
        for(float t=0;t<.2f;t+=Time.unscaledDeltaTime){group.alpha=t/.2f;yield return null;}group.alpha=1;
        for(int lineIndex=startLine;lineIndex<data.lines.Length;lineIndex++){
            var line=data.lines[lineIndex];
            if(skip)break;next=false;speaker.text=line.speaker;body.text="";
            SetPortrait(left,line.leftPortrait,!line.speakerOnRight);SetPortrait(right,line.rightPortrait,line.speakerOnRight);
            string text=line.text??"";
            if(manualVideo){body.text=text;yield return null;while(!next)yield return null;continue;}
            float letters=0;
            while(letters<text.Length&&!skip&&!next){letters+=Time.unscaledDeltaTime*42;body.text=text.Substring(0,Mathf.Min(text.Length,(int)letters));yield return null;}
            body.text=text;next=false;yield return null;
            while(!next&&!skip)yield return null;
        }
        for(float t=0;t<.18f;t+=Time.unscaledDeltaTime){group.alpha=1-t/.18f;yield return null;}
    }
    Image Portrait(string name,Vector2 position){var img=UIFactory.CreateImage(name,transform,Color.white);img.preserveAspect=true;UIFactory.Place(img.rectTransform,position,new Vector2(.5f,.5f),new Vector2(0,45),new Vector2(660,690));return img;}
    void SetPortrait(Image image,Sprite sprite,bool active){image.sprite=sprite;image.enabled=sprite!=null;image.color=active?Color.white:new Color(.5f,.5f,.5f,1);}
}
