using UnityEngine;
using UnityEngine.UI;

/// <summary>Start/map-only palette and typography; combat HUD is independent.</summary>
public static class WuxiaTheme
{
    public static readonly Color Paper=new Color(.94f,.87f,.73f);
    public static readonly Color Ink=new Color(.19f,.17f,.14f);
    public static readonly Color Vermilion=new Color(.62f,.20f,.13f);
    public static readonly Color Gold=new Color(.78f,.61f,.35f);
    public static readonly Color Jade=new Color(.23f,.36f,.29f);
    static Font font,displayFont,menuFont;
    public static Font DisplayFont {get{if(displayFont==null)displayFont=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Georgia","Batang","Times New Roman"},76);return displayFont!=null?displayFont:Font;}}
    public static Font MenuFont {get{if(menuFont==null)menuFont=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Batang","Noto Serif CJK KR","Malgun Gothic"},32);return menuFont!=null?menuFont:Font;}}
    public static Font Font
    {
        get
        {
            if(font==null)font=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Apple SD Gothic Neo","Noto Sans CJK KR","Arial"},28);
            return font!=null?font:UIFactory.DefaultFont;
        }
    }
    public static Text Text(string name,Transform parent,string value,int size,TextAnchor align,Color color)
    {
        var t=UIFactory.CreateText(name,parent,value,size,align,color);t.font=Font;return t;
    }
    public static WuxiaGraphic Ornament(string name,Transform parent,WuxiaGraphic.Shape shape,Color color)
    {
        var r=UIFactory.CreateRect(name,parent);var g=r.gameObject.AddComponent<WuxiaGraphic>();g.shape=shape;g.color=color;g.raycastTarget=false;return g;
    }
    public static void Rule(Transform parent,float y,Color color)
    {
        var line=UIFactory.CreateImage("FineRule",parent,color);
        line.rectTransform.anchorMin=new Vector2(0,y);line.rectTransform.anchorMax=new Vector2(1,y);
        line.rectTransform.sizeDelta=new Vector2(0,1.5f);line.rectTransform.anchoredPosition=Vector2.zero;
    }
    public static void StyleMenuButton(Button button,bool primary)
    {
        var root=button.GetComponent<RectTransform>();
        button.GetComponent<Image>().color=Color.clear;
        var body=Ornament("LacquerPlaque",root,WuxiaGraphic.Shape.Plaque,new Color(.09f,.105f,.095f,.92f));
        UIFactory.Stretch(body.rectTransform);body.transform.SetAsFirstSibling();
        var frame=Ornament("BronzeFrame",root,WuxiaGraphic.Shape.PlaqueFrame,Gold);
        UIFactory.Stretch(frame.rectTransform);
        var inner=Ornament("InsetFrame",root,WuxiaGraphic.Shape.PlaqueFrame,new Color(Gold.r,Gold.g,Gold.b,.32f));
        UIFactory.Stretch(inner.rectTransform,6,6,6,6);
        var text=button.GetComponentInChildren<Text>();text.font=MenuFont;text.color=Paper;text.fontSize=32;
        text.fontStyle=FontStyle.Bold;
        var kind=primary?WuxiaGraphic.Shape.Swords:
            button.name=="ContinueButton"?WuxiaGraphic.Shape.Resume:
            button.name=="HelpButton"||button.name=="CreditsButton"?WuxiaGraphic.Shape.Guide:
            button.name=="SettingsButton"?WuxiaGraphic.Shape.Settings:WuxiaGraphic.Shape.Exit;
        var icon=Ornament("MenuIcon",root,kind,primary?Paper:Gold);
        UIFactory.Place(icon.rectTransform,new Vector2(0,.5f),new Vector2(.5f,.5f),new Vector2(36,0),new Vector2(38,38));
        button.transition=UnityEngine.UI.Selectable.Transition.None;
        var feedback=button.gameObject.AddComponent<WuxiaMenuFeedback>();feedback.Initialize(button,body,frame,text,primary);
    }
    public static void StyleButton(Button button)
    {
        var text=button.GetComponentInChildren<Text>();if(text!=null){text.font=Font;text.color=Paper;}
        var c=button.colors;c.highlightedColor=new Color(1.35f,1.2f,1.05f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.7f,.65f,.55f);c.fadeDuration=.16f;button.colors=c;
        Rule(button.transform,0,new Color(Gold.r,Gold.g,Gold.b,.7f));
        // 2026-11-24: 글자 왼쪽의 금빛 잎 모양 장식("MenuMark")은 넣지 않는다. 보조 버튼은 글자만 둔다.
    }
}
