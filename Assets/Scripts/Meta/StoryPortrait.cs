using UnityEngine;
using UnityEngine.UI;

/// <summary>Small, bounded 2D puppet motion. Speech follows text reveal, not phoneme lip sync.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StoryPortrait : MaskableGraphic
{
    public StoryActorDefinition Actor {get;private set;}
    public bool Speaking {get;set;}
    public bool Highlighted {get;set;}
    public float PreviewTime = -1;
    float elapsed, emphasis, beat;
    public void BeginBeat(){beat=0;}
    const int Columns=30, Rows=42;
    public override Texture mainTexture => Actor!=null&&Actor.artwork!=null?Actor.artwork:Texture2D.whiteTexture;
    public void SetActor(StoryActorDefinition actor){
        gameObject.SetActive(actor!=null);
        if(Actor==actor)return;Actor=actor;elapsed=0;SetAllDirty();
    }
    protected override void Awake(){base.Awake();raycastTarget=false;}
    void Update(){
        elapsed+=Time.unscaledDeltaTime;beat+=Time.unscaledDeltaTime;
        emphasis=Mathf.MoveTowards(emphasis,Highlighted?1:0,Time.unscaledDeltaTime*4);
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh){
        vh.Clear();if(Actor==null||Actor.artwork==null)return;
        var bounds=rectTransform.rect;
        float aspect=Actor.artwork.width*Actor.crop.width/(Actor.artwork.height*Actor.crop.height);
        float h=Mathf.Min(bounds.height,bounds.width/aspect),w=h*aspect;
        float x0=bounds.center.x-w*.5f,y0=bounds.yMin;
        float t=PreviewTime>=0?PreviewTime:elapsed;
        float speech=Speaking?Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*17)*Mathf.Sin(t*3.3f)),.7f):0;
        float breath=Mathf.Sin(t*1.7f)*4.8f;
        float entrance=(1-Mathf.Exp(-beat*5)) ;
        float lean=(Highlighted?1:0)*(.5f+.5f*Mathf.Sin(t*1.9f));
        var tint=Color.Lerp(new Color(.65f,.64f,.61f,1),Color.white,emphasis)*color;
        for(int y=0;y<=Rows;y++)for(int x=0;x<=Columns;x++){
            Vector2 p=new Vector2((float)x/Columns,(float)y/Rows);
            float edge=Mathf.Sin(p.x*Mathf.PI)*Mathf.Sin(p.y*Mathf.PI);
            float jaw=Gaussian(p-Actor.mouth,new Vector2(.10f,.07f));
            float hand=Gaussian(p-Actor.hand,new Vector2(.15f,.18f));
            float nod=Gaussian(p-new Vector2(.5f,.82f),new Vector2(.26f,.22f));
            Vector2 delta=new Vector2(Mathf.Sin(t*.9f)*3.4f*nod,breath*edge);
            delta.y-=jaw*speech*(Actor.masked?0:4f);
            delta.x+=hand*Mathf.Sin(t*1.5f)*(Highlighted?18:3);
            delta.y+=hand*Mathf.Sin(t*1.5f+.5f)*(Highlighted?18:3);
            delta.y+=nod*Mathf.Sin(t*2.1f)*(Speaking?5f:1.2f);
            delta.x+=lean*8*p.y + (1-entrance)*-28;
            delta.y+=Mathf.Sin(t*.85f+p.x*3)*4*Mathf.Pow(1-p.y,2);
            var v=UIVertex.simpleVert;v.color=tint;
            v.position=new Vector3(x0+p.x*w+delta.x,y0+p.y*h+delta.y,0);
            v.uv0=new Vector2(Actor.crop.x+p.x*Actor.crop.width,Actor.crop.y+p.y*Actor.crop.height);
            vh.AddVert(v);
        }
        for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++){
            int i=y*(Columns+1)+x;vh.AddTriangle(i,i+Columns+1,i+1);vh.AddTriangle(i+1,i+Columns+1,i+Columns+2);
        }
    }
    static float Gaussian(Vector2 p,Vector2 radius){p/=radius;return Mathf.Exp(-Vector2.Dot(p,p)*2);}
}
