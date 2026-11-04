using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Builds the scout-only mountain pass from the four authored Tripo props.</summary>
public static class ScoutPassBuilder
{
    public const string Root="Assets/Environments/ScoutPass";
    public const string ScenePath="Assets/Scenes/ScoutPass.unity";
    static readonly string[] Names={"LowBoulder","CliffRock","BentPine","TimberFence"};
    static readonly Dictionary<string,GameObject> props=new Dictionary<string,GameObject>();
    static Material dirt,earth,wood,red,ink,grass;
    static Transform environment;

    public static void BuildAndPreview(){Build();ScoutArena360Builder.BuildAndPreview();}

    [MenuItem("Tools/VisionStriker/Build Scout Mountain Pass")]
    public static void Build()
    {
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the open scene before rebuilding the scout pass.");
        Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Meshes");Directory.CreateDirectory(Root+"/Prefabs");
        AssetDatabase.Refresh();
        foreach(string name in Names)
        {
            Texture(name+"_basecolor",false,2048);Texture(name+"_normal",true,1024);
            var importer=(ModelImporter)AssetImporter.GetAtPath(Root+"/Models/"+name+".fbx");
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;
            importer.isReadable=false;importer.meshCompression=ModelImporterMeshCompression.Low;
            importer.SaveAndReimport();
            MakeProp(name);
        }
        Texture("PathGround",false,2048);Texture("DistantMountains",false,2048,false);
        dirt=Mat("MountainPath",new Color(.88f,.80f,.66f),"PathGround");
        earth=Mat("MossyEarth",Color.white,"PathGround","VisionStriker/Scout Terrain");
        wood=Mat("WeatheredTimber",new Color(.27f,.22f,.15f));
        red=Mat("CultCloth",new Color(.48f,.065f,.045f));red.SetFloat("_Cull",0);
        ink=Mat("CultEmblem",new Color(.07f,.055f,.043f));ink.SetFloat("_Cull",0);
        grass=Mat("MountainGrass",new Color(.26f,.31f,.15f));grass.SetFloat("_Cull",0);
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        var old=GameObject.Find("Arena Environment 3D");
        if(old==null)throw new InvalidOperationException("Expected original environment root not found; no scene saved.");
        Object.DestroyImmediate(old);
        environment=new GameObject("Scout Pass · 청계역 감시 길목").transform;
        Terrain();Rocks();Trees();Understory();Checkpoint();Vegetation();Atmosphere();
        var camera=Camera.main;
        camera.farClipPlane=180;camera.nearClipPlane=.1f;camera.fieldOfView=47;
        camera.transform.position=new Vector3(0,2.5f,-7.8f);
        camera.transform.LookAt(new Vector3(0,1.05f,0));
        camera.backgroundColor=new Color(.63f,.73f,.79f);
        CombatFramingInstaller.ApplyToCamera(camera);
        var player=Object.FindAnyObjectByType<PlayerController>();var enemy=Object.FindAnyObjectByType<EnemyAI>();
        player.transform.position=new Vector3(-2.1f,player.transform.position.y,0);
        enemy.transform.position=new Vector3(2.1f,enemy.transform.position.y,0);
        ApplyScout(enemy);
        var eh=new SerializedObject(enemy.GetComponent<Health>());
        eh.FindProperty("displayName").stringValue="산길 척후병";eh.ApplyModifiedPropertiesWithoutUndo();
        var config=AssetDatabase.LoadAssetAtPath<RunConfig>("Assets/Resources/RunConfig.asset");
        if(config.nodeBattleScenes==null)config.nodeBattleScenes=new List<NodeBattleScene>();
        config.nodeBattleScenes.RemoveAll(x=>x!=null&&x.nodeId=="scout");
        config.nodeBattleScenes.Add(new NodeBattleScene{nodeId="scout",sceneName="ScoutPass"});EditorUtility.SetDirty(config);
        EditorSceneManager.SaveScene(scene,ScenePath);
        var scenes=EditorBuildSettings.scenes.ToList();
        var existing=scenes.FindIndex(s=>s.path==ScenePath);
        if(existing<0)scenes.Add(new EditorBuildSettingsScene(ScenePath,true));else scenes[existing].enabled=true;
        EditorBuildSettings.scenes=scenes.ToArray();
        AssetDatabase.SaveAssets();
        File.WriteAllText(Out("build_done.txt"),"ScoutPass scene and scout route created.\n"+Stats());
        Debug.Log("[ScoutPass] "+Stats());
    }

