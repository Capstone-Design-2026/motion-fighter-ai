using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 스토리 무대(3D 사물·바닥)와 고화질 배경 그림을 합친 예시 무대를 만든다.
/// 스토리 씬을 복사한 뒤 하늘과 먼 산을 원경 그림으로 바꾸고, 해·안개·주변광·후처리를 그 그림에 맞춘다. 원본 스토리 씬은 건드리지 않는다.
/// Unity -batchmode -executeMethod HybridStageBuilder.BuildAndShoot -redbool-keyboard
/// </summary>
public static class HybridStageBuilder
{
    public const string Root = "Assets/Environments/HybridStages", SceneDir = "Assets/Scenes/Hybrid", Source = "StageConcepts/Backdrops";

    [Flags] public enum Air { None = 0, Mist = 1, Motes = 2, Snow = 4, Embers = 8 }
    public enum Leaf { None, Petal, Bamboo, Maple, Ash }

    // 리메이크 무대에 세우는 그림 한 장: 파일 이름, 가운데 x, 깊이 z, 폭(m), 좌우 뒤집기
    public sealed class Cut
    {
        public string file; public float x, z, width; public bool flip;
        // 좌우 끝까지 이어지는 넓은 배경층: 같은 그림을 번갈아 뒤집어 가며 옆으로 이어 붙인다 (tile = 한쪽에 덧붙이는 장수, dir = 0 양쪽 / +1 오른쪽만 / -1 왼쪽만)
        public int tile, dir;
        public Cut(string file, float x, float z, float width, bool flip = false) { this.file = file; this.x = x; this.z = z; this.width = width; this.flip = flip; }
    }
    // 리메이크 무대 뒤쪽에 흩어 놓는 기존 3D 자재: 프리팹 이름, x, z, 크기(m), 방향
    public sealed class Prop
    {
        public string prefab; public float x, z, size, yaw;
        public Prop(string prefab, float x, float z, float size, float yaw = 0) { this.prefab = prefab; this.x = x; this.z = z; this.size = size; this.yaw = yaw; }
    }

    public sealed class Stage
    {
        public string key, id, sourceScene, sceneName, painting, title;
        public float groundRow, lift;                 // 그림에서 눈높이에 놓을 줄(위에서부터의 비율), 그림을 들어 올릴 각도
        public float arc = 84f, skyExposure = 1.12f;  // 그림이 덮는 방위각(작을수록 선명하고 해·달이 화면 안으로 내려온다), 하늘 밝기
        public float lightU, lightRow, minElevation;  // 그림 속 해·달의 위치, 그림자가 너무 길어지지 않게 하는 최소 고도
        public Color keyColor, fillColor, ambientSky, ambientEquator, ambientGround;
        public float keyIntensity, fillIntensity, fogStart, fogEnd;
        public float bloom, contrast, saturation, exposure, temperature, vignette;
        public Air air;
        public Leaf leaf;                             // 흩날리는 꽃잎·잎·재
        public float sheen, rays, glow = .9f, rim = .55f; // 젖은 바닥 반사, 빛살, 해·달 빛 번짐, 역광 세기
        public float mist = 1f;
        // 리메이크: 원래 3D 사물을 끄고 새 바닥과 그림 구조물로 다시 짓는다 (StageConcepts/Remake/<remake>/ready 의 그림 사용)
        public string remake; public float floorTile = 9f, floorSmoothness = .38f;
        public string floorAlbedoAsset, floorNormalAsset;   // 새로 그린 바닥 대신 프로젝트에 있는 재질을 쓸 때
        public Color floorTint = new Color(.82f, .80f, .78f);
        public Cut[] cuts; public Prop[] props; public Vector3[] fires;
        public float fireIntensity = 6f, fireRange = 13f;   // 그림 속 횃불 자리에 놓는 조명의 세기와 닿는 거리
        public Vector4[] glows;                             // 넓게 퍼지는 큰 불빛 (x, y, z, 세기) — 모닥불처럼 싸우는 자리까지 닿아야 하는 빛
        public float cutGlow, baseFog;                      // 그림 속 불꽃·등롱의 빛 번짐, 구조물 밑동에 섞는 안개
    }

