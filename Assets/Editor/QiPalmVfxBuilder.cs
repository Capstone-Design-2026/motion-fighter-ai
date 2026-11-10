using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 파운장(破雲掌) 이펙트를 코드로 만든다: 재질, 기 모으기/기파/적중/발사 섬광/충전 기운 프리팹, Resources 의 QiPalmAssets.
/// 색: 청백색 기운(HDR, 블룸으로 번짐) + 먹빛 구름(흑운). 다시 실행하면 같은 경로의 에셋을 덮어쓴다.
/// Run: Unity -batchmode -executeMethod QiPalmVfxBuilder.Build
/// </summary>
public static class QiPalmVfxBuilder
{
    const string Root = "Assets/Effects/QiPalm", Tex = Root + "/Textures", Mats = Root + "/Materials", Prefabs = Root + "/Prefabs";
    const string AssetsPath = "Assets/Resources/QiPalm/QiPalmAssets.asset";
    static readonly Color Cyan = new Color(.45f, .85f, 1f), Azure = new Color(.18f, .5f, 1f), Ink = new Color(.03f, .035f, .05f);

    static Material glow, core, ring, spark, streak, mist, ink, orb, shell;

    [MenuItem("Tools/VisionStriker/Build QiPalm VFX")]
    public static void BuildMenu() => BuildAll(false);
    public static void Build() => BuildAll(true);

    static void BuildAll(bool exit)
    {
        try
        {
            foreach (var d in new[] { Mats, Prefabs, "Assets/Resources/QiPalm" }) Directory.CreateDirectory(d);
            ImportTextures();
            BuildMaterials();
            var assets = ScriptableObject.CreateInstance<QiPalmAssets>();
            assets.charge = Save(BuildCharge(), "QiCharge");
            assets.wave = Save(BuildWave(), "QiWave");
            assets.impact = Save(BuildImpact(), "QiImpact");
            assets.muzzle = Save(BuildMuzzle(), "QiMuzzle");
            assets.ready = Save(BuildReady(), "QiReady");
            assets.brush = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/qi_brush.png");
            assets.glow = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/qi_glow.png");
            assets.cutInArt = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/qi_cutin_art.png");
            string a = "Assets/Resources/QiPalm/Audio/";
            assets.chargeStart = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_charge_start.wav");
            assets.chargeLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_charge_loop.wav");
            assets.release = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_release.wav");
            assets.impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_impact.wav");
            assets.readyClip = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_ready.wav");
            assets.fizzle = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "qi_fizzle.wav");
            SaveAsset(assets, AssetsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[QiPalm] VFX built");
            if (exit)
            {
                var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-qiReport");
                string report = i >= 0 && i + 1 < args.Length ? args[i + 1] : Application.temporaryCachePath;
                Directory.CreateDirectory(report); File.WriteAllText(Path.Combine(report, "vfx_done.txt"), "PASS"); EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); if (exit) EditorApplication.Exit(1); }
    }