    static string Out(string f)
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-scoutReport");
        string dir=i>=0?args[i+1]:Application.temporaryCachePath;Directory.CreateDirectory(dir);return Path.Combine(dir,f);
    }
    static void Texture(string name,bool normal,int size,bool repeat=true)
    {
        string path=Root+"/Textures/"+name+".png";
        var ti=(TextureImporter)AssetImporter.GetAtPath(path);
        if(ti==null)throw new FileNotFoundException(path);
        ti.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        ti.sRGBTexture=!normal;ti.mipmapEnabled=true;ti.isReadable=false;
        ti.maxTextureSize=size;ti.textureCompression=TextureImporterCompression.Compressed;
        ti.anisoLevel=repeat?8:1;ti.wrapMode=repeat?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }
    static Material Mat(string name,Color color,string texture=null,string shader="Universal Render Pipeline/Lit")
    {
        string path=Root+"/Materials/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(material,path);}
        material.shader=Shader.Find(shader);material.SetColor("_BaseColor",color);
        material.SetFloat("_Smoothness",.07f);material.SetFloat("_Metallic",0);
        if(texture!=null)material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+texture+".png"));
        material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
    }
    static void MakeProp(string name)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/"+name+".fbx");
        var root=Object.Instantiate(prefab);root.name=name;
        var mat=Mat(name,Color.white,name+"_basecolor",name=="BentPine"?"VisionStriker/Scout Pine Wind":"Universal Render Pipeline/Lit");
        mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+name+"_normal.png"));
        mat.SetFloat("_BumpScale",.45f);mat.EnableKeyword("_NORMALMAP");
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {renderer.gameObject.SetActive(true);renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.On;}
        var lods=new LOD[3];
        for(int i=0;i<3;i++)
        {
            int level=i;
            var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.Contains("LOD"+level)).ToArray();
            if(renderers.Length==0)throw new InvalidOperationException("Missing LOD "+name+" "+i);
            lods[i]=new LOD(i==0?.15f:i==1?.04f:.006f,renderers);
        }
        var lodGroup=root.GetComponent<LODGroup>()??root.AddComponent<LODGroup>();lodGroup.SetLODs(lods);lodGroup.RecalculateBounds();
        props[name]=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+name+".prefab");Object.DestroyImmediate(root);
    }
    static GameObject Place(string name,Vector3 position,Vector3 scale,float yaw=0)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(props[name],environment);
        go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
        return go;
    }
    static GameObject Place(string name,float x,float z,float scale,float yaw=0,float sink=0)
        =>Place(name,new Vector3(x,Height(x,z)+sink,z),Vector3.one*scale,yaw);
    static float Height(float x,float z)
    {
        if(z<3.3f&&Mathf.Abs(x)<9)return -.015f;
        float rise=Mathf.Max(0,z-4)*.027f;
        float rough=(Mathf.PerlinNoise(x*.16f+22,z*.17f+13)-.5f)*.60f;
        float sides=Mathf.SmoothStep(0,1,(Mathf.Abs(x)-8)/10f)*.6f;
        return -.025f+rise+rough*Mathf.Clamp01((z-3.3f)*.4f)+sides;
    }
    static void Terrain()
    {
        const int nx=112,nz=100;var vs=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {float px=-28+x*.5f,pz=-8+z*.5f;vs.Add(new Vector3(px,Height(px,pz),pz));uv.Add(new Vector2(px,pz)/4f);}
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int a=z*(nx+1)+x;tris.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
        MeshObject("Terrain",vs,uv,tris,earth);
        // A single textured surface blends trail edges continuously; physics stays at y=0.
    }

    static void Rocks()
    {
        Place("CliffRock",-9,3.5f,1.5f,30,-.65f);Place("CliffRock",-11,7,1.8f,115,-.5f);
        Place("CliffRock",-8,9.5f,1.35f,250,-.4f);Place("CliffRock",-13,13,1.9f,170,-.7f);
        Place("CliffRock",12,7,1.3f,200,-.7f);Place("CliffRock",15,12,1.7f,90,-.6f);
        Place("CliffRock",-12,22,2.1f,35,-.8f);Place("CliffRock",-8,30,2.3f,170,-.8f);
        Place("CliffRock",11,27,1.8f,265,-.6f);Place("CliffRock",18,31,2.2f,150,-.6f);
        Place("LowBoulder",-6.7f,3.1f,1.25f,55,-.3f);Place("LowBoulder",-4.7f,5.4f,.7f,190,-.2f);
        Place("LowBoulder",6,3.5f,.9f,230,-.25f);Place("LowBoulder",8.8f,2.4f,1.2f,50,-.3f);
        Place("LowBoulder",-7,-2.5f,.8f,260,-.4f);Place("LowBoulder",7.8f,-2.7f,1,145,-.5f);
        var random=new System.Random(684);
        for(int i=0;i<30;i++)
        {
            float z=5+(float)random.NextDouble()*31;
            float x=(i%2==0?-1:1)*(7+(float)random.NextDouble()*13);
            Place("LowBoulder",x,z,.24f+(float)random.NextDouble()*.6f,(float)random.NextDouble()*360,-.12f);
        }
    }
    static void Trees()
    {
        Place("BentPine",new Vector3(-6.7f,Height(-6.7f,3.5f)-.05f,3.5f),new Vector3(1.7f,.92f,1.2f),75);
        Place("BentPine",10,6,.85f,-30,-.05f);
        Place("BentPine",-11,13,.9f,130);Place("BentPine",8.7f,16,.76f,240);
        Place("BentPine",-7,20,.82f,290);Place("BentPine",12,22,1.05f,150);
        Place("BentPine",-18,25,1.2f,50);Place("BentPine",15,34,1.3f,225);
        Place("BentPine",-12,35,1.15f,175);Place("BentPine",-2,39,.84f,75);
    }
    static void Understory()
    {
        // Reuse the same authored meshes for low pine scrub and broken rock banks.
        // Keep every plant behind the fighters or beyond the side walls.
        var rng=new System.Random(311);
        for(int i=0;i<38;i++)
        {
            float z=4.2f+(float)rng.NextDouble()*31;
            float road=Mathf.Sin((z-4)*.15f)*3+1.2f;
            float x=road+(i%2==0?-1:1)*(3.8f+(float)rng.NextDouble()*4.8f);
            float size=.12f+(float)rng.NextDouble()*.20f;
            // Small branches were too thin in the side camera; keep the understory as grass and stones.
            if(i%3==0)Place("LowBoulder",x,z,size*2.4f,(float)rng.NextDouble()*360,-.12f);
        }
        for(int i=0;i<9;i++)
        {
            float x=-15+i*4;
            Place("LowBoulder",new Vector3(x,Height(x,40)-.2f,40),new Vector3(2.1f,1.9f,2f),i*49);
        }
        Place("CliffRock",new Vector3(6.7f,Height(6.7f,36)-.4f,36),new Vector3(1.5f,1.35f,1.7f),35);
        Place("BentPine",4.2f,36,1.15f,80);Place("BentPine",-5,38,1.2f,180);
    }
    static void Checkpoint()
    {
        Place("TimberFence",6.9f,4.3f,1f,-10);Place("TimberFence",9.7f,4.8f,1f,-15);
        Place("TimberFence",-4.4f,9.7f,.85f,30);Place("TimberFence",5.9f,16.5f,.85f,20);
        float z=28,y=Height(1,z);
        var gate=new GameObject("ConvoyCheckpoint").transform;gate.SetParent(environment,false);
        Log(gate,"LeftPost",new Vector3(-.5f,y,z),new Vector3(-.5f,y+4.1f,z),.18f);
        Log(gate,"RightPost",new Vector3(3.4f,y,z),new Vector3(3.4f,y+4.1f,z),.18f);
        Log(gate,"Lintel",new Vector3(-1,y+3.8f,z),new Vector3(3.9f,y+3.8f,z),.20f);
        Log(gate,"UpperBeam",new Vector3(-.9f,y+4.14f,z),new Vector3(3.8f,y+4.14f,z),.14f);
        Log(gate,"LeftBrace",new Vector3(-.5f,y+2.9f,z),new Vector3(.4f,y+3.8f,z),.10f);
        Log(gate,"RightBrace",new Vector3(3.4f,y+2.9f,z),new Vector3(2.5f,y+3.8f,z),.10f);
        Flag(new Vector3(5.2f,Height(5.2f,3.6f),3.6f),4.1f,1.15f,2.2f,15);
        Flag(new Vector3(-1.5f,y,27.3f),4.2f,.8f,1.8f,-20);
        AddGateRoof(gate,new Vector3(1.45f,y+4.05f,z));
        var cart=AssetDatabase.FindAssets("Cart t:Prefab",new[]{"Assets/Naganeupseong/Prefabs/Prop"}).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)=="Cart");
        if(cart!=null)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(cart),environment);
            go.name="ConvoySupplies";go.transform.localPosition=new Vector3(7.3f,Height(7.3f,7),7);go.transform.localRotation=Quaternion.Euler(0,45,0);go.transform.localScale=Vector3.one*.72f;
            foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        }
    }
    static void AddGateRoof(Transform parent,Vector3 location)
    {
        var slate=Mat("CheckpointRoof",new Color(.21f,.25f,.26f));slate.SetFloat("_Cull",0);
        var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
        const int nx=92,nz=40;
        for(int zi=0;zi<=nz;zi++)for(int xi=0;xi<=nx;xi++)
        {
            float x=-2.8f+xi*5.6f/nx,z=-1.45f+zi*2.9f/nz;
            float ax=Mathf.Abs(x)/2.8f,az=Mathf.Abs(z)/1.45f;
            float y=.60f*(1-az)+.14f*az*az*az*az+.22f*Mathf.Pow(ax,4)+.025f*Mathf.Cos(x*26f);
            v.Add(location+new Vector3(x,y,z));uv.Add(new Vector2(x,z));
            if(xi<nx&&zi<nz){int a=zi*(nx+1)+xi;tri.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
        }
        var roof=MeshObject("CheckpointCurvedRoof",v,uv,tri,slate);roof.transform.SetParent(parent,true);
        Log(parent,"RoofRidge",location+new Vector3(-2.85f,.66f,0),location+new Vector3(2.85f,.66f,0),.08f);
        parent.Find("RoofRidge").GetComponent<Renderer>().sharedMaterial=slate;
    }
    static void Log(Transform parent,string name,Vector3 a,Vector3 b,float radius)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);g.name=name;g.transform.SetParent(parent,false);
        g.transform.position=(a+b)*.5f;g.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
        g.transform.localScale=new Vector3(radius*2,(b-a).magnitude*.5f,radius*2);g.GetComponent<Renderer>().sharedMaterial=wood;Object.DestroyImmediate(g.GetComponent<Collider>());
    }
    static void Flag(Vector3 basePoint,float height,float width,float length,float yaw)
    {
        var root=new GameObject("CultSignalFlag").transform;root.SetParent(environment,false);root.position=basePoint;root.rotation=Quaternion.Euler(0,yaw,0);
        Log(root,"FlagPole",basePoint,basePoint+Vector3.up*(height+.12f),.065f);
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();var emblem=new List<int>();
        const int cols=24,rows=48;
        for(int y=0;y<=rows;y++)for(int x=0;x<=cols;x++)
        {float xx=x/(float)cols,yy=y/(float)rows;v.Add(new Vector3(xx*width,-yy*length,.04f*Mathf.Sin(xx*5+yy*8)*xx));uv.Add(new Vector2(xx,yy));}
        for(int y=0;y<rows;y++)for(int x=0;x<cols;x++)
        {
            int a=y*(cols+1)+x;float xx=(x+.5f)/cols,yy=(y+.5f)/rows;
            bool mark=Mathf.Abs(xx-.52f)/.33f+Mathf.Abs(yy-.46f)/.21f<1;
            (mark?emblem:tr).AddRange(new[]{a,a+1,a+cols+1,a+1,a+cols+2,a+cols+1});
        }
        var mesh=new Mesh();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.subMeshCount=2;mesh.SetTriangles(tr,0);mesh.SetTriangles(emblem,1);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var cloth=new GameObject("WindCloth",typeof(MeshFilter),typeof(MeshRenderer),typeof(ScoutPassWind));cloth.transform.SetParent(root,false);cloth.transform.localPosition=new Vector3(.02f,height,0);
        cloth.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,"Flag_"+height.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
        cloth.GetComponent<MeshRenderer>().sharedMaterials=new[]{red,ink};cloth.GetComponent<ScoutPassWind>().phase=basePoint.x;
    }
    static void Vegetation()
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();var random=new System.Random(725);
        for(int t=0;t<1350;t++)
        {
            float x=-15+(float)random.NextDouble()*30,z=-4+(float)random.NextDouble()*33;
            float road=Mathf.Sin((z-4)*.15f)*3+1.2f;
            if(z<4&&Mathf.Abs(x)<6.8f)continue;
            if(z>=4&&Mathf.Abs(x-road)<3.3f)continue;
            for(int blade=0;blade<5;blade++)
            {
                float a=(float)random.NextDouble()*Mathf.PI*2,h=.12f+(float)random.NextDouble()*.32f;
                var center=new Vector3(x+(float)random.NextDouble()*.15f,Height(x,z),z);
                var side=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.025f;
                int i=v.Count;v.Add(center-side);v.Add(center+side);v.Add(center+new Vector3(Mathf.Cos(a)*.08f,h,Mathf.Sin(a)*.08f));
                uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(new Vector2(.5f,1));tri.AddRange(new[]{i,i+2,i+1});
            }
        }
        MeshObject("PathsideGrass",v,uv,tri,grass);
    }
    static void Atmosphere()
    {
        foreach(var light in Object.FindObjectsByType<Light>())Object.DestroyImmediate(light.gameObject);
        var sun=new GameObject("Late Afternoon Sun",typeof(Light)).GetComponent<Light>();sun.transform.SetParent(environment,false);
        sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(35,-40,0);sun.color=new Color(1,.93f,.82f);sun.intensity=1.35f;
        sun.shadows=LightShadows.Soft;sun.shadowStrength=.85f;sun.shadowBias=.035f;sun.shadowNormalBias=.25f;
        RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.55f,.65f,.72f);RenderSettings.ambientEquatorColor=new Color(.40f,.43f,.41f);RenderSettings.ambientGroundColor=new Color(.28f,.24f,.18f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=27;RenderSettings.fogEndDistance=115;RenderSettings.fogColor=new Color(.58f,.67f,.70f);
        var bg=GameObject.CreatePrimitive(PrimitiveType.Quad);bg.name="DistantMountainBackdrop";bg.transform.SetParent(environment,false);
        bg.transform.position=new Vector3(0,22,72);bg.transform.localScale=new Vector3(154,64,1);
        var mat=Mat("DistantMountains",Color.white,"DistantMountains","Universal Render Pipeline/Unlit");bg.GetComponent<Renderer>().sharedMaterial=mat;
        bg.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;bg.GetComponent<Renderer>().receiveShadows=false;Object.DestroyImmediate(bg.GetComponent<Collider>());
        var p=new GameObject("WindborneDust",typeof(ParticleSystem));p.transform.SetParent(environment,false);p.transform.position=new Vector3(0,1,3);
        var ps=p.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.duration=12;main.startLifetime=new ParticleSystem.MinMaxCurve(7,12);main.startSpeed=.08f;
        main.startSize=new ParticleSystem.MinMaxCurve(.012f,.04f);main.maxParticles=55;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startColor=new Color(.92f,.82f,.58f,.23f);
        var emission=ps.emission;emission.rateOverTime=3;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(22,4,10);
        var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.x=.18f;velocity.y=.015f;velocity.z=.03f;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();var dm=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        dm.SetFloat("_Surface",1);dm.SetFloat("_Blend",0);dm.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);dm.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);dm.SetFloat("_ZWrite",0);dm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");dm.renderQueue=3000;
        string dmPath=Root+"/Materials/Dust.mat";SaveAsset(dm,dmPath);renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(dmPath);renderer.shadowCastingMode=ShadowCastingMode.Off;
        var fade=ps.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.5f,.25f),new GradientAlphaKey(.4f,.7f),new GradientAlphaKey(0,1)});fade.color=gradient;
    }
    static void ApplyScout(EnemyAI enemy)
    {
        var view=enemy.GetComponent<Fighter3DView>();var old=view.visualRoot;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Scout/Scout_Visual.prefab"),enemy.transform);
        model.transform.localPosition=old.localPosition;model.transform.localRotation=old.localRotation;
        view.visualRoot=model.transform;view.modelAnimator=model.GetComponentInChildren<Animator>();view.fighterTint=Color.white;
        Object.DestroyImmediate(old.gameObject);
    }
    static GameObject MeshObject(string name,List<Vector3> v,List<Vector2> uv,List<int> t,Material mat)
    {
        var mesh=new Mesh{name=name};if(v.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(environment,false);
        g.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,name);g.GetComponent<MeshRenderer>().sharedMaterial=mat;return g;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {string path=Root+"/Meshes/"+name+".asset";SaveAsset(mesh,path);return AssetDatabase.LoadAssetAtPath<Mesh>(path);}
    static void SaveAsset(Object asset,string path)
    {
        var old=AssetDatabase.LoadAssetAtPath<Object>(path);
        if(old==null)AssetDatabase.CreateAsset(asset,path);
        else {EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);}
    }
    static string Stats()
    {
        var meshes=environment.GetComponentsInChildren<MeshFilter>(true);
        long tris=meshes.Sum(m=>m.sharedMesh!=null?(long)m.sharedMesh.GetIndexCount(0)/3:0);
        return "Props="+environment.GetComponentsInChildren<LODGroup>().Length+", mesh triangles (all LODs)="+tris+", physics colliders="+environment.GetComponentsInChildren<Collider2D>().Length;
    }
}
