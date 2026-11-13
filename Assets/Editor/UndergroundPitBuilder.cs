using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// PvP 전용 맵 "지하 동굴 투기장"을 만든다.
/// PvP가 쓰는 전투 씬(ScoutPass)을 복사해 인물·카메라·HUD·경기장 벽은 그대로 두고, 풍경만 걷어내고 새로 짓는다.
/// 카메라가 싸움을 따라 돌기 때문에 배경은 평면 한 장이 아니라 경기장을 둥글게 두르는 벽이다(목책 → 관중석 → 동굴 벽).
///
///   Unity -batchmode -executeMethod UndergroundPitBuilder.BuildAndShoot [-pitOut 폴더]
///   메뉴: Tools/VisionStriker/PvP/Build Underground Pit
///
/// 그림은 ArtSource/UndergroundPit/ready 에서 가져온다(없으면 프로젝트 안의 사본을 그대로 쓴다). 방법과 수치는 Docs/UndergroundPit.md.
/// </summary>
public static class UndergroundPitBuilder
{
    public const string Root = "Assets/Environments/UndergroundPit";
    public const string SceneFolder = "Assets/Scenes/PvpStages", ScenePath = SceneFolder + "/UndergroundPit.unity";
    public const string SourceScene = "Assets/Scenes/ScoutPass.unity";
    public const string Source = "ArtSource/UndergroundPit/ready";

    // ── 배치 (m). 인물은 x 축 위(-6.5 ~ 6.5)에서 싸우고 카메라는 -z 쪽에서 본다. ──
    const float FloorTile = 4f;                                             // 바닥 그림 한 장의 크기 (이음매 없이 반복)
    const float WallRadius = 11f; const int WallRepeats = 4;                // 목책
    const float GalleryRadius = 14.5f, GalleryBase = 1.6f; const int GalleryRepeats = 4;   // 관중석
    const float RockRadius = 12.6f, RockTop = 8.1f; const int RockRepeats = 3;             // 위에서 내려온 종유석
    const float CavernRadius = 34f, CavernBase = -6f; const int CavernRepeats = 3;         // 동굴 벽
    const float GateWidth = 8.7f;
    const float CornerX = 8.3f, CornerZ = 2.2f, CornerHeight = 4.6f;        // 청·홍 진영 기둥
    const float BrazierX = 5.4f, BrazierBottom = 3.6f, BrazierZ = 7.9f, BrazierHeight = 3.3f;   // 매달린 화로 (불은 밑에서 1/3 높이)
    static readonly Color Haze = new Color(.085f, .062f, .045f);
    static readonly Color Fire = new Color(1f, .56f, .24f);
    static readonly Color Blue = new Color(.25f, .5f, 1f), Red = new Color(1f, .22f, .16f);
    // 목책 그림 속 횃불 자리 (거울로 이어 붙인 한 쌍 안에서의 가로 위치, 바닥에서의 높이 비율)
    static readonly float[] TorchU = { .0795f, .25f, .42f, .58f, .75f, .9205f }; const float TorchV = .685f;

