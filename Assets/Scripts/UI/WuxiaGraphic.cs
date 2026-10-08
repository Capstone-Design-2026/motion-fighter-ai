using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free decorative geometry. All ornaments ignore input.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class WuxiaGraphic : MaskableGraphic
{
    public enum Shape { Disc, Ring, Mist, Leaf, Swords, Pavilion, Fortress, Fade, Cloud, Plaque, PlaqueFrame, Resume, Guide, Exit, Traveler }
    public Shape shape;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if(shape==Shape.Traveler)
        {
            // Cloaked silhouette and head; the base marks the precise route position.
            Poly(vh,r,new[]{new Vector2(-.08f,.25f),new Vector2(.10f,.25f),new Vector2(.25f,-.35f),new Vector2(-.26f,-.35f)});
            Poly(vh,r,new[]{new Vector2(-.10f,.32f),new Vector2(-.06f,.45f),new Vector2(.06f,.45f),new Vector2(.12f,.32f),new Vector2(.04f,.25f)});
            Stroke(vh,r,new Vector2(-.30f,-.42f),new Vector2(.30f,-.42f),.06f);return;
        }
        if(shape==Shape.Resume)
        {
            // Open circular arrow: return to the saved journey.
            for(int i=0;i<24;i++)
            {
                float a=(45+i*11)*Mathf.Deg2Rad,b=(45+(i+1)*11)*Mathf.Deg2Rad;
                Stroke(vh,r,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*.32f,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*.32f,.075f);
            }
            Poly(vh,r,new[]{new Vector2(.10f,.32f),new Vector2(.35f,.35f),new Vector2(.30f,.10f)});
            return;
        }
        if(shape==Shape.Guide)
        {
            // An open manual, with a visible spine and two lines per page.
            Vector2[] pts={new Vector2(-.40f,.32f),new Vector2(-.08f,.27f),new Vector2(0,.19f),new Vector2(.08f,.27f),new Vector2(.40f,.32f),new Vector2(.40f,-.30f),new Vector2(.08f,-.34f),new Vector2(0,-.40f),new Vector2(-.08f,-.34f),new Vector2(-.40f,-.30f)};
            for(int i=0;i<pts.Length;i++)Stroke(vh,r,pts[i],pts[(i+1)%pts.Length],.055f);
            Stroke(vh,r,new Vector2(0,.19f),new Vector2(0,-.38f),.045f);
            for(int i=0;i<2;i++)for(int side=-1;side<=1;side+=2)
                Stroke(vh,r,new Vector2(side*.12f,.08f-i*.18f),new Vector2(side*.30f,.12f-i*.18f),.04f);
            return;
        }
        if(shape==Shape.Exit)
        {
            Stroke(vh,r,new Vector2(.02f,.36f),new Vector2(-.35f,.36f),.065f);
            Stroke(vh,r,new Vector2(-.35f,.36f),new Vector2(-.35f,-.36f),.065f);
            Stroke(vh,r,new Vector2(-.35f,-.36f),new Vector2(.02f,-.36f),.065f);
            Stroke(vh,r,new Vector2(-.12f,0),new Vector2(.38f,0),.07f);
            Stroke(vh,r,new Vector2(.20f,.19f),new Vector2(.40f,0),.07f);
            Stroke(vh,r,new Vector2(.40f,0),new Vector2(.20f,-.19f),.07f);
            return;
        }
        if(shape==Shape.Cloud)
        {
            // Overlapping soft lobes make a cloud silhouette, rather than a flat fog strip.
            for(int k=0;k<7;k++)
            {
                float x=(k-3)*r.width*.115f;
                float y=Mathf.Sin(k*1.9f)*r.height*.09f;
                float ry=r.height*(.22f+.08f*Mathf.Sin(k*2.3f+1));
                for(int band=0;band<12;band++)
                {
                    float inner=band/12f,outer=(band+1)/12f;
                    Color ci=color,co=color;ci.a*=Mathf.Pow(1-inner,1.35f);co.a*=Mathf.Pow(1-outer,1.35f);
                    for(int j=0;j<40;j++)
                    {
                        float a=j*Mathf.PI/20,b=(j+1)*Mathf.PI/20;
                        Vector2 center=r.center+new Vector2(x,y);
                        Vector2 u=new Vector2(Mathf.Cos(a)*r.width*.21f,Mathf.Sin(a)*ry);
                        Vector2 v=new Vector2(Mathf.Cos(b)*r.width*.21f,Mathf.Sin(b)*ry);
                        Quad(vh,center+u*inner,center+v*inner,center+v*outer,center+u*outer,ci,co);
                    }
                }
            }
            return;
        }
        if(shape==Shape.Plaque||shape==Shape.PlaqueFrame)
        {
            float cut=12;
            Vector2[] points={new Vector2(r.xMin+cut,r.yMin),new Vector2(r.xMin,r.yMin+cut),new Vector2(r.xMin,r.yMax-cut),new Vector2(r.xMin+cut,r.yMax),new Vector2(r.xMax-cut,r.yMax),new Vector2(r.xMax,r.yMax-cut),new Vector2(r.xMax,r.yMin+cut),new Vector2(r.xMax-cut,r.yMin)};
            if(shape==Shape.Plaque)
            {
                int first=vh.currentVertCount;foreach(var p in points)vh.AddVert(p,color,Vector2.zero);
                for(int i=1;i<7;i++)vh.AddTriangle(first,first+i,first+i+1);
            }
            else for(int i=0;i<8;i++)
            {
                Vector2 a=points[i],b=points[(i+1)%8];
                Vector2 ai=new Vector2(Mathf.Lerp(a.x,r.center.x,3/r.width),Mathf.Lerp(a.y,r.center.y,3/r.height));
                Vector2 bi=new Vector2(Mathf.Lerp(b.x,r.center.x,3/r.width),Mathf.Lerp(b.y,r.center.y,3/r.height));
                Quad(vh,a,b,bi,ai,color,color);
            }
            return;
        }
        if (shape == Shape.Fade)
        {
            Quad(vh, new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin),color,new Color(color.r,color.g,color.b,0)); return;
        }
        if (shape == Shape.Disc || shape == Shape.Ring || shape == Shape.Mist)
        {
            const int n=64;
            int bands=shape==Shape.Mist?10:1;
            for(int b=0;b<bands;b++)
            {
                float inner=shape==Shape.Ring?.91f:(float)b/bands;
                float outer=shape==Shape.Ring?1f:(float)(b+1)/bands;
                Color ci=color,co=color;
                if(shape==Shape.Mist){ci.a*=Mathf.Pow(1-inner,2);co.a*=Mathf.Pow(1-outer,2);}
                for(int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n, z=(i+1)*Mathf.PI*2/n;
                    Vector2 p=new Vector2(Mathf.Cos(a)*r.width/2,Mathf.Sin(a)*r.height/2),q=new Vector2(Mathf.Cos(z)*r.width/2,Mathf.Sin(z)*r.height/2);
                    Quad(vh,r.center+p*inner,r.center+q*inner,r.center+q*outer,r.center+p*outer,ci,co);
                }
            }
            return;
        }
        if(shape==Shape.Leaf){Poly(vh,r,new[]{new Vector2(-.5f,0),new Vector2(-.1f,.24f),new Vector2(.5f,0),new Vector2(.05f,-.2f)});return;}
        if(shape==Shape.Swords)
        {
            Stroke(vh,r,new Vector2(-.28f,-.32f),new Vector2(.30f,.34f),.075f);
            Stroke(vh,r,new Vector2(.28f,-.32f),new Vector2(-.30f,.34f),.075f);
            Stroke(vh,r,new Vector2(-.38f,-.13f),new Vector2(-.10f,-.35f),.065f);
            Stroke(vh,r,new Vector2(.38f,-.13f),new Vector2(.10f,-.35f),.065f);
            return;
        }
        // Roof, pillars and lintel: resting pavilion / fortified final gate.
        Poly(vh,r,new[]{new Vector2(-.43f,.12f),new Vector2(0,.40f),new Vector2(.43f,.12f),new Vector2(.32f,.04f),new Vector2(-.32f,.04f)});
        Stroke(vh,r,new Vector2(-.25f,.03f),new Vector2(-.25f,-.30f),.06f);
        Stroke(vh,r,new Vector2(.25f,.03f),new Vector2(.25f,-.30f),.06f);
        Stroke(vh,r,new Vector2(-.40f,-.32f),new Vector2(.40f,-.32f),.06f);
        if(shape==Shape.Fortress)
        {
            Stroke(vh,r,new Vector2(-.12f,-.28f),new Vector2(-.12f,-.04f),.065f);
            Stroke(vh,r,new Vector2(.12f,-.28f),new Vector2(.12f,-.04f),.065f);
            Stroke(vh,r,new Vector2(-.12f,-.04f),new Vector2(.12f,-.04f),.065f);
            Stroke(vh,r,new Vector2(-.25f,.39f),new Vector2(.25f,.39f),.05f);
        }
    }
    void Stroke(VertexHelper vh,Rect r,Vector2 a,Vector2 b,float width)
    {
        Vector2 d=(b-a).normalized,p=new Vector2(-d.y,d.x)*width/2;
        Poly(vh,r,new[]{a-p,a+p,b+p,b-p});
    }
    void Poly(VertexHelper vh,Rect r,Vector2[] points)
    {
        int start=vh.currentVertCount;
        foreach(var p in points) vh.AddVert(new Vector3(r.center.x+p.x*r.width,r.center.y+p.y*r.height),color,Vector2.zero);
        for(int i=1;i<points.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);
    }
    static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color left,Color right)
    {
        int i=vh.currentVertCount;vh.AddVert(a,left,Vector2.zero);vh.AddVert(b,left,Vector2.zero);vh.AddVert(c,right,Vector2.zero);vh.AddVert(d,right,Vector2.zero);
        vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
}