    /// <summary>컷인 UI 를 정해진 시각에 렌더링해 확인한다(-qiReport 폴더에 cutin_*.png).</summary>
    public static void PreviewCutIn()
    {
        var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-qiReport");
        string report = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : Application.temporaryCachePath;
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .52f, .45f);
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; var img = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var cut = QiCutIn.Play(AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/qi_brush.png"), -1,
                                   AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/qi_cutin_art.png"));
            var canvas = cut.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
            foreach (float t in new[] { .06f, .14f, .3f, .6f, .85f })
            {
                cut.Seek(t); Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
                RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); img.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(report, $"cutin_{t:0.00}.png"), img.EncodeToPNG());
            }
            var stroke = cut.transform.Find("Stroke") as RectTransform;
            File.WriteAllText(Path.Combine(report, "cutin_done.txt"), $"PASS stroke={stroke?.anchoredPosition} size={stroke?.rect.size} scale={stroke?.localScale} canvasScale={canvas.scaleFactor}");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(report, "cutin_done.txt"), "FAIL " + e); EditorApplication.Exit(1); }
    }

    // ───────────── 에셋 ─────────────
    static void ImportTextures()
    {
        AssetDatabase.Refresh();
        foreach (var path in Directory.GetFiles(Tex, "*.png"))
        {
            var p = path.Replace('\\', '/');
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
            ti.wrapMode = p.Contains("noise") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.mipmapEnabled = true; ti.sRGBTexture = !p.Contains("noise"); ti.textureCompression = TextureImporterCompression.CompressedHQ;
            // 컷인 일러스트는 UI에 원본 비율 그대로 쓴다(2의 거듭제곱으로 늘리면 납작해진다).
            if (p.Contains("cutin_art")) { ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = false; ti.maxTextureSize = 2048; }
            ti.SaveAndReimport();
        }
    }
    static Texture2D T(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + name + ".png");
    static Material Mat(string name, string shader, string tex, Color color, float intensity = 1f)
    {
        var m = new Material(Shader.Find(shader)) { name = name };
        if (tex != null) m.SetTexture(shader.EndsWith("QiOrb") ? "_NoiseTex" : "_MainTex", T(tex));
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Intensity")) m.SetFloat("_Intensity", intensity);
        return SaveAsset(m, Mats + "/" + name + ".mat");
    }
    static void BuildMaterials()
    {
        glow = Mat("M_QiGlow", "VisionStriker/QiAdditive", "qi_glow", new Color(.5f, .9f, 1.6f, 1), 1.4f);
        core = Mat("M_QiCore", "VisionStriker/QiAdditive", "qi_glow", new Color(2.2f, 2.5f, 2.8f, 1), 1.2f);
        ring = Mat("M_QiRing", "VisionStriker/QiAdditive", "qi_ring", new Color(.4f, 1f, 2.4f, 1), 1.3f);
        spark = Mat("M_QiSpark", "VisionStriker/QiAdditive", "qi_spark", new Color(1.4f, 2f, 2.8f, 1), 1.6f);
        streak = Mat("M_QiStreak", "VisionStriker/QiAdditive", "qi_streak", new Color(.6f, 1.4f, 2.6f, 1), 1.3f);
        mist = Mat("M_QiMist", "VisionStriker/QiAdditive", "qi_smoke", new Color(.3f, .6f, 1.1f, .55f), 1f);
        ink = Mat("M_InkSmoke", "VisionStriker/QiSmoke", "qi_smoke", new Color(Ink.r, Ink.g, Ink.b, .85f));
        orb = Mat("M_QiOrb", "VisionStriker/QiOrb", "qi_noise", Color.white);
        orb.SetColor("_CoreColor", new Color(1.1f, 1.75f, 2.6f)); orb.SetColor("_RimColor", new Color(.25f, 1f, 3.2f)); orb.SetFloat("_Wobble", .05f);
        shell = Mat("M_QiWaveShell", "VisionStriker/QiOrb", "qi_noise", Color.white);
        shell.SetColor("_CoreColor", new Color(.5f, 1.1f, 2.2f)); shell.SetColor("_RimColor", new Color(.25f, .9f, 3.2f)); shell.SetFloat("_RimPower", 1.6f);
        shell.SetFloat("_Wobble", .09f); shell.SetFloat("_NoiseScale", 2.2f); shell.SetVector("_Speed", new Vector4(.2f, 2.4f, -.8f, 1.6f)); shell.SetFloat("_Alpha", .8f);
        foreach (var m in new[] { orb, shell }) EditorUtility.SetDirty(m);
    }
    static T SaveAsset<T>(T asset, string path) where T : Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old) { EditorUtility.CopySerialized(asset, old); Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(asset, path); return asset;
    }
    static GameObject Save(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ───────────── 부품 ─────────────
    static GameObject Child(GameObject parent, string name, Vector3 pos = default)
    {
        var go = new GameObject(name); go.transform.SetParent(parent.transform, false); go.transform.localPosition = pos; return go;
    }
    static GameObject Sphere(GameObject parent, string name, Material m, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false); go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return go;
    }
    static GameObject Quad(GameObject parent, string name, Material m, float size, Vector3 euler)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false); go.transform.localScale = Vector3.one * size; go.transform.localEulerAngles = euler;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return go;
    }
    static Light PointLight(GameObject parent, Color c, float intensity, float range)
    {
        var l = Child(parent, "Light").AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }
    static TrailRenderer Trail(GameObject go, float time, float width, Gradient g)
    {
        var t = go.AddComponent<TrailRenderer>();
        t.time = time; t.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)); t.widthMultiplier = width;
        t.colorGradient = g; t.sharedMaterial = streak; t.minVertexDistance = .015f; t.numCapVertices = 3; t.alignment = LineAlignment.View;
        t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false;
        return t;
    }
    static Gradient Grad(params (float t, Color c, float a)[] keys)
    {
        var g = new Gradient();
        g.SetKeys(keys.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), keys.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
        return g;
    }
    static AnimationCurve Curve(params float[] tv) { var c = new AnimationCurve(); for (int i = 0; i < tv.Length; i += 2) c.AddKey(tv[i], tv[i + 1]); return c; }
    static ParticleSystem.MinMaxCurve R(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

    /// <summary>파티클 하나. 기본값을 지우고 이펙트에 쓰는 모듈만 켠다.</summary>
    static ParticleSystem PS(GameObject parent, string name, Material m, bool world, bool loop, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
        float speedMin = 0, float speedMax = 0, int max = 200, bool stretch = false, Vector3 pos = default)
    {
        var go = Child(parent, name, pos);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 1f; main.loop = loop; main.playOnAwake = true; main.maxParticles = max;
        main.startLifetime = R(lifeMin, lifeMax); main.startSize = R(sizeMin, sizeMax); main.startSpeed = R(speedMin, speedMax);
        main.startColor = Color.white; main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.startRotation = R(0, Mathf.PI * 2);
        var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = 0;
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .05f; sh.radiusThickness = 1f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch) { r.velocityScale = .05f; r.lengthScale = 1.4f; }
        return ps;
    }
    static void Fade(ParticleSystem ps, Gradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = new ParticleSystem.MinMaxGradient(g); }
    static void Grow(ParticleSystem ps, float from, float to) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, Curve(0, from, 1, to)); }
    static void Burst(ParticleSystem ps, int count, float time = 0f) { var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) }); }
    static void Radial(ParticleSystem ps, float speed, bool world = false)
    {
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        v.x = 0; v.y = 0; v.z = 0; v.radial = speed;
    }
    static void Rise(ParticleSystem ps, float up)
    {
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = 0; v.y = up; v.z = 0;
    }
    static void Smoke(ParticleSystem ps, float spinDeg)
    {
        var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = R(0f, 3.99f); tsa.cycleCount = 1;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = R(-spinDeg * Mathf.Deg2Rad, spinDeg * Mathf.Deg2Rad);
    }
    static Gradient InkFade(float peak, bool fadeIn) => fadeIn
        ? Grad((0, Color.white, 0), (.3f, Color.white, peak), (1, Color.white, 0))
        : Grad((0, Color.white, peak), (.5f, Color.white, peak * .6f), (1, Color.white, 0));

    // ───────────── 기 모으기 ─────────────
    static GameObject BuildCharge()
    {
        var root = new GameObject("QiCharge");
        var fx = root.AddComponent<QiChargeFx>();
        var orbGo = Sphere(root, "Orb", orb, Vector3.one * .12f);

        var coreGlow = PS(root, "Core", glow, false, true, .25f, .35f, .3f, .42f, max: 20);
        var em = coreGlow.emission; em.rateOverTime = 16; Fade(coreGlow, Grad((0, Color.white, 0), (.3f, Color.white, .55f), (1, Cyan, 0)));

        var inflow = PS(root, "Inflow", spark, false, true, .28f, .36f, .02f, .045f, max: 120, stretch: true);
        em = inflow.emission; em.rateOverTime = 85; var sh = inflow.shape; sh.radius = .55f; sh.radiusThickness = 0f;
        Radial(inflow, -1.7f); Fade(inflow, Grad((0, Color.white, 0), (.25f, Color.white, 1), (1, Cyan, 0)));
        inflow.GetComponent<ParticleSystemRenderer>().velocityScale = .07f;

        var inkIn = PS(root, "InkInflow", ink, false, true, .38f, .46f, .3f, .55f, max: 40);
        em = inkIn.emission; em.rateOverTime = 24; sh = inkIn.shape; sh.radius = 1.1f; sh.radiusThickness = 0f;
        Radial(inkIn, -2.6f); Grow(inkIn, 1f, .25f); Smoke(inkIn, 90f); Fade(inkIn, InkFade(.65f, true));

        var swirl = Child(root, "Swirl"); var orbit = swirl.AddComponent<QiOrbit>(); orbit.axis = Vector3.up; orbit.degreesPerSecond = 640f;
        var ribbonGrad = Grad((0, Color.white, .9f), (.5f, Cyan, .6f), (1, Azure, 0));
        Trail(Child(swirl, "RibbonA", new Vector3(.14f, .02f, 0)), .2f, .032f, ribbonGrad);
        Trail(Child(swirl, "RibbonB", new Vector3(-.14f, -.02f, 0)), .2f, .032f, ribbonGrad);

        var light = PointLight(root, Cyan, .8f, 1.2f);

        var ground = Child(root, "Ground");
        var ringA = Quad(ground, "Ring", ring, 1.5f, new Vector3(90, 0, 0)); var spinA = ringA.AddComponent<QiOrbit>(); spinA.axis = Vector3.forward; spinA.degreesPerSecond = 50f;
        var ringB = Quad(ground, "RingInner", ring, .95f, new Vector3(90, 0, 0)); var spinB = ringB.AddComponent<QiOrbit>(); spinB.axis = Vector3.forward; spinB.degreesPerSecond = -90f;
        var wisps = PS(ground, "Wisps", mist, true, true, .9f, 1.3f, .12f, .24f, max: 60);
        em = wisps.emission; em.rateOverTime = 26; sh = wisps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .5f; sh.radiusThickness = .2f; sh.rotation = new Vector3(90, 0, 0);
        Rise(wisps, .8f); Grow(wisps, .6f, 1.3f); Fade(wisps, Grad((0, Cyan, 0), (.3f, Cyan, .5f), (1, Azure, 0)));

        fx.orb = orbGo.transform; fx.glow = light; fx.ground = ground.transform;
        fx.emitters = new[] { coreGlow, inflow, inkIn, wisps };
        return root;
    }

    // ───────────── 기파 ─────────────
    static GameObject BuildWave()
    {
        var root = new GameObject("QiWave");
        var wave = root.AddComponent<QiWave>();
        var body = Child(root, "Body");
        Sphere(body, "Shell", shell, new Vector3(.46f, .46f, .7f));
        Sphere(body, "Core", orb, Vector3.one * .2f);
        var halo = PS(body, "Halo", glow, false, true, .1f, .14f, .9f, 1.15f, max: 20);
        var em = halo.emission; em.rateOverTime = 40; Fade(halo, Grad((0, Cyan, .32f), (1, Azure, 0)));
        var spiral = Child(body, "Spiral"); var orbit = spiral.AddComponent<QiOrbit>(); orbit.axis = Vector3.forward; orbit.degreesPerSecond = 900f;
        var ribbonGrad = Grad((0, Color.white, 1f), (.4f, Cyan, .7f), (1, Azure, 0));
        var ra = Trail(Child(spiral, "RibbonA", new Vector3(.22f, 0, 0)), .3f, .07f, ribbonGrad);
        var rb = Trail(Child(spiral, "RibbonB", new Vector3(-.22f, 0, 0)), .3f, .07f, ribbonGrad);

        var sparks = PS(root, "Sparks", spark, true, true, .25f, .45f, .02f, .045f, .4f, 1.8f, 200, true);
        em = sparks.emission; em.rateOverDistance = 24; var sh = sparks.shape; sh.radius = .18f;
        Fade(sparks, Grad((0, Color.white, 1), (.5f, Cyan, .8f), (1, Azure, 0)));
        var inkPart = PS(root, "InkPart", ink, true, true, .7f, 1.1f, .35f, .7f, 1.2f, 2.6f, 120);
        em = inkPart.emission; em.rateOverDistance = 7; sh = inkPart.shape; sh.radius = .32f; sh.radiusThickness = 0f;
        Grow(inkPart, .6f, 1.5f); Smoke(inkPart, 60f); Fade(inkPart, InkFade(.55f, false));
        var mistTrail = PS(root, "Mist", mist, true, true, .4f, .7f, .3f, .55f, .1f, .4f, 120);
        em = mistTrail.emission; em.rateOverDistance = 10; Grow(mistTrail, .7f, 1.6f); Smoke(mistTrail, 40f);
        Fade(mistTrail, Grad((0, Cyan, .45f), (1, Azure, 0)));

        var main = Trail(root, .22f, .5f, Grad((0, Color.white, .9f), (.35f, Cyan, .6f), (1, Azure, 0)));
        var light = PointLight(root, Cyan, 4f, 3.5f);
        wave.body = body.transform; wave.glow = light;
        wave.detachOnEnd = new[] { sparks, inkPart, mistTrail };
        wave.trails = new[] { main, ra, rb };
        return root;
    }

    // ───────────── 적중 ─────────────
    static GameObject BuildImpact()
    {
        var root = new GameObject("QiImpact");
        var life = root.AddComponent<QiFxLife>(); life.lifetime = 2.2f; life.flashIntensity = 9f; life.flashTime = .35f;
        var flash = PS(root, "Flash", core, false, false, .18f, .18f, 2.6f, 2.6f, max: 1); Burst(flash, 1); Grow(flash, .35f, 1.1f);
        Fade(flash, Grad((0, Color.white, 1), (1, Cyan, 0)));
        var ringA = PS(root, "Shockwave", ring, false, false, .38f, .38f, 1f, 1f, max: 1); Burst(ringA, 1); Grow(ringA, .1f, 3.4f);
        Fade(ringA, Grad((0, Color.white, 1), (.4f, Cyan, .8f), (1, Azure, 0)));
        var ringB = PS(root, "Shockwave2", ring, false, false, .5f, .5f, 1f, 1f, max: 1); Burst(ringB, 1, .05f); Grow(ringB, .2f, 4.6f);
        Fade(ringB, Grad((0, Cyan, .5f), (1, Azure, 0)));
        var sparks = PS(root, "Sparks", spark, false, false, .22f, .55f, .025f, .06f, 4f, 11f, 60, true); Burst(sparks, 48);
        var main = sparks.main; main.gravityModifier = .7f; sparks.GetComponent<ParticleSystemRenderer>().velocityScale = .045f;
        Fade(sparks, Grad((0, Color.white, 1), (.5f, Cyan, .9f), (1, Azure, 0)));
        var mistBurst = PS(root, "Mist", mist, false, false, .6f, 1f, .6f, 1.2f, .6f, 1.6f, 20); Burst(mistBurst, 10); Grow(mistBurst, .5f, 1.5f); Smoke(mistBurst, 40f);
        Fade(mistBurst, Grad((0, Cyan, .5f), (1, Azure, 0)));
        var inkBurst = PS(root, "Ink", ink, false, false, .9f, 1.5f, .5f, 1f, 1.5f, 3.4f, 20); Burst(inkBurst, 14); Grow(inkBurst, .6f, 1.8f); Smoke(inkBurst, 60f);
        var lim = inkBurst.limitVelocityOverLifetime; lim.enabled = true; lim.limit = .4f; lim.dampen = .25f;
        Fade(inkBurst, InkFade(.75f, false));
        life.flash = PointLight(root, Cyan, 12f, 5f);
        return root;
    }

    // ───────────── 발사 섬광 (로컬 +Z 가 발사 방향) ─────────────
    static GameObject BuildMuzzle()
    {
        var root = new GameObject("QiMuzzle");
        var life = root.AddComponent<QiFxLife>(); life.lifetime = 1.2f; life.flashIntensity = 7f; life.flashTime = .2f;
        var flash = PS(root, "Flash", core, false, false, .12f, .12f, 1.25f, 1.25f, max: 1); Burst(flash, 1); Grow(flash, .5f, 1.2f);
        Fade(flash, Grad((0, Color.white, 1), (1, Cyan, 0)));
        var streaks = PS(root, "Streaks", spark, false, false, .12f, .25f, .03f, .06f, 6f, 11f, 20, true); Burst(streaks, 14);
        var sh = streaks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = .05f;
        Fade(streaks, Grad((0, Color.white, 1), (1, Cyan, 0)));
        var ringFx = PS(root, "Ring", ring, false, false, .25f, .25f, 1f, 1f, max: 1); Burst(ringFx, 1); Grow(ringFx, .2f, 1.6f);
        Fade(ringFx, Grad((0, Color.white, 1), (1, Azure, 0)));
        life.flash = PointLight(root, Cyan, 7f, 3f);
        return root;
    }

    // ───────────── 게이지 충전 기운 (손에 붙는다) ─────────────
    static GameObject BuildReady()
    {
        var root = new GameObject("QiReady");
        var wisp = PS(root, "Wisp", mist, true, true, .5f, .8f, .05f, .1f, .05f, .2f, 40);
        var em = wisp.emission; em.rateOverTime = 10; var sh = wisp.shape; sh.radius = .07f;
        Rise(wisp, .3f); Grow(wisp, .8f, 1.4f); Fade(wisp, Grad((0, Cyan, .7f), (1, Azure, 0)));
        var main = wisp.main; main.scalingMode = ParticleSystemScalingMode.Local;
        var glint = PS(root, "Glint", spark, true, true, .2f, .35f, .012f, .022f, .1f, .4f, 20, true);
        em = glint.emission; em.rateOverTime = 5; main = glint.main; main.scalingMode = ParticleSystemScalingMode.Local;
        Fade(glint, Grad((0, Color.white, 1), (1, Cyan, 0)));
        return root;
    }
}
