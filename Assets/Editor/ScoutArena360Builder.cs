using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Expand the authored side-view pass into a complete arena. Combat objects are left intact.</summary>
public static class ScoutArena360Builder
{
    const string Root="Assets/Environments/ScoutPass";
    static Transform world;
    static Material soil,grass;
    static System.Random random;
    static float Rand(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    public static void BuildAndPreview(){Build();ScoutPassVerification.Run();}
    [MenuItem("Tools/VisionStriker/Build Scout 360 Arena")]
    public static void Build()
    {
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the current scene first.");
        AssetDatabase.Refresh();
        var scene=EditorSceneManager.OpenScene(ScoutPassBuilder.ScenePath);
        var environment=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Scout Pass"));
        bool extended=environment.transform.Find("Scout 360 Extension")!=null;
        if(extended)Object.DestroyImmediate(environment.transform.Find("Scout 360 Extension").gameObject);
        foreach(string name in new[]{"DistantMountainBackdrop","Terrain","PathsideGrass"})
        {var old=environment.transform.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);}
        if(!extended)
        {
            // Reserve a 17m radius for every camera angle. Existing landmarks remain on the north side.
            foreach(Transform child in environment.transform)
            {
                if(child.GetComponent<LODGroup>()==null&&!child.name.StartsWith("CultSignalFlag")&&child.name!="ConvoySupplies")continue;
                Vector3 p=child.position;float radius=new Vector2(p.x,p.z).magnitude;
                if(radius<20)
                {Vector2 radial=new Vector2(p.x,p.z).normalized*20;child.position=new Vector3(radial.x,p.y,radial.y);}
            }
        }
        world=new GameObject("Scout 360 Extension").transform;world.SetParent(environment.transform,false);
        random=new System.Random(1017);
        soil=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/MossyEarth.mat");
        grass=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/MountainGrass.mat");
        Ground();Mountains();Surroundings();Grass();Sky();
        foreach(var lod in environment.GetComponentsInChildren<LODGroup>())GroundProp(lod);
        foreach(var lod in environment.GetComponentsInChildren<LODGroup>())AddCameraHull(lod);
        var camera=Camera.main;camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=360;
        camera.nearClipPlane=.10f;camera.fieldOfView=47;
        var orbit=camera.GetComponent<Battle3DCamera>();orbit.orbitArena=true;orbit.allowMouseOrbit=true;
        orbit.automaticCombatFraming=true;orbit.maximumMovementYaw=14;orbit.movementYawPerMetre=3.2f;
        orbit.movementDeadZone=.2f;orbit.orbitSmoothTime=.38f;orbit.maximumOrbitSpeed=40;orbit.manualReturnDelay=2.5f;
        orbit.minimumDistance=5.4f;orbit.maximumDistance=16;orbit.combatPitch=8;orbit.cameraBoundaryRadius=17;
        camera.transform.position=new Vector3(0,2.02f,-6.8f);camera.transform.LookAt(new Vector3(0,1.05f,0));
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=42;RenderSettings.fogEndDistance=285;
        RenderSettings.fogColor=new Color(.57f,.66f,.71f);
        RenderSettings.ambientSkyColor=new Color(.58f,.66f,.73f);
        CombatFramingInstaller.ApplyToCamera(camera);
        ScoutStageDressing.ApplyToScene();
        EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("[Scout360] Complete terrain, three mountain rings, four-sided prop dressing, animated cloud sky and orbit camera installed.");
    }
    static float Height(float x,float z)
    {
        float radius=new Vector2(x,z).magnitude;
        float rise=Mathf.SmoothStep(0,1,(radius-18)/16f);
        return -.015f+rise*(.5f+Mathf.PerlinNoise(x*.055f+28,z*.055f+6)*2.7f);
    }
    static void Ground()
    {
        const int n=180;var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
        {float px=x-n*.5f,pz=z-n*.5f;v.Add(new Vector3(px,Height(px,pz),pz));uv.Add(new Vector2(px,pz)/4);}
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x;tr.AddRange(new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2});}
        MeshObject("Arena360Ground",v,uv,tr,soil);
    }
    static void Mountains()
    {
        // Closed rings have real near/far surfaces and shared seam positions; no camera-facing cards.
        for(int ring=0;ring<3;ring++)
        {
            int segments=384,rows=22;float radius=ring==0?83:ring==1?132:207;
            float width=ring==0?38:ring==1?52:72;
            var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
            for(int j=0;j<=rows;j++)for(int i=0;i<=segments;i++)
            {
                float angle=(i%segments)*Mathf.PI*2/segments;
                float cx=Mathf.Cos(angle),cz=Mathf.Sin(angle),u=j/(float)rows;
                float peak=.55f+.45f*Mathf.PerlinNoise(cx*4.2f+ring*9+17,cz*4.2f+ring*3+11);
                peak+=.20f*Mathf.Sin(angle*11+ring*1.7f)+.09f*Mathf.Sin(angle*27+ring);
                float height=(ring==0?19:ring==1?27:38)*peak*Mathf.Pow(Mathf.Max(0,Mathf.Sin(u*Mathf.PI)),.8f);
                float r=radius+u*width+Mathf.Sin(angle*7+ring)*5;
                float noise=(Mathf.PerlinNoise(cx*r*.21f+12,cz*r*.21f+22)-.5f)*3*Mathf.Sin(u*Mathf.PI);
                v.Add(new Vector3(cx*r,Mathf.Max(-2,height+noise-2),cz*r));uv.Add(new Vector2(cx*r,cz*r)/8);
            }
            for(int j=0;j<rows;j++)for(int i=0;i<segments;i++)
            {int a=j*(segments+1)+i;tr.AddRange(new[]{a,a+1,a+segments+1,a+1,a+segments+2,a+segments+1});}
            var mat=Material("MountainRing"+ring,new Color(.46f-ring*.025f,.51f-ring*.015f,.51f),"Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/CliffRock_basecolor.png"));
            mat.SetFloat("_Cull",0);mat.SetFloat("_Smoothness",.02f);
            var g=MeshObject("MountainRing"+ring,v,uv,tr,mat);
            g.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            g.GetComponent<Renderer>().receiveShadows=false;
        }
    }
    static void Surroundings()
    {
        // South is the path back into the valley, east the cliff bank, west a pine ridge.
        // All props remain outside the swept camera area; use the same authored four assets.
        for(int i=0;i<22;i++)
        {
            float angle=(i*360f/22+Rand(-5,5))*Mathf.Deg2Rad;
            if(Mathf.Sin(angle)>.5f)continue; // existing north dressing
            float radius=Rand(28,44);
            Place("CliffRock",Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,Rand(1.35f,2.5f),Rand(0,360));
        }
        for(int i=0;i<27;i++)
        {
            float angle=(i*360f/27+Rand(-7,7))*Mathf.Deg2Rad;
            if(Mathf.Sin(angle)>.55f)continue;
            float radius=Rand(23,52);
            Place("BentPine",Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,Rand(.85f,1.65f),Rand(0,360));
        }
        for(int i=0;i<75;i++)
        {
            float angle=Rand(0,Mathf.PI*2),r=Rand(21,62);
            Place("LowBoulder",Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,Rand(.25f,1.2f),Rand(0,360));
        }
        for(int i=0;i<7;i++)Place("TimberFence",-11+i*3.3f,-22,1.1f,175+Rand(-8,8));
        Place("BentPine",-19,-15,1.9f,220);Place("CliffRock",-23,-17,2.2f,125);
        Place("BentPine",19,-21,1.7f,110);Place("CliffRock",24,-12,2.4f,280);
        for(int i=0;i<65;i++)
        {
            float a=Rand(0,Mathf.PI*2),r=Rand(8,18);float x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(Mathf.Abs(z)<3.0f&&Mathf.Abs(x)<8)continue;
            Place("LowBoulder",x,z,Rand(.055f,.11f),Rand(0,360));
        }
        // Distant tree lines interrupt identical radial silhouettes.
        for(int i=0;i<25;i++)
        {float a=i*Mathf.PI*2/25,r=Rand(58,76);Place("BentPine",Mathf.Cos(a)*r,Mathf.Sin(a)*r,Rand(1.5f,2.6f),Rand(0,360));}
    }
    static void Place(string name,float x,float z,float scale,float yaw)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+name+".prefab");
        var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,world);
        g.transform.position=new Vector3(x,Height(x,z)-(name.Contains("Rock")||name.Contains("Boulder")?.3f:0),z);
        g.transform.localScale=Vector3.one*scale;g.transform.rotation=Quaternion.Euler(0,yaw,0);
    }
    static void Grass()
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int i=0;i<4400;i++)
        {
            float a=Rand(0,Mathf.PI*2),radius=Rand(7.5f,65),x=Mathf.Cos(a)*radius,z=Mathf.Sin(a)*radius;
            if(Mathf.Abs(z)<3&&Mathf.Abs(x)<8)continue;
            if(Mathf.Abs(x-Mathf.Sin(z*.1f)*3)<3.3f)continue;
            for(int b=0;b<4;b++)
            {
                float angle=Rand(0,Mathf.PI*2),h=radius<18?Rand(.07f,.18f):Rand(.15f,.48f);Vector3 p=new Vector3(x,Height(x,z),z),side=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.025f;
                int k=v.Count;v.Add(p-side);v.Add(p+side);v.Add(p+Vector3.up*h+side*2);uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.up);
                tr.AddRange(new[]{k,k+2,k+1});
            }
        }
        MeshObject("Arena360Grass",v,uv,tr,grass);
    }
    static void Sky()
    {
        string path=Root+"/Textures/CloudLayer.png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
        importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;
        importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;importer.SaveAndReimport();
        var mat=Material("CloudSky360",Color.white,"VisionStriker/Scout 360 Sky");
        mat.SetTexture("_CloudTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        mat.SetColor("_SkyTop",new Color(.25f,.43f,.65f));mat.SetColor("_SkyHorizon",new Color(.70f,.77f,.79f));
        mat.SetFloat("_CloudSpeed",.0035f);mat.SetFloat("_CloudStrength",.8f);RenderSettings.skybox=mat;
    }
    static void GroundProp(LODGroup lod)
    {
        var rs=lod.GetLODs()[0].renderers;float bottom=rs.Min(r=>r.bounds.min.y);
        Vector3 p=lod.transform.position;float inset=lod.name.Contains("Rock")||lod.name.Contains("Boulder")?.12f:.03f;
        lod.transform.position+=Vector3.up*(Height(p.x,p.z)-inset-bottom);
    }
    static void AddCameraHull(LODGroup lod)
    {
        if(lod.transform.Find("CameraHull")!=null)Object.DestroyImmediate(lod.transform.Find("CameraHull").gameObject);
        var renderers=lod.GetLODs()[0].renderers;if(renderers.Length==0)return;
        Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        var hull=new GameObject("CameraHull",typeof(BoxCollider),typeof(BattleCameraObstacle));hull.layer=2;hull.transform.position=bounds.center;
        hull.GetComponent<BoxCollider>().size=bounds.size;hull.GetComponent<BoxCollider>().isTrigger=true;hull.transform.SetParent(lod.transform,true);
    }
    static Material Material(string name,Color color,string shader)
    {
        string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
        m.shader=Shader.Find(shader);if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
        m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    static GameObject MeshObject(string name,List<Vector3> v,List<Vector2> uv,List<int> t,Material material)
    {
        string path=Root+"/Meshes/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool fresh=mesh==null;
        if(fresh)mesh=new Mesh{name=name};else mesh.Clear();
        mesh.indexFormat=v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
        if(v.Any(p=>float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.y)))throw new InvalidOperationException("Invalid terrain vertices: "+name);
        mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        if(fresh)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(world,false);
        g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<Renderer>().sharedMaterial=material;return g;
    }
}