    static Shader pieceShader, glowShader, hazeShader, paintShader, litShader;
    static readonly StringBuilder report = new StringBuilder();

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    /// <summary>배치용: 무대를 만들고 정지 화면을 찍은 뒤 끝낸다.</summary>
    public static void BuildAndShoot()
    {
        string outDir = Arg("-pitOut", "Logs/UndergroundPit");
        Directory.CreateDirectory(outDir);
        try
        {
            Build();
            Shots(outDir);
            File.WriteAllText(Path.Combine(outDir, "build_report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(outDir, "build_report.txt"), report + "\nERROR " + e);
            File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "FAIL " + e.Message);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>배치용: 이미 만든 무대의 정지 화면만 다시 찍는다.</summary>
    public static void ShootOnly()
    {
        string outDir = Arg("-pitOut", "Logs/UndergroundPit");
        Directory.CreateDirectory(outDir);
        try { Shots(outDir); File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "PASS"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "FAIL " + e.Message); EditorApplication.Exit(1); }
    }

    /// <summary>
    /// 배치용: 같은 구도로 기존 전투 씬 두 곳과 이 무대를 그려, 1920x1080 한 장을 그리는 데 걸리는 시간을 견준다.
    /// 플레이 없이 카메라만 돌리므로 전투 로직·애니메이션 비용은 들어 있지 않다. 결과는 render_cost.txt.
    /// </summary>
    public static void RenderCost()
    {
        string outDir = Arg("-pitOut", "Logs/UndergroundPit");
        Directory.CreateDirectory(outDir);
        var lines = new StringBuilder();
        try
        {
            lines.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + ", 1920x1080, edit-mode camera renders with a forced GPU sync");
            foreach (string path in new[] { SourceScene, "Assets/Scenes/StoryStages/SealedGate.unity", ScenePath })
            {
                EditorSceneManager.OpenScene(path);
                var camera = Camera.main;
                var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(8f, true, true);
                foreach (var pose in new[] { ("close", 3.75f), ("far apart", 9.9f) })
                {
                    Vector3 eye = new Vector3(0, 1.06f, 0), back = Vector3.back * Mathf.Cos(3.5f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(3.5f * Mathf.Deg2Rad);
                    camera.transform.position = eye + back * pose.Item2; camera.transform.rotation = Quaternion.LookRotation(eye - camera.transform.position);
                    foreach (var prop in Object.FindObjectsByType<UndergroundPitProp>(FindObjectsSortMode.None)) prop.Face(camera.transform.position, 1.3f);
                    camera.targetTexture = rt;
                    void Frame() { camera.Render(); RenderTexture.active = rt; probe.ReadPixels(new Rect(960, 540, 1, 1), 0, 0); RenderTexture.active = null; }
                    for (int i = 0; i < 40; i++) Frame();
                    const int frames = 240;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < frames; i++) Frame();
                    watch.Stop();
                    camera.targetTexture = null;
                    lines.AppendLine(Path.GetFileNameWithoutExtension(path) + " (" + pose.Item1 + "): " + (watch.Elapsed.TotalMilliseconds / frames).ToString("F2") + " ms per frame");
                }
                Object.DestroyImmediate(rt); Object.DestroyImmediate(probe);
            }
            File.WriteAllText(Path.Combine(outDir, "render_cost.txt"), lines.ToString());
            File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(outDir, "render_cost.txt"), lines + "\nERROR " + e);
            File.WriteAllText(Path.Combine(outDir, "build_done.txt"), "FAIL " + e.Message);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/VisionStriker/PvP/Build Underground Pit")]
    public static void Build()
    {
        report.Length = 0;
        foreach (string dir in new[] { "Textures", "Materials", "Meshes", "Profiles" }) Directory.CreateDirectory(Root + "/" + dir);
        Directory.CreateDirectory(SceneFolder);
        AssetDatabase.Refresh();

        pieceShader = Shader.Find("VisionStriker/Pit/Set Piece"); glowShader = Shader.Find("VisionStriker/Pit/Glow");
        hazeShader = Shader.Find("VisionStriker/Pit/Haze"); paintShader = Shader.Find("VisionStriker/Pit/Floor Paint");
        litShader = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var shader in new[] { pieceShader, glowShader, hazeShader, paintShader, litShader })
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Pit shader missing or broken: " + (shader ? shader.name + " " + string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message + " line " + m.line)) : "not found"));

        // 그림
        var gate = Import("gate", Kind.Cutout); var palisade = Import("palisade", Kind.Strip); var gallery = Import("gallery", Kind.Strip);
        var rocks = Import("stalactites", Kind.Strip); var cavern = Import("cavern", Kind.Backdrop);
        var cornerBlue = Import("corner_blue", Kind.Cutout); var cornerRed = Import("corner_red", Kind.Cutout);
        var cage = Import("hang_cage", Kind.Cutout); var brazier = Import("hang_brazier", Kind.Cutout); var lamp = Import("hang_lamp", Kind.Cutout);
        var floorAlbedo = Import("floor_albedo", Kind.Floor); var floorNormal = Import("floor_normal", Kind.Normal); var floorRing = Import("floor_ring", Kind.Paint); var floorShade = Import("floor_shade", Kind.Paint);
        var fxGlow = Import("fx_glow", Kind.Sprite); var fxFlame = Import("fx_flame", Kind.Sprite); var fxShaft = Import("fx_shaft", Kind.Sprite); var fxHaze = Import("fx_haze", Kind.Sprite);

        // 씬: 원본을 복사해 사본만 고친다. 다시 만들 때는 파일 내용만 덮어써서 씬의 GUID 가 바뀌지 않게 한다.
        if (File.Exists(ScenePath)) { File.Copy(SourceScene, ScenePath, true); AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate); }
        else if (!AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new Exception("Could not copy " + SourceScene);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        StripScenery(scene);

        var root = new GameObject("Underground Pit").transform;
        var card = Card("PitCard", 0f, 1f); var hang = Card("PitHang", -1f, 0f);

        // ── 바닥: 실제 조명과 그림자를 받는다 ──
        var floorMaterial = Mat("Floor", litShader);
        const float floorSize = 96f; float tiling = floorSize / FloorTile;
        floorMaterial.SetTexture("_BaseMap", floorAlbedo); floorMaterial.SetTextureScale("_BaseMap", new Vector2(tiling, tiling)); floorMaterial.SetTextureOffset("_BaseMap", Vector2.zero);
        floorMaterial.SetTexture("_BumpMap", floorNormal); floorMaterial.EnableKeyword("_NORMALMAP"); floorMaterial.SetFloat("_BumpScale", 1.15f);
        floorMaterial.SetColor("_BaseColor", new Color(.92f, .83f, .72f)); floorMaterial.SetFloat("_Smoothness", .24f); floorMaterial.SetFloat("_Metallic", 0);
        EditorUtility.SetDirty(floorMaterial);
        var floor = Quad("Floor", root, floorMaterial, new Vector3(0, 0, 0), Quaternion.Euler(90, 0, 0), new Vector3(floorSize, floorSize, 1));
        floor.receiveShadows = true;
        // 넓은 얼룩: 모래가 쌓인 곳, 밟혀 어두운 곳, 벽 쪽 그늘. 반복되지 않아서 4m 타일의 반복을 가린다.
        var shading = Mat("FloorShade", paintShader); shading.SetTexture("_MainTex", floorShade); shading.SetFloat("_Strength", 1f); EditorUtility.SetDirty(shading);
        Quad("Floor shading", root, shading, new Vector3(0, .003f, 0), Quaternion.Euler(90, 0, 0), new Vector3(40, 40, 1));
        // 바랜 경기선: 가운데 타일 2 x 2 장 위에 정확히 겹친다 (돌 위에만 남고 모래 낀 틈에서는 지워진 그림)
        var paint = Mat("FloorPaint", paintShader); paint.SetTexture("_MainTex", floorRing); paint.SetFloat("_Strength", 1f); EditorUtility.SetDirty(paint);
        Quad("Floor paint", root, paint, new Vector3(0, .005f, 0), Quaternion.Euler(90, 0, 0), new Vector3(8, 8, 1));

        // ── 둥근 벽: 먼 것부터 그린다 (재질의 그리는 순서로 정한다) ──
        float cavernHeight = Circumference(CavernRadius) / CavernRepeats * cavern.height / cavern.width;
        Ring("Cavern wall", root, Piece("Cavern", cavern, 0, .14f, 0, 1, 1.2f, new Color(1.15f, 1.05f, .98f)), CavernRadius, CavernBase, CavernRadius, CavernBase + cavernHeight, CavernRepeats, .25f);
        // 천장: 종유석 위에서 동굴 벽 꼭대기까지 비스듬히 덮어 위쪽이 비어 보이지 않게 한다.
        Ring("Cavern ceiling", root, Piece("Ceiling", cavern, 2, .16f, 0, 1, 0f, new Color(1.5f, 1.0f, .68f)), RockRadius + .15f, RockTop - .05f, CavernRadius, CavernBase + cavernHeight, 4, .1f);

        float galleryHeight = Circumference(GalleryRadius) / GalleryRepeats * gallery.height / gallery.width;
        // 관중석 바로 뒤의 등불 받은 바위벽: 검은 관중이 이 벽을 등지고 윤곽으로 보인다.
        Ring("Gallery backlight", root, Piece("GalleryBack", cavern, 8, .05f, 0, 1, 0f, new Color(2.3f, 1.5f, .88f)), GalleryRadius + .5f, GalleryBase + .8f, GalleryRadius + .5f, GalleryBase + galleryHeight + .4f, 6, .1f);
        var galleryMaterial = Piece("Gallery", gallery, 10, .10f, 0, 1, 2.0f, new Color(1f, .95f, .90f));
        Ring("Spectator galleries", root, galleryMaterial, GalleryRadius, GalleryBase, GalleryRadius, GalleryBase + galleryHeight, GalleryRepeats, 0f);

        float rockHeight = Circumference(RockRadius) / RockRepeats * rocks.height / rocks.width;
        Ring("Stalactites", root, Piece("Stalactites", rocks, 14, .22f, 0, 1, 0f, new Color(.80f, .74f, .70f)), RockRadius, RockTop - rockHeight, RockRadius, RockTop, RockRepeats, 0f);

        float wallHeight = Circumference(WallRadius) / WallRepeats * palisade.height / palisade.width;
        var wallMaterial = Piece("Palisade", palisade, 20, .06f, .34f, 1.3f, 1.5f, Color.white);
        Ring("Palisade", root, wallMaterial, WallRadius, 0, WallRadius, wallHeight, WallRepeats, 0f);
        // 목책이 바닥에 닿는 자리의 그늘
        var shade = Mat("ContactShade", hazeShader); shade.SetColor("_Color", new Color(0, 0, 0, .62f)); shade.renderQueue = 3000 + 21; EditorUtility.SetDirty(shade);
        Annulus("Palisade contact shade", root, shade, WallRadius - 1.5f, WallRadius + .1f, .012f);

        // ── 정문 (뒤 가운데)과 맞은편 문 ──
        float gateHeight = GateWidth * gate.height / gate.width;
        var gateMaterial = Piece("Gate", gate, 22, .03f, .22f, 1.1f, 1.6f, Color.white);
        Plate("Main gate", root, card, gateMaterial, new Vector3(0, -.03f, WallRadius - .35f), Quaternion.identity, new Vector3(GateWidth, gateHeight, 1));
        Plate("Rear gate", root, card, gateMaterial, new Vector3(0, -.03f, -(WallRadius - .35f)), Quaternion.Euler(0, 180, 0), new Vector3(GateWidth, gateHeight, 1));

        // ── 청·홍 진영 기둥: 늘 카메라를 본다 ──
        var blobShade = Mat("BlobShade", hazeShader); blobShade.SetTexture("_MainTex", fxGlow); blobShade.SetColor("_Color", new Color(0, 0, 0, .7f)); blobShade.renderQueue = 3000 + 23; EditorUtility.SetDirty(blobShade);
        float cornerWidth = CornerHeight * cornerBlue.width / cornerBlue.height;
        foreach (int side in new[] { -1, 1 })
        {
            var material = Piece(side < 0 ? "CornerBlue" : "CornerRed", side < 0 ? cornerBlue : cornerRed, 30, .02f, .15f, .9f, 2.2f, Color.white);
            var post = Plate(side < 0 ? "Blue corner" : "Red corner", root, card, material, new Vector3(side * CornerX, -.02f, CornerZ), Quaternion.identity, new Vector3(cornerWidth, CornerHeight, 1));
            post.gameObject.AddComponent<UndergroundPitProp>();
            Quad("Corner shade", root, blobShade, new Vector3(side * CornerX, .014f, CornerZ - .1f), Quaternion.Euler(90, 0, 0), new Vector3(cornerWidth * 1.5f, 1.5f, 1));
        }

        // ── 사슬에 매달린 것들: 천천히 흔들린다. 싸우는 자리 위는 비워 둔다. ──
        var hangMaterials = new Dictionary<Texture2D, Material>
        {
            { brazier, Piece("HangBrazier", brazier, 30, .04f, 0, 1, 2.0f, Color.white) },
            { cage, Piece("HangCage", cage, 30, .08f, 0, 1, .6f, new Color(.9f, .86f, .82f)) },
            { lamp, Piece("HangLamp", lamp, 30, .04f, 0, 1, 2.4f, Color.white) },
        };
        var hangs = new (Texture2D picture, float x, float bottom, float z, float height, float swing, float period)[]
        {
            (brazier, -BrazierX, BrazierBottom, BrazierZ, BrazierHeight, 0f, 5f), (brazier, BrazierX, BrazierBottom, BrazierZ, BrazierHeight, 0f, 5f),
            (cage, -8.2f, 3.5f, 6.4f, 3.0f, 2.6f, 4.1f), (cage, 8.4f, 3.7f, 6.0f, 3.0f, 2.6f, 3.7f),
            (lamp, -2.3f, 4.0f, 9.2f, 1.8f, 3.2f, 3.1f), (lamp, 2.4f, 4.1f, 9.1f, 1.8f, 3.2f, 3.4f),
            (lamp, -6.6f, 3.9f, 8.6f, 1.8f, 3.2f, 2.9f), (lamp, 6.5f, 3.8f, 8.7f, 1.8f, 3.2f, 3.6f),
        };
        var chainMaterial = Mat("Chain", hazeShader); chainMaterial.SetTexture("_MainTex", null); chainMaterial.SetColor("_Color", new Color(.07f, .05f, .04f, 1f)); chainMaterial.renderQueue = 3000 + 29; EditorUtility.SetDirty(chainMaterial);
        int number = 0;
        foreach (var h in hangs)
        {
            float width = h.height * h.picture.width / h.picture.height;
            var plate = Plate("Hanging " + h.picture.name + " " + ++number, root, hang, hangMaterials[h.picture], new Vector3(h.x, h.bottom + h.height, h.z), Quaternion.identity, new Vector3(width, h.height, 1));
            var prop = plate.gameObject.AddComponent<UndergroundPitProp>(); prop.swing = h.swing; prop.period = h.period; prop.phase = number * 1.7f;
            // 그림의 사슬은 그림 위쪽 끝에서 끊기므로 천장까지 이어 준다.
            float chainTop = RockTop + 2.5f;
            var chain = Plate("Chain " + number, root, card, chainMaterial, new Vector3(h.x, h.bottom + h.height - .05f, h.z), Quaternion.identity, new Vector3(.055f, chainTop - (h.bottom + h.height) + .05f, 1));
            chain.gameObject.AddComponent<UndergroundPitProp>();
        }

        // ── 빛 ──
        var lights = new GameObject("Lights").transform; lights.SetParent(root, false);
        // 천장 구멍에서 내려오는 차가운 빛: 인물의 윤곽을 따 주고 그림자를 만든다.
        var roof = NewLight(lights, "Roof light (shadows)", LightType.Directional, new Color(.90f, .93f, 1f), .42f);
        roof.transform.rotation = Quaternion.LookRotation(new Vector3(.15f, -.86f, -.48f)); roof.shadows = LightShadows.Soft; roof.shadowStrength = .78f;
        RenderSettings.sun = roof;
        var fill = NewLight(lights, "Warm front fill", LightType.Directional, new Color(1f, .76f, .54f), .52f);
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(.38f, -.30f, .86f));
        var rim = NewLight(lights, "Back rim", LightType.Directional, new Color(1f, .84f, .64f), .6f);
        rim.transform.rotation = Quaternion.LookRotation(new Vector3(-.2f, -.34f, -.92f));
        var pool = NewLight(lights, "Roof shaft pool", LightType.Spot, new Color(1f, .94f, .84f), 70f);
        pool.transform.position = new Vector3(0, 17f, 9.5f); pool.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, 2.2f) - pool.transform.position);
        pool.range = 34; pool.spotAngle = 36; pool.innerSpotAngle = 18;

        float gateFlameX = (.5f - .083f) * GateWidth, gateFlameY = .44f * gateHeight;
        foreach (int side in new[] { -1, 1 })
        {
            Torch(lights, "Gate brazier light", new Vector3(side * gateFlameX, gateFlameY + .25f, WallRadius - 1.1f), Fire, 12f, 13f, side);
            Torch(lights, "Hanging brazier light", new Vector3(side * BrazierX, BrazierBottom + BrazierHeight * .33f - .2f, BrazierZ - .2f), Fire, 9f, 11f, side * 2);
            Torch(lights, side < 0 ? "Blue lantern light" : "Red lantern light", new Vector3(side * (CornerX - .3f), 3.2f, CornerZ - .25f), side < 0 ? Blue : Red, 7f, 7.5f, side * 3);
        }

        // ── 불빛과 불꽃 ──
        var fx = new GameObject("Fire and air").transform; fx.SetParent(root, false);
        var torchGlow = Glow("TorchGlow", fxGlow, Bright(new Color(1f, .52f, .18f), 1.25f), .5f, true);
        var bigGlow = Glow("BrazierGlow", fxGlow, Bright(new Color(1f, .5f, .16f), .6f), .35f, true);
        int torchCount = 0, torchLights = 0;
        for (int k = 0; k < WallRepeats; k++)
            foreach (float u in TorchU)
            {
                float angle = Mathf.DeltaAngle(0, (k + u) / WallRepeats * 360f);          // 0 = 뒤 가운데, + 는 오른쪽
                if (Mathf.Abs(angle) < 26f || Mathf.Abs(angle) > 154f) continue;         // 문 뒤에 가려진 횃불
                Vector3 at = OnRing(WallRadius - .22f, angle, TorchV * wallHeight);
                Quad("Torch glow", fx, torchGlow, at, Quaternion.identity, new Vector3(1.45f, 1.45f, 1));
                Embers(fx, at + Vector3.up * .15f, 3f, .5f);
                torchCount++;
                // 실제 조명은 화면에 주로 보이는 뒤쪽 절반에서 하나 걸러 하나만 둔다.
                if (Near(Mathf.Abs(angle), 37.8f) || Near(Mathf.Abs(angle), 67.5f)) { Torch(lights, "Wall torch light", OnRing(WallRadius - .8f, angle, TorchV * wallHeight + .1f), Fire, 6f, 8.5f, torchCount); torchLights++; }
            }
        var flame = Glow("Flame", fxFlame, new Color(1.5f, 1.05f, .6f), 0, false);
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 gateFire = new Vector3(side * gateFlameX, gateFlameY, WallRadius - .5f), hangFire = new Vector3(side * BrazierX, BrazierBottom + BrazierHeight * .33f, BrazierZ - .05f);
            Flames(fx, "Gate brazier fire", gateFire, .95f, flame); Embers(fx, gateFire + Vector3.up * .3f, 8f, .9f);
            Quad("Gate brazier glow", fx, bigGlow, gateFire + new Vector3(0, .3f, -.1f), Quaternion.identity, new Vector3(3.4f, 3.4f, 1));
            Flames(fx, "Hanging brazier fire", hangFire, .8f, flame); Embers(fx, hangFire + Vector3.up * .3f, 6f, .8f);
            Quad("Hanging brazier glow", fx, bigGlow, hangFire + new Vector3(0, .2f, -.1f), Quaternion.identity, new Vector3(2.7f, 2.7f, 1));
            var lantern = Glow(side < 0 ? "BlueGlow" : "RedGlow", fxGlow, Bright(side < 0 ? new Color(.2f, .5f, 1f) : new Color(1f, .16f, .12f), .8f), .15f, true);
            Quad("Lantern glow", fx, lantern, new Vector3(side * (CornerX - .25f), 3.35f, CornerZ - .2f), Quaternion.identity, new Vector3(2.6f, 2.6f, 1));
        }

        // 천장 구멍의 빛줄기: 축을 중심으로 세 장을 엇갈려 세워 어느 쪽에서 봐도 부피가 있다.
        var shaft = Glow("Shaft", fxShaft, Bright(new Color(.80f, .86f, .96f), .085f), 0, false); shaft.SetFloat("_Drift", .55f); EditorUtility.SetDirty(shaft);
        Vector3 shaftTop = new Vector3(0, 19f, 10.5f), shaftFoot = new Vector3(0, 0, 3.0f), axis = (shaftTop - shaftFoot).normalized;
        for (int k = 0; k < 3; k++)
        {
            Vector3 facing = Quaternion.AngleAxis(k * 60f, axis) * Vector3.Cross(axis, Vector3.right).normalized;
            Quad("Light shaft", fx, shaft, (shaftTop + shaftFoot) * .5f, Quaternion.LookRotation(facing, axis), new Vector3(4.6f, (shaftTop - shaftFoot).magnitude, 1));
        }
        var poolGlow = Glow("PoolGlow", fxGlow, Bright(new Color(1f, .9f, .72f), .035f), 0, false);
        Quad("Shaft pool glow", fx, poolGlow, new Vector3(0, .02f, 2.6f), Quaternion.Euler(90, 0, 0), new Vector3(11f, 9f, 1));

        // 공기: 낮게 깔린 연기, 위쪽 연무, 떠다니는 먼지, 빛줄기 속 먼지
        var smoke = Mat("Smoke", hazeShader); smoke.SetTexture("_MainTex", fxHaze); smoke.SetColor("_Color", new Color(.42f, .33f, .26f, .13f)); smoke.renderQueue = 3000 + 40; EditorUtility.SetDirty(smoke);
        Drift(fx, "Low smoke (wall)", new Vector3(0, 1.1f, 8.2f), new Vector3(22, 1.2f, 4.5f), smoke, 1.6f, 20, 5f, 9f, new Vector3(.16f, .02f, 0), 40);
        Drift(fx, "High smoke", new Vector3(0, 5.4f, 9.5f), new Vector3(24, 1.6f, 5f), smoke, 1.0f, 26, 8f, 13f, new Vector3(-.10f, .03f, 0), 34);
        var mote = Glow("Mote", fxGlow, Bright(new Color(1f, .78f, .5f), .8f), 0, false);
        Drift(fx, "Floating dust", new Vector3(0, 2.1f, 2.2f), new Vector3(17, 3.8f, 8f), mote, 11f, 9, .02f, .05f, new Vector3(.09f, .04f, 0), 120);
        var shaftMote = Glow("ShaftMote", fxGlow, Bright(new Color(.8f, .88f, 1f), 1.1f), 0, false);
        Drift(fx, "Dust in the shaft", new Vector3(0, 3.6f, 4.4f), new Vector3(3.6f, 7f, 3.2f), shaftMote, 9f, 8, .02f, .045f, new Vector3(.03f, -.05f, 0), 90);

        // ── 주변광·안개·카메라·후처리 ──
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.19f, .155f, .125f); RenderSettings.ambientEquatorColor = new Color(.24f, .175f, .125f); RenderSettings.ambientGroundColor = new Color(.11f, .08f, .06f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Haze; RenderSettings.fogStartDistance = 14; RenderSettings.fogEndDistance = 64;
        RenderSettings.reflectionIntensity = .25f;

        var camera = Camera.main;
        if (!camera) throw new Exception("Main camera not found in " + SourceScene);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Bright(Haze, .45f);
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData) { cameraData.renderPostProcessing = true; cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cameraData.antialiasingQuality = AntialiasingQuality.High; EditorUtility.SetDirty(cameraData); }
        var selective = camera.GetComponent<SelectiveSceneryAtmosphere>();
        if (selective) { selective.hazeColor = Haze; EditorUtility.SetDirty(selective); }
        EditorUtility.SetDirty(camera);

        var volume = new GameObject("Pit post-processing", typeof(Volume)).GetComponent<Volume>();
        volume.transform.SetParent(root, false); volume.isGlobal = true; volume.priority = 10; volume.sharedProfile = Profile();

        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        // 빌드 목록에 넣는다 (목록에 없는 씬은 SceneRoutes.LoadBattle 이 기본 전투 씬으로 바꿔 버린다).
        var build = EditorBuildSettings.scenes.ToList();
        if (!build.Any(b => b.path == ScenePath)) { build.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = build.ToArray(); report.AppendLine("build list: added " + ScenePath); }

        report.AppendLine($"sizes: palisade {wallHeight:F2} m high, gate {GateWidth:F1} x {gateHeight:F2} m, galleries {GalleryBase:F1}..{GalleryBase + galleryHeight:F2} m, stalactites {RockTop - rockHeight:F2}..{RockTop:F1} m, cavern {CavernBase:F0}..{CavernBase + cavernHeight:F1} m");
        report.AppendLine($"torches drawn {torchCount}, with a real light {torchLights}; lights in the set {lights.childCount}");
        Debug.Log("[UndergroundPit] built " + ScenePath + "\n" + report);
    }

    // 원본 씬의 풍경을 걷어낸다. 인물·카메라·HUD·경기장 벽과 바닥 판정은 그대로 둔다.
    static void StripScenery(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var rootObject in scene.GetRootGameObjects())
        {
            bool scenery = rootObject.name.StartsWith("Scout Pass") || rootObject.name.StartsWith("Scout 360") || rootObject.name.StartsWith("Story Stage");
            report.AppendLine("root " + rootObject.name + (scenery ? "  [scenery]" : "") + "  comps=" + string.Join(",", rootObject.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)));
            if (!scenery) continue;
            foreach (Transform child in rootObject.transform)
            {
                // 2D 판정을 가진 것은 살려 두고 보이는 것만 끈다. 나머지는 통째로 끈다.
                if (child.GetComponentsInChildren<Collider2D>(true).Length == 0) { child.gameObject.SetActive(false); continue; }
                Hide(child.gameObject); report.AppendLine("   kept for its 2D colliders, hidden: " + child.name);
            }
            Hide(rootObject, false);
        }
        // 풍경 밖에 따로 있는 것 중 보이면 안 되는 것 (바닥 판정용 판, 경기장 벽, 원래 조명)
        foreach (var rootObject in scene.GetRootGameObjects())
        {
            if (rootObject.GetComponentInChildren<Fighter>(true) || rootObject.GetComponentInChildren<Camera>(true) || rootObject.GetComponentInChildren<Canvas>(true)) continue;
            foreach (var l in rootObject.GetComponentsInChildren<Light>(true)) if (l.enabled && l.gameObject.activeInHierarchy) { l.enabled = false; report.AppendLine("   light off: " + l.name + " (" + l.type + ")"); }
            if (rootObject.name == "Ground" || rootObject.name.StartsWith("ArenaWall"))
                foreach (var r in rootObject.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; report.AppendLine("   renderer off: " + r.name); }
            foreach (var v in rootObject.GetComponentsInChildren<Volume>(true)) report.AppendLine("   existing volume: " + v.name + " global=" + v.isGlobal + " priority=" + v.priority + " profile=" + (v.sharedProfile ? v.sharedProfile.name : "-"));
        }
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && !r.GetComponentInParent<Fighter>() && !r.GetComponentInParent<Canvas>())
                report.AppendLine("   still visible: " + PathOf(r.transform) + " (" + r.GetType().Name + ")");
        foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            report.AppendLine("   2D collider: " + PathOf(c.transform) + " centre " + c.bounds.center.ToString("F1") + " size " + c.bounds.size.ToString("F1"));
        var cam = Camera.main; var rig = cam ? cam.GetComponent<Battle3DCamera>() : null;
        if (cam) report.AppendLine("camera fov " + cam.fieldOfView + (rig ? " pitch " + rig.combatPitch + " distance " + rig.minimumDistance + "~" + rig.maximumDistance + " focus height " + rig.focusHeight + " boundary " + rig.cameraBoundaryRadius + " mouse orbit " + rig.allowMouseOrbit : ""));
    }

    static void Hide(GameObject g, bool children = true)
    {
        foreach (var r in children ? g.GetComponentsInChildren<Renderer>(true) : g.GetComponents<Renderer>()) r.enabled = false;
        foreach (var l in children ? g.GetComponentsInChildren<Light>(true) : g.GetComponents<Light>()) l.enabled = false;
        if (!children) return;
        foreach (var p in g.GetComponentsInChildren<ParticleSystem>(true)) p.gameObject.SetActive(false);
        foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    static string PathOf(Transform t) { string p = t.name; while (t.parent) { t = t.parent; p = t.name + "/" + p; } return p; }

    // ── 정지 화면: 플레이 없이 여러 구도에서 찍는다. 씬에 있는 두 인물을 옮겨 가며 게임과 같은 식으로 카메라를 잡는다. 저장하지 않는다. ──
    public static void Shots(string outDir)
    {
        EditorSceneManager.OpenScene(ScenePath);
        var camera = Camera.main;
        if (!camera) throw new Exception("Main camera not found in " + ScenePath);
        var rig = camera.GetComponent<Battle3DCamera>();
        var player = Object.FindAnyObjectByType<PlayerController>().transform; var enemy = Object.FindAnyObjectByType<EnemyAI>().transform;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(8f, true, true);
        var props = Object.FindObjectsByType<UndergroundPitProp>(FindObjectsSortMode.None);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        float focusHeight = rig ? rig.focusHeight : 1.06f, combatPitch = rig ? rig.combatPitch : 3.5f, minimum = rig ? rig.minimumDistance : 3.35f;
        float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);

        // yaw 가 NaN, pitch·distance 가 음수면 게임이 정하는 값을 쓴다 (가운데에서 벗어난 만큼 돌고, 두 인물이 다 들어오게 물러난다).
        void Shot(string name, float playerX, float enemyX, float yaw = float.NaN, float pitch = -1, float distance = -1, float lookUp = 0)
        {
            player.position = new Vector3(playerX, player.position.y, 0); enemy.position = new Vector3(enemyX, enemy.position.y, 0);
            float centre = (playerX + enemyX) * .5f, apart = Mathf.Abs(enemyX - playerX);
            if (float.IsNaN(yaw)) yaw = Mathf.Clamp(Mathf.Sign(centre) * Mathf.Max(0, Mathf.Abs(centre) - .2f) * 3.2f, -14, 14);
            if (pitch < 0) pitch = combatPitch;
            if (distance < 0) distance = Mathf.Max(minimum, (2.15f - focusHeight) / (tangent * .8f) + .3f, (apart * .5f + .65f) / (tangent * 16f / 9f * .9f) + .3f);
            Vector3 eye = new Vector3(centre, focusHeight, 0);
            Vector3 horizontal = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.back;
            Vector3 back = horizontal * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
            camera.transform.position = eye + back * distance; camera.transform.rotation = Quaternion.LookRotation(eye + Vector3.up * lookUp - camera.transform.position);
            foreach (var prop in props) prop.Face(camera.transform.position, 1.3f);
            camera.targetTexture = rt; camera.Render();
            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); RenderTexture.active = null; camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, name + ".jpg"), image.EncodeToJPG(92));
        }
        Shot("1_fight_close", -.9f, .9f);
        Shot("2_round_start", -2.1f, 2.1f);
        Shot("3_blue_corner", -5.6f, -3.9f);
        Shot("4_red_corner", 3.9f, 5.6f);
        Shot("5_far_apart", -5f, 5f);
        Shot("6_wide", -2.1f, 2.1f, 0, 9f, 11f);
        Shot("7_orbit_60", -2.1f, 2.1f, 60, 9f, 8f);
        Shot("8_orbit_180", -2.1f, 2.1f, 180, 9f, 8f);
        Shot("9_looking_up", -2.1f, 2.1f, 0, 2f, 13f, 3.5f);
        Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        Debug.Log("[UndergroundPit] stills saved to " + outDir);
    }

    // ── 재료 ──
    enum Kind { Cutout, Strip, Backdrop, Floor, Normal, Paint, Sprite }

    static Texture2D Import(string name, Kind kind)
    {
        string path = Root + "/Textures/" + name + ".png", source = Source + "/" + name + ".png";
        // 가공 전 원본과 가공본(ArtSource)은 저장소에 올리지 않는다. 없으면 프로젝트 안의 사본을 그대로 쓴다.
        if (File.Exists(source)) File.Copy(source, path, true);
        else if (!File.Exists(path)) throw new FileNotFoundException(source);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool alpha = kind == Kind.Cutout || kind == Kind.Strip || kind == Kind.Sprite;
        bool ground = kind == Kind.Floor || kind == Kind.Normal;
        importer.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = kind != Kind.Normal && kind != Kind.Paint;
        importer.npotScale = TextureImporterNPOTScale.None; importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = ground || kind == Kind.Paint ? 16 : 8;
        importer.wrapModeU = kind == Kind.Strip || kind == Kind.Backdrop || ground ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.wrapModeV = ground ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; importer.alphaIsTransparency = alpha && kind != Kind.Sprite;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture) throw new Exception("Texture import failed: " + path);
        return texture;
    }

    static Material Mat(string name, Shader shader)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        return m;
    }

    // 그림 구조물 재질. order 가 클수록 나중에(앞에) 그려진다.
    static Material Piece(string name, Texture2D picture, int order, float fog, float baseFog, float baseFogHeight, float glow, Color tint)
    {
        var m = Mat(name, pieceShader);
        m.SetTexture("_MainTex", picture); m.SetColor("_Tint", tint); m.SetColor("_FogColor", Haze);
        m.SetFloat("_Fog", fog); m.SetFloat("_BaseFog", baseFog); m.SetFloat("_BaseFogHeight", baseFogHeight);
        m.SetFloat("_Glow", glow); m.SetFloat("_GlowThreshold", .6f); m.SetFloat("_Flicker", .3f);
        m.renderQueue = 3000 + order;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Glow(string name, Texture2D shape, Color colour, float flicker, bool billboard)
    {
        var m = Mat(name, glowShader);
        m.SetTexture("_MainTex", shape); m.SetColor("_Color", colour); m.SetFloat("_Flicker", flicker); m.SetFloat("_FlickerSpeed", 1f);
        m.SetFloat("_Billboard", billboard ? 1 : 0); m.SetFloat("_Drift", 0);
        m.renderQueue = 3000 + 60;
        EditorUtility.SetDirty(m);
        return m;
    }

    static VolumeProfile Profile()
    {
        string path = Root + "/Profiles/UndergroundPit.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
        foreach (var old in profile.components.ToArray()) { profile.components.Remove(old); Object.DestroyImmediate(old, true); }
        Add<Tonemapping>(profile).mode.value = TonemappingMode.Neutral;
        var bloom = Add<Bloom>(profile); bloom.threshold.value = .85f; bloom.intensity.value = .95f; bloom.scatter.value = .78f; bloom.highQualityFiltering.value = true;
        var grade = Add<ColorAdjustments>(profile); grade.contrast.value = 14; grade.saturation.value = 6; grade.postExposure.value = .2f;
        var split = Add<SplitToning>(profile); split.shadows.value = new Color(.46f, .50f, .58f); split.highlights.value = new Color(.60f, .53f, .46f);
        Add<WhiteBalance>(profile).temperature.value = 6;
        var vignette = Add<Vignette>(profile); vignette.intensity.value = .32f; vignette.smoothness.value = .5f;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>(true);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    // ── 모양 ──
    static float Circumference(float radius) => 2 * Mathf.PI * radius;
    static Color Bright(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
    static bool Near(float a, float b) => Mathf.Abs(a - b) < 1.5f;

    // 0 도 = 뒤 가운데(+z), + 는 카메라에서 볼 때 오른쪽
    static Vector3 OnRing(float radius, float degrees, float y)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Root + "/Meshes/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); existing.name = name; EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); return existing; }
        mesh.name = name; AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // 안쪽에서만 보이는 둥근 벽. 그림을 repeats 번 이어 두르고, 뒤 가운데에 그림의 uAtBack 자리가 오게 한다.
    // 아래 반지름과 위 반지름이 다르면 비스듬한 면(천장)이 된다.
    static void Ring(string name, Transform parent, Material material, float radius, float y0, float topRadius, float y1, int repeats, float uAtBack)
    {
        const int segments = 96;
        var vertices = new Vector3[(segments + 1) * 2]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float f = (float)i / segments, a = (f - .5f) * 2 * Mathf.PI;
            float sin = Mathf.Sin(a), cos = Mathf.Cos(a), u = uAtBack + (f - .5f) * repeats;
            vertices[i * 2] = new Vector3(sin * radius, y0, cos * radius); vertices[i * 2 + 1] = new Vector3(sin * topRadius, y1, cos * topRadius);
            normals[i * 2] = normals[i * 2 + 1] = new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a));
            uv[i * 2] = new Vector2(u, 0); uv[i * 2 + 1] = new Vector2(u, 1);
        }
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2, t = b + 1, b2 = b + 2, t2 = b + 3, k = i * 6;
            triangles[k] = b; triangles[k + 1] = t; triangles[k + 2] = b2; triangles[k + 3] = t; triangles[k + 4] = t2; triangles[k + 5] = b2;
        }
        var mesh = new Mesh { vertices = vertices, normals = normals, uv = uv, triangles = triangles };
        mesh.RecalculateBounds();
        Shape(name, parent, SaveMesh(mesh, "Ring_" + name.Replace(" ", "")), material, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // 바닥에 누운 고리. 안쪽은 투명, 바깥쪽은 불투명 (그늘용)
    static void Annulus(string name, Transform parent, Material material, float inner, float outer, float y)
    {
        const int segments = 96;
        var vertices = new Vector3[(segments + 1) * 2]; var colours = new Color[vertices.Length]; var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = (float)i / segments * 2 * Mathf.PI, s = Mathf.Sin(a), c = Mathf.Cos(a);
            vertices[i * 2] = new Vector3(s * inner, y, c * inner); vertices[i * 2 + 1] = new Vector3(s * outer, y, c * outer);
            colours[i * 2] = new Color(1, 1, 1, 0); colours[i * 2 + 1] = Color.white;
            uv[i * 2] = uv[i * 2 + 1] = new Vector2(.5f, .5f);
        }
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2, t = b + 1, b2 = b + 2, t2 = b + 3, k = i * 6;
            triangles[k] = b; triangles[k + 1] = t; triangles[k + 2] = b2; triangles[k + 3] = t; triangles[k + 4] = t2; triangles[k + 5] = b2;
        }
        var mesh = new Mesh { vertices = vertices, colors = colours, uv = uv, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        Shape(name, parent, SaveMesh(mesh, "Annulus_" + name.Replace(" ", "")), material, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // 세워 놓는 그림판: 폭 1, 높이 y0~y1, 앞면이 -z. (y0=0: 밑동이 기준 / y1=0: 꼭대기가 기준)
    static Mesh Card(string name, float y0, float y1)
    {
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-.5f, y0, 0), new Vector3(-.5f, y1, 0), new Vector3(.5f, y1, 0), new Vector3(.5f, y0, 0) },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        mesh.RecalculateBounds();
        return SaveMesh(mesh, name);
    }

    static MeshRenderer Shape(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        g.transform.SetParent(parent, false); g.transform.SetPositionAndRotation(position, rotation); g.transform.localScale = scale;
        g.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    static Transform Plate(string name, Transform parent, Mesh card, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        => Shape(name, parent, card, material, position, rotation, scale).transform;

    // Unity 기본 사각형 (가운데가 기준, 앞면이 -z)
    static MeshRenderer Quad(string name, Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name; g.transform.SetParent(parent, false); g.transform.SetPositionAndRotation(position, rotation); g.transform.localScale = scale;
        foreach (var c in g.GetComponents<Collider>()) Object.DestroyImmediate(c);
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    // ── 빛과 입자 ──
    static Light NewLight(Transform parent, string name, LightType type, Color colour, float intensity)
    {
        var l = new GameObject(name, typeof(Light)).GetComponent<Light>();
        l.transform.SetParent(parent, false); l.type = type; l.color = colour; l.intensity = intensity; l.shadows = LightShadows.None;
        return l;
    }

    // 일렁이는 불빛 (StoryStageMotion 의 flicker 사용)
    static void Torch(Transform parent, string name, Vector3 position, Color colour, float intensity, float range, int seed)
    {
        var l = NewLight(parent, name, LightType.Point, colour, intensity);
        l.transform.position = position; l.range = range;
        var motion = l.gameObject.AddComponent<StoryStageMotion>();
        motion.flicker = true; motion.speed = .8f + Mathf.Abs(seed % 5) * .09f; motion.phase = seed * 1.37f;
    }

    static ParticleSystem Particles(Transform parent, string name, Vector3 position, Material material, int max)
    {
        var ps = new GameObject(name, typeof(ParticleSystem)).GetComponent<ParticleSystem>();
        ps.transform.SetParent(parent, false); ps.transform.position = position;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.prewarm = true; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = Color.white;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }

    static Gradient Ramp(Color[] colours, float[] colourTimes, float[] alphas, float[] alphaTimes)
    {
        var g = new Gradient();
        g.SetKeys(colours.Select((c, i) => new GradientColorKey(c, colourTimes[i])).ToArray(), alphas.Select((a, i) => new GradientAlphaKey(a, alphaTimes[i])).ToArray());
        return g;
    }

    static void Flames(Transform parent, string name, Vector3 position, float size, Material material)
    {
        var ps = Particles(parent, name, position, material, 40);
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .85f); main.startSpeed = new ParticleSystem.MinMaxCurve(.9f, 1.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(.55f * size, .95f * size); main.startRotation = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 7; shape.radius = .2f * size; shape.rotation = new Vector3(-90, 0, 0);
        var emission = ps.emission; emission.rateOverTime = 18;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        colour.color = Ramp(new[] { new Color(1f, .95f, .62f), new Color(1f, .62f, .2f), new Color(.85f, .22f, .06f) }, new[] { 0f, .45f, 1f }, new[] { 0f, 1f, .7f, 0f }, new[] { 0f, .12f, .6f, 1f });
        var shrink = ps.sizeOverLifetime; shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .35f));
    }

    static void Embers(Transform parent, Vector3 position, float rate, float spread)
    {
        var ps = Particles(parent, "Embers", position, Mat("Ember", glowShader), 40);
        var m = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/fx_glow.png")); m.SetColor("_Color", Bright(new Color(1f, .5f, .16f), 2.2f));
        m.SetFloat("_Flicker", 0); m.SetFloat("_Billboard", 0); m.SetFloat("_Drift", 0); m.renderQueue = 3000 + 62; EditorUtility.SetDirty(m);
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 18; shape.radius = .22f * spread; shape.rotation = new Vector3(-90, 0, 0);
        var emission = ps.emission; emission.rateOverTime = rate;
        var noise = ps.noise; noise.enabled = true; noise.strength = .45f; noise.frequency = .6f; noise.scrollSpeed = .4f;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        colour.color = Ramp(new[] { new Color(1f, .9f, .6f), new Color(1f, .45f, .12f) }, new[] { 0f, 1f }, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, .1f, .65f, 1f });
    }

    // 상자 안에서 천천히 떠다니는 입자 (연기, 먼지)
    static void Drift(Transform parent, string name, Vector3 centre, Vector3 box, Material material, float rate, float life, float sizeMin, float sizeMax, Vector3 velocity, int max)
    {
        var ps = Particles(parent, name, centre, material, max);
        var main = ps.main; main.startLifetime = life; main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var emission = ps.emission; emission.rateOverTime = rate;
        var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = ParticleSystemSimulationSpace.World;
        drift.x = new ParticleSystem.MinMaxCurve(velocity.x * .6f, velocity.x * 1.4f); drift.y = new ParticleSystem.MinMaxCurve(velocity.y * .6f, velocity.y * 1.4f); drift.z = new ParticleSystem.MinMaxCurve(-.03f, .03f);
        var colour = ps.colorOverLifetime; colour.enabled = true;
        colour.color = Ramp(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, .22f, .75f, 1f });
    }
}
