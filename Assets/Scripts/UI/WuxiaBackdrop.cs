using UnityEngine;
using UnityEngine.UI;

/// <summary>Static cutout foreground occludes the moving photographic sky. Maple sprites tumble separately.</summary>
public class WuxiaBackdrop : MonoBehaviour
{
    public bool animate=true;
    public bool showClouds=true;
    [Range(0,2)] public float cloudSpeed=1f;
    [Range(0,2)] public float motionAmount=1f;
    RectTransform host,art,sky;
    bool map;
    float aspect,skyAspect,elapsed;
    readonly RectTransform[] mist=new RectTransform[5],leaves=new RectTransform[12];

    static RawImage Picture(string name,Transform parent,Texture texture)
    {
        var rect=UIFactory.CreateRect(name,parent);
        var image=rect.gameObject.AddComponent<RawImage>();
        image.texture=texture;image.raycastTarget=false;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        return image;
    }
    public static WuxiaBackdrop Create(Transform parent,Sprite custom,bool isMap)
    {
        var root=UIFactory.CreateRect("WuxiaBackdrop",parent);UIFactory.Stretch(root);
        var b=root.gameObject.AddComponent<WuxiaBackdrop>();b.host=root;b.map=isMap;
        Texture texture=Resources.Load<Texture2D>("UI/Wuxia/"+(isMap?"JourneyMap":"StartCourtyard"));
        if(!isMap&&custom==null)
        {
            var foreground=Resources.Load<Texture2D>("UI/Wuxia/StartForeground");
            var skyTexture=Resources.Load<Texture2D>("UI/Wuxia/StartSky");
            if(foreground!=null&&skyTexture!=null)
            {
                b.sky=Picture("MovingSky",root,skyTexture).rectTransform;
                b.skyAspect=(float)skyTexture.width/skyTexture.height;
                texture=foreground;
            }
        }
        if(custom!=null)
        {
            var image=UIFactory.CreateImage("Artwork",root,Color.white);image.sprite=custom;
            b.art=image.rectTransform;b.aspect=custom.rect.width/custom.rect.height;
        }
        else
        {
            b.art=Picture("Artwork",root,texture).rectTransform;
            b.aspect=texture!=null?(float)texture.width/texture.height:16f/9;
        }
        b.art.anchorMin=b.art.anchorMax=b.art.pivot=new Vector2(.5f,.5f);
        if(!isMap)
        {
            for(int i=0;i<b.mist.Length;i++)
            {
                var g=WuxiaTheme.Ornament("ValleyMist",root,WuxiaGraphic.Shape.Mist,new Color(.89f,.90f,.83f,.08f));
                b.mist[i]=g.rectTransform;
                UIFactory.Place(b.mist[i],new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1400+i*160,220+i*55));
            }
            var shade=WuxiaTheme.Ornament("MenuShade",root,WuxiaGraphic.Shape.Fade,new Color(.025f,.04f,.055f,.68f));
            UIFactory.Stretch(shade.rectTransform);shade.rectTransform.anchorMax=new Vector2(.62f,1);
            var leafTexture=Resources.Load<Texture2D>("UI/Wuxia/MapleLeaf");
            for(int i=0;i<b.leaves.Length;i++)
            {
                var leaf=Picture("FallingMapleLeaf_"+i,root,leafTexture);
                leaf.color=new Color(1f,.88f+(i%3)*.04f,.76f+(i%3)*.08f,.72f+(i%3)*.09f);
                b.leaves[i]=leaf.rectTransform;
                float size=30+i%5*7;
                UIFactory.Place(b.leaves[i],new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(size,size));
            }
        }
        b.Layout();return b;
    }
    void OnEnable(){Canvas.willRenderCanvases+=Layout;}
    void OnDisable(){Canvas.willRenderCanvases-=Layout;}
    void Update(){if(animate)elapsed+=Time.unscaledDeltaTime*motionAmount;}
    void Layout()
    {
        if(host==null||art==null)return;
        float w=host.rect.width,h=host.rect.height,t=elapsed;
        float ah=Mathf.Max(h,w/aspect)*1.015f;
        art.sizeDelta=new Vector2(ah*aspect,ah);
        art.anchoredPosition=Vector2.zero; // Mountains, architecture and sun stay still.
        if(map)return;
        if(sky!=null)
        {
            float sh=Mathf.Max(h,w/skyAspect)*1.22f;
            sky.sizeDelta=new Vector2(sh*skyAspect,sh);
            float phase=showClouds?t*cloudSpeed:0;
            // Overscan keeps both edges covered throughout the slow back-and-forth drift.
            sky.anchoredPosition=new Vector2(Mathf.Sin(phase*.055f)*w*.085f,Mathf.Sin(phase*.025f)*h*.008f);
        }
        for(int i=0;i<mist.Length;i++)
            if(mist[i]!=null)mist[i].anchoredPosition=new Vector2(Mathf.Sin(t*.025f+i*1.7f)*w*.45f,-h*.3f+i*h*.10f+Mathf.Sin(t*.04f+i)*12);
        for(int i=0;i<leaves.Length;i++)
        {
            if(leaves[i]==null)continue;
            float p=Mathf.Repeat(t*(.014f+i*.0006f)+i*.087f,1);
            leaves[i].anchoredPosition=new Vector2(w*(.1f+(i%5)*.16f-p*.28f)+Mathf.Sin(t*(.65f+i*.035f)+i)*38,h*(.62f-p*1.24f));
            leaves[i].localRotation=Quaternion.Euler(0,0,i*31+t*(12+i*2)+Mathf.Sin(t*.8f+i)*24);
            leaves[i].localScale=new Vector3(.4f+Mathf.Abs(Mathf.Cos(t*.8f+i))*.6f,1,1);
        }
    }
}
