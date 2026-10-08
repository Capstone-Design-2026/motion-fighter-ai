using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Soft ink-cloud veil. Unseen nodes are not rendered at all, so opacity never leaks destinations.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StoryMapFog : MaskableGraphic
{
    public readonly List<Vector2> revealed = new List<Vector2>();
    public float radius = 190f;
    public readonly List<Vector2> routePoints = new List<Vector2>();
    public float routeRadius = 95f;
    float[] clearance;
    Rect cachedRect;
    int cachedPlaces=-1,cachedRoutes=-1;
    // Ambient clouds are decorative: they never change discovered destinations or click targets.
    public bool ambientOnly;
    public float driftSpeed = 9f;
    float nextFrame;
    void Update()
    {
        if (Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + .12f;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float cell = Mathf.Max(32f,Mathf.Sqrt(r.width*r.height/14000f));
        int nx = Mathf.CeilToInt(r.width/cell), ny = Mathf.CeilToInt(r.height/cell);
        // The static reveal mask is cached; animated clouds do not repeat route-distance searches.
        int count=(nx+1)*(ny+1);
        if(!ambientOnly && (clearance==null || clearance.Length!=count || cachedRect!=r || cachedPlaces!=revealed.Count || cachedRoutes!=routePoints.Count)) {
            clearance=new float[count];cachedRect=r;cachedPlaces=revealed.Count;cachedRoutes=routePoints.Count;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++) {
                Vector2 point=new Vector2(r.xMin+r.width*x/nx,r.yMin+r.height*y/ny);
                float d=float.MaxValue;
                foreach(var known in revealed)d=Mathf.Min(d,Vector2.Distance(point,known)-radius);
                foreach(var route in routePoints)d=Mathf.Min(d,Vector2.Distance(point,route)-routeRadius);
                clearance[y*(nx+1)+x]=d;
            }
        }
        // Each vertex is shared between neighboring cells; keep well below uGUI's vertex limit.
        for (int y=0; y<=ny; y++) for (int x=0; x<=nx; x++)
        {
            Vector2 point = new Vector2(r.xMin+r.width*x/nx,r.yMin+r.height*y/ny);
            float distance = ambientOnly?0:clearance[y*(nx+1)+x];
            // Broad banks, smaller billows and fine wisps move at slightly different speeds.
            // Coverage varies spatially instead of laying a near-opaque grey sheet over the painting.
            float drift = Time.unscaledTime * driftSpeed;
            float broad = Mathf.PerlinNoise((point.x-drift)*.0035f,point.y*.005f);
            float billow = Mathf.PerlinNoise((point.x+drift*.7f)*.011f+37,point.y*.014f+19);
            float wisp = Mathf.PerlinNoise(point.x*.025f+73,(point.y+drift*.5f)*.021f);
            float density = broad*.55f + billow*.32f + wisp*.13f;
            float edge = Mathf.SmoothStep(0,1,Mathf.InverseLerp(-35,115,
                distance+(billow-.5f)*35));
            float cloud = Mathf.SmoothStep(0,1,Mathf.InverseLerp(.30f,.72f,density));
            // Pale, low-contrast mist stays opaque enough to conceal unexplored artwork.
            // Softness comes from color and wider feathering, not exposing future landmarks.
            Color c = ambientOnly
                ? Color.Lerp(new Color(.89f,.90f,.87f),new Color(.98f,.97f,.93f),cloud)
                : Color.Lerp(new Color(.925f,.925f,.895f),new Color(.975f,.967f,.932f),cloud);
            c.a = ambientOnly ? cloud*.15f : edge * Mathf.Lerp(.86f,.93f,cloud);
            vh.AddVert(point,c,Vector2.zero);
        }
        for (int y=0;y<ny;y++) for(int x=0;x<nx;x++)
        {
            int i=y*(nx+1)+x;
            vh.AddTriangle(i,i+nx+1,i+1);vh.AddTriangle(i+1,i+nx+1,i+nx+2);
        }
    }
}
