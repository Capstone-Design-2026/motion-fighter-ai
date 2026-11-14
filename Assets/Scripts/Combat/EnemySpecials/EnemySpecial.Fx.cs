using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// 적 필살기의 연출 도구: 시간에 따라 움직이는 값(Tween), 몸 띄우기·기울이기, 주변 어둡게 하기,
/// 빛나는 띠·고리·연기·불티·충격파·갈라진 땅·글자 진·잔상, 소품용 도형(회전체·고리).
/// 미리보기 도구(Assets/Editor/EnemySpecialPreview)에서 확정한 것을 실제 시간 기준으로 옮긴 것이다.
/// 만든 것은 전부 root 아래에 두어, 도중에 멈출 때 한 번에 치운다.
/// </summary>
public sealed partial class EnemySpecial
{
    // 전투 뷰가 매 프레임 자세를 되돌리므로, 그 뒤(LateUpdate)에 얹는 몸 전체의 뜨기·돌기·기울이기
    private Vector3 foeLift, heroLift;
    private float foeSpin, foeTilt, heroSpin, heroTilt, pull;

    private readonly List<Func<bool>> updaters = new List<Func<bool>>();
    private readonly List<UnityEngine.Object> extras = new List<UnityEngine.Object>();   // root 밖(상대 뼈 등)에 붙인 것
    private Transform root;
    private readonly Dictionary<Material, Material> recolored = new Dictionary<Material, Material>();
    private readonly Dictionary<string, Material> glowMats = new Dictionary<string, Material>();
    private Material smokeMat, softMat, sparkMat, darkMat, crackMat;
    private float smokeFlash;

    private Light sun;
    private float sunBase, dimLevel = 1f, skyExposure;
    private Color skyBase, equatorBase, groundBase, flatBase, fogBase;
    private SphericalHarmonicsL2 probeBase;
    private Material skyMat, skyOriginal;
    private bool lightingCaptured;

    const float RibbonWidth = 2.3f;   // 빛나는 띠는 가장자리가 번지며 사라지므로 이만큼 넓혀 그린다

    // ───────────── 기본 도구 ─────────────

