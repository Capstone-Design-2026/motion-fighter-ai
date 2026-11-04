using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tracked 2D lip/jaw puppet over the original cinematic; never modifies its media file.</summary>
public class StorySpeakingImage : RawImage
{
    [Serializable] public class Face { public float time, x, y, width, angle, confidence; }
    [Serializable] public class Track { public Face[] entry, hold; }
    TextAsset asset;
    Track track;
    StoryMouth mouth;
    Face face;
    float amount;
    public void Speech(StoryDialogueLine line, double time, bool holding, float opening)
    {
        if (asset != line.videoSpeechTrack)
        {
            asset = line.videoSpeechTrack;
            track = asset != null ? JsonUtility.FromJson<Track>(asset.text) : null;
        }
        var frames = track == null ? null : holding ? track.hold : track.entry;
        face = null; amount = 0;
        if (frames != null && frames.Length > 0)
        {
            int k = 0;
            while (k + 1 < frames.Length && frames[k+1].time <= time) k++;
            var a = frames[k]; var b = frames[Mathf.Min(k+1, frames.Length-1)];
            float f = b.time > a.time ? Mathf.Clamp01(((float)time-a.time)/(b.time-a.time)) : 0;
            face = new Face { x=Mathf.Lerp(a.x,b.x,f), y=Mathf.Lerp(a.y,b.y,f), width=Mathf.Lerp(a.width,b.width,f), angle=Mathf.Lerp(a.angle,b.angle,f) };
            if (Mathf.Min(a.confidence,b.confidence) >= .55f) amount = opening;
        }
        if (mouth == null) mouth = StoryMouth.Create(transform);
        var r = rectTransform.rect;
        Vector2 p = face != null ? new Vector2((face.x-.5f)*r.width, (face.y-.5f)*r.height) : Vector2.zero;
        mouth.Pose(p, face != null ? face.width*r.width : 0, amount, face != null ? face.angle : 0);
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        if (face == null || amount < .065f) { base.OnPopulateMesh(vh); return; }
        vh.Clear(); var r = rectTransform.rect;
        const int cols=100, rows=56;
        for(int y=0;y<=rows;y++)for(int x=0;x<=cols;x++)
        {
            float u=(float)x/cols, v=(float)y/rows;
            float dx=(u-face.x)/Mathf.Max(.008f,face.width*1.2f);
            float dy=(v-face.y+.008f)/Mathf.Max(.008f,face.width*1.3f);
            float jaw=Mathf.Exp(-(dx*dx+dy*dy)*2)*amount*face.width*r.width*.15f;
            vh.AddVert(new Vector3(r.xMin+u*r.width,r.yMin+v*r.height-jaw),color,new Vector2(uvRect.x+u*uvRect.width,uvRect.y+v*uvRect.height));
        }
        for(int y=0;y<rows;y++)for(int x=0;x<cols;x++){int i=y*(cols+1)+x;vh.AddTriangle(i,i+cols+1,i+1);vh.AddTriangle(i+1,i+cols+1,i+cols+2);}
    }
}
