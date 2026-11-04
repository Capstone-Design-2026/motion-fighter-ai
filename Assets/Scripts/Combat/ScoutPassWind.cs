using UnityEngine;

/// <summary>Lightweight cloth motion. Only small flag meshes are updated; colliders are unchanged.</summary>
[RequireComponent(typeof(MeshFilter))]
public sealed class ScoutPassWind : MonoBehaviour
{
    public float amplitude=.12f;
    public float speed=1.8f;
    public float phase;
    Mesh mesh;Vector3[] rest,points;
    void Awake()
    {
        mesh=GetComponent<MeshFilter>().mesh;
        mesh.MarkDynamic();rest=mesh.vertices;points=new Vector3[rest.Length];
    }
    void Update()
    {
        if(mesh==null)return;
        float time=Time.time*speed+phase;
        for(int i=0;i<rest.Length;i++)
        {
            Vector3 p=rest[i];float loose=Mathf.Clamp01(p.x);
            p.z+=amplitude*loose*(Mathf.Sin(time+p.x*3.5f+p.y*2)+.35f*Mathf.Sin(time*1.7f+p.y*5));
            points[i]=p;
        }
        mesh.vertices=points;mesh.RecalculateNormals();mesh.RecalculateBounds();
    }
    void OnDestroy(){if(mesh!=null)Destroy(mesh);}
}
