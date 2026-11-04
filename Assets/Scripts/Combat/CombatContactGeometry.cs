using UnityEngine;

/// <summary>Capsules projected onto the established XY combat plane. Z is retained for effects.</summary>
public static class CombatContactGeometry
{
    public struct Capsule
    {
        public Vector3 a, b; public float radius;
        public Capsule(Vector3 a, Vector3 b, float radius) { this.a=a; this.b=b; this.radius=radius; }
        public static Capsule Lerp(Capsule a,Capsule b,float t) => new Capsule(Vector3.Lerp(a.a,b.a,t),Vector3.Lerp(a.b,b.b,t),Mathf.Lerp(a.radius,b.radius,t));
    }
    // Closest points of two finite segments, including points and parallel segments.
    public static float Distance(Capsule a,Capsule b,out Vector3 pa,out Vector3 pb)
    {
        Vector2 p=a.a,q=b.a,d1=(Vector2)(a.b-a.a),d2=(Vector2)(b.b-b.a),r=p-q;
        float aa=Vector2.Dot(d1,d1),ee=Vector2.Dot(d2,d2),ff=Vector2.Dot(d2,r),s,t;
        const float epsilon=1e-8f;
        if(aa<=epsilon&&ee<=epsilon){s=t=0;}
        else if(aa<=epsilon){s=0;t=Mathf.Clamp01(ff/ee);}
        else{
            float cc=Vector2.Dot(d1,r);
            if(ee<=epsilon){t=0;s=Mathf.Clamp01(-cc/aa);}
            else{
                float bb=Vector2.Dot(d1,d2),den=aa*ee-bb*bb;
                s=den>epsilon?Mathf.Clamp01((bb*ff-cc*ee)/den):0;
                t=(bb*s+ff)/ee;
                if(t<0){t=0;s=Mathf.Clamp01(-cc/aa);}else if(t>1){t=1;s=Mathf.Clamp01((bb-cc)/aa);}
            }
        }
        pa=Vector3.Lerp(a.a,a.b,s);pb=Vector3.Lerp(b.a,b.b,t);
        return Vector2.Distance(pa,pb);
    }
    static float Travel(Capsule a,Capsule b)=>Mathf.Max(((Vector2)(a.a-b.a)).magnitude,((Vector2)(a.b-b.b)).magnitude)+Mathf.Abs(a.radius-b.radius);

    /// <summary>Conservative advancement: tests the complete relative path, not just endpoints.</summary>
    public static bool Sweep(Capsule a0,Capsule a1,Capsule b0,Capsule b1,float from,float to,out float time,out Vector3 point)
    {
        time=from;point=default;if(to<from)return false;
        float speed=Travel(a0,a1)+Travel(b0,b1);
        for(int i=0;i<128;i++){
            var a=Capsule.Lerp(a0,a1,time);var b=Capsule.Lerp(b0,b1,time);
            float distance=Distance(a,b,out var pa,out var pb),gap=distance-a.radius-b.radius;
            if(gap<=.001f){
                Vector2 n=(Vector2)(pa-pb);if(n.sqrMagnitude<1e-8f)n=Vector2.right;else n.Normalize();
                point=(pa-(Vector3)n*a.radius+pb+(Vector3)n*b.radius)*.5f;return true;
            }
            if(speed<1e-7f||time>=to)return false;
            // Distance cannot decrease faster than this bound; never jump across a contact.
            float next=time+Mathf.Max(.000001f,(gap-.0005f)/speed);
            if(next>to){
                var endA=Capsule.Lerp(a0,a1,to);var endB=Capsule.Lerp(b0,b1,to);
                if(Distance(endA,endB,out _,out _)>endA.radius+endB.radius+.001f)return false;
                next=to;
            }
            time=next;
        }
        return false;
    }
}
