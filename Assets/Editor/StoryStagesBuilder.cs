using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Ten story stages sharing the established combat scene, with replaceable node visuals.</summary>
public static partial class StoryStagesBuilder
{
    public const string Root="Assets/Environments/StoryStages";
    const string Scout="Assets/Environments/ScoutPass";
    public const string Output=@"C:\Users\sbpar\Documents\VisionStriker_StageFinishing_20261021";
    public static readonly string[] Ids={"river","ridge","old_ferry","gate","quarry","east","camp","rescue","entrance","final"};
    public static readonly string[] Scenes={"RiverAmbush","WindRidge","OldFerry","SealedGate","HiddenQuarry","EasternRidge","SearchCamp","PrisonStockade","BlackCloudEntrance","SluiceFortress"};
    public static readonly string[] Titles={"계곡 · 마교 척후병","산등성이 · 고용 산적","옛 나루 · 약탈자","폐쇄된 관문 · 철문","채석장 · 정예병","동쪽 능선 · 정예병","수색대 야영지","포로 수용소","흑운협 입구 · 흑견","수문 요새 · 무진"};
    static Transform world;static int index;static System.Random rng;
    static Material wood,stone,iron,cloth,roof,ground;
    static float Rand(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
    public static void BuildAndReview(){Build();StoryStagesReview.Run();}
    public static void RefreshStoneAndReview()
    {
        var tex=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Environments/StoryStages/Textures/PBR/rock_face_03_Diffuse.jpg");
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"/Materials"}))
        {var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));if(m.shader.name=="VisionStriker/Story Stone"){m.SetTexture("_BaseMap",tex);EditorUtility.SetDirty(m);}}
        AssetDatabase.SaveAssets();StoryStagesReview.Run();
    }
    [MenuItem("Tools/VisionStriker/Build Ten Story Stages")]
    public static void Build()
    {
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the open scene before rebuilding.");
        foreach(string dir in new[]{"Materials","Meshes","Prefabs","Models","Textures"})Directory.CreateDirectory(Root+"/"+dir);
        Directory.CreateDirectory("Assets/Scenes/StoryStages");
        AssetDatabase.Refresh();ImportPbrTextures();ImportProps();
        wood=Mat("WeatheredTimber",new Color(.48f,.36f,.23f),"Assets/Naganeupseong/Textures/Structure/Pillar/T_Pillar02a_BC.png");
        stone=Mat("FortressStone",new Color(.70f,.72f,.69f),"Assets/Environments/StoryStages/Textures/PBR/rock_face_03_Diffuse.jpg","VisionStriker/Story Stone");
        iron=Mat("BlackIron",new Color(.11f,.13f,.13f));
        cloth=Mat("CultCrimson",new Color(.30f,.035f,.025f),Scout+"/Textures/PathGround.png");cloth.SetFloat("_Cull",0);
        roof=Mat("WeatheredRoof",new Color(.24f,.29f,.29f),Scout+"/Textures/CliffRock_basecolor.png");roof.SetFloat("_Cull",0);
        var records=new List<string>();
        for(index=0;index<Ids.Length;index++)
        {
            rng=new System.Random(7410+index);
            stone=Mat(Ids[index]+"_Stone",new Color(.59f,.68f,.79f),"Assets/Environments/StoryStages/Textures/PBR/rock_face_03_Diffuse.jpg","VisionStriker/Story Stone");stone.SetFloat("_Blocks",Ids[index]=="quarry"?0:1);
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
            var old=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Scout Pass"));
            var surround=old.transform.Find("Scout 360 Extension");
            world=new GameObject("Story Stage · "+Ids[index]).transform;
            foreach(string name in new[]{"Arena360Ground","MountainRing0","MountainRing1","MountainRing2"})
            {
                var child=surround.Find(name);if(!child)throw new Exception("Missing Scout shared environment "+name);
                child.SetParent(world,true);
            }
            Object.DestroyImmediate(old);
            SetGround();SetLight();Perimeter();
            switch(Ids[index])
            {
                case "river":River();break;case "ridge":Ridge();break;case "old_ferry":Ferry();break;
                case "gate":Gate();break;case "quarry":Quarry();break;case "east":East();break;
                case "camp":Camp();break;case "rescue":Rescue();break;case "entrance":Entrance();break;
                case "final":Final();break;
            }
            RefineEnvironment();
            FinishStageDressing();
            ValidateArchitecture();
            StageAtmosphereInstaller.ApplyCurrent(Ids[index],world);
            var e=Object.FindAnyObjectByType<EnemyAI>();
            var health=new SerializedObject(e.GetComponent<Health>());health.FindProperty("displayName").stringValue=Titles[index];health.ApplyModifiedPropertiesWithoutUndo();
            StoryCharacterInstaller.ApplyConfiguredVisual(Ids[index]);
            var camera=Camera.main;var urp=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(urp){urp.requiresDepthTexture=true;urp.requiresColorTexture=true;}camera.farClipPlane=360;camera.clearFlags=CameraClearFlags.Skybox;
            var rig=camera.GetComponent<Battle3DCamera>();rig.orbitArena=true;rig.automaticCombatFraming=true;rig.maximumMovementYaw=14;rig.cameraBoundaryRadius=17;
            CombatFramingInstaller.ApplyToCamera(camera);
            // Use the source scene's already tested camera, floor, arena bounds, players, HUD and animations.
            if(world.GetComponentsInChildren<Collider2D>(true).Length>0)throw new Exception("Scenery introduced combat colliders.");
            foreach(var r in world.GetComponentsInChildren<Renderer>(true))
                if(!(r is ParticleSystemRenderer)&&r.sharedMaterials.Any(m=>!m||ShaderUtil.ShaderHasError(m.shader)))throw new Exception("Missing/invalid material: "+r.name);
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/StoryStages/"+Scenes[index]+".unity");
            records.Add(Ids[index]+" | "+Scenes[index]+" | renderers="+world.GetComponentsInChildren<Renderer>(true).Length+" | hulls="+world.GetComponentsInChildren<BattleCameraObstacle>().Length);
        }
        var config=AssetDatabase.LoadAssetAtPath<RunConfig>("Assets/Resources/RunConfig.asset");
        var scout=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Scout/Scout_Visual.prefab");
        for(int i=0;i<Ids.Length;i++)
        {
            config.nodeBattleScenes.RemoveAll(n=>n!=null&&n.nodeId==Ids[i]);
            // Preserve distinct opponents when rebuilding stage scenery.
            config.nodeBattleScenes.Add(new NodeBattleScene{nodeId=Ids[i],sceneName=Scenes[i]});
            if(config.GetEnemyVisual(Ids[i])==null) { config.nodeEnemyVisuals.RemoveAll(n=>n!=null&&n.nodeId==Ids[i]); config.nodeEnemyVisuals.Add(new NodeEnemyVisual{nodeId=Ids[i],visualPrefab=scout}); }
        }
        EditorUtility.SetDirty(config);
        var builds=EditorBuildSettings.scenes.ToList();
        foreach(string name in Scenes)
        {
            string path="Assets/Scenes/StoryStages/"+name+".unity";
            builds.RemoveAll(s=>s.path==path);builds.Add(new EditorBuildSettingsScene(path,true));
        }
        EditorBuildSettings.scenes=builds.ToArray();AssetDatabase.SaveAssets();
        File.WriteAllLines(Path.Combine(Output,"stage_inventory.txt"),records);
        Debug.Log("[StoryStages] Ten maps built and node routes registered.");
    }
    static void ImportProps()
    {
        foreach(string file in Directory.GetFiles(Root+"/Textures","*.png"))
        {
            var imp=(TextureImporter)AssetImporter.GetAtPath(file.Replace('\\','/'));imp.mipmapEnabled=true;imp.maxTextureSize=file.Contains("basecolor")?2048:1024;
            imp.textureCompression=TextureImporterCompression.Compressed;
            imp.textureType=file.Contains("_normal")?TextureImporterType.NormalMap:TextureImporterType.Default;
            imp.sRGBTexture=file.Contains("basecolor");imp.SaveAndReimport();
        }
        foreach(string name in new[]{"CargoBoat","GateDoors","QuarryHoist","PrisonCage","SluiceWinch","BankOutcrop","EvergreenShrub","MilitaryTent"})
        {
            string path=Root+"/Models/"+name+".fbx";var imp=(ModelImporter)AssetImporter.GetAtPath(path);
            if(imp==null)throw new Exception("Prop export missing: "+path);
            imp.importAnimation=false;imp.importCameras=false;imp.importLights=false;imp.isReadable=false;
            imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.meshCompression=ModelImporterMeshCompression.Low;imp.SaveAndReimport();
            var m=Mat(name,Color.white,Root+"/Textures/"+name+"_basecolor.png");
            m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+name+"_normal.png"));m.EnableKeyword("_NORMALMAP");m.SetFloat("_BumpScale",.6f);
            var g=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));g.name=name;
            var lods=new List<LOD>();
            for(int i=0;i<3;i++)
            {
                var rs=g.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.EndsWith("LOD"+i)).ToArray();
                if(rs.Length==0)throw new Exception(name+" missing LOD "+i);
                foreach(var r in rs)r.sharedMaterial=m;
                lods.Add(new LOD(i==0?.17f:i==1?.055f:.006f,rs));
            }
            var group=g.GetComponent<LODGroup>();if(!group)group=g.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(g,Root+"/Prefabs/"+name+".prefab");Object.DestroyImmediate(g);
        }
    }
    static Material Mat(string name,Color tint,string texture=null,string shader="Universal Render Pipeline/Lit")
    {
        string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
        m.shader=Shader.Find(shader);m.SetColor("_BaseColor",tint);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.12f);m.enableInstancing=true;
        if(texture!=null)
        {
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
            if(!tex) {string leaf=Path.GetFileNameWithoutExtension(texture);string found=AssetDatabase.FindAssets(leaf+" t:Texture2D").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==leaf);if(found!=null)tex=AssetDatabase.LoadAssetAtPath<Texture2D>(found);}
            if(!tex)throw new Exception("Missing texture "+texture);m.SetTexture("_BaseMap",tex);
        }
        EditorUtility.SetDirty(m);return m;
    }
    static Bounds Bounds(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).ToArray();
        var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;
    }
    static Transform Group(string name,Vector3 position,float yaw=0)
    {var t=new GameObject(name).transform;t.SetParent(world,false);t.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));return t;}
    static GameObject Box(Transform p,string name,Vector3 pos,Vector3 size,Material mat)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(p,false);g.transform.localPosition=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(g.GetComponent<Collider>());return g;
    }
    static void Beam(Transform p,Vector3 a,Vector3 b,float width,Material mat)
    {var g=Box(p,"Beam",(a+b)*.5f,new Vector3(width,Vector3.Distance(a,b),width),mat);g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}
    static GameObject Prefab(string path,string name,Vector3 p,float maxSize,float yaw=0,bool hull=true)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)throw new Exception("Missing "+path);
        var g=(GameObject)PrefabUtility.InstantiatePrefab(asset,world);g.name=name;g.transform.rotation=Quaternion.Euler(0,yaw,0);
        var b=Bounds(g.transform);g.transform.localScale*=maxSize/Mathf.Max(b.size.x,b.size.y,b.size.z);b=Bounds(g.transform);
        g.transform.position+=p-new Vector3(b.center.x,b.min.y,b.center.z);
        foreach(var c in g.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        foreach(var c in g.GetComponentsInChildren<Collider2D>(true))Object.DestroyImmediate(c);
        if(hull)Hull(g.transform);return g;
    }
    static GameObject Prop(string name,Vector3 p,float size,float yaw=0)=>Prefab(Root+"/Prefabs/"+name+".prefab",name,p,size,yaw);
    static GameObject Old(string name,Vector3 p,float size,float yaw=0)=>Prefab("Assets/Naganeupseong/Prefabs/Prop/"+name+".prefab",name,p,size,yaw,false);
    static GameObject House(string name,Vector3 p,float size,float yaw=0)=>Prefab("Assets/Naganeupseong/Prefabs/Build/"+name+".prefab",name,p,size,yaw);
    static GameObject Nature(string name,Vector3 p,float size,float yaw=0,bool hull=true)=>Prefab(Scout+"/Prefabs/"+name+".prefab",name,p,size,yaw,hull);
    static void Hull(Transform t)
    {
        var b=Bounds(t);if(b.size.y<.6f)return;
        var g=new GameObject("CameraHull",typeof(BoxCollider),typeof(BattleCameraObstacle));g.layer=2;g.transform.position=b.center;g.transform.SetParent(t,true);
        var c=g.GetComponent<BoxCollider>();c.isTrigger=true;c.size=b.size;
    }
    static Mesh SaveMesh(Mesh mesh,string name)
    {
        mesh.name=Ids[index]+"_"+name;string path=Root+"/Meshes/"+Ids[index]+"_"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static GameObject MeshObject(Transform parent,string name,List<Vector3> v,List<int> tr,Material mat,List<Vector2> uv=null)
    {
        var mesh=new Mesh{name=name};if(v.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(v);mesh.SetUVs(0,uv??v.Select(p=>new Vector2(p.x,p.z)).ToList());mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);g.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,name);g.GetComponent<Renderer>().sharedMaterial=mat;return g;
    }
    static void Combine(Transform p,string name)
    {
        var rs=p.GetComponentsInChildren<MeshRenderer>().Where(r=>!r.GetComponent<ScoutPassWind>()&&r.GetComponent<MeshFilter>()!=null).ToArray();
        int i=0;foreach(var set in rs.GroupBy(r=>r.sharedMaterial))
        {
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(set.Select(r=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=p.worldToLocalMatrix*r.transform.localToWorldMatrix}).ToArray());
            var g=new GameObject(name+i,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(p,false);g.GetComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,name+i);g.GetComponent<Renderer>().sharedMaterial=set.Key;i++;
        }
        foreach(var r in rs)Object.DestroyImmediate(r.gameObject);
    }
    static void SetGround()
    {
        ground=ContinuousGround();
        var terrain=world.Find("Arena360Ground").GetComponent<MeshFilter>();var mesh=Object.Instantiate(terrain.sharedMesh);var vs=mesh.vertices;
        for(int i=0;i<vs.Length;i++)
        {
            var p=vs[i];
            if(Ids[index]=="river"){
                float rz=p.z-Mathf.Sin(p.x*.16f)*1.1f;
                float dip=Mathf.SmoothStep(0,1,(rz-9)/5)*(1-Mathf.SmoothStep(0,1,(rz-26)/10));
                float feeder=(1-Mathf.SmoothStep(0,1,(Mathf.Abs(p.x+12)-1.8f)/1.7f))*Mathf.SmoothStep(0,1,(p.z-27)/3)*(1-Mathf.SmoothStep(0,1,(p.z-36)/2));
                p.y=Mathf.Lerp(p.y,-1.1f,Mathf.Max(dip,feeder));
            }
            if(Ids[index]=="old_ferry"&&p.z>11)p.y=Mathf.Lerp(p.y,-2.5f,Mathf.SmoothStep(0,1,(p.z-11)/5));
            if(Ids[index]=="final"&&p.z>15)p.y=-4.2f;
            vs[i]=p;
        }
        mesh.vertices=vs;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();terrain.sharedMesh=SaveMesh(mesh,"Ground");
        terrain.GetComponent<Renderer>().sharedMaterial=ground;
    }
    static void SetLight()
    {
        bool night=new[]{"old_ferry","camp","rescue","entrance","final"}.Contains(Ids[index]);
        Color horizon=night?new Color(.35f,.43f,.50f):Ids[index]=="ridge"?new Color(.72f,.68f,.54f):new Color(.56f,.66f,.67f);
        var sun=RenderSettings.sun;if(!sun)sun=Object.FindObjectsByType<Light>().FirstOrDefault(l=>l.type==LightType.Directional);
        if(!sun){sun=new GameObject("Stage daylight",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;sun.transform.SetParent(world,false);RenderSettings.sun=sun;}
        sun.transform.rotation=Quaternion.Euler(index==1?19:32,-38+index*9,0);sun.color=night?new Color(.77f,.85f,1):new Color(1,.90f,.76f);sun.intensity=night?1.25f:1.5f;sun.shadowStrength=.70f;
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=night?new Color(.38f,.45f,.57f):new Color(.55f,.61f,.66f);
        RenderSettings.ambientEquatorColor=new Color(.34f,.36f,.37f);RenderSettings.ambientGroundColor=new Color(.20f,.19f,.18f);
        var fill=new GameObject("Soft character fill",typeof(Light)).GetComponent<Light>();fill.transform.SetParent(world,false);fill.type=LightType.Directional;fill.shadows=LightShadows.None;fill.intensity=night?.3f:.15f;fill.color=new Color(.80f,.87f,1);fill.transform.rotation=Quaternion.Euler(28,155,0);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=horizon;RenderSettings.fogStartDistance=night?42:55;RenderSettings.fogEndDistance=night?190:260;
        var skyPath=Root+"/Materials/"+Ids[index]+"_Sky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if(!sky){sky=new Material(RenderSettings.skybox);AssetDatabase.CreateAsset(sky,skyPath);}
        sky.SetColor("_SkyHorizon",horizon);sky.SetColor("_SkyTop",night?new Color(.08f,.16f,.28f):new Color(.28f,.45f,.60f));sky.SetFloat("_CloudSpeed",.0045f);sky.SetFloat("_CloudStrength",.7f);RenderSettings.skybox=sky;EditorUtility.SetDirty(sky);
    }
    static void Perimeter()
    {
        bool gorge=Ids[index]=="entrance",open=Ids[index]=="ridge";
        for(int i=0;i<24;i++)
        {
            float a=(i*15+Rand(-5,5))*Mathf.Deg2Rad,r=Rand(30,52);
            float x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(z>18&&Mathf.Abs(x)<17)continue;
            Nature("CliffRock",new Vector3(x,-.5f,z),gorge?Rand(14,24):Rand(6,11),Rand(0,360));
            if(!gorge&&(!open||i%3==0))Nature("BentPine",new Vector3(x+4,0,z+3),Rand(8,14),Rand(0,360));
        }
        for(int i=0;i<16;i++)
        {
            float a=i*22.5f*Mathf.Deg2Rad,r=Rand(56,72);
            Nature("BentPine",new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),Rand(10,18),Rand(0,360),false);
        }
    }
    static void GroundDetails()
    {
        var patches=Group("Ground edging and worn paving",Vector3.zero);
        if(new[]{"gate","old_ferry","entrance","final"}.Contains(Ids[index]))
        {
            var pave=Group("Worn stone fighting apron",Vector3.zero);
            var paving=Mat(Ids[index]+"_Paving",new Color(.86f,.88f,.84f),"Assets/Environments/StoryStages/Textures/PBR/rock_face_03_Diffuse.jpg","VisionStriker/Story Stone");paving.SetFloat("_Blocks",0);
            for(int z=-9;z<=10;z++)for(int x=-11;x<=11;x++)
            {
                float px=x*1.3f+(z%2==0?.65f:0),pz=z*.85f;
                var slab=Box(pave,"Worn flagstone",new Vector3(px,-.025f,pz),new Vector3(1.27f,.06f,.82f),paving);
            }
            Combine(pave,"FightingApron");
        }
        for(int i=0;i<65;i++)
        {
            float x=Rand(-18,18),z=Rand(-15,16);if(Mathf.Abs(x)<7.2f&&Mathf.Abs(z)<2.5f)continue;
            if((Ids[index]=="river"&&z>10)||(Ids[index]=="old_ferry"&&z>12)||(Ids[index]=="final"&&z>14))continue;
            var g=Box(patches,"Shallow path stone",new Vector3(x,.004f,z),new Vector3(Rand(.15f,.55f),Rand(.015f,.05f),Rand(.15f,.6f)),stone);g.transform.localRotation=Quaternion.Euler(0,Rand(0,180),0);
        }
        Combine(patches,"PathStones");
        // Low ground cover adds foreground depth without obscuring the fighters or camera.
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int i=0;i<1800;i++)
        {
            float a=Rand(0,Mathf.PI*2),r=Rand(8,34),x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            if(z>10&&new[]{"river","old_ferry","final"}.Contains(Ids[index]))continue;
            if(Mathf.Abs(x)<9&&z>0)continue;
            float h=Rand(.06f,.22f);
            for(int j=0;j<2;j++){int k=v.Count;float dx=j==0?.045f:0,dz=j==1?.045f:0;v.AddRange(new[]{new Vector3(x-dx,0,z-dz),new Vector3(x+dx,0,z+dz),new Vector3(x+.03f,h,z+.02f)});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up});tr.AddRange(new[]{k,k+2,k+1});}
        }
        var grass=Mat(Ids[index]+"_Grass",Ids[index]=="ridge"?new Color(.40f,.36f,.18f):new Color(.21f,.29f,.18f));grass.SetFloat("_Cull",0);MeshObject(world,"Grass fringes",v,tr,grass,uv);
    }
    static void Water(string name,Vector3 center,Vector2 size,bool fall=false)
    {
        RefinedWater(name,center,size,fall);
    }

    static void Flag(Vector3 p,float height=4.5f,float yaw=0)
    {
        var parent=Group("Crimson cult banner",p,yaw);Box(parent,"Pole",new Vector3(0,height*.5f,0),new Vector3(.12f,height,.12f),wood);
        Beam(parent,new Vector3(-.1f,height,0),new Vector3(1.45f,height,0),.08f,wood);
        var v=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
        for(int y=0;y<=10;y++)for(int x=0;x<=6;x++){v.Add(new Vector3(x/6f*1.35f,height-y/10f*2.2f,0));uv.Add(new Vector2(x/6f,y/10f));}
        for(int y=0;y<10;y++)for(int x=0;x<6;x++){int a=y*7+x;tr.AddRange(new[]{a,a+7,a+1,a+1,a+7,a+8});}
        var flag=MeshObject(parent,"Banner_"+(p.x*10).ToString("0")+"_"+(p.z*10).ToString("0"),v,tr,cloth,uv);var wind=flag.AddComponent<ScoutPassWind>();wind.amplitude=.09f;wind.speed=2;wind.phase=p.x;
    }
    static void Fire(Vector3 p)
    {
        Old("Brazer",p,.8f);
        var g=new GameObject("Warm brazier light",typeof(Light),typeof(StoryStageMotion));g.transform.SetParent(world,false);g.transform.position=p+Vector3.up*.7f;
        var l=g.GetComponent<Light>();l.type=LightType.Point;l.color=new Color(1,.41f,.10f);l.range=6;l.intensity=2.2f;l.shadows=LightShadows.None;g.GetComponent<StoryStageMotion>().flicker=true;
        var ember=Mat("Ember",new Color(1,.35f,.055f,.8f),null,"VisionStriker/Camp Smoke");
        var ps=new GameObject("Rising embers",typeof(ParticleSystem)).GetComponent<ParticleSystem>();ps.transform.SetParent(world,false);ps.transform.position=p+Vector3.up*.6f;ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var m=ps.main;m.loop=true;m.prewarm=true;m.startLifetime=.8f;m.startSpeed=.7f;m.startSize=new ParticleSystem.MinMaxCurve(.08f,.20f);m.maxParticles=24;m.startColor=Color.white;
        var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=7;sh.radius=.18f;sh.rotation=new Vector3(-90,0,0);var em=ps.emission;em.rateOverTime=7;
        var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=ember;
    }
    static void Wall(Vector3 p,float length,float height=4,float yaw=0,bool timber=false)
    {
        var t=Group(timber?"Stockade":"Fortress wall",p,yaw);
        if(timber){for(int i=0;i<Mathf.CeilToInt(length/.33f);i++)Box(t,"Palisade",new Vector3(-length/2+i*.33f,height*.5f,0),new Vector3(.27f,height+Rand(-.15f,.15f),.28f),wood);for(int i=0;i<2;i++)Box(t,"Rail",new Vector3(0,1+i*1.4f,-.23f),new Vector3(length,.18f,.20f),wood);}
        else
        {
            Box(t,"Masonry",new Vector3(0,height*.5f,0),new Vector3(length,height,1.2f),stone);
            Box(t,"Cornice",new Vector3(0,height+.07f,0),new Vector3(length+.1f,.22f,1.5f),roof);
            for(float x=-length/2+.45f;x<length/2;x+=1.35f)Box(t,"Battlement",new Vector3(x,height+.5f,0),new Vector3(.9f,.8f,1.3f),stone);
        }
        Combine(t,"Wall_"+p.x+"_"+p.z);Hull(t);
    }
    static void Tower(Vector3 p,float height=8)
    {
        var t=Group("Watchtower",p);
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box(t,"Timber column",new Vector3(x*1.65f,height*.44f,z*1.65f),new Vector3(.28f,height*.88f,.28f),wood);
        Box(t,"Watch deck",new Vector3(0,height*.55f,0),new Vector3(3.8f,.22f,3.8f),wood);
        for(int z=-1;z<=1;z+=2)for(int j=0;j<2;j++)Box(t,"Deck rail",new Vector3(0,height*.55f+.5f+j*.45f,z*1.8f),new Vector3(3.8f,.12f,.12f),wood);
        for(int x=-1;x<=1;x+=2)for(int j=0;j<2;j++)Box(t,"Deck rail",new Vector3(x*1.8f,height*.55f+.5f+j*.45f,0),new Vector3(.12f,.12f,3.8f),wood);
        for(int z=-1;z<=1;z+=2){Beam(t,new Vector3(-1.65f,0,z*1.65f),new Vector3(1.65f,height*.55f,z*1.65f),.14f,wood);Beam(t,new Vector3(1.65f,0,z*1.65f),new Vector3(-1.65f,height*.55f,z*1.65f),.14f,wood);}
        var verts=new List<Vector3>{new Vector3(-2.5f,height*.88f,-2.5f),new Vector3(2.5f,height*.88f,-2.5f),new Vector3(-2.5f,height*.88f,2.5f),new Vector3(2.5f,height*.88f,2.5f),new Vector3(-2,height,0),new Vector3(2,height,0)};
        MeshObject(t,"TowerRoof"+p.x+"_"+p.z,verts,new List<int>{0,4,1,1,4,5,2,3,4,3,5,4,0,2,4,1,5,3},roof);
        Combine(t,"Tower"+p.x+"_"+p.z);Hull(t);
    }
    static void Tent(Vector3 p,float yaw=0)
    {
        Prop("MilitaryTent",p,7.4f,yaw+180);
    }
    static void Supplies(Vector3 p,float yaw=0)
    {
        Old("Cart",p,3.5f,yaw);Old("Straw_Bag",p+new Vector3(-2,0,-1),.8f,35);Old("Rice_Chest",p+new Vector3(2,0,-1),1.2f,-20);Old("Water_Jar",p+new Vector3(1.6f,0,1),1,0);
    }
    static void Gatehouse(Vector3 p,float scale=1)
    {
        ConnectedGate(p,6.4f*scale,Ids[index]=="entrance"?24:21,5.5f*scale,false);
        Flag(p+new Vector3(-5.5f*scale,0,-2),5.8f*scale);Flag(p+new Vector3(5.5f*scale,0,-2),5.8f*scale,180);
    }
    static void River()
    {
        Water("Jade stream behind the fighting bank",new Vector3(0,-.35f,22),new Vector2(116,40));
        for(int i=0;i<18;i++){float x=-34+i*4;Nature("LowBoulder",new Vector3(x,-.5f,i%2==0?11:31),Rand(3,5),Rand(0,360));}
        Nature("CliffRock",new Vector3(-17,0,37),19,33);Nature("CliffRock",new Vector3(-8,0,41),17,120);
        Water("Distant low falls",new Vector3(-12,3.8f,35),new Vector2(4,8),true);

        Nature("BentPine",new Vector3(13,0,30),15,35);Nature("BentPine",new Vector3(-16,0,28),12,70);
        Supplies(new Vector3(12,0,7),25);
        Flag(new Vector3(-13,0,8),3.4f,24);
    }
    static void Ridge()
    {
        for(int i=0;i<6;i++)Nature("TimberFence",new Vector3(-12+i*4,0,20),3.6f,0);
        Tower(new Vector3(18,0,33),9);Supplies(new Vector3(-13,0,15),90);
        Nature("CliffRock",new Vector3(-22,-1,20),14,45);Nature("BentPine",new Vector3(-18,0,19),13,90);
        Flag(new Vector3(14,0,20),4.4f);
        for(int i=0;i<6;i++)Nature("LowBoulder",new Vector3(-16+i*7,-.3f,-24),3.3f,i*43);
    }
    static void Ferry()
    {
        Water("Wide river at the old landing",new Vector3(0,-.45f,49),new Vector2(150,72));
        var dock=Group("Cargo landing pier",new Vector3(-4,-.12f,19),-8);
        for(int i=0;i<15;i++)Box(dock,"Deck plank",new Vector3(0,0,-6+i*.8f),new Vector3(5.3f,.2f,.73f),wood);
        for(int i=0;i<4;i++)foreach(int s in new[]{-1,1})Box(dock,"Mooring post",new Vector3(s*2.7f,-.45f,-5+i*3),new Vector3(.24f,2.8f,.24f),wood);
        Combine(dock,"FerryPier");Hull(dock);
        var boat=Prop("CargoBoat",new Vector3(1.2f,-1.0f,21),7.5f,8);boat.AddComponent<StoryStageMotion>();
        House("Tavern_House_01",new Vector3(16,0,12),11,210);Supplies(new Vector3(-12,0,10),-10);
        for(int i=0;i<8;i++)Nature("LowBoulder",new Vector3(-28+i*8,-.4f,12.5f),Rand(2,3.4f),i*30);
        Fire(new Vector3(-8,0,10));Fire(new Vector3(9,0,10));
    }
    static void Gate()
    {
        Gatehouse(new Vector3(0,0,22));
        EncloseCourtyard(21,-25,22,4,false);
        Tower(new Vector3(-15,0,24),10);Tower(new Vector3(15,0,24),10);
        House("Local_Clerks_House_01",new Vector3(-14,0,12),8,90);
        Supplies(new Vector3(13,0,12),-20);Fire(new Vector3(-7,0,12));Fire(new Vector3(7,0,12));
        
    }
    static void Quarry()
    {
        var t=Group("Cut granite terraces",Vector3.zero);
        for(int row=0;row<3;row++)
        {
            for(int side=-1;side<=1;side+=2)Box(t,"Worked quarry face",new Vector3(side*(15+row*6),2+row*2.5f,29+row*8),new Vector3(13,5+row*5,15),stone);
        }
        for(int i=0;i<12;i++)Box(t,"Cut stone block",new Vector3((i%2==0?-1:1)*Rand(10,16),.8f,Rand(8,19)),new Vector3(2.1f,1.6f,1.8f),stone);
        Box(t,"Secret tunnel darkness",new Vector3(0,3.2f,34),new Vector3(6,6.4f,.3f),iron);Box(t,"Tunnel lintel",new Vector3(0,7,34),new Vector3(10,2,5),stone);
        Box(t,"Tunnel left pier",new Vector3(-4,3,34),new Vector3(2,6,5),stone);Box(t,"Tunnel right pier",new Vector3(4,3,34),new Vector3(2,6,5),stone);
        Combine(t,"QuarryCuts");Hull(t); // The aggregate camera hull is removed: individual terraces are outside the orbit.
        Object.DestroyImmediate(t.Find("CameraHull").gameObject);
        Prop("QuarryHoist",new Vector3(-8,0,21),7,20);Supplies(new Vector3(12,0,18),-65);
        Nature("CliffRock",new Vector3(0,-1,48),24,0);Flag(new Vector3(7,0,29),4.5f);
        Fire(new Vector3(-4,0,34));
    }
    static void East()
    {
        for(int i=0;i<9;i++)Nature("BentPine",new Vector3(-22+i*5.5f,0,27+Mathf.Abs(i-4)*3),Rand(12,17),i*41);
        Tower(new Vector3(10,0,25),9);
        for(int i=0;i<7;i++)Nature("TimberFence",new Vector3(-16+i*5,0,19),4.2f,10);
        Nature("CliffRock",new Vector3(-16,-.4f,16),8,40);
        Old("Cart",new Vector3(-10,0,17),3.7f,45);Flag(new Vector3(7,0,22),4.7f);Fire(new Vector3(11,0,20));
    }
    static void Camp()
    {
        Tent(new Vector3(-10,0,20),15);Tent(new Vector3(0,0,25),0);Tent(new Vector3(11,0,21),-20);
        Prop("PrisonCage",new Vector3(16,0,12),4,-30);
        Supplies(new Vector3(-14,0,10),25);Old("Low_Wooden_Bench",new Vector3(0,0,17),2.5f);
        Old("Rice_Chest",new Vector3(2,0,18),1.2f);Fire(new Vector3(-5,0,12));Fire(new Vector3(7,0,13));
        Flag(new Vector3(-3,0,22),5.5f);
        for(int i=0;i<5;i++)Nature("TimberFence",new Vector3(-10+i*5,0,-24),4.4f,180);
    }
    static void Rescue()
    {
        EncloseCourtyard(23,-26,27,4.5f,true);
        ConnectedGate(new Vector3(0,0,27),6,23,4.5f,true);
        Prop("PrisonCage",new Vector3(-10,0,18),5.2f,10);Prop("PrisonCage",new Vector3(10,0,18),5.2f,-10);
        Tower(new Vector3(-20,0,25),9);Tower(new Vector3(20,0,25),9);
        House("Wooden_Bench_House_03",new Vector3(-18,0,4),8,90);
        Old("Low_Wooden_Bench",new Vector3(16,0,11),2.4f);Old("Water_Jar",new Vector3(14,0,11),1.3f);
        Fire(new Vector3(-5,0,16));Fire(new Vector3(5,0,16));Flag(new Vector3(8,0,25),5.5f);
    }
    static void Entrance()
    {
        Nature("CliffRock",new Vector3(-18,-2,26),28,80);Nature("CliffRock",new Vector3(19,-2,30),31,230);
        Gatehouse(new Vector3(0,0,26),1.12f);
        Prop("PrisonCage",new Vector3(-12,0,12),4.5f,30);
        Fire(new Vector3(-7,0,16));Fire(new Vector3(7,0,16));
        for(int i=0;i<4;i++)Flag(new Vector3(i%2==0?-13:13,0,14+(i/2)*9),6.8f,i%2*180);
        EncloseCourtyard(24,-29,26,4.8f,false);
    }
    static void Final()
    {
        Water("Reservoir below fortress",new Vector3(0,-3.4f,60),new Vector2(180,90));
        var dam=Group("Three monumental sluices",Vector3.zero);
        for(int i=-1;i<=1;i++)
        {
            float x=i*13;
            Box(dam,"Sluice shadow",new Vector3(x,1,35),new Vector3(9,12,3),iron);
            Water("Sluice cascade "+i,new Vector3(x,1.665f,33.3f),new Vector2(8,10.07f),true);
            Box(dam,"Raised water gate",new Vector3(x,7.2f,34),new Vector3(9,3,1.2f),wood);
            for(int j=-1;j<=1;j++)Box(dam,"Gate iron band",new Vector3(x+j*3,7.2f,33.35f),new Vector3(.25f,3,.15f),iron);
        }
        foreach(float x in new[]{-21f,-6.5f,6.5f,21f})Box(dam,"Massive stone buttress",new Vector3(x,4.0f,36),new Vector3(4,16,9),stone);
        Box(dam,"Fortress upper walkway",new Vector3(0,12,36),new Vector3(47,1.5f,9),stone);
        for(int i=-15;i<=15;i++)Box(dam,"Rampart crenellation",new Vector3(i*1.5f,13.4f,32),new Vector3(.85f,1.5f,1.2f),stone);
        Box(dam,"Control terrace retaining wall",new Vector3(0,-2,14.7f),new Vector3(44,4,2),stone);
        Combine(dam,"MonumentalSluices"); // Entire dam is outside the camera sweep.
        GrandFortress();
        Prop("SluiceWinch",new Vector3(-10,0,11),4.5f,105);
        Prop("SluiceWinch",new Vector3(10,0,11),4.5f,-105);
        var railing=Group("Terrace rails",Vector3.zero);
        foreach(int sign in new[]{-1,1})for(int i=0;i<9;i++)
        {Box(railing,"Stone rail post",new Vector3(sign*(7+i*1.65f),.65f,14.5f),new Vector3(.32f,1.3f,.32f),stone);Box(railing,"Stone rail",new Vector3(sign*(7.7f+i*1.65f),1.1f,14.5f),new Vector3(1.4f,.18f,.20f),stone);}
        Combine(railing,"TerraceRails");
        Fire(new Vector3(-6,0,12));Fire(new Vector3(6,0,12));
        Flag(new Vector3(-19,0,13),7.5f);Flag(new Vector3(19,0,13),7.5f,180);
        EncloseCourtyard(24,-25,15,4.5f,false);
    }
}
