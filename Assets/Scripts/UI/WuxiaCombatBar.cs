using UnityEngine;
using UnityEngine.UI;

/// <summary>벡터로 그리는 청동 프레임. 텍스처 없이 해상도에 맞게 선명하게 표시한다.</summary>
[AddComponentMenu("UI/Wuxia Combat Bar")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WuxiaCombatBar : MaskableGraphic
{
    public bool resource;
    public bool mirror;
    public Color fillColor = new Color(.9f, .59f, .15f);
    public Color trailColor = new Color(.97f, .84f, .53f);
    public float FillRatio { get; private set; }
    public float TrailRatio { get; private set; }
    public bool Ready { get; private set; }
    public bool Available { get; private set; } = true;
    static readonly Color Bronze = new Color(.48f, .37f, .22f);
    static readonly Color LightBronze = new Color(.76f, .64f, .42f);

    public void SetValues(float value, float trail, bool ready = false, bool available = true)
    {
        value = Mathf.Clamp01(value); trail = Mathf.Clamp01(trail);
        if (FillRatio == value && TrailRatio == trail && Ready == ready && Available == available) return;
        FillRatio = value; TrailRatio = trail; Ready = ready; Available = available;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float w = r.width, h = r.height;
        if (w < 60 || h < 4) return;
        // All points are measured in local pixels from the bottom-left; mirror the whole ornament.
        Bevel(vh, r, 1, -3, w-1, h-3, resource ? 8 : 20, new Color(0,0,0,.55f), new Color(0,0,0,.55f));
        Bevel(vh, r, 0, 0, w, h, resource ? 9 : 21, new Color(.10f,.09f,.07f), new Color(.23f,.20f,.14f));
        Bevel(vh, r, 2, 2, w-2, h-2, resource ? 8 : 20, Bronze*.65f, LightBronze);
        Bevel(vh, r, 4, 4, w-4, h-4, resource ? 7 : 18, new Color(.035f,.039f,.037f), new Color(.10f,.11f,.10f));
        float x = resource ? 11 : 28, y = resource ? 4 : 8;
        float cut = resource ? 5 : 9;
        float right = w-x, top = h-y;
        Bevel(vh, r, x-1, y-1, right+1, top+1, cut, new Color(.31f,.25f,.16f), new Color(.34f,.29f,.20f));
        Bevel(vh, r, x, y, right, top, cut, new Color(.045f,.045f,.04f), new Color(.10f,.08f,.055f));
        if (resource)
            Bevel(vh, r, x, y, right, top, cut, new Color(.025f,.06f,.08f), new Color(.065f,.12f,.15f));
        if (Available)
        {
            Fill(vh,r,x,y,right,top,cut,TrailRatio,trailColor*.5f,trailColor);
            var upper=Color.Lerp(fillColor,Color.white,Ready?.52f:.28f);
            Fill(vh,r,x,y,right,top,cut,FillRatio,Color.Lerp(fillColor,Color.black,.37f),upper);
            // Fine upper reflection; remains clipped to the actual amount of health/charge.
            float end=x+(right-x)*FillRatio;
            if(end>x+cut+2)
                Line(vh,r,new Vector2(x+cut,top-1.3f),new Vector2(Mathf.Min(end,right-cut),top-1.3f),1,new Color(1,.94f,.75f,resource?.26f:.52f));
        }
        if (!resource)
        {
            // Etched border: small breaks and double lines, rather than a large decorative panel.
            Line(vh,r,new Vector2(32,h-3),new Vector2(w-32,h-3),.6f,new Color(.94f,.82f,.57f,.6f));
            Line(vh,r,new Vector2(32,3),new Vector2(w-32,3),.8f,new Color(.61f,.47f,.26f,.75f));
            for(int i=0;i<2;i++) Ornament(vh,r,i==0?17:w-17,h*.5f,h*.46f);
        }
    }

    void Ornament(VertexHelper vh,Rect r,float x,float y,float size)
    {
        Diamond(vh,r,x,y,size,new Color(.055f,.056f,.05f));
        DiamondOutline(vh,r,x,y,size,1.4f,Bronze);
        DiamondOutline(vh,r,x,y,size*.64f,.9f,LightBronze);
        Diamond(vh,r,x,y,3.5f,new Color(.27f,.24f,.17f));
        Line(vh,r,new Vector2(x-size*.75f,y),new Vector2(x+size*.75f,y),.7f,Bronze);
        Line(vh,r,new Vector2(x,y-size*.75f),new Vector2(x,y+size*.75f),.7f,Bronze);
        // Four folded, spear-like details inside each end cap.
        for(int side=-1;side<=1;side+=2)
        {
            Line(vh,r,new Vector2(x+side*size*.3f,y+size*.15f),new Vector2(x+side*size*.53f,y+size*.34f),1,LightBronze);
            Line(vh,r,new Vector2(x+side*size*.3f,y-size*.15f),new Vector2(x+side*size*.53f,y-size*.34f),1,Bronze);
        }
    }
    void Diamond(VertexHelper v,Rect r,float x,float y,float s,Color c)
        => Quad(v,r,new Vector2(x-s,y),new Vector2(x,y-s),new Vector2(x+s,y),new Vector2(x,y+s),c,c);
    void DiamondOutline(VertexHelper v,Rect r,float x,float y,float s,float thick,Color c)
    {
        Vector2[] p={new Vector2(x-s,y),new Vector2(x,y-s),new Vector2(x+s,y),new Vector2(x,y+s)};
        for(int i=0;i<4;i++)Line(v,r,p[i],p[(i+1)%4],thick,c);
    }
    void Bevel(VertexHelper v,Rect r,float l,float b,float right,float t,float cut,Color cb,Color ct)
    {
        if(t<=b||right<=l)return;
        float mid=(b+t)*.5f;cut=Mathf.Min(cut,(right-l)*.45f);
        Quad(v,r,new Vector2(l+cut,b),new Vector2(right-cut,b),new Vector2(right,mid),new Vector2(l,mid),cb,Color.Lerp(cb,ct,.5f));
        Quad(v,r,new Vector2(l,mid),new Vector2(right,mid),new Vector2(right-cut,t),new Vector2(l+cut,t),Color.Lerp(cb,ct,.5f),ct);
    }
    void Fill(VertexHelper v,Rect r,float l,float b,float right,float t,float cut,float ratio,Color cb,Color ct)
    {
        if(ratio<=0)return;
        // Split at the clipping corner as well as at each reflection band. Very small HP remains proportional.
        float end=Mathf.Lerp(l,right,ratio);
        float corner=Mathf.Clamp01((end-l)/cut)*.5f;
        float[] levels={0,corner,.5f,.74f,1-corner,1};
        System.Array.Sort(levels);
        for(int i=0;i<levels.Length-1;i++)
        {
            float a=levels[i],z=levels[i+1];if(z-a<.0001f)continue;
            float ca=cut*Mathf.Abs(a*2-1),cz=cut*Mathf.Abs(z*2-1);
            float la=l+ca,lz=l+cz,ra=Mathf.Min(right-ca,end),rz=Mathf.Min(right-cz,end);
            if(ra<la&&rz<lz)continue;
            Color c0=Color.Lerp(cb,ct,a),c1=Color.Lerp(cb,ct,z);
            Quad(v,r,new Vector2(la,Mathf.Lerp(b,t,a)),new Vector2(Mathf.Max(la,ra),Mathf.Lerp(b,t,a)),
                new Vector2(Mathf.Max(lz,rz),Mathf.Lerp(b,t,z)),new Vector2(lz,Mathf.Lerp(b,t,z)),c0,c1);
        }
    }
    void Line(VertexHelper v,Rect r,Vector2 a,Vector2 b,float width,Color c)
    {
        Vector2 d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;
        Quad(v,r,a-n,b-n,b+n,a+n,c,c);
    }
    void Quad(VertexHelper v,Rect r,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color low,Color high)
    {
        int i=v.currentVertCount;
        v.AddVert(Point(r,a),low,Vector2.zero);v.AddVert(Point(r,b),low,Vector2.zero);
        v.AddVert(Point(r,c),high,Vector2.zero);v.AddVert(Point(r,d),high,Vector2.zero);
        if(mirror){v.AddTriangle(i,i+2,i+1);v.AddTriangle(i,i+3,i+2);}
        else {v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
    }
    Vector2 Point(Rect r,Vector2 p)=>new Vector2(r.xMin+(mirror?r.width-p.x:p.x),r.yMin+p.y);
}
