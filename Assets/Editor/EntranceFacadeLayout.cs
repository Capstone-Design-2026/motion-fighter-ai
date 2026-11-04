using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

/// <summary>Approved continuous entrance frontage, with persistent reusable chamfered meshes.</summary>
public static class EntranceFacadeLayout
{
    static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);}
    static Bounds AllBounds(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).ToArray();
        if(rs.Length==0)return new Bounds(t.position,Vector3.zero);
        var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
    }
    static Mesh ChamferedBox(Vector3 size,float bevel)
    {
        var h=size*.5f;float b=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.8f);
        var inner=h-Vector3.one*b;var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        Action<Vector3[],Vector3> face=(points,n)=>
        {
            if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),n)<0)Array.Reverse(points);
            int start=vertices.Count;foreach(var p in points){vertices.Add(p);uv.Add(Mathf.Abs(n.y)>.5f?new Vector2(p.x,p.z):Mathf.Abs(n.x)>.5f?new Vector2(p.z,p.y):new Vector2(p.x,p.y));}
            for(int k=1;k<points.Length-1;k++){tris.Add(start);tris.Add(start+k);tris.Add(start+k+1);}
        };
        for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
        {
            int u=(axis+1)%3,v=(axis+2)%3;var points=new Vector3[4];int[] us={-1,1,1,-1},vs={-1,-1,1,1};
            for(int k=0;k<4;k++){var p=Vector3.zero;p[axis]=sign*h[axis];p[u]=us[k]*inner[u];p[v]=vs[k]*inner[v];points[k]=p;}
            var normal=Vector3.zero;normal[axis]=sign;face(points,normal);
        }
        for(int a=0;a<3;a++)for(int c=a+1;c<3;c++)for(int sa=-1;sa<=1;sa+=2)for(int sc=-1;sc<=1;sc+=2)
        {
            int along=3-a-c;var p0=Vector3.zero;var p1=Vector3.zero;
            p0[a]=sa*h[a];p0[c]=sc*inner[c];p0[along]=-inner[along];
            p1[a]=sa*inner[a];p1[c]=sc*h[c];p1[along]=-inner[along];
            var p2=p1;var p3=p0;p2[along]=p3[along]=inner[along];var n=Vector3.zero;n[a]=sa;n[c]=sc;
            face(new[]{p0,p1,p2,p3},n.normalized);
        }
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
        {
            var signs=new Vector3(x,y,z);var p0=Vector3.Scale(signs,inner);var p1=p0;var p2=p0;
            p0.x=x*h.x;p1.y=y*h.y;p2.z=z*h.z;face(new[]{p0,p1,p2},signs.normalized);
        }
        var mesh=new Mesh{name="Chamfered entrance masonry"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        string key=size.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+size.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+size.z.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"_"+bevel.ToString("F3",System.Globalization.CultureInfo.InvariantCulture);
        string path=StageAtmosphereInstaller.Root+"/Meshes/Entrance_"+key+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved){Object.DestroyImmediate(mesh);return saved;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static Renderer FacadeBlock(Transform parent,string name,Vector3 p,Vector3 size,Material mat,float bevel=.10f)
    {
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);g.transform.position=p;
        g.GetComponent<MeshFilter>().sharedMesh=ChamferedBox(size,bevel);var r=g.GetComponent<MeshRenderer>();r.sharedMaterial=mat;return r;
    }
    public static void Apply(Transform env)
    {
        if(env.Find("Fortress wall - continuous entrance facade")) return;
        Directory.CreateDirectory(StageAtmosphereInstaller.Root+"/Meshes");
        AssetDatabase.Refresh();
        var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/Environments/StoryStages/Materials/entrance_Stone.mat");
        var roof=AssetDatabase.LoadAssetAtPath<Material>("Assets/Environments/StoryStages/Materials/WeatheredRoof.mat");
        var door=env.Cast<Transform>().First(t=>t.name=="GateDoors");var doorBounds=AllBounds(door);var doorStart=door.position;
        float plane=26,wallHeight=6.16f,edge=36,wallDepth=2.35f;
        var children=env.Cast<Transform>().ToArray();int moved=0;
        var movements=new List<string>{"object,old_x,old_z,new_x,new_z,minimum_z"};
        foreach(var t in children)
        {
            bool rock=t.name=="CliffRock"||t.name=="BankOutcrop"||t.name=="LowBoulder";
            if(!rock)continue;var bounds=AllBounds(t);
            // Anything breaking the gate frontage is placed completely behind its rear face.
            if(bounds.max.z>10&&bounds.min.z<plane+wallDepth*.5f+4&&Mathf.Abs(bounds.center.x)<56)
            {
                var old=t.position;float rear=plane+wallDepth*.5f+5+Mathf.Abs(bounds.center.x)*.055f;
                t.position+=Vector3.forward*(rear-bounds.min.z);
                movements.Add(t.name+","+old.x+","+old.z+","+t.position.x+","+t.position.z+","+AllBounds(t).min.z);moved++;
            }
        }
        foreach(var t in children)
        {
            if(t.name=="Fortress wall"&&Mathf.Abs(t.position.z-plane)<.2f&&Mathf.Abs(t.position.x)<25)t.gameObject.SetActive(false);
            if(t.name.StartsWith("GorgeCornerHall"))t.position=new Vector3(Mathf.Sign(t.position.x)*28,wallHeight+.20f,plane);
            if(t.name=="BentPine")
            {
                var b=AllBounds(t);if(b.max.z>18&&b.min.z<30&&Mathf.Abs(b.center.x)<39)t.position+=Vector3.forward*(33-b.min.z);
            }
        }
        var group=new GameObject("Fortress wall - continuous entrance facade").transform;group.SetParent(env,false);
        var darkIron=AssetDatabase.LoadAssetAtPath<Material>("Assets/Environments/StoryStages/Materials/BlackIron.mat");
        FacadeBlock(group,"Recessed gate backing",new Vector3(doorBounds.center.x,doorBounds.center.y,doorBounds.max.z+.10f),new Vector3(doorBounds.size.x+.65f,doorBounds.size.y+.24f,.36f),darkIron,.04f);
        // Extend under the existing jambs. Keep the original detailed central doors and gatehouse.
        float start=doorBounds.extents.x+.28f;var wings=new List<Renderer>();
        for(int side=-1;side<=1;side+=2)
        {
            float width=edge-start,cx=side*(edge+start)*.5f;
            wings.Add(FacadeBlock(group,"Continuous stone wing",new Vector3(cx,wallHeight*.5f-.06f,plane),new Vector3(width,wallHeight+.12f,wallDepth),stone,.10f));
            FacadeBlock(group,"Continuous softened coping",new Vector3(cx,wallHeight+.13f,plane),new Vector3(width+.10f,.38f,wallDepth+.22f),roof,.08f);
            FacadeBlock(group,"Continuous low stone footing",new Vector3(cx,.23f,plane-.05f),new Vector3(width+.10f,.62f,wallDepth+.30f),stone,.08f);
            for(float distance=start+.55f;distance<edge-.30f;distance+=1.45f)
                FacadeBlock(group,"Soft-edged battlement",new Vector3(side*distance,wallHeight+.64f,plane),new Vector3(.96f,.90f,wallDepth*.80f),stone,.12f);
            foreach(float distance in new[]{12f,22f,35.2f})
            {
                FacadeBlock(group,"Integrated stone buttress",new Vector3(side*distance,wallHeight*.47f,plane-.14f),new Vector3(.80f,wallHeight*.94f,wallDepth+.38f),stone,.11f);
                FacadeBlock(group,"Buttress shoulder",new Vector3(side*distance,wallHeight-.12f,plane-.14f),new Vector3(1.03f,.36f,wallDepth+.50f),roof,.08f);
            }
        }
        foreach(var wing in wings)
        {
            var hull=new GameObject("CameraHull",typeof(BoxCollider),typeof(BattleCameraObstacle));
            hull.layer=2;hull.transform.SetParent(group,false);hull.transform.position=wing.bounds.center;
            var box=hull.GetComponent<BoxCollider>();box.size=wing.bounds.size;box.isTrigger=true;
        }
        var frame=env.Cast<Transform>().First(t=>t.name=="Joined stone gate");var frameBounds=AllBounds(frame);
        Check(wings.All(r=>r.bounds.Intersects(frameBounds)),"Connected front wings overlap the original gate frame");
        Check(wings.All(r=>r.bounds.min.y<0),"Facade foundations sink below continuous arena ground");
        Check(moved>0,"Foreground cliffs moved behind gate facade");
        Check(door.position==doorStart,"Detailed gate doors retain placement");
        File.WriteAllLines(StageAtmosphereInstaller.Output+"/rock_placement.csv",movements);
        File.WriteAllText(StageAtmosphereInstaller.Output+"/layout.txt","Applied. Continuous 72m front facade; central original door retained; wall height 6.16m; rocks moved behind facade: "+moved+"; chamfered wall edges; original combat collision unchanged.");
    }
}