    // 스토리 지도에서 싸우는 순서
    public static readonly Stage[] Stages =
    {
        new Stage{key="scout_moon",id="scout",sourceScene="Assets/Scenes/ScoutPass.unity",sceneName="ScoutPass_Moon",painting="entrance_moon.png",title="산길 · 달밤 설산",
            arc=70,skyExposure=1.3f,groundRow=0.42f,lift=0,lightU=0.685f,lightRow=0.16f,minElevation=24,
            keyColor=new Color(0.66f,0.77f,1f),keyIntensity=1.4f,fillColor=new Color(0.54f,0.64f,0.94f),fillIntensity=0.55f,
            ambientSky=new Color(0.3f,0.39f,0.58f),ambientEquator=new Color(0.22f,0.27f,0.38f),ambientGround=new Color(0.11f,0.12f,0.16f),fogStart=30,fogEnd=170,
            sheen=.16f,rays=.05f,glow=.9f,bloom=0.75f,contrast=14,saturation=8,exposure=0.5f,temperature=-8,vignette=0.28f,air=Air.Snow|Air.Embers},
        new Stage{key="river_morning",id="river",sourceScene="Assets/Scenes/StoryStages/RiverAmbush.unity",sceneName="RiverAmbush_Morning",painting="river_morning.png",title="계곡 · 아침 협곡",
            arc=84,skyExposure=1.1f,groundRow=0.48f,lift=0,lightU=0.09f,lightRow=0.1f,minElevation=40,
            keyColor=new Color(1f,0.95f,0.84f),keyIntensity=1.5f,fillColor=new Color(0.62f,0.72f,0.84f),fillIntensity=.7f,
            ambientSky=new Color(0.7f,0.78f,0.86f),ambientEquator=new Color(0.58f,0.64f,0.6f),ambientGround=new Color(0.3f,0.33f,0.28f),fogStart=26,fogEnd=150,
            leaf=Leaf.Bamboo,sheen=.10f,rays=.12f,glow=1f,mist=.6f,bloom=0.45f,contrast=14,saturation=14,exposure=.28f,temperature=0,vignette=0.2f,air=Air.Mist|Air.Motes},
        new Stage{key="ridge_sunrise",id="ridge",sourceScene="Assets/Scenes/StoryStages/WindRidge.unity",sceneName="WindRidge_Sunrise",painting="ridge_sunrise.png",title="산등성이 · 일출 운해",
            arc=84,skyExposure=1.12f,groundRow=0.6f,lift=0,lightU=0.305f,lightRow=0.357f,minElevation=20,
            keyColor=new Color(1f,0.8f,0.58f),keyIntensity=1.6f,fillColor=new Color(0.6f,0.66f,0.9f),fillIntensity=0.6f,
            ambientSky=new Color(0.62f,0.66f,0.86f),ambientEquator=new Color(0.54f,0.5f,0.56f),ambientGround=new Color(0.26f,0.22f,0.23f),fogStart=18,fogEnd=130,
            leaf=Leaf.Petal,sheen=.08f,rays=.13f,glow=1.1f,remake="ridge",
            floorTile=9f,cutGlow=1f,baseFog=.22f,
            // 뒤쪽: 능선 끝을 따라 바위와 통나무 울타리, 망대와 깃발, 소나무를 좌우 끝까지 잇는다 (낮아서 운해는 그대로 보인다)
            cuts=new[]{new Cut("wide",0,25,22){tile=2},new Cut("centre",0,17,18),new Cut("side",-15.5f,12,12),new Cut("side",15.5f,12,12,true)},
            props=new[]{new Prop("BentPine",-31,31,10,20),new Prop("BentPine",29,33,11,200),new Prop("CliffRock",-40,42,18,40),new Prop("CliffRock",38,46,20,160),new Prop("LowBoulder",-13,21,3.2f),new Prop("LowBoulder",15,23,3.6f,90)},
            fires=new[]{new Vector3(-3.9f,1.6f,16.2f),new Vector3(3.9f,1.6f,16.2f)},
            fireIntensity=9f,fireRange=15f,
            bloom=0.55f,contrast=12,saturation=2,exposure=0.05f,temperature=2,vignette=0.24f,air=Air.Mist|Air.Motes|Air.Embers},
        new Stage{key="ferry_dawn",id="old_ferry",sourceScene="Assets/Scenes/StoryStages/OldFerry.unity",sceneName="OldFerry_Dawn",painting="ferry_dawn.png",title="옛 나루 · 새벽 물안개",
            arc=84,skyExposure=1.12f,groundRow=0.445f,lift=0,lightU=0.33f,lightRow=0.21f,minElevation=22,
            keyColor=new Color(1f,0.86f,0.76f),keyIntensity=1.15f,fillColor=new Color(0.6f,0.7f,0.8f),fillIntensity=0.6f,
            ambientSky=new Color(0.66f,0.7f,0.76f),ambientEquator=new Color(0.5f,0.56f,0.58f),ambientGround=new Color(0.22f,0.26f,0.26f),fogStart=22,fogEnd=130,
            leaf=Leaf.Petal,sheen=.42f,rays=.09f,glow=1f,bloom=0.5f,contrast=11,saturation=10,exposure=0.12f,temperature=-2,vignette=0.22f,air=Air.Mist|Air.Motes},
        new Stage{key="gate_storm",id="gate",sourceScene="Assets/Scenes/StoryStages/SealedGate.unity",sceneName="SealedGate_Storm",painting="gate_storm.png",title="폐쇄된 관문 · 먹구름 고갯길",
            arc=84,skyExposure=1.15f,groundRow=0.62f,lift=0,lightU=0.66f,lightRow=0.2f,minElevation=44,
            keyColor=new Color(0.8f,0.86f,0.96f),keyIntensity=1.15f,fillColor=new Color(0.6f,0.66f,0.76f),fillIntensity=0.6f,
            ambientSky=new Color(0.48f,0.54f,0.64f),ambientEquator=new Color(0.4f,0.44f,0.5f),ambientGround=new Color(0.2f,0.22f,0.25f),fogStart=28,fogEnd=160,
            mist=.5f,sheen=.5f,rays=.08f,glow=.8f,bloom=0.45f,contrast=14,saturation=-6,exposure=.28f,temperature=-6,vignette=0.28f,air=Air.Mist|Air.Snow},
        new Stage{key="quarry_gold",id="quarry",sourceScene="Assets/Scenes/StoryStages/HiddenQuarry.unity",sceneName="HiddenQuarry_Gold",painting="quarry_gold.png",title="채석장 · 황금빛 먼지",
            arc=84,skyExposure=1.12f,groundRow=0.56f,lift=0,lightU=0.855f,lightRow=0.335f,minElevation=22,
            keyColor=new Color(1f,0.78f,0.5f),keyIntensity=1.6f,fillColor=new Color(0.62f,0.64f,0.8f),fillIntensity=0.55f,
            ambientSky=new Color(0.7f,0.66f,0.66f),ambientEquator=new Color(0.58f,0.5f,0.44f),ambientGround=new Color(0.28f,0.23f,0.19f),fogStart=20,fogEnd=150,
            mist=.5f,sheen=.08f,rays=.07f,glow=1.1f,remake="quarry",
            floorTile=8f,
            floorSmoothness=.3f,cutGlow=1f,baseFog=.2f,
            // 뒤쪽: 계단식으로 깎은 채석장 벽과 비계·기중기를 좌우 끝까지 잇는다
            cuts=new[]{new Cut("wide",0,34,24){tile=2},new Cut("centre",0,24,13),new Cut("side",-14,12,11),new Cut("side",14,12,11,true)},
            props=new[]{new Prop("CliffRock",-50,54,32,80),new Prop("CliffRock",48,56,34,210),new Prop("LowBoulder",-9,27,4f),new Prop("LowBoulder",10,28,4.5f,60)},
            fires=new[]{new Vector3(-5.6f,3.2f,23.5f),new Vector3(-8.8f,2.6f,11.6f),new Vector3(8.8f,2.6f,11.6f)},
            bloom=0.55f,contrast=12,saturation=0,exposure=0.05f,temperature=2,vignette=0.24f,air=Air.Mist|Air.Motes|Air.Embers},
        new Stage{key="east_twilight",id="east",sourceScene="Assets/Scenes/StoryStages/EasternRidge.unity",sceneName="EasternRidge_Twilight",painting="east_twilight.png",title="동쪽 능선 · 해 진 뒤 노을",
            arc=84,skyExposure=1.2f,groundRow=0.5f,lift=0,lightU=0.27f,lightRow=0.41f,minElevation=18,
            keyColor=new Color(1f,0.62f,0.58f),keyIntensity=1.2f,fillColor=new Color(0.5f,0.48f,0.8f),fillIntensity=.75f,
            ambientSky=new Color(0.46f,0.4f,0.7f),ambientEquator=new Color(0.48f,0.36f,0.52f),ambientGround=new Color(0.18f,0.15f,0.22f),fogStart=14,fogEnd=110,
            leaf=Leaf.Maple,sheen=.18f,rays=.05f,glow=1f,remake="east",
            floorTile=7.5f,floorSmoothness=.42f,floorTint=new Color(.80f,.76f,.80f),
            mist=.7f,cutGlow=1.2f,baseFog=.35f,
            // 뒤쪽: 단풍나무와 사당 지붕이 늘어선 돌난간 담장을 좌우 끝까지 잇는다 (탑이 가운데 아치 사이로 보이게 조금 왼쪽으로 민다)
            cuts=new[]{new Cut("wide",-2.5f,29,24){tile=2},new Cut("centre",0,18,18),new Cut("side",-14f,11.5f,11),new Cut("side",14f,11.5f,11,true)},
            props=new[]{new Prop("LowBoulder",-11,23,3.4f),new Prop("LowBoulder",12,24,3.8f,70)},
            fires=new[]{new Vector3(-10.4f,2.2f,11f),new Vector3(10.4f,2.2f,11f),new Vector3(0,4.5f,17.4f)},
            fireIntensity=9f,fireRange=16f,
            glows=new[]{new Vector4(-6.5f,3.2f,16.6f,22),new Vector4(6.5f,3.2f,16.6f,22)},
            bloom=0.6f,contrast=12,saturation=10,exposure=.6f,temperature=-2,vignette=0.28f,air=Air.Mist|Air.Motes|Air.Embers},
        new Stage{key="camp_stars",id="camp",sourceScene="Assets/Scenes/StoryStages/SearchCamp.unity",sceneName="SearchCamp_Stars",painting="camp_stars.png",title="추포대 야영지 · 은하수",
            arc=84,skyExposure=1.3f,groundRow=0.5f,lift=0,lightU=0.8f,lightRow=0.24f,minElevation=24,
            keyColor=new Color(0.62f,0.76f,1f),keyIntensity=1.1f,fillColor=new Color(0.5f,0.62f,0.9f),fillIntensity=0.5f,
            ambientSky=new Color(0.26f,0.36f,0.56f),ambientEquator=new Color(0.2f,0.27f,0.38f),ambientGround=new Color(0.1f,0.12f,0.16f),fogStart=12,fogEnd=95,
            sheen=.08f,rays=.03f,glow=.7f,remake="camp",
            floorTile=6f,
            floorSmoothness=.22f,
            mist=.7f,cutGlow=1.2f,baseFog=.3f,
            floorTint=new Color(.64f,.60f,.56f),
            // 뒤쪽: 천막과 목책, 소나무 숲이 늘어선 야영지를 좌우 끝까지 잇는다
            cuts=new[]{new Cut("wide",0,27,34){tile=2},new Cut("centre",0,16,17),new Cut("side",-14,11,11),new Cut("side",14,11,11,true)},
            fires=new[]{new Vector3(-3.9f,2.2f,15.5f),new Vector3(3.9f,2.2f,15.5f),new Vector3(-9.3f,3.8f,10.6f),new Vector3(9.3f,3.8f,10.6f)},
            fireIntensity=9f,fireRange=16f,
            glows=new[]{new Vector4(0,2.4f,13.4f,65),new Vector4(-13f,2.2f,25.6f,16),new Vector4(13f,2.2f,25.6f,16)},
            bloom=0.7f,contrast=12,saturation=10,exposure=.6f,temperature=-8,vignette=0.28f,air=Air.Mist|Air.Embers|Air.Motes},
        new Stage{key="rescue_predawn",id="rescue",sourceScene="Assets/Scenes/StoryStages/PrisonStockade.unity",sceneName="PrisonStockade_Predawn",painting="rescue_predawn.png",title="포로 뇌옥 · 동트기 전 안개",
            arc=84,skyExposure=1.25f,groundRow=0.5f,lift=4,lightU=0.86f,lightRow=0.33f,minElevation=40,
            keyColor=new Color(0.74f,0.76f,0.92f),keyIntensity=.95f,fillColor=new Color(0.46f,0.56f,0.82f),fillIntensity=0.65f,
            ambientSky=new Color(0.26f,0.32f,0.48f),ambientEquator=new Color(0.22f,0.25f,0.34f),ambientGround=new Color(0.1f,0.11f,0.15f),fogStart=12,fogEnd=105,
            leaf=Leaf.Ash,sheen=.5f,glow=.8f,remake="rescue",
            floorTile=10f,
            floorSmoothness=.6f,
            mist=.6f,cutGlow=1.3f,baseFog=.2f,
            floorTint=new Color(.46f,.46f,.52f),
            // 뒤쪽: 망루가 솟은 통나무 목책 성벽을 문 양옆에서 좌우 끝까지 잇는다 (문 뒤에 반쯤 숨겨 한 줄의 성벽처럼 보이게)
            cuts=new[]{new Cut("wide",15.55f,22,21.1f){tile=3,dir=1},new Cut("wide",-15.55f,22,21.1f,true){tile=3,dir=-1},new Cut("centre",0,19,15.5f),new Cut("side",-14f,11.5f,10),new Cut("side",14f,11.5f,10,true)},
            props=new[]{new Prop("BentPine",-30,31,12,40),new Prop("BentPine",29,33,12,200),new Prop("CliffRock",-46,48,24,60),new Prop("CliffRock",46,50,24,180)},
            fires=new[]{new Vector3(-3.3f,2.6f,18.6f),new Vector3(3.3f,2.6f,18.6f),new Vector3(-10.8f,3.4f,21.4f),new Vector3(10.8f,3.4f,21.4f),new Vector3(-15.5f,3.4f,21.4f),new Vector3(15.5f,3.4f,21.4f)},
            fireIntensity=18f,fireRange=18f,
            glows=new[]{new Vector4(-9.6f,3.6f,11f,45),new Vector4(9.6f,3.6f,11f,45)},
            bloom=0.6f,contrast=14,saturation=2,exposure=.5f,temperature=-6,vignette=0.3f,air=Air.Mist|Air.Embers},
        new Stage{key="entrance_blackcloud",id="entrance",sourceScene="Assets/Scenes/StoryStages/BlackCloudEntrance.unity",sceneName="BlackCloudEntrance_BlackCloud",painting="entrance_blackcloud.png",title="흑운협 입구 · 붉은 먹구름",
            arc=70,skyExposure=1.3f,groundRow=0.5f,lift=4,lightU=0.51f,lightRow=0.27f,minElevation=45,
            keyColor=new Color(1f,0.48f,0.4f),keyIntensity=.85f,fillColor=new Color(0.42f,0.48f,0.7f),fillIntensity=.8f,
            ambientSky=new Color(0.34f,0.2f,0.24f),ambientEquator=new Color(0.24f,0.17f,0.22f),ambientGround=new Color(0.1f,0.08f,0.1f),fogStart=28,fogEnd=160,
            mist=.5f,leaf=Leaf.Ash,sheen=.45f,rays=.05f,glow=1f,bloom=0.8f,contrast=16,saturation=8,exposure=.55f,temperature=0,vignette=0.34f,air=Air.Mist|Air.Embers},
        new Stage{key="final_sunset",id="final",sourceScene="Assets/Scenes/StoryStages/SluiceFortress.unity",sceneName="SluiceFortress_Sunset",painting="final_sunset.png",title="수문 요새 · 불타는 노을",
            arc=70,skyExposure=1.2f,groundRow=0.5f,lift=8,lightU=0.5f,lightRow=0.325f,minElevation=40,
            keyColor=new Color(1f,0.56f,0.3f),keyIntensity=1.7f,fillColor=new Color(0.62f,0.52f,0.7f),fillIntensity=0.55f,
            ambientSky=new Color(0.62f,0.42f,0.4f),ambientEquator=new Color(0.5f,0.34f,0.32f),ambientGround=new Color(0.2f,0.14f,0.14f),fogStart=18,fogEnd=135,
            mist=.5f,leaf=Leaf.Ash,sheen=.45f,rays=.12f,glow=1.1f,remake="final",
            floorTile=8f,
            floorSmoothness=.62f,cutGlow=.6f,baseFog=.25f,
            floorTint=new Color(.72f,.72f,.76f),
            // 뒤쪽: 망루가 솟고 수문에서 물이 쏟아지는 댐 성벽을 문루 양옆에서 좌우 끝까지 잇는다
            cuts=new[]{new Cut("wide",17.8f,25,19.6f){tile=3,dir=1},new Cut("wide",-17.8f,25,19.6f,true){tile=3,dir=-1},new Cut("centre",0,24,22),new Cut("side",-14.5f,12,9.5f),new Cut("side",14.5f,12,9.5f,true)},
            props=new[]{new Prop("CliffRock",-44,46,26,40),new Prop("CliffRock",44,48,26,170)},
            fires=new[]{new Vector3(-10.1f,4.2f,11.6f),new Vector3(10.1f,4.2f,11.6f)},
            bloom=0.7f,contrast=14,saturation=10,exposure=0.15f,temperature=6,vignette=0.28f,air=Air.Mist|Air.Embers},
    };