    static float Rand(float a, float b) => Random.Range(a, b);
    static Color Hdr(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
    float HeroX => hero.transform.position.x;
    float FoeX => transform.position.x;
    /// <summary>카메라에서 먼 쪽(무대 안쪽).</summary>
    Vector3 Far => cam != null && cam.transform.position.z > 0f ? Vector3.back : Vector3.forward;
    /// <summary>적에게서 주인공 쪽으로, 주인공이 밀려나는 방향.</summary>
    Vector3 Out => new Vector3(-S, 0f, 0f);
    Quaternion LookOut => Quaternion.LookRotation(Out, Vector3.up);
    static readonly Quaternion Down = Quaternion.LookRotation(Vector3.down, Vector3.right), Up = Quaternion.LookRotation(Vector3.up, Vector3.right);
    static Vector3 Ground(float x) => new Vector3(x, 0f, 0f);

    Transform Root
    {
        get
        {
            if (root == null) root = new GameObject("EnemySpecial FX").transform;
            return root;
        }
    }
    GameObject NewObject(string name) { var go = new GameObject(name); go.transform.SetParent(Root, false); return go; }
    T Own<T>(T thing) where T : Component { if (thing != null) thing.transform.SetParent(Root, true); return thing; }
    GameObject Own(GameObject go) { if (go != null) go.transform.SetParent(Root, true); return go; }

    void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var rb = fighter.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = new Vector2(x, rb.position.y); rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y); }
        Physics2D.SyncTransforms();
    }
    Vector3 Hands()
    {
        var anim = foeView != null ? foeView.modelAnimator : null;
        if (anim != null && anim.isHuman)
        {
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand); var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (l && r) return (l.position + r.position) * .5f;
        }
        return transform.position + Vector3.up * 1.15f + Out * .4f;
    }
    Transform HeroBoneTransform(HumanBodyBones bone)
    {
        var anim = heroView != null ? heroView.modelAnimator : null;
        return anim != null && anim.isHuman ? anim.GetBoneTransform(bone) : null;
    }
    Vector3 HeroBone(HumanBodyBones bone, float fallbackHeight)
    {
        var t = HeroBoneTransform(bone);
        return t ? t.position : new Vector3(HeroX, fallbackHeight, 0);
    }
    Vector3 HeroChest => HeroBone(HumanBodyBones.Chest, 1.25f);

    void StepUpdaters() { for (int i = updaters.Count - 1; i >= 0; i--) if (i < updaters.Count && updaters[i]()) updaters.RemoveAt(i); }
    static IEnumerator Hold(float seconds) { float t = 0f; while (t < seconds) { t += Time.deltaTime; yield return null; } }
    void Tween(float seconds, Action<float> step, Action done = null)
    {
        float t = 0f;
        updaters.Add(() =>
        {
            t += Time.deltaTime;
            float u = seconds <= 0f ? 1f : Mathf.Clamp01(t / seconds);
            step(u);
            if (u < 1f) return false;
            done?.Invoke(); return true;
        });
    }
    void After(float seconds, Action action) => Tween(seconds, u => { }, action);
    void Move(Transform t, Vector3 to, float seconds, Action done = null, int ease = 0)
    {
        Vector3 from = t.position;
        Tween(seconds, u => { if (!t) return; float e = ease == 1 ? u * u : ease == 2 ? 1 - (1 - u) * (1 - u) : u; t.position = Vector3.LerpUnclamped(from, to, e); }, done);
    }
    void Slide(GameObject fighter, float toX, float seconds)
    {
        float from = fighter.transform.position.x;
        Tween(seconds, u => { if (fighter) Place(fighter, Mathf.Lerp(from, toX, u)); });
    }
    /// <summary>몸 전체가 포물선으로 뛴다(애니메이션 위에 얹는다).</summary>
    void Jump(bool isFoe, float height, float seconds, Action landed = null)
    {
        Tween(seconds, u => { float y = 4 * height * u * (1 - u); if (isFoe) foeLift.y = y; else heroLift.y = y; }, () => { if (isFoe) foeLift.y = 0; else heroLift.y = 0; landed?.Invoke(); });
    }
    /// <summary>주인공이 떠올랐다 떨어지며 뒤로 젖혀진다. 막았으면 뜨지 않는다.</summary>
    void Launch(float height, float seconds, float tilt)
    {
        if (guardedSpecial) return;
        Jump(false, height, seconds);
        Tween(seconds, u => heroTilt = tilt * Mathf.Sin(u * Mathf.PI), () => heroTilt = 0f);
    }
    /// <summary>카메라가 물러나 큰 기술을 한 화면에 담는다.</summary>
    void Pull(float to, float seconds) { float from = pull; Tween(seconds, u => { pull = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u)); QiCameraFx.Pull(pull * .8f); }); }
    void Shake(float amplitude, float decay = 6f) => QiCameraFx.Shake(amplitude * .45f, decay);
    void ShowFoe(bool visible)
    {
        if (foeView == null || foeView.visualRoot == null) return;
        foreach (var r in foeView.visualRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }
    void ApplyPose()
    {
        if (foeView != null && foeView.visualRoot != null && (foeLift != Vector3.zero || foeSpin != 0f || foeTilt != 0f))
        {
            var t = foeView.visualRoot; t.position += foeLift;
            if (foeSpin != 0f || foeTilt != 0f) t.rotation = Quaternion.Euler(0, foeSpin, 0) * t.rotation * Quaternion.Euler(foeTilt, 0, 0);
        }
        if (heroView != null && heroView.visualRoot != null && (heroLift != Vector3.zero || heroSpin != 0f || heroTilt != 0f))
        {
            var t = heroView.visualRoot; t.position += heroLift;
            if (heroSpin != 0f || heroTilt != 0f) t.rotation = Quaternion.Euler(0, heroSpin, 0) * t.rotation * Quaternion.Euler(heroTilt, 0, 0);
        }
    }

    // ───────────── 주변 어둡게 ─────────────

    void CaptureLighting()
    {
        if (lightingCaptured) return;
        lightingCaptured = true;
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
        sunBase = sun != null ? sun.intensity : 1f; dimLevel = 1f;
        skyBase = RenderSettings.ambientSkyColor; equatorBase = RenderSettings.ambientEquatorColor; groundBase = RenderSettings.ambientGroundColor; flatBase = RenderSettings.ambientLight;
        fogBase = RenderSettings.fogColor; probeBase = RenderSettings.ambientProbe;
        // 하늘은 복사본을 걸어 놓고 바꾼다(원본 재질 파일을 건드리지 않기 위해). 끝나면 원본으로 되돌린다.
        skyOriginal = RenderSettings.skybox;
        skyMat = skyOriginal != null && skyOriginal.HasProperty("_Exposure") ? new Material(skyOriginal) : null;
        if (skyMat != null) { RenderSettings.skybox = skyMat; skyExposure = skyMat.GetFloat("_Exposure"); }
    }
    void SetDim(float k)
    {
        if (!lightingCaptured) return;
        dimLevel = k;
        if (sun != null) sun.intensity = sunBase * k;
        RenderSettings.ambientSkyColor = skyBase * k; RenderSettings.ambientEquatorColor = equatorBase * k; RenderSettings.ambientGroundColor = groundBase * k; RenderSettings.ambientLight = flatBase * k;
        RenderSettings.ambientProbe = probeBase * k;
        RenderSettings.fogColor = fogBase * Mathf.Lerp(.3f, 1f, k);
        if (skyMat != null) skyMat.SetFloat("_Exposure", skyExposure * Mathf.Lerp(.25f, 1f, k));
    }
    void RestoreSky()
    {
        if (!lightingCaptured) return;
        if (skyMat != null) { if (RenderSettings.skybox == skyMat) RenderSettings.skybox = skyOriginal; Destroy(skyMat); skyMat = null; }
        lightingCaptured = false;
    }
    /// <summary>주변을 어둡게(1 = 원래 밝기). 원래 밝기로 돌아오면 하늘 재질도 원본으로 되돌린다.</summary>
    void Dim(float to, float seconds)
    {
        float from = dimLevel;
        Tween(seconds, u => SetDim(Mathf.Lerp(from, to, u)), () => { if (to >= 1f) RestoreSky(); });
    }

    // ───────────── 파운장 이펙트 빌려 쓰기 ─────────────

    Color Recolor(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        if (max <= 0f) return c;
        var hue = Color.Lerp(Color.white, spec.tint, Mathf.Clamp01((1f - min / max) * 1.35f));
        return new Color(hue.r * max, hue.g * max, hue.b * max, c.a);
    }
    Gradient Recolor(Gradient g)
    {
        if (g == null) return null;
        var keys = g.colorKeys; for (int i = 0; i < keys.Length; i++) keys[i].color = Recolor(keys[i].color);
        var result = new Gradient(); result.SetKeys(keys, g.alphaKeys); result.mode = g.mode; return result;
    }
    ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient c)
    {
        switch (c.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Recolor(c.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Recolor(c.colorMin), Recolor(c.colorMax));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Recolor(c.gradient));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(c.gradientMin), Recolor(c.gradientMax));
            default: return c;
        }
    }
    /// <summary>파운장 이펙트를 이 적의 색으로 다시 칠한다.</summary>
    void Tint(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i]; if (!m) continue;
                if (!recolored.TryGetValue(m, out var n))
                {
                    n = new Material(m); string shader = m.shader.name;
                    if (shader.EndsWith("QiSmoke")) { var c = spec.smoke; c.a = m.GetColor("_Color").a; n.SetColor("_Color", c); }
                    else if (shader.EndsWith("QiOrb")) { n.SetColor("_CoreColor", Recolor(m.GetColor("_CoreColor"))); n.SetColor("_RimColor", Recolor(m.GetColor("_RimColor"))); }
                    else if (n.HasProperty("_Color")) n.SetColor("_Color", Recolor(m.GetColor("_Color")));
                    recolored[m] = n;
                }
                mats[i] = n;
            }
            r.sharedMaterials = mats;
        }
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.color = Recolor(l.color);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main; main.startColor = Recolor(main.startColor);
            var over = ps.colorOverLifetime; if (over.enabled) over.color = Recolor(over.color);
        }
        foreach (var trail in go.GetComponentsInChildren<TrailRenderer>(true)) trail.colorGradient = Recolor(trail.colorGradient);
    }
    GameObject Fx(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f, float life = 0f)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, position, rotation); go.transform.localScale = Vector3.one * scale; Tint(go);
        // 스스로 사라지지 않는 이펙트(준비 표식 등)는 수명을 주거나, 도중에 멈출 때 치울 수 있게 적어 둔다.
        if (life > 0f) Destroy(go, life);
        else if (go.GetComponent<QiFxLife>() == null && go.GetComponent<QiChargeFx>() == null) extras.Add(go);
        return go;
    }
    GameObject Burst(Vector3 position, float scale) => Fx(qi.impact, position, Quaternion.identity, scale);
    /// <summary>기파 몸체만 빌려 쓴다(판정 스크립트는 끄고 직접 움직인다).</summary>
    GameObject Wave(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = Own(Instantiate(qi.wave, position, rotation));
        var wave = go.GetComponent<QiWave>(); if (wave != null) wave.enabled = false;
        go.transform.localScale = scale; Tint(go); return go;
    }
    void EndWave(GameObject go)
    {
        if (!go) return;
        var w = go.GetComponent<QiWave>();
        if (w != null)
        {
            if (w.body) w.body.gameObject.SetActive(false);
            if (w.glow) w.glow.enabled = false;
            if (w.trails != null) foreach (var t in w.trails) if (t) t.emitting = false;
            if (w.detachOnEnd != null) foreach (var ps in w.detachOnEnd) if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        Destroy(go, .7f);
    }
    void CutIn()
    {
        if (qi.brush == null) return;
        var cut = QiCutIn.Play(qi.brush, S > 0f ? 1 : -1);
        foreach (var t in cut.GetComponentsInChildren<Text>(true))
        {
            if (t.name == "Hanja") t.text = spec.hanja;
            else if (t.name == "Hangul") t.text = string.Join(" ", spec.hangul.ToCharArray());
            var outline = t.GetComponent<Outline>(); if (outline) outline.effectColor = new Color(spec.tint.r * .8f, spec.tint.g * .8f, spec.tint.b * .8f, .85f);
        }
    }

    // ───────────── 재질 ─────────────

    Material RibbonMaterial(Texture texture, Color color, float erode, float hot, float soft, Vector4 flow)
    {
        var m = new Material(fx.ribbon);
        m.SetTexture("_MainTex", texture); m.SetTexture("_NoiseTex", fx.noiseTex); m.SetColor("_Color", color); m.SetFloat("_Intensity", 1f);
        m.SetFloat("_Erode", erode); m.SetFloat("_Hot", hot); m.SetFloat("_Soft", soft); m.SetVector("_Flow", flow);
        return m;
    }
    Material FlatMaterial(Texture texture, Color color, float shadeFloor)
    {
        var m = new Material(fx.smoke);
        m.SetTexture("_MainTex", texture); m.SetColor("_Color", color); m.SetColor("_Glow", Color.black); m.SetFloat("_Shade", shadeFloor); m.SetFloat("_Soft", 0f); m.SetFloat("_GroundFade", 0f);
        return m;
    }
    Material DarkMaterial => darkMat != null ? darkMat : darkMat = FlatMaterial(Texture2D.whiteTexture, new Color(.012f, .01f, .016f, 1f), 1f);
    Material CrackMaterial => crackMat != null ? crackMat : crackMat = FlatMaterial(fx.crackTex, new Color(.36f, .29f, .23f, 1f), 0f);
    /// <summary>가운데만 하얗게 달아오르고 가장자리는 제 색으로 번지는 띠. 5 이상은 벼락 속심처럼 통째로 하얗다.</summary>
    Material Glow(float intensity = 2.4f)
    {
        string key = intensity.ToString("0.00");
        if (!glowMats.TryGetValue(key, out var m))
        {
            bool core = intensity >= 5f;
            m = RibbonMaterial(fx.ribbonTex, Hdr(spec.tint, core ? intensity : intensity * .8f), core ? 0f : .4f, core ? 1f : .6f, 0f, new Vector4(-1.7f, .12f, 3f, 1f));
            glowMats[key] = m;
        }
        return m;
    }
    Material SmokeMaterial()
    {
        if (smokeMat != null) return smokeMat;
        smokeMat = new Material(fx.smoke); smokeMat.SetTexture("_MainTex", fx.smokeTex);
        var c = spec.smoke; c.a = .9f; smokeMat.SetColor("_Color", c); smokeMat.SetColor("_Glow", Color.black);
        smokeMat.SetFloat("_Shade", .42f); smokeMat.SetFloat("_Soft", 1.5f); smokeMat.SetFloat("_GroundFade", .45f);
        return smokeMat;
    }
    Material SoftGlow() => softMat != null ? softMat : softMat = RibbonMaterial(fx.glowTex, Hdr(spec.tint, 1f), 0f, .3f, 1.2f, Vector4.zero);
    Material SparkMaterial() => sparkMat != null ? sparkMat : sparkMat = RibbonMaterial(fx.glowTex, Hdr(Color.Lerp(spec.tint, Color.white, .25f), 3f), 0f, .7f, 0f, Vector4.zero);
    Material WoodMaterial(float shade)
    {
        var m = new Material(fx.wood); m.SetColor("_BaseColor", new Color(shade, shade, shade)); m.SetTextureOffset("_BaseMap", new Vector2(Rand(0f, .8f), 0f)); return m;
    }
    Material Metal(Material source, Vector2 tile, Color emission)
    {
        var m = new Material(source); m.SetTextureScale("_BaseMap", tile); m.SetTextureOffset("_BaseMap", new Vector2(Rand(0f, 1f), Rand(0f, 1f)));
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        return m;
    }
    Material BronzeMaterial(Vector2 tile, Color emission = default) => Metal(fx.bronze, tile, emission);
    Material IronMaterial(Vector2 tile, Color emission = default) => Metal(fx.iron, tile, emission);
    /// <summary>가장자리가 빛나는 기운 몸체(잔상, 종 모양 기운).</summary>
    Material OrbMaterial(Color core, Color rim, float rimPower)
    {
        var m = new Material(fx.orb);
        m.SetTexture("_NoiseTex", fx.noiseTex); m.SetColor("_CoreColor", core); m.SetColor("_RimColor", rim); m.SetFloat("_RimPower", rimPower);
        m.SetFloat("_NoiseScale", 2.4f); m.SetFloat("_Wobble", 0f); m.SetFloat("_Alpha", 1f);
        return m;
    }
    /// <summary>번쩍임이 연기 속을 밝히는 값을 줄여 가며 재질에 넣는다.</summary>
    void StepLook()
    {
        if (smokeFlash <= 0f && smokeMat == null) return;
        smokeFlash *= Mathf.Pow(.7f, Time.deltaTime * 30f); if (smokeFlash < .003f) smokeFlash = 0f;
        if (smokeMat != null) smokeMat.SetColor("_Glow", Hdr(Color.Lerp(spec.tint, Color.white, .45f), smokeFlash));
    }

    // ───────────── 연기·빛·불티 ─────────────

    void Puff(Vector3 position, float size, float life) => Smoke(position, Vector3.one * .05f, size, 0, .1f, life, 0f, 1, 1f, SoftGlow());
    /// <summary>연기·안개·먼지·먹구름. rate 는 초당 개수, burst 는 한 번에 터지는 개수.</summary>
    GameObject Smoke(Vector3 center, Vector3 box, float size, float rate, float duration, float life, float rise, int burst = 0, float alpha = .6f, Material material = null)
    {
        var go = NewObject("Special smoke"); go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.duration = Mathf.Max(.1f, duration); main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .7f, life); main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * .6f, size); main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1f, 1f, 1f, alpha); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
        var emission = ps.emission; emission.rateOverTime = rate;
        if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-.25f, .25f); velocity.y = new ParticleSystem.MinMaxCurve(rise * .5f, rise); velocity.z = new ParticleSystem.MinMaxCurve(-.15f, .15f);
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime; color.enabled = true; color.color = new ParticleSystem.MinMaxGradient(fade);
        var grow = ps.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .6f, 1f, 1.35f));
        var turn = ps.rotationOverLifetime; turn.enabled = true; turn.z = new ParticleSystem.MinMaxCurve(-.25f, .25f);
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material != null ? material : SmokeMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off;
        if (material == null)
        {
            // 연기 덩어리 16종 가운데 하나씩 고른다. 위쪽이 밝은 명암이 유지되게 조금만 돌리고, 좌우를 섞고, 밝기를 조금씩 다르게 한다.
            var sheet = ps.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = 4; sheet.numTilesY = 4; sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f); sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, .999f); sheet.cycleCount = 1;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.6f, .6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.76f, .76f, .76f, alpha), new Color(1f, 1f, 1f, alpha));
            var drift = ps.noise; drift.enabled = true; drift.strength = .22f; drift.frequency = .5f; drift.scrollSpeed = .3f;
            renderer.flip = new Vector3(.5f, 0f, 0f);
        }
        ps.Play(); Destroy(go, duration + life + .6f);
        return go;
    }
    void Flash(Vector3 position, float intensity, float range, float life)
    {
        var go = NewObject("Special flash"); go.transform.position = position;
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = Color.Lerp(spec.tint, Color.white, .3f); l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
        smokeFlash = Mathf.Max(smokeFlash, intensity * .028f);
        Tween(life, u => { if (l) l.intensity = intensity * (1f - u); }, () => { if (go) Destroy(go); });
    }
    /// <summary>튀는 불티(쇠가 부딪칠 때), 물방울, 돌가루. 날아가는 방향으로 길게 늘어나고 떨어진다.</summary>
    void Sparks(Vector3 position, Vector3 direction, int count, float speed, float life = .7f, float spread = 55f, float gravity = 1.6f, float size = .05f)
    {
        var go = NewObject("Special sparks");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.sqrMagnitude > .001f ? direction.normalized : Vector3.up));
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.duration = .2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .45f, life); main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .3f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size); main.gravityModifier = gravity; main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 500;
        var emission = ps.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = Mathf.Clamp(spread, 1f, 89f); shape.radius = .06f;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .55f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime; color.enabled = true; color.color = new ParticleSystem.MinMaxGradient(fade);
        var shrink = ps.sizeOverLifetime; shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .35f));
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .05f; renderer.lengthScale = 1.6f;
        renderer.sharedMaterial = SparkMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play(); Destroy(go, life + .6f);
    }
    /// <summary>천천히 떠오르는 불씨·기운 알갱이.</summary>
    void Embers(Vector3 center, Vector3 box, int count, float rise, float life, float size = .045f)
    {
        var go = NewObject("Special embers"); go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.duration = .2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * .5f, life); main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(size * .4f, size);
        main.startColor = Color.white; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
        var emission = ps.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-.3f, .3f); velocity.y = new ParticleSystem.MinMaxCurve(rise * .4f, rise); velocity.z = new ParticleSystem.MinMaxCurve(-.2f, .2f);
        var drift = ps.noise; drift.enabled = true; drift.strength = .5f; drift.frequency = 1.2f;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f) });
        var color = ps.colorOverLifetime; color.enabled = true; color.color = new ParticleSystem.MinMaxGradient(fade);
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = SparkMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play(); Destroy(go, life + .6f);
    }

    // ───────────── 줄·고리·충격파 ─────────────

    LineRenderer Line(Material m, float width0, float width1, float life, bool loop = false, bool flat = false, bool fadeEnds = true)
    {
        var go = NewObject("Special line"); var lr = go.AddComponent<LineRenderer>();
        if (flat) { go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); lr.alignment = LineAlignment.TransformZ; }
        bool ribbon = m != null && m.shader == fx.ribbon; float k = ribbon ? RibbonWidth : 1f;
        lr.sharedMaterial = m; lr.widthCurve = AnimationCurve.Linear(0, width0 * k, 1, width1 * k); lr.widthMultiplier = 1f; lr.numCapVertices = ribbon ? 0 : 2; lr.numCornerVertices = 3; lr.useWorldSpace = true; lr.loop = loop;
        lr.textureMode = LineTextureMode.Stretch;
        if (ribbon && !loop && fadeEnds)
        {
            var ends = new Gradient();
            ends.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(1f, .88f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = ends;
        }
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.positionCount = 0;
        Destroy(go, life); return lr;
    }
    /// <summary>빠르게 스치는 한 줄기(바람 가르는 선, 물줄기).</summary>
    void Streak(Vector3 from, Vector3 to, float width, float life, float intensity = 2.4f)
    {
        var lr = Line(Glow(intensity), width * .2f, width, life + .03f); lr.positionCount = 2;
        Tween(life, u => { if (!lr) return; lr.SetPosition(0, Vector3.Lerp(from, to, Mathf.Clamp01(u * 1.6f - .6f))); lr.SetPosition(1, Vector3.Lerp(from, to, Mathf.Clamp01(u * 2.2f))); lr.widthMultiplier = 1f - u * .7f; });
    }
    /// <summary>벼락 한 줄기: 꺾이는 굵은 줄 위에 흰 속심, 곁가지.</summary>
    void Bolt(Vector3 from, Vector3 to, float width, float life, int forks = 2)
    {
        Vector3[] Strand(Vector3 a, Vector3 b, float jitter)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(Vector3.Distance(a, b) / .32f));
            var points = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) { float t = i / (float)n, j = jitter * Mathf.Sin(t * Mathf.PI); points[i] = Vector3.Lerp(a, b, t) + new Vector3(Rand(-j, j), Rand(-j, j) * .3f, Rand(-j, j) * .5f); }
            return points;
        }
        void Draw(Vector3[] points, float w)
        {
            foreach (var layer in new[] { (Glow(2.4f), w), (Glow(7f), w * .34f) })
            {
                var lr = Line(layer.Item1, layer.Item2, layer.Item2 * .6f, life + .03f, false, false, false); lr.positionCount = points.Length; lr.SetPositions(points);
                Tween(life, u => { if (lr) lr.widthMultiplier = u < .5f ? 1f : 1f - (u - .5f) * 1.6f; });
            }
        }
        var main = Strand(from, to, .34f); Draw(main, width);
        for (int f = 0; f < forks; f++)
        {
            Vector3 a = main[Random.Range(1, main.Length - 2)];
            Draw(Strand(a, a + new Vector3(Rand(-1.3f, 1.3f), Rand(-1.5f, -.5f), Rand(-.5f, .5f)), .16f), width * .5f);
        }
    }
    /// <summary>바닥을 타고 뻗는 금. 들쭉날쭉한 틈 양옆으로 들뜬 돌 조각과 새어 나오는 빛을 세워, 낮은 카메라에서도 땅이 깨진 것으로 읽히게 한다.</summary>
    void Cracks(Vector3 origin, float dirX, float length, int count, float spreadDegrees, float grow, float life = 2.6f)
    {
        for (int c = 0; c < count; c++)
        {
            float angle = (count == 1 ? 0 : Mathf.Lerp(-spreadDegrees, spreadDegrees, c / (count - 1f))) + Rand(-5, 5);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * new Vector3(dirX, 0, 0), side = Vector3.Cross(Vector3.up, dir).normalized, p = origin;
            float len = length * Rand(.7f, 1f); int pts = Mathf.Max(6, Mathf.RoundToInt(len / .26f));
            var path = new Vector3[pts];
            for (int i = 0; i < pts; i++) { path[i] = p + Vector3.up * .03f; p += dir * (len / pts) + side * Rand(-.2f, .2f); }
            foreach (var layer in new[] { (CrackMaterial, .62f, .5f, 0f), (Glow(2.4f), .1f, .02f, .012f) })
            {
                var lr = Line(layer.Item1, layer.Item2, layer.Item3, life, false, true); float lift = layer.Item4;
                Tween(grow, u => { if (!lr) return; int show = Mathf.Clamp(Mathf.CeilToInt(pts * u), 2, pts); lr.positionCount = show; for (int i = 0; i < show; i++) lr.SetPosition(i, path[i] + Vector3.up * lift); });
            }
            for (int i = 1; i < pts; i++)
            {
                Vector3 at = path[i]; float delay = grow * i / pts, taper = 1f - .65f * i / pts; bool spark = i % 2 == 1;
                After(delay, () =>
                {
                    if (spark) Streak(at, at + Vector3.up * Rand(.3f, .7f) * taper, .06f, .32f, 2.4f);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        if (Random.value < .3f) continue;
                        float size = Rand(.16f, .44f) * taper;
                        var rock = Rock(at + side * (k * Rand(.12f, .26f)) + Vector3.down * size * .28f, size); Vector3 full = rock.transform.localScale, rest = rock.transform.position;
                        rock.transform.localScale = full * .05f;
                        Tween(.09f, u => { if (!rock) return; rock.transform.localScale = full * Mathf.Lerp(.05f, 1f, u); rock.transform.position = rest + Vector3.up * (.16f * Mathf.Sin(u * Mathf.PI)); });
                        Destroy(rock, Mathf.Max(.2f, life - delay));
                    }
                });
            }
        }
    }
    /// <summary>고리. normal 에 수직인 원을 그리고, 시간에 따라 반지름과 중심을 바꾼다.</summary>
    LineRenderer Ring(Func<float, Vector3> center, Vector3 normal, Func<float, float> radius, float seconds, float width, float intensity = 2.4f, bool fade = true)
    {
        var lr = Line(Glow(intensity), width, width, seconds + .05f, true);
        normal.Normalize();
        Vector3 a = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .9f ? Vector3.right : Vector3.up).normalized, b = Vector3.Cross(normal, a).normalized;
        const int n = 96; lr.positionCount = n;
        Action<float> draw = u =>
        {
            if (!lr) return; Vector3 c = center(u); float r = radius(u);
            for (int i = 0; i < n; i++) { float t = i * Mathf.PI * 2 / n; lr.SetPosition(i, c + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * r); }
            if (fade) lr.widthMultiplier = 1f - Mathf.Clamp01((u - .6f) / .4f);
        };
        draw(0f); Tween(seconds, draw);
        return lr;
    }
    /// <summary>옆에서 보이는 음파 호: 중심에서 주인공 쪽으로 퍼지는 부채꼴 곡선.</summary>
    void Arc(Func<float, Vector3> center, Func<float, float> radius, float halfDegrees, float seconds, float width, float intensity = 2.6f)
    {
        var lr = Line(Glow(intensity), width, width, seconds + .05f); const int n = 28; lr.positionCount = n;
        Tween(seconds, u =>
        {
            if (!lr) return; Vector3 c = center(u); float r = radius(u);
            for (int i = 0; i < n; i++) { float a = Mathf.Lerp(-halfDegrees, halfDegrees, i / (n - 1f)) * Mathf.Deg2Rad; lr.SetPosition(i, c + new Vector3(-S * Mathf.Cos(a), Mathf.Sin(a), 0) * r); }
            lr.widthMultiplier = 1f - Mathf.Clamp01((u - .5f) / .5f);
        });
    }
    /// <summary>바닥을 타고 퍼지는 충격파 고리.</summary>
    void Shockwave(Vector3 center, float fromRadius, float toRadius, float seconds, float intensity = 2f)
    {
        var m = RibbonMaterial(fx.shockTex, Hdr(spec.tint, intensity), 0f, .5f, 0f, Vector4.zero);
        var quad = Quad(new Vector3(center.x, center.y + .035f, center.z), Quaternion.LookRotation(Vector3.down, Vector3.forward), fromRadius * 2f / .92f, m);
        Tween(seconds, u =>
        {
            if (!quad) return;
            quad.transform.localScale = Vector3.one * (Mathf.Lerp(fromRadius, toRadius, 1f - (1f - u) * (1f - u)) * 2f / .92f);
            m.SetFloat("_Intensity", 1f - u * u);
        }, () => { if (quad) Destroy(quad); Destroy(m); });
    }
    /// <summary>허공에 뜨는 큰 글자 진(陣). 카메라가 낮아 바닥 글씨는 안 보이므로 상대 뒤에 세운다. 글자 둘레로 부적 문양이 천천히 돈다.</summary>
    void Seal(string glyph, Vector3 center, float size, float life)
    {
        var go = NewObject("Seal glyph"); go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(Far));
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = -5;
        ((RectTransform)go.transform).sizeDelta = new Vector2(400, 400);
        var text = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Text>(); text.transform.SetParent(go.transform, false);
        text.text = glyph; text.font = WuxiaTheme.MenuFont; text.fontSize = 300; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; text.rectTransform.sizeDelta = new Vector2(400, 400);
        var color = Color.Lerp(spec.tint, Color.white, .25f);
        var halo = text.gameObject.AddComponent<Outline>(); halo.effectColor = new Color(color.r, color.g, color.b, .35f); halo.effectDistance = new Vector2(2.5f, 2.5f);
        const float discSize = 1.55f;
        var discMat = RibbonMaterial(fx.talismanTex, Hdr(spec.tint, 1.15f), 0f, .2f, 0f, Vector4.zero);
        var disc = Quad(center + Far * .03f, Quaternion.LookRotation(Far), size * discSize, discMat);
        var hazeMat = RibbonMaterial(fx.glowTex, Hdr(spec.tint, .16f), 0f, 0f, 0f, Vector4.zero);
        var haze = Quad(center + Far * .06f, Quaternion.LookRotation(Far), size * 2.1f, hazeMat);
        Tween(life, u =>
        {
            if (!go) return;
            float pop = Mathf.Lerp(1.5f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .14f))), fade = Mathf.Clamp01(u / .08f) * (1f - Mathf.Clamp01((u - .7f) / .3f));
            go.transform.localScale = Vector3.one * (size / 400f) * pop;
            text.color = new Color(color.r, color.g, color.b, .92f * fade);
            if (disc) { disc.transform.localScale = Vector3.one * (size * discSize * Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .2f)))); disc.transform.Rotate(0f, 0f, 16f * Time.deltaTime, Space.Self); }
            discMat.SetFloat("_Intensity", fade); hazeMat.SetFloat("_Intensity", fade * .8f);
        }, () => { if (go) Destroy(go); if (disc) Destroy(disc); if (haze) Destroy(haze); Destroy(discMat); Destroy(hazeMat); });
    }

    // ───────────── 소품 ─────────────

    /// <summary>실제 바위 모델. 큰 것은 던지고, 작은 것은 파편으로 튄다.</summary>
    GameObject Rock(Vector3 position, float size)
    {
        var go = Own(Instantiate(fx.rock, position, Quaternion.Euler(Rand(0, 360), Rand(0, 360), Rand(0, 360))));
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
        foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) { c.enabled = false; Destroy(c); }
        var lod = go.GetComponentInChildren<LODGroup>();
        if (lod != null)
        {
            var lods = lod.GetLODs();
            for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r) r.enabled = false;
            lod.enabled = false;
        }
        Bounds Measure()
        {
            bool any = false; var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (!r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
        var bounds = Measure();
        go.transform.localScale *= size / Mathf.Max(.01f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
        go.transform.position += position - Measure().center;
        return go;
    }
    void Toss(GameObject go, Vector3 velocity, float life)
    {
        Vector3 spin = new Vector3(Rand(-400, 400), Rand(-400, 400), Rand(-400, 400)); int bounces = 0;
        updaters.Add(() =>
        {
            if (!go) return true;
            float dt = Time.deltaTime;
            velocity += Vector3.down * (16f * dt); go.transform.position += velocity * dt; go.transform.Rotate(spin * dt, Space.World);
            if (go.transform.position.y < .06f && velocity.y < 0)
            {
                var p = go.transform.position; p.y = .06f; go.transform.position = p; velocity = new Vector3(velocity.x * .45f, -velocity.y * .32f, velocity.z * .45f);
                if (++bounces > 2 || velocity.y < .7f) return true;
            }
            return false;
        });
        Destroy(go, life);
    }
    void Debris(Vector3 origin, int count, float speed, float sizeMin, float sizeMax, float dirX = 0f)
    {
        for (int i = 0; i < count; i++)
            Toss(Rock(origin + new Vector3(Rand(-.3f, .3f), .12f, Rand(-.3f, .3f)), Rand(sizeMin, sizeMax)), new Vector3(dirX * Rand(.5f, 2.5f) + Rand(-1.6f, 1.6f), Rand(.6f, 1f) * speed, Rand(-1.2f, 1.2f)), 2.4f);
    }
    GameObject Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        var collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Destroy(collider); }
        go.transform.SetParent(parent != null ? parent : Root, false);
        go.transform.localPosition = local; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
    GameObject Quad(Vector3 position, Quaternion rotation, float size, Material material)
    {
        var go = Part(null, PrimitiveType.Quad, position, Vector3.one * size, material); go.transform.rotation = rotation;
        var renderer = go.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return go;
    }
    /// <summary>적의 지금 모습을 본뜬 잔상(dark 면 검은 그림자 분신). 지정한 동작을 재생하고 사라진다.</summary>
    GameObject Ghost(Vector3 position, Vector3 lookAt, string state, float life, bool dark = false, float speed = 1f)
    {
        var source = foeView.visualRoot.gameObject;
        // 꺼진 부모 밑에 복제해 전투 스크립트가 한 번도 깨어나지 않게 한 뒤 떼어 낸다.
        var holder = new GameObject("Ghost holder"); holder.SetActive(false);
        var go = Instantiate(source, holder.transform);
        for (int pass = 0; pass < 2; pass++) foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb) DestroyImmediate(mb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) DestroyImmediate(c);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        var look = lookAt - position; look.y = 0f;
        go.transform.SetParent(Root, false);
        go.transform.SetPositionAndRotation(new Vector3(position.x, source.transform.position.y - foeLift.y, position.z), look.sqrMagnitude > .001f ? Quaternion.LookRotation(look) : source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        Destroy(holder);
        var anim = go.GetComponentInChildren<Animator>(); if (anim != null) { anim.applyRootMotion = false; anim.speed = speed; anim.Play(state, 0, 0f); }
        void Paint(GameObject target, Material material)
        {
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true; var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = material; r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off; r.SetPropertyBlock(null);
            }
        }
        // 잔상은 가장자리가 빛나는 기운 몸체로 그린다. 그림자 분신은 검은 몸 위에, 같은 동작을 하는 빛 테두리를 겹친다.
        var orb = OrbMaterial(dark ? Color.black : Hdr(spec.tint, .2f), Hdr(spec.tint, dark ? 1.6f : 2.3f), dark ? 2.6f : 1.7f);
        if (dark)
        {
            Paint(go, DarkMaterial);
            var rim = Instantiate(go, go.transform); rim.transform.localPosition = Vector3.zero; rim.transform.localRotation = Quaternion.identity; rim.transform.localScale = Vector3.one;
            Paint(rim, orb);
            var rimAnim = rim.GetComponentInChildren<Animator>(); if (rimAnim != null) { rimAnim.applyRootMotion = false; rimAnim.speed = speed; rimAnim.Play(state, 0, 0f); }
        }
        else Paint(go, orb);
        Tween(life, u => orb.SetFloat("_Alpha", 1f - Mathf.Clamp01((u - .65f) / .35f)), () => { if (go) Destroy(go); Destroy(orb); });
        return go;
    }
    /// <summary>한쪽에서 다른 쪽으로 빨려 가는 기운 알갱이.</summary>
    void Drain(Func<Vector3> from, Func<Vector3> to, int count, float seconds)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 wobble = new Vector3(0, Rand(-.4f, .4f), Rand(-.3f, .3f));
            After(seconds * i / count, () =>
            {
                var orb = Wave(from(), LookOut, Vector3.one * .16f);
                Tween(.28f, u => { if (!orb) return; orb.transform.position = Vector3.Lerp(from(), to(), u) + wobble * Mathf.Sin(u * Mathf.PI); }, () => EndWave(orb));
            });
        }
    }
    /// <summary>회전체. profile 은 (높이, 반지름)을 아래에서 위로. 바깥쪽이 앞면이다.</summary>
    GameObject Lathe(Vector3 basePosition, Vector2[] profile, Material material, int segments = 44, float tileU = 1f, float tileV = 1f)
    {
        int rows = profile.Length, stride = segments + 1;
        var vertices = new Vector3[rows * stride]; var normals = new Vector3[vertices.Length]; var uvs = new Vector2[vertices.Length]; var triangles = new int[(rows - 1) * segments * 6];
        var along = new float[rows]; float length = 0f;
        for (int r = 1; r < rows; r++) { length += Vector2.Distance(profile[r], profile[r - 1]); along[r] = length; }
        for (int r = 0; r < rows; r++)
        {
            Vector2 tangent = (profile[Mathf.Min(r + 1, rows - 1)] - profile[Mathf.Max(r - 1, 0)]).normalized;   // (높이 변화, 반지름 변화)
            for (int k = 0; k <= segments; k++)
            {
                float a = k * Mathf.PI * 2f / segments, c = Mathf.Cos(a), s = Mathf.Sin(a);
                vertices[r * stride + k] = new Vector3(c * profile[r].y, profile[r].x, s * profile[r].y);
                normals[r * stride + k] = new Vector3(c * tangent.x, -tangent.y, s * tangent.x).normalized;
                uvs[r * stride + k] = new Vector2(k / (float)segments * tileU, along[r] / Mathf.Max(.0001f, length) * tileV);
            }
        }
        int t = 0;
        for (int r = 0; r < rows - 1; r++) for (int k = 0; k < segments; k++)
        {
            int i0 = r * stride + k, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
            triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1; triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
        }
        return MeshObject("Special lathe", basePosition, vertices, normals, uvs, triangles, material);
    }
    /// <summary>고리(도넛). 축은 y 방향이다.</summary>
    GameObject Torus(Vector3 position, float major, float minor, Material material, int segments = 40, int sides = 12, float tileU = 6f)
    {
        int stride = sides + 1;
        var vertices = new Vector3[(segments + 1) * stride]; var normals = new Vector3[vertices.Length]; var uvs = new Vector2[vertices.Length]; var triangles = new int[segments * sides * 6];
        for (int i = 0; i <= segments; i++) for (int j = 0; j <= sides; j++)
        {
            float u = i * Mathf.PI * 2f / segments, v = j * Mathf.PI * 2f / sides, cu = Mathf.Cos(u), su = Mathf.Sin(u), cv = Mathf.Cos(v), sv = Mathf.Sin(v);
            vertices[i * stride + j] = new Vector3((major + minor * cv) * cu, minor * sv, (major + minor * cv) * su);
            normals[i * stride + j] = new Vector3(cv * cu, sv, cv * su);
            uvs[i * stride + j] = new Vector2(i / (float)segments * tileU, j / (float)sides);
        }
        int t = 0;
        for (int i = 0; i < segments; i++) for (int j = 0; j < sides; j++)
        {
            int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
            triangles[t++] = a; triangles[t++] = b; triangles[t++] = c; triangles[t++] = c; triangles[t++] = b; triangles[t++] = d;
        }
        return MeshObject("Special torus", position, vertices, normals, uvs, triangles, material);
    }
    GameObject MeshObject(string name, Vector3 position, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles, Material material)
    {
        var mesh = new Mesh { vertices = vertices, normals = normals, uv = uvs, triangles = triangles }; mesh.RecalculateBounds(); mesh.RecalculateTangents();
        var go = NewObject(name); go.transform.position = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        bool lit = material != null && material.shader.name.EndsWith("/Lit");
        renderer.shadowCastingMode = lit ? ShadowCastingMode.On : ShadowCastingMode.Off; renderer.receiveShadows = lit;
        return go;
    }
}
