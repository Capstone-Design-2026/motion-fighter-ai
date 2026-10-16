using UnityEngine;
using UnityEngine.UI;

/// <summary>Bounded camera drift within a source image. Never changes the image aspect.</summary>
public class StorySceneMotion : MonoBehaviour
{
    public RawImage background;
    public Rect view=new Rect(0,0,1,1);
    float elapsed;
    void Update(){
        if(background==null)return;elapsed+=Time.unscaledDeltaTime;
        var r=view;
        float marginX=Mathf.Min(.004f,Mathf.Min(r.xMin,1-r.xMax));
        float marginY=Mathf.Min(.003f,Mathf.Min(r.yMin,1-r.yMax));
        r.x+=Mathf.Sin(elapsed*.14f)*Mathf.Max(0,marginX);
        r.y+=Mathf.Sin(elapsed*.11f)*Mathf.Max(0,marginY);
        background.uvRect=r;
    }
}