    public static string ScenePath(Stage s) => SceneDir + "/" + s.sceneName + ".unity";

    // 실행 인자 -hybridOnly key1,key2 로 일부 무대만 다시 만들거나 찍을 수 있다.
    public static Stage[] ActiveStages()
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-hybridOnly");
        if (i < 0 || i + 1 >= args.Length) return Stages;
        var keys = args[i + 1].Split(',');
        return Stages.Where(s => keys.Contains(s.key)).ToArray();
    }

    public static void BuildAndShoot()
    {
        try { Build(); }
        catch (Exception e)
        {
            Directory.CreateDirectory("Docs/HybridStages");
            File.WriteAllText("Docs/HybridStages/build_error.txt", e.ToString());
            Debug.LogException(e); EditorApplication.Exit(1); return;
        }
        StoryStageVideo.HybridStills();
    }

    [MenuItem("Tools/VisionStriker/Build Hybrid Stage Examples (3)")]
    public static void Build()
    {
        foreach (string dir in new[] { "Textures", "Materials", "Profiles" }) Directory.CreateDirectory(Root + "/" + dir);
        Directory.CreateDirectory(SceneDir);
        AssetDatabase.Refresh();
        var skyShader = Shader.Find("VisionStriker/Painted Panorama Sky");
        var dotShader = Shader.Find("VisionStriker/Camp Smoke");
        var leafShader = Shader.Find("VisionStriker/Hybrid Leaf");
        var sheenShader = Shader.Find("VisionStriker/Hybrid Wet Sheen");
        var shaftShader = Shader.Find("VisionStriker/Hybrid Light Shaft");
        var cutShader = Shader.Find("VisionStriker/Remake Cutout");
        foreach (var shader in new[] { skyShader, dotShader, leafShader, sheenShader, shaftShader, cutShader })
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Hybrid stage shader missing/invalid: " + (shader ? shader.name + " " + string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message + " line " + m.line)) : "not found"));
        LeafTexture("Petal", false); LeafTexture("LeafBlade", true);
        var fx = new Fx { leaf = leafShader, sheen = sheenShader, shaft = shaftShader, cut = cutShader, dot = dotShader };
        foreach (var stage in ActiveStages()) BuildStage(stage, skyShader, dotShader, fx);
        AssetDatabase.SaveAssets();
    }

    // 스토리 모드가 새 무대를 불러오게 연결한다. 지도 노드별 씬 이름(RunConfig)을 바꾸고 빌드 목록에 새 씬을 넣는다.
    // (빌드 목록에 없는 씬은 SceneRoutes.LoadBattle 이 조용히 기본 전투 씬으로 바꿔 버리므로 둘을 항상 같이 한다.)
    [MenuItem("Tools/VisionStriker/Story Stages/Use New Stages")]
    public static void ApplyToStory() { SetStoryScenes(true); }

    // 원래 스토리 무대로 되돌린다. 새 씬은 빌드 목록에서 뺀다.
    [MenuItem("Tools/VisionStriker/Story Stages/Use Original Stages")]
    public static void RestoreOriginalStory() { SetStoryScenes(false); }

    // 배치: 새 무대를 연결한 뒤, 실제 스토리 진입 경로로 11곳을 차례로 불러 확인하고 영상으로 남긴다.
    public static void ApplyToStoryAndVerify() { SetStoryScenes(true); StoryStageVideo.Record(); }

    static void SetStoryScenes(bool useNew)
    {
        var config = Resources.Load<RunConfig>("RunConfig");
        if (!config) throw new Exception("RunConfig not found in Resources");
        var build = EditorBuildSettings.scenes.ToList();
        foreach (var s in Stages)
        {
            string path = ScenePath(s);
            if (useNew && !File.Exists(path)) throw new FileNotFoundException("Build the stages first: " + path);
            var entry = config.nodeBattleScenes.FirstOrDefault(e => e != null && e.nodeId == s.id);
            if (entry == null) { entry = new NodeBattleScene { nodeId = s.id }; config.nodeBattleScenes.Add(entry); }
            entry.sceneName = useNew ? s.sceneName : Path.GetFileNameWithoutExtension(s.sourceScene);
            build.RemoveAll(b => b.path == path);
            if (useNew) build.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = build.ToArray();
        EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
        Debug.Log("[HybridStages] story route -> " + (useNew ? "new stages" : "original stages") + ": " + string.Join(", ", config.nodeBattleScenes.Select(e => e.nodeId + "=" + e.sceneName)));
    }

    sealed class Fx { public Shader leaf, sheen, shaft, cut, dot; }

    static void BuildStage(Stage s, Shader skyShader, Shader dotShader, Fx fx)
    {
        // 배경 그림 가져오기 (2배로 선명하게 키운 것이 있으면 그것을 쓴다)
        // 확대본(StageConcepts/Backdrops/x2)은 저장소에 올리지 않는다. 없으면 이미 프로젝트에 들어 있는 사본을 그대로 쓴다.
        string texturePath = Root + "/Textures/" + s.key + ".png";
        string source = Source + "/x2/" + s.painting;
        if (File.Exists(source)) File.Copy(source, texturePath, true);
        else if (!File.Exists(texturePath))
        {
            source = Source + "/" + s.painting;
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            File.Copy(source, texturePath, true);
        }
        source = texturePath;
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4;
        importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        probe.LoadImage(File.ReadAllBytes(source));
        float aspect = (float)probe.width / probe.height;
        Color zenith = Average(probe, 0, 1, 0, .04f);
        Color haze = Average(probe, 0, 1, s.groundRow - .03f, s.groundRow + .02f);
        Object.DestroyImmediate(probe);

        // 스토리 씬을 복사해서 그 사본만 고친다.
        string path = ScenePath(s);
        AssetDatabase.DeleteAsset(path);
        if (!AssetDatabase.CopyAsset(s.sourceScene, path)) throw new Exception("Could not copy " + s.sourceScene);
        var scene = EditorSceneManager.OpenScene(path);
        var environment = scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("Story Stage") || g.name.StartsWith("Scout Pass"));
        if (!environment) throw new Exception("Environment root not found in " + s.sourceScene);

        if (s.remake != null) Remake(s, environment.transform, fx, haze);

        // 하늘과 먼 산: 단색 산 고리를 끄고 그림을 하늘로 두른다.
        int rings = 0;
        foreach (var t in environment.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("MountainRing")) { t.gameObject.SetActive(false); rings++; }
        var sky = LoadOrCreate(s.key + "_Sky", skyShader);
        sky.SetTexture("_MainTex", texture); sky.SetFloat("_Arc", s.arc); sky.SetFloat("_Center", 0); sky.SetFloat("_GroundV", 1 - s.groundRow);
        sky.SetFloat("_Lift", s.lift); sky.SetFloat("_Aspect", aspect); sky.SetColor("_Zenith", zenith); sky.SetColor("_Below", haze); sky.SetFloat("_Exposure", s.skyExposure); sky.SetFloat("_Glow", s.glow);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        // 해·달: 그림 속 위치에서 비추게 한다.
        float azimuth = (s.lightU - .5f) * s.arc;
        float elevation = Mathf.Max(s.minElevation, s.lift + (s.groundRow - s.lightRow) * (s.arc / aspect));
        var sun = RenderSettings.sun;
        if (!sun) sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (!sun) { sun = new GameObject("Stage daylight", typeof(Light)).GetComponent<Light>(); sun.type = LightType.Directional; sun.transform.SetParent(environment.transform, false); }
        sun.transform.rotation = Quaternion.LookRotation(-ToLight(azimuth, elevation));
        sun.color = s.keyColor; sun.intensity = s.keyIntensity; sun.shadows = LightShadows.Soft; sun.shadowStrength = .8f;
        RenderSettings.sun = sun;
        var fill = new GameObject("Hybrid soft fill", typeof(Light)).GetComponent<Light>();
        fill.transform.SetParent(environment.transform, false); fill.type = LightType.Directional; fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(.28f, -.34f, 1f));
        fill.color = s.fillColor; fill.intensity = s.fillIntensity;
        // 역광: 주광 반대편 뒤쪽에서 인물과 사물의 윤곽을 따 준다.
        var rim = new GameObject("Hybrid rim light", typeof(Light)).GetComponent<Light>();
        rim.transform.SetParent(environment.transform, false); rim.type = LightType.Directional; rim.shadows = LightShadows.None;
        rim.transform.rotation = Quaternion.LookRotation(-ToLight(azimuth >= 0 ? -58f : 58f, 34f));
        rim.color = Color.Lerp(s.keyColor, Color.white, .35f); rim.intensity = s.rim;

        // 공기: 안개와 주변광을 그림의 지평선 색에 맞춰 먼 지형이 그림 속으로 녹아들게 한다.
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = haze;
        RenderSettings.fogStartDistance = s.fogStart; RenderSettings.fogEndDistance = s.fogEnd;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = s.ambientSky; RenderSettings.ambientEquatorColor = s.ambientEquator; RenderSettings.ambientGroundColor = s.ambientGround;

        var camera = Camera.main;
        if (!camera) throw new Exception("Main camera not found in " + s.sourceScene);
        camera.clearFlags = CameraClearFlags.Skybox; camera.farClipPlane = Mathf.Max(camera.farClipPlane, 600);
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData)
        {
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cameraData.antialiasingQuality = AntialiasingQuality.High;
            EditorUtility.SetDirty(cameraData);
        }
        var selective = camera.GetComponent<SelectiveSceneryAtmosphere>();
        if (selective) { selective.hazeColor = haze * .92f; EditorUtility.SetDirty(selective); }

        // 후처리: 빛 번짐과 색 보정으로 3D 사물과 그림을 한 화면처럼 묶는다.
        var volume = new GameObject("Hybrid post-processing", typeof(Volume)).GetComponent<Volume>();
        volume.isGlobal = true; volume.priority = 5; volume.sharedProfile = Profile(s);

        // 떠다니는 것들
        var airRoot = new GameObject("Hybrid air").transform; airRoot.SetParent(environment.transform, false);
        if ((s.air & Air.Mist) != 0)
        {
            var mist = Dot(s.key + "_Mist", dotShader, new Color(Mathf.Lerp(haze.r, 1, .35f), Mathf.Lerp(haze.g, 1, .35f), Mathf.Lerp(haze.b, 1, .35f), .10f * s.mist));
            Drift(airRoot, "Low mist (back)", new Vector3(0, 1.4f, 26), new Vector3(80, 2.6f, 24), mist, 5, 16, 9, 16, new Vector3(.35f, .02f, 0), 110);
            Drift(airRoot, "Low mist (arena)", new Vector3(0, .35f, 4), new Vector3(30, .5f, 10), Dot(s.key + "_MistThin", dotShader, new Color(Mathf.Lerp(haze.r, 1, .35f), Mathf.Lerp(haze.g, 1, .35f), Mathf.Lerp(haze.b, 1, .35f), .08f * s.mist)), 3, 12, 4, 8, new Vector3(.25f, 0, 0), 50);
        }
        if ((s.air & Air.Motes) != 0)
            Drift(airRoot, "Floating motes", new Vector3(0, 2.2f, 1.5f), new Vector3(20, 4, 9), Dot(s.key + "_Mote", dotShader, new Color(Mathf.Min(1, s.keyColor.r * 1.1f), s.keyColor.g, s.keyColor.b * .9f, .85f)), 9, 9, .02f, .05f, new Vector3(.12f, .05f, 0), 110);
        if ((s.air & Air.Snow) != 0)
        {
            Drift(airRoot, "Snow (near)", new Vector3(0, 6, 3), new Vector3(24, 1, 8), Dot(s.key + "_Snow", dotShader, new Color(.95f, .97f, 1f, .9f)), 45, 7, .02f, .04f, new Vector3(-.5f, -1.1f, 0), 340);
            Drift(airRoot, "Snow (far)", new Vector3(0, 10, 22), new Vector3(70, 1, 24), Dot(s.key + "_SnowFar", dotShader, new Color(.85f, .9f, 1f, .6f)), 80, 10, .04f, .07f, new Vector3(-.6f, -1.2f, 0), 820);
        }
        if ((s.air & Air.Embers) != 0)
        {
            var ember = Dot(s.key + "_Ember", dotShader, new Color(1f, .55f, .18f, 1f));
            if (s.fires != null)
                foreach (var fire in s.fires)
                    Drift(airRoot, "Fire embers", fire + Vector3.up * .3f, new Vector3(.6f, .3f, .6f), ember, 9, 2.4f, .025f, .05f, new Vector3(.12f, 1.2f, 0), 30);
            foreach (var lamp in Object.FindObjectsByType<Light>().Where(l => l.type == LightType.Point && l.color.r > l.color.b * 1.3f && !l.name.StartsWith("Remake")))
                Drift(airRoot, "Brazier embers", lamp.transform.position + Vector3.up * .3f, new Vector3(.5f, .3f, .5f), ember, 7, 2.2f, .02f, .045f, new Vector3(.1f, 1.1f, 0), 24);
        }

        // 젖은 바닥 반사: 싸우는 자리 위에 얇게 덧씌워 하늘 그림과 해·달빛이 비치게 한다.
        if (s.sheen > 0)
        {
            var m = LoadOrCreate(s.key + "_Sheen", fx.sheen);
            m.SetTexture("_MainTex", texture); m.SetFloat("_Arc", s.arc); m.SetFloat("_Center", 0); m.SetFloat("_GroundV", 1 - s.groundRow);
            m.SetFloat("_Lift", s.lift); m.SetFloat("_Aspect", aspect); m.SetFloat("_Strength", s.sheen); m.SetFloat("_Spec", s.sheen * 1.6f);
            m.SetFloat("_Rough", 3.5f); m.SetVector("_Radius", new Vector4(7, 11.5f, 0, 0)); m.SetFloat("_Patch", .4f);
            EditorUtility.SetDirty(m);
            Plate("Wet sheen", environment.transform, m, new Vector3(0, .02f, 0), Quaternion.Euler(90, 0, 0), new Vector3(25, 25, 1));
        }
        // 빛살: 해·달 쪽에서 무대 뒤로 비스듬히 내려온다.
        if (s.rays > 0)
        {
            var m = LoadOrCreate(s.key + "_Shaft", fx.shaft);
            m.SetColor("_BaseColor", s.keyColor); m.SetFloat("_Strength", s.rays); m.SetFloat("_Speed", .03f);
            EditorUtility.SetDirty(m);
            Vector3 toLight = ToLight(azimuth, elevation);
            Vector3 facing = (Vector3.back - toLight * Vector3.Dot(Vector3.back, toLight)).normalized;
            for (int k = 0; k < 7; k++)
            {
                float x = Mathf.Lerp(-20, 20, k / 6f) + (k % 2 == 0 ? 1.5f : -1.2f), z = 7 + (k * 5 % 3) * 5f;
                const float length = 50; float width = 3f + (k % 3) * 1.6f;
                Plate("Light shaft", airRoot, m, new Vector3(x, 0, z) + toLight * (length * .45f), Quaternion.LookRotation(facing, toLight), new Vector3(width, length, 1));
            }
        }
        // 흩날리는 꽃잎·잎·재
        if (s.leaf != Leaf.None)
        {
            bool blade = s.leaf == Leaf.Bamboo || s.leaf == Leaf.Maple;
            var m = LoadOrCreate(s.key + "_Leaf", fx.leaf);
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + (blade ? "LeafBlade" : "Petal") + ".png"));
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Lit", s.leaf == Leaf.Ash ? .25f : .6f);
            EditorUtility.SetDirty(m);
            if (s.leaf == Leaf.Petal) Leaves(airRoot, "Drifting petals", m, new Color(1f, .80f, .86f), new Color(1f, .93f, .95f), 12, .06f, .11f, new Vector3(1.1f, -.45f, 0), 150);
            if (s.leaf == Leaf.Bamboo) Leaves(airRoot, "Drifting bamboo leaves", m, new Color(.50f, .72f, .30f), new Color(.72f, .80f, .38f), 12, .09f, .16f, new Vector3(1.2f, -.5f, 0), 150);
            if (s.leaf == Leaf.Maple) Leaves(airRoot, "Drifting maple leaves", m, new Color(.92f, .30f, .10f), new Color(1f, .58f, .16f), 12, .09f, .15f, new Vector3(1.0f, -.45f, 0), 150);
            if (s.leaf == Leaf.Ash) Leaves(airRoot, "Drifting ash", m, new Color(.42f, .40f, .42f), new Color(1f, .52f, .20f), 18, .03f, .07f, new Vector3(.7f, .12f, 0), 220);
        }

        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[HybridStages] " + s.key + " scene=" + path + " rings off=" + rings + " light az=" + azimuth.ToString("0.0") + " el=" + elevation.ToString("0.0") + " haze=" + haze + " post=" + (cameraData ? "on" : "no camera data"));
    }

    // 리메이크: 원래 무대의 3D 사물을 모두 끄고, 실제 조명을 받는 새 바닥과 여러 겹의 그림 구조물로 다시 짓는다.
    static void Remake(Stage s, Transform environment, Fx fx, Color haze)
    {
        foreach (Transform child in environment)
        {
            bool holdsSun = child.GetComponentsInChildren<Light>(true).Any(l => l.type == LightType.Directional);
            if (!holdsSun) { child.gameObject.SetActive(false); continue; }
            foreach (var r in child.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var l in child.GetComponentsInChildren<Light>(true)) if (l.type != LightType.Directional) l.enabled = false;
        }
        string sourceDir = "StageConcepts/Remake/" + s.remake + "/ready", assetDir = Root + "/Remake/" + s.remake;
        Directory.CreateDirectory(assetDir);
        var root = new GameObject("Remake set").transform; root.SetParent(environment, false);

        // 바닥: 새로 그린 재질을 타일로 깔고 실제 조명과 그림자를 받게 한다.
        Texture2D albedo, normal;
        if (s.floorAlbedoAsset != null)
        {
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(s.floorAlbedoAsset);
            normal = s.floorNormalAsset != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(s.floorNormalAsset) : null;
            if (!albedo) throw new Exception("Missing floor texture " + s.floorAlbedoAsset);
        }
        else
        {
            albedo = ImportRemake(sourceDir + "/floor_albedo.png", assetDir + "/floor_albedo.png", false, false);
            normal = ImportRemake(sourceDir + "/floor_normal.png", assetDir + "/floor_normal.png", true, false);
        }
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var floorMaterial = LoadOrCreate(s.key + "_RemakeFloor", lit);
        const float floorSize = 160f; float tiling = floorSize / s.floorTile;
        floorMaterial.SetTexture("_BaseMap", albedo); floorMaterial.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        floorMaterial.SetColor("_BaseColor", s.floorTint);
        if (normal) { floorMaterial.SetTexture("_BumpMap", normal); floorMaterial.EnableKeyword("_NORMALMAP"); floorMaterial.SetFloat("_BumpScale", 1.1f); }
        floorMaterial.SetFloat("_Smoothness", s.floorSmoothness); floorMaterial.SetFloat("_Metallic", 0);
        EditorUtility.SetDirty(floorMaterial);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Remake floor"; floor.transform.SetParent(root, false);
        floor.transform.SetPositionAndRotation(new Vector3(0, 0, 20), Quaternion.Euler(90, 0, 0)); floor.transform.localScale = new Vector3(floorSize, floorSize, 1);
        foreach (var c in floor.GetComponents<Collider>()) Object.DestroyImmediate(c);
        var floorRenderer = floor.GetComponent<MeshRenderer>();
        floorRenderer.sharedMaterial = floorMaterial; floorRenderer.shadowCastingMode = ShadowCastingMode.Off; floorRenderer.receiveShadows = true;

        // 구조물 그림: 배경을 걷어낸 그림을 깊이를 달리해 세운다. 바닥에 닿는 자리에는 부드러운 그늘을 깐다.
        var shade = Dot(s.key + "_RemakeShade", fx.dot, new Color(0, 0, 0, .5f));
        int index = 0;
        foreach (var cut in s.cuts ?? new Cut[0])
        {
            var texture = ImportRemake(sourceDir + "/" + cut.file + ".png", assetDir + "/" + cut.file + ".png", false, true);
            float height = cut.width * texture.height / texture.width;
            var m = LoadOrCreate(s.key + "_Cut" + index++, fx.cut);
            m.SetTexture("_MainTex", texture); m.SetColor("_Tint", Color.white); m.SetColor("_FogColor", haze);
            m.SetFloat("_Fog", Mathf.Clamp01(Mathf.InverseLerp(s.fogStart, s.fogEnd, cut.z + 5)) * .7f);
            m.SetFloat("_Glow", s.cutGlow); m.SetFloat("_BaseFog", s.baseFog); m.SetFloat("_BaseFogHeight", Mathf.Max(2f, height * .3f));
            EditorUtility.SetDirty(m);
            // 이어 붙이는 장은 번갈아 뒤집어서 이음매의 그림이 맞물리게 하고, 틈이 보이지 않게 조금 겹친다.
            float step = cut.width - .04f;
            int from = cut.dir > 0 ? 0 : -cut.tile, to = cut.dir < 0 ? 0 : cut.tile;
            for (int k = from; k <= to; k++)
            {
                bool flip = cut.flip ^ (Mathf.Abs(k) % 2 == 1);
                Plate("Set piece · " + cut.file + (k == 0 ? "" : " (" + k + ")"), root, m, new Vector3(cut.x + k * step, height * .5f - .06f, cut.z), Quaternion.identity, new Vector3(flip ? -cut.width : cut.width, height, 1));
            }
            float spanCentre = cut.x + (from + to) * .5f * step, spanWidth = cut.width + (to - from) * step;
            Plate("Contact shade", root, shade, new Vector3(spanCentre, .015f, cut.z - .8f), Quaternion.Euler(90, 0, 0), new Vector3(spanWidth * 1.05f, 5f, 1));
        }

        // 뒤쪽 원경: 기존 3D 소나무·바위를 흩어 놓아 그림 구조물 뒤로 실제 깊이를 만든다.
        foreach (var prop in s.props ?? new Prop[0])
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environments/ScoutPass/Prefabs/" + prop.prefab + ".prefab");
            if (!asset) throw new Exception("Missing prefab " + prop.prefab);
            var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
            g.transform.rotation = Quaternion.Euler(0, prop.yaw, 0);
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<Collider2D>(true)) Object.DestroyImmediate(c);
            var renderers = g.GetComponentsInChildren<Renderer>(true); var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            float k = prop.size / Mathf.Max(b.size.x, b.size.y, b.size.z); g.transform.localScale *= k;
            b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            g.transform.position += new Vector3(prop.x, 0, prop.z) - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        // 화톳불: 그림 속 불 자리에 실제 조명을 놓아 인물과 바닥이 불빛을 받게 한다.
        foreach (var fire in s.fires ?? new Vector3[0])
        {
            var lamp = new GameObject("Remake fire light", typeof(Light), typeof(StoryStageMotion));
            lamp.transform.SetParent(root, false); lamp.transform.position = fire;
            var l = lamp.GetComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .58f, .26f); l.range = s.fireRange; l.intensity = s.fireIntensity; l.shadows = LightShadows.None;
            lamp.GetComponent<StoryStageMotion>().flicker = true;
        }
        // 큰 불빛: 모닥불·등롱 무리처럼 넓게 퍼져 바닥에 따뜻한 빛 웅덩이를 만들고 인물 등 뒤를 비춘다.
        foreach (var glow in s.glows ?? new Vector4[0])
        {
            var lamp = new GameObject("Remake glow light", typeof(Light), typeof(StoryStageMotion));
            lamp.transform.SetParent(root, false); lamp.transform.position = new Vector3(glow.x, glow.y, glow.z);
            var l = lamp.GetComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .6f, .3f); l.range = 30; l.intensity = glow.w; l.shadows = LightShadows.None;
            lamp.GetComponent<StoryStageMotion>().flicker = true;
        }
    }

    static Texture2D ImportRemake(string source, string path, bool isNormal, bool cutout)
    {
        // 가공본(StageConcepts/Remake/<무대>/ready)은 저장소에 올리지 않는다. 없으면 이미 프로젝트에 들어 있는 사본을 그대로 쓴다.
        if (File.Exists(source)) File.Copy(source, path, true);
        else if (!File.Exists(path)) throw new FileNotFoundException(source);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !isNormal; importer.npotScale = TextureImporterNPOTScale.None; importer.mipmapEnabled = true;
        importer.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 16;
        importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.alphaSource = cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; importer.alphaIsTransparency = cutout;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture) throw new Exception("Texture import failed: " + path);
        return texture;
    }

    static VolumeProfile Profile(Stage s)
    {
        string path = Root + "/Profiles/" + s.key + ".asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        // 영화풍: 밝은 곳이 타지 않게 눌러 주고(톤매핑), 빛 번짐을 키우고, 그늘과 밝은 곳에 서로 다른 색을 입힌다.
        Add<Tonemapping>(profile).mode.value = TonemappingMode.Neutral;
        var bloom = Add<Bloom>(profile); bloom.threshold.value = .9f; bloom.intensity.value = s.bloom + .25f; bloom.scatter.value = .8f; bloom.highQualityFiltering.value = true;
        var grade = Add<ColorAdjustments>(profile); grade.contrast.value = s.contrast + 6; grade.saturation.value = s.saturation + 6; grade.postExposure.value = s.exposure + .18f;
        var split = Add<SplitToning>(profile);
        split.shadows.value = Color.Lerp(new Color(.5f, .5f, .5f), s.fillColor, .22f); split.highlights.value = Color.Lerp(new Color(.5f, .5f, .5f), s.keyColor, .18f);
        Add<WhiteBalance>(profile).temperature.value = s.temperature;
        var vignette = Add<Vignette>(profile); vignette.intensity.value = s.vignette; vignette.smoothness.value = .5f;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(true);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    static Vector3 ToLight(float azimuth, float elevation)
    {
        float a = azimuth * Mathf.Deg2Rad, e = elevation * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
    }

    static Color Average(Texture2D t, float x0, float x1, float fromTop0, float fromTop1)
    {
        int w = t.width, h = t.height;
        int xa = Mathf.Clamp((int)(x0 * w), 0, w - 1), xb = Mathf.Clamp((int)(x1 * w), xa + 1, w);
        int ya = Mathf.Clamp((int)((1 - Mathf.Clamp01(fromTop1)) * h), 0, h - 1), yb = Mathf.Clamp((int)((1 - Mathf.Clamp01(fromTop0)) * h), ya + 1, h);
        var pixels = t.GetPixels(xa, ya, xb - xa, yb - ya);
        float r = 0, g = 0, b = 0;
        foreach (var p in pixels) { r += p.r; g += p.g; b += p.b; }
        float n = Mathf.Max(1, pixels.Length);
        return new Color(r / n, g / n, b / n, 1);
    }

    static Material LoadOrCreate(string name, Shader shader)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        return m;
    }

    static Material Dot(string name, Shader shader, Color color)
    {
        var m = LoadOrCreate(name, shader);
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Plate(string name, Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name; g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(position, rotation); g.transform.localScale = scale;
        foreach (var c in g.GetComponents<Collider>()) Object.DestroyImmediate(c);
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // 잎 모양 텍스처 (끝이 뾰족한 잎 / 둥근 꽃잎). 색은 입자 쪽에서 입힌다.
    static void LeafTexture(string name, bool pointed)
    {
        const int n = 64; var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1, t = Mathf.Abs(v);
            float half = pointed ? .40f * Mathf.Pow(Mathf.Max(0, 1 - t), .75f) : .55f * Mathf.Sqrt(Mathf.Max(0, 1 - t * t)) * (v > 0 ? 1f : .8f);
            float a = Mathf.Clamp01((half - Mathf.Abs(u)) / .08f) * Mathf.Clamp01((1 - t) / .06f);
            float shade = .80f + .20f * Mathf.Abs(u) / Mathf.Max(half, .01f);
            if (pointed && Mathf.Abs(u) < .04f) shade *= .8f;
            px[y * n + x] = new Color(shade, shade, shade, a);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
        string path = Root + "/Textures/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true; importer.maxTextureSize = 64; importer.SaveAndReimport();
    }

    static void Leaves(Transform parent, string name, Material material, Color a, Color b, float rate, float sizeMin, float sizeMax, Vector3 wind, int max)
    {
        var ps = new GameObject(name, typeof(ParticleSystem)).GetComponent<ParticleSystem>();
        ps.transform.SetParent(parent, false); ps.transform.position = new Vector3(0, 4.5f, 3);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(7, 10); main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); main.maxParticles = max; main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(32, 6, 14);
        var emission = ps.emission; emission.rateOverTime = rate;
        var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = ParticleSystemSimulationSpace.World;
        drift.x = new ParticleSystem.MinMaxCurve(wind.x * .7f, wind.x * 1.3f); drift.y = new ParticleSystem.MinMaxCurve(wind.y - .15f, wind.y + .15f); drift.z = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        var spin = ps.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-2.6f, 2.6f);
        var flutter = ps.noise; flutter.enabled = true; flutter.strength = .55f; flutter.frequency = .35f; flutter.scrollSpeed = .3f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(1, .8f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void Drift(Transform parent, string name, Vector3 center, Vector3 box, Material material, float rate, float life, float sizeMin, float sizeMax, Vector3 velocity, int max)
    {
        var ps = new GameObject(name, typeof(ParticleSystem)).GetComponent<ParticleSystem>();
        ps.transform.SetParent(parent, false); ps.transform.position = center;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = life; main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); main.maxParticles = max; main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var emission = ps.emission; emission.rateOverTime = rate;
        var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = ParticleSystemSimulationSpace.World;
        drift.x = new ParticleSystem.MinMaxCurve(velocity.x); drift.y = new ParticleSystem.MinMaxCurve(velocity.y); drift.z = new ParticleSystem.MinMaxCurve(velocity.z);
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(1, .75f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
