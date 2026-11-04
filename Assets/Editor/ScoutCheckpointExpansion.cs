using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Reusable checkpoint dressing, assembled entirely from existing project assets.</summary>
public static class ScoutCheckpointExpansion
{
    const string Base="Assets/Environments/ScoutPass";
    static Transform root;
    static Material timber,cloth,roof,stone,green,leaf;
    static System.Random rng;
    static float Rand(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
    public static void Apply(Transform originalDressing)
    {
        var old=originalDressing.parent.Find("Checkpoint Surroundings");
        if(old)Object.DestroyImmediate(old.gameObject);
        root=new GameObject("Checkpoint Surroundings").transform;root.SetParent(originalDressing.parent,false);
        rng=new System.Random(10202026);
        timber=Mat("CheckpointTimber",new Color(.66f,.60f,.49f),"Assets/Naganeupseong/Resource/Textures/Pillar/T_Pillar02a_BC.png");
        cloth=Mat("ConvoyCanvas",new Color(.40f,.31f,.21f),"Assets/Naganeupseong/Resource/Textures/Prop/T_Straw_Bag_BC.png");cloth.SetFloat("_Cull",0);
        roof=Mat("LookoutThatch",new Color(.55f,.54f,.46f),"Assets/Naganeupseong/Resource/Textures/Roof/T_Roof01a_01_BC.png");
        roof.SetFloat("_Cull",0);
        stone=Mat("RoadCurbStone",new Color(.64f,.65f,.59f),Base+"/Textures/CliffRock_basecolor.png");
        green=Mat("CheckpointUndergrowth",new Color(.39f,.42f,.23f));green.SetFloat("_Cull",0);
        leaf=Mat("CheckpointLeafLitter",new Color(.39f,.25f,.12f));leaf.SetFloat("_Cull",0);
        Shelter();Lookout();ClearIntersectingPines();SupplyClusters(originalDressing);RoadBanks();GroundCover();SurroundingDetails();
        foreach(var r in root.GetComponentsInChildren<Renderer>())r.receiveShadows=true;
        if(root.GetComponentsInChildren<Collider2D>().Length!=0)throw new Exception("Scenery must not introduce combat colliders.");
        if(root.GetComponentsInChildren<Collider>().Any(c=>c.gameObject.layer!=2||!c.isTrigger))throw new Exception("Only camera-only hulls are allowed.");
        var p=Object.FindAnyObjectByType<PlayerController>();var e=Object.FindAnyObjectByType<EnemyAI>();
        Physics.SyncTransforms();
        foreach(var c in root.GetComponentsInChildren<Collider>())
            if(c.bounds.Contains(p.transform.position+Vector3.up)||c.bounds.Contains(e.transform.position+Vector3.up))
                throw new Exception("Checkpoint props overlap the fighting lane: "+c.transform.parent.name+" "+c.bounds);
        var args=Environment.GetCommandLineArgs();int output=Array.IndexOf(args,"-scoutReport");
        if(output>=0) {
            Directory.CreateDirectory(args[output+1]);
            File.WriteAllText(Path.Combine(args[output+1],"expansion_inventory.txt"),
                "Existing assets only; no external models or textures downloaded.\n"+
                "New 2D combat colliders: "+root.GetComponentsInChildren<Collider2D>().Length+
                "\nNew camera-only hulls: "+root.GetComponentsInChildren<BattleCameraObstacle>().Length+
                "\nNew renderers (includes inactive LOD levels): "+root.GetComponentsInChildren<Renderer>(true).Length+
                "\nNew moving cloth panels: "+root.GetComponentsInChildren<ScoutPassWind>().Length+
                "\nShared render materials: "+root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count());
        }
        AssetDatabase.SaveAssets();
    }
    static float H(float x,float z)
    {
        float rise=Mathf.SmoothStep(0,1,(new Vector2(x,z).magnitude-18)/16f);
        return -.015f+rise*(.5f+Mathf.PerlinNoise(x*.055f+28,z*.055f+6)*2.7f);
    }
    static float RoadX(float z)=>z<19.5f?Mathf.Lerp(.3f,1.45f,Mathf.SmoothStep(0,1,(z-3)/16.5f)):1.45f+Mathf.Sin((z-19.5f)*.16f)*4.2f;
    static Material Mat(string name,Color tint,string texture=null)
    {
        var path=Base+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",tint);m.SetFloat("_Smoothness",.08f);m.enableInstancing=true;
        if(texture!=null)m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        EditorUtility.SetDirty(m);return m;
    }
    static Transform Group(string name,Vector3 pos,float yaw=0)
    {
        var t=new GameObject(name).transform;t.SetParent(root,false);
        t.SetPositionAndRotation(pos+Vector3.up*H(pos.x,pos.z),Quaternion.Euler(0,yaw,0));return t;
    }
    static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 size,Material mat,Quaternion? rotation=null)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;
        g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=size;
        if(rotation.HasValue)g.transform.localRotation=rotation.Value;
        g.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(g.GetComponent<Collider>());return g;
    }
    static void Beam(Transform parent,Vector3 a,Vector3 b,float thick,Material mat)
    {Box(parent,"Timber", (a+b)*.5f,new Vector3(thick,(b-a).magnitude,thick),mat,Quaternion.FromToRotation(Vector3.up,b-a));}
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        mesh.name=name;mesh.RecalculateBounds();
        var path=Base+"/Meshes/"+name+".asset";var stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(stored){EditorUtility.CopySerialized(mesh,stored);EditorUtility.SetDirty(stored);Object.DestroyImmediate(mesh);return stored;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static GameObject MeshObject(Transform parent,string name,List<Vector3> v,List<int> tri,Material mat,List<Vector2> uv=null)
    {
        var mesh=new Mesh{name=name};if(v.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(v);mesh.SetTriangles(tri,0);
        mesh.SetUVs(0,uv??v.Select(p=>new Vector2(p.x,p.z)*.25f).ToList());
        mesh.RecalculateNormals();mesh.RecalculateTangents();
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);
        g.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,name);g.GetComponent<Renderer>().sharedMaterial=mat;return g;
    }
    static void Combine(Transform parent,string name)
    {
        // Keep structure draw calls low; animated cloth and existing LOD prefabs remain separate.
        var renderers=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>r.GetComponent<ScoutPassWind>()==null).ToArray();
        int i=0;
        foreach(var set in renderers.GroupBy(r=>r.sharedMaterial))
        {
            var pieces=set.Select(r=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=parent.worldToLocalMatrix*r.transform.localToWorldMatrix}).ToArray();
            var mesh=new Mesh{name=name+"_"+i};mesh.CombineMeshes(pieces,true,true);
            var g=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);
            g.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,mesh.name);g.GetComponent<MeshRenderer>().sharedMaterial=set.Key;i++;
        }
        foreach(var r in renderers)Object.DestroyImmediate(r.gameObject);
    }
    static Bounds Bounds(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>().Where(r=>!(r is ParticleSystemRenderer)).ToArray();
        var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;
    }
    static void ClearIntersectingPines()
    {
        // Retain the existing tree assets, moving only branches that cross the new structures.
        var occupied=new[]{Bounds(root.Find("Convoy shelter")),Bounds(root.Find("Mountain lookout"))};
        for(int i=0;i<occupied.Length;i++)occupied[i].Expand(.65f);
        foreach(var lod in root.parent.GetComponentsInChildren<LODGroup>())
        {
            if(lod.transform.IsChildOf(root)||!lod.name.Contains("BentPine"))continue;
            var b=Bounds(lod.transform);
            if(!occupied.Any(o=>o.Intersects(b)))continue;
            var before=lod.transform.position;
            for(int step=0;step<5&&occupied.Any(o=>o.Intersects(b));step++)
            {
                lod.transform.position+=new Vector3(b.center.x<1.45f?-6f:6f,0,4f);
                b=Bounds(lod.transform);
                lod.transform.position+=Vector3.up*(H(b.center.x,b.center.z)-b.min.y-.025f);
                b=Bounds(lod.transform);
            }
            Debug.Log("[ScoutCheckpoint] Tree clearance "+before+" -> "+lod.transform.position);
        }
    }
    static void Hull(Transform t)
    {
        var b=Bounds(t);var g=new GameObject("CameraHull",typeof(BoxCollider),typeof(BattleCameraObstacle));
        g.layer=2;g.transform.position=b.center;g.GetComponent<BoxCollider>().size=b.size;g.GetComponent<BoxCollider>().isTrigger=true;g.transform.SetParent(t,true);
    }
    static GameObject Prop(string name,Vector3 position,float span,float yaw,bool cameraHull=true)
    {
        var path="Assets/Naganeupseong/Prefabs/Prop/"+name+".prefab";
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)throw new FileNotFoundException(path);
        var g=(GameObject)PrefabUtility.InstantiatePrefab(asset,root);g.name="Convoy · "+name;
        g.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        foreach(var c in g.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        foreach(var c in g.GetComponentsInChildren<Collider2D>())Object.DestroyImmediate(c);
        var b=Bounds(g.transform);g.transform.localScale*=span/Mathf.Max(b.size.x,b.size.y,b.size.z);
        b=Bounds(g.transform);g.transform.position+=new Vector3(position.x-b.center.x,H(position.x,position.z)+position.y-b.min.y,position.z-b.center.z);
        if(cameraHull&&Bounds(g.transform).size.y>.5f)Hull(g.transform);return g;
    }
    static GameObject Rock(Vector3 p,float scale,float yaw,string prefab="LowBoulder")
    {
        var g=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base+"/Prefabs/"+prefab+".prefab"),root);
        g.name="Grounded bank · "+prefab;g.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));g.transform.localScale=Vector3.one*scale;
        foreach(var c in g.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        var b=Bounds(g.transform);g.transform.position+=new Vector3(p.x-b.center.x,H(p.x,p.z)+p.y-b.min.y-.025f,p.z-b.center.z);
        return g;
    }
    static void Roof(Transform parent,string name,float halfWidth,float halfDepth,float eaves,float ridge,Material material)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
        const int n=24,m=10;
        for(int z=0;z<=m;z++)for(int x=0;x<=n;x++)
        {
            float u=x/(float)n,w=z/(float)m,px=(u*2-1)*halfWidth;
            float y=Mathf.Lerp(ridge,eaves,Mathf.Abs(px)/halfWidth)+.12f*Mathf.Pow(Mathf.Abs(px)/halfWidth,5);
            v.Add(new Vector3(px,y,(w*2-1)*halfDepth));
            uv.Add(material==roof?new Vector2(w*2,Mathf.Abs(px)/halfWidth*1.2f):new Vector2(u*2,w));
        }
        for(int z=0;z<m;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x;tri.AddRange(new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2});}
        MeshObject(parent,name,v,tri,material,uv);
    }
    static void Shelter()
    {
        var g=Group("Convoy shelter",new Vector3(-8.7f,0,16.1f),-8);
        foreach(float x in new[]{-3f,3f})foreach(float z in new[]{-2f,2f})
            Beam(g,new Vector3(x,0,z),new Vector3(x,2.9f,z),.18f,timber);
        foreach(float z in new[]{-2f,2f})
        {Beam(g,new Vector3(-3.2f,2.65f,z),new Vector3(3.2f,2.65f,z),.16f,timber);
         Beam(g,new Vector3(-3,1.7f,z),new Vector3(-2,2.65f,z),.11f,timber);
         Beam(g,new Vector3(3,1.7f,z),new Vector3(2,2.65f,z),.11f,timber);}
        Beam(g,new Vector3(0,3.55f,-2.5f),new Vector3(0,3.55f,2.5f),.13f,timber);
        Roof(g,"ConvoyCanvasRoof",3.5f,2.6f,2.65f,3.55f,cloth);
        // An open front keeps the supplies visible. A low back panel breaks the empty horizon.
        Box(g,"Rear canvas screen",new Vector3(0,1.25f,2.05f),new Vector3(5.8f,2.25f,.035f),cloth);
        Combine(g,"ConvoyShelter");
        var flap=Banner(g,"CanvasWindValance",new Vector3(-3.4f,2.73f,-2.6f),6.8f,.40f,cloth,.025f);
        flap.transform.localRotation=Quaternion.identity;
        Hull(g);
        Prop("Rice_Chest",new Vector3(-10,0,15),1.35f,-12);
        Prop("Straw_Bag",new Vector3(-8.3f,0,16.5f),1.15f,20);
        Prop("Straw_Bag",new Vector3(-7.3f,0,16.4f),1.1f,-25);
        Prop("Straw_Bag",new Vector3(-7.8f,.55f,16.4f),.9f,55);
        Prop("Water_Jar",new Vector3(-11,0,14.8f),1.05f,10);
        Prop("Basket01a",new Vector3(-6.4f,0,14.5f),.7f,-10);
        Prop("Low_Wooden_Bench",new Vector3(-9.2f,0,13.7f),2f,-8);
    }
    static GameObject Banner(Transform parent,string name,Vector3 pos,float width,float height,Material mat,float amplitude)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();const int nx=18,ny=5;
        for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){v.Add(new Vector3(x*width/nx,-y*height/ny,0));uv.Add(new Vector2(x/(float)nx,y/(float)ny));}
        for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int a=y*(nx+1)+x;t.AddRange(new[]{a,a+1,a+nx+1,a+1,a+nx+2,a+nx+1});}
        var g=MeshObject(parent,name,v,t,mat,uv);g.transform.localPosition=pos;
        var wind=g.AddComponent<ScoutPassWind>();wind.amplitude=amplitude;wind.speed=1.35f;wind.phase=3.7f;return g;
    }
    static void Lookout()
    {
        var g=Group("Mountain lookout",new Vector3(9.6f,0,18.8f),-12);
        const float deck=3.1f;
        foreach(float x in new[]{-1.35f,1.35f})foreach(float z in new[]{-1.35f,1.35f})
            Beam(g,new Vector3(x,0,z),new Vector3(x,5.2f,z),.22f,timber);
        for(int i=0;i<11;i++)Box(g,"Deck planks",new Vector3(-1.5f+i*.3f,deck,0),new Vector3(.28f,.15f,3.3f),timber);
        foreach(float z in new[]{-1.35f,1.35f}) {
            Beam(g,new Vector3(-1.35f,.25f,z),new Vector3(1.35f,2.9f,z),.12f,timber);
            Beam(g,new Vector3(1.35f,.25f,z),new Vector3(-1.35f,2.9f,z),.12f,timber);
            Beam(g,new Vector3(-1.5f,4.05f,z),new Vector3(1.5f,4.05f,z),.12f,timber);
            for(int i=0;i<7;i++)Beam(g,new Vector3(-1.35f+i*.45f,deck+.15f,z),new Vector3(-1.35f+i*.45f,4.05f,z),.065f,timber);
        }
        foreach(float x in new[]{-1.35f,1.35f}) {
            Beam(g,new Vector3(x,4.05f,-1.35f),new Vector3(x,4.05f,1.35f),.12f,timber);
            Beam(g,new Vector3(x,.25f,-1.35f),new Vector3(x,2.9f,1.35f),.13f,timber);
            Beam(g,new Vector3(x,.25f,1.35f),new Vector3(x,2.9f,-1.35f),.13f,timber);
        }
        foreach(float x in new[]{-.38f,.38f})Beam(g,new Vector3(x,0,-3.0f),new Vector3(x,3.2f,-1.5f),.09f,timber);
        for(int i=1;i<12;i++){float u=i/12f;Beam(g,new Vector3(-.38f,u*3.2f,Mathf.Lerp(-3,-1.5f,u)),new Vector3(.38f,u*3.2f,Mathf.Lerp(-3,-1.5f,u)),.075f,timber);}
        Roof(g,"LookoutSlateRoof",2.0f,1.95f,5.1f,6.05f,roof);
        Beam(g,new Vector3(0,6.05f,-2f),new Vector3(0,6.05f,2f),.16f,timber);
        foreach(float z in new[]{-1.95f,1.95f}) {
            Beam(g,new Vector3(-2,5.17f,z),new Vector3(0,6.07f,z),.12f,timber);
            Beam(g,new Vector3(0,6.07f,z),new Vector3(2,5.17f,z),.12f,timber);
        }
        Combine(g,"LookoutStructure");
        Banner(g,"LookoutCultBanner",new Vector3(-1.1f,4.8f,-1.48f),.9f,1.9f,AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/CultCloth.mat"),.07f);
        Hull(g);
        Prop("Rice_Chest",new Vector3(11.5f,0,17.1f),1.4f,15);
        Prop("Straw_Jar01a",new Vector3(12.3f,0,18),1.05f,0);
        Prop("Cart",new Vector3(14.5f,0,22.5f),3.9f,155);
    }
    static void SupplyClusters(Transform original)
    {
        var cart=original.parent.Find("ConvoySupplies");
        if(cart){cart.rotation=Quaternion.Euler(0,-22,0);var b=Bounds(cart);cart.position+=new Vector3(-7.9f-b.center.x,H(-7.9f,11.7f)-b.min.y,11.7f-b.center.z);}
        Prop("Straw_Bag",new Vector3(-9.8f,0,11.5f),.85f,65);
        Prop("Wicker_Basket",new Vector3(-6.2f,0,11.2f),.65f,-10);
        Prop("Jars",new Vector3(8.8f,0,12.1f),1.25f,20);
        Prop("Rice_Straw",new Vector3(10.1f,0,13),1.1f,-20);
        var racks=Group("Stored timber",new Vector3(12,0,14.5f),18);
        for(int i=0;i<9;i++){int row=i<5?0:1;int col=row==0?i:i-5;Beam(racks,new Vector3((col-2)*.27f+row*.14f,.14f+row*.25f,-1.1f),new Vector3((col-2)*.27f+row*.14f,.14f+row*.25f,1.1f),.24f,timber);}
        Combine(racks,"StoredTimber");Hull(racks);
    }
    static void RoadBanks()
    {
        // Soft mounds blend back below the old ground; the central fighting plane is untouched.
        var soil=AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/MossyEarth.mat");
        foreach(var spec in new[]{new Vector4(-7,29,10,2.4f),new Vector4(12,32,11,3.0f),new Vector4(-18,-21,8,1.5f),new Vector4(19,-19,9,1.8f)})
        {
            var v=new List<Vector3>();var tr=new List<int>();const int n=32;
            for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){
                float px=spec.x+(x/(float)n*2-1)*spec.z,pz=spec.y+(z/(float)n*2-1)*spec.z;
                float radius=new Vector2((px-spec.x)/spec.z,(pz-spec.y)/spec.z).magnitude;
                float mound=Mathf.Pow(Mathf.Max(0,1-radius*radius),2)*spec.w;
                if(pz>15)mound*=Mathf.SmoothStep(0,1,(Mathf.Abs(px-RoadX(pz))-2.6f)/3.0f);
                v.Add(new Vector3(px,H(px,pz)+mound-.012f,pz));
            }
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x;tr.AddRange(new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2});}
            MeshObject(root,"ConvoyBank_"+(int)spec.x+"_"+(int)spec.y,v,tr,soil);
        }
        for(int i=0;i<32;i++){
            float z=21+i*.85f;
            foreach(float side in new[]{-1f,1f})Rock(new Vector3(RoadX(z)+side*Rand(2.2f,3.0f),0,z),Rand(.10f,.20f),Rand(0,360));
        }
        var steps=Group("Worn mountain road stones",Vector3.zero);
        for(int i=0;i<9;i++){
            float z=23.3f+i*1.25f;
            Box(steps,"Inset stepping slab",new Vector3(RoadX(z)+Rand(-.1f,.1f),H(RoadX(z),z)+.025f,z),new Vector3(Rand(1.5f,2.1f),.11f,.55f),stone,Quaternion.Euler(0,Rand(-7,7),0));
        }
        Combine(steps,"WornRoadStones");
    }
    static void GroundCover()
    {
        var blades=new List<Vector3>();var tris=new List<int>();var leaves=new List<Vector3>();var lt=new List<int>();
        var clusters=new[]{new Vector2(-7,-2),new Vector2(-10,5),new Vector2(-5,8),new Vector2(8,4),new Vector2(10,10),new Vector2(6,-6),new Vector2(-9,-8),new Vector2(-16,8),new Vector2(16,9),new Vector2(14,-14),new Vector2(-12,-16),new Vector2(4,-18),new Vector2(-12,18),new Vector2(14,20)};
        foreach(var c in clusters)for(int i=0;i<45;i++)
        {
            float a=Rand(0,Mathf.PI*2),r=Mathf.Sqrt(Rand(0,1))*2.7f;
            var p=new Vector3(c.x+Mathf.Cos(a)*r,0,c.y+Mathf.Sin(a)*r);
            if(Mathf.Abs(p.x)<6.8f&&Mathf.Abs(p.z)<2.8f)continue;
            p.y=H(p.x,p.z);
            for(int b=0;b<4;b++){
                float angle=Rand(0,Mathf.PI*2),height=Rand(.10f,.25f),width=Rand(.010f,.022f);
                var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));int k=blades.Count;
                var bend=new Vector3(-dir.z,0,dir.x)*height*.30f;
                var middle=p+Vector3.up*height*.6f+bend*.3f;
                blades.AddRange(new[]{p-dir*width,p+dir*width,middle-dir*width*.55f,middle+dir*width*.55f,p+Vector3.up*height+bend});
                tris.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+2,k+4,k+3});
            }
            if(i%7==0)Rock(new Vector3(p.x,0,p.z),.045f+Rand(0,.055f),Rand(0,360));
            for(int j=0;j<2;j++){
                float angle=Rand(0,Mathf.PI*2);var u=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.1f;
                var w=new Vector3(-u.z,0,u.x)*.45f;var pos=p+new Vector3(Rand(-.6f,.6f),.018f,Rand(-.6f,.6f));int k=leaves.Count;
                leaves.AddRange(new[]{pos-u,pos+w,pos+u,pos-w});lt.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
            }
        }
        var grass=MeshObject(root,"ClusteredUndergrowth",blades,tris,green);grass.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        var litter=MeshObject(root,"ConvoyLeafLitter",leaves,lt,leaf);litter.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        // Low pine growth anchors the cliffs without filling the fighters' silhouette.
        foreach(var p in new[]{new Vector3(-14,0,10),new Vector3(15,0,12),new Vector3(-13,0,-12),new Vector3(14,0,-14)})
            Rock(p,.25f,Rand(0,360),"BentPine");
    }
    static void SurroundingDetails()
    {
        // Detail the back and flanks as well, so orbiting never reveals an empty reverse set.
        foreach(var p in new[]{new Vector3(-15,0,-19),new Vector3(12,0,-22),new Vector3(-19,0,5),new Vector3(20,0,2)})
        {
            Rock(p,.6f,Rand(0,360));Rock(p+new Vector3(1.5f,0,1.1f),.28f,Rand(0,360));
        }
        Prop("Cart",new Vector3(-13,0,-23),3.5f,125);
        Prop("Straw_Bag",new Vector3(-11.6f,0,-21.5f),1f,20);
        Prop("Water_Jar",new Vector3(13.5f,0,-22),1.15f,0);
        Prop("Rice_Chest",new Vector3(14.5f,0,-23.1f),1.3f,40);
    }
}
