using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 적 필살기 미리보기의 겉모습: 실행 중에 만들어 쓰는 텍스처(나무·쇠·청동·밧줄·연기·부적 문양 등)와
/// 그것을 쓰는 재질·도형·불티. 텍스처 파일을 프로젝트에 남기지 않고, 게임 쪽 재질과 셰이더도 건드리지 않는다.
/// </summary>
public static partial class EnemySpecialPreview
{
    const string FxFolder = "Assets/Effects/EnemySpecials/Shaders/";
    static Shader ribbonShader, smokeShader;
    static Texture2D texRibbon, texBrush, texNoise, texSmoke, texTalisman, texShock, texCrack, texBell, texRope;
    static Texture2D woodAlbedo, woodNormal, ironAlbedo, ironNormal, ironMask, bronzeAlbedo, bronzeNormal, bronzeMask;
    static float smokeFlash;
    static readonly Dictionary<string, Material> sparkMats = new Dictionary<string, Material>();
    static readonly int[] Trigram = { 7, 3, 5, 1, 6, 2, 4, 0 };

    static void BuildLook()
    {
        ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(FxFolder + "SpecialRibbon.shader");
        smokeShader = AssetDatabase.LoadAssetAtPath<Shader>(FxFolder + "SpecialSmoke.shader");
        if (ribbonShader == null || smokeShader == null) throw new Exception("Preview shaders are missing in " + FxFolder);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        texNoise = Bake(256, 256, true, (u, v) => { float n = Stretch(FbmUV(u, v, 5f, 4)); return new Color(n, n, n, 1f); });
        texRibbon = Bake(512, 64, true, RibbonPixel);
        texBrush = Bake(512, 64, true, BrushPixel);
        texTalisman = Bake(1024, 1024, true, TalismanPixel, TextureWrapMode.Clamp);
        texShock = Bake(512, 512, true, ShockPixel, TextureWrapMode.Clamp);
        texCrack = Bake(512, 128, true, CrackPixel, TextureWrapMode.Clamp);
        texBell = Bake(1024, 512, true, BellPixel);
        texRope = Bake(256, 64, false, RopePixel);
        texSmoke = BakeSmoke();
        BakeWood(); BakeIron(); BakeBronze();
        Debug.Log("[EnemySpecialPreview] look textures built in " + watch.ElapsedMilliseconds + " ms");
    }

    /// <summary>화면 효과: 색 번짐(색수차)은 글자를 뭉개므로 끄고, 블룸은 고품질 필터로 바꾼다. 블룸 세기 등은 게임 값 그대로다.</summary>
    static void SetupPostFx()
    {
        var go = new GameObject("EnemySpecialPreview PostFx"); Object.DontDestroyOnLoad(go);
        var volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 60f; volume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var aberration = profile.Add<ChromaticAberration>(false); aberration.intensity.Override(0f);
        var bloom = profile.Add<Bloom>(false); bloom.highQualityFiltering.Override(true);
        volume.sharedProfile = profile;
    }

    /// <summary>연기와 빛 덩이가 바닥에 닿는 곳을 부드럽게 지우려면 화면의 깊이 정보가 필요하다.</summary>
    static void PrepareCamera() { var data = cam.GetUniversalAdditionalCameraData(); if (data != null) data.requiresDepthTexture = true; }

    /// <summary>번쩍임이 연기 속을 밝히는 값을 매 프레임 줄여 가며 재질에 넣는다.</summary>
    static void StepLook()
    {
        smokeFlash *= .7f; if (smokeFlash < .003f) smokeFlash = 0f;
        if (spec != null && smokeMats.TryGetValue(spec.key, out var m)) m.SetColor("_Glow", Hdr(Color.Lerp(spec.tint, Color.white, .45f), smokeFlash));
    }

    // ───────────── 노이즈 ─────────────

    static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }
    static float Stretch(float n) => Mathf.Clamp01((n - .25f) / .5f);
    static Color Hdr(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
    static float Fbm(float x, float y, int octaves = 4)
    {
        float sum = 0f, amp = .5f, norm = 0f; x += 100f; y += 100f;
        for (int i = 0; i < octaves; i++) { sum += amp * Mathf.PerlinNoise(x + i * 17.3f, y + i * 9.1f); norm += amp; x *= 2.03f; y *= 2.03f; amp *= .5f; }
        return sum / norm;
    }
    /// <summary>u 방향으로 이어지는(반복되는) 노이즈.</summary>
    static float FbmU(float u, float v, float fx, float fy, int octaves = 4) => Mathf.Lerp(Fbm(u * fx, v * fy, octaves), Fbm((u - 1f) * fx, v * fy, octaves), u);
    /// <summary>양쪽으로 이어지는 노이즈.</summary>
    static float FbmUV(float u, float v, float f, int octaves = 4)
    {
        float a = Fbm(u * f, v * f, octaves), b = Fbm((u - 1f) * f, v * f, octaves), c = Fbm(u * f, (v - 1f) * f, octaves), d = Fbm((u - 1f) * f, (v - 1f) * f, octaves);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
    static float Hash(int x, int y, int seed)
    {
        unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; }
    }
    /// <summary>가장 가까운 점까지의 거리(망치 자국 같은 무늬). 양쪽으로 이어진다.</summary>
    static float Worley(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells, best = 9f; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx, cy = iy + dy, wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
            float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 31), d = (px - x) * (px - x) + (py - y) * (py - y);
            if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }

    // ───────────── 텍스처 굽기 ─────────────

    static Texture2D Finish(Color[] pixels, int w, int h, bool linear, TextureWrapMode wrap)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        tex.SetPixels(pixels); tex.Apply(true, false); return tex;
    }
    static Texture2D Bake(int w, int h, bool linear, Func<float, float, Color> pixel, TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) { float v = (y + .5f) / h; for (int x = 0; x < w; x++) px[y * w + x] = pixel((x + .5f) / w, v); }
        return Finish(px, w, h, linear, wrap);
    }
    static Texture2D NormalMap(float[] height, int w, int h, float strength)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float l = height[y * w + (x + w - 1) % w], r = height[y * w + (x + 1) % w], d = height[((y + h - 1) % h) * w + x], u = height[((y + 1) % h) * w + x];
            var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
            px[y * w + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1f);
        }
        return Finish(px, w, h, true, TextureWrapMode.Repeat);
    }

    /// <summary>빛나는 띠의 단면: 가운데 가는 속심과 넓게 번지는 빛, 길이 방향으로 결이 흐른다.</summary>
    static Color RibbonPixel(float u, float v)
    {
        float d = Mathf.Abs(v * 2f - 1f);
        float core = Mathf.Exp(-d * d * 34f), halo = Mathf.Exp(-d * d * 5.5f) * .36f;
        float streak = Stretch(FbmU(u, v, 5f, 2.5f, 3)), fibre = Stretch(FbmU(u, v + .3f, 24f, 9f, 2));
        float a = (core * (.72f + .28f * fibre) + halo) * (.6f + .4f * streak) * Smooth(1f, .75f, d);
        return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
    }
    /// <summary>마른 붓으로 그은 획: 넓은 띠에 붓털 자국이 길게 이어지고 군데군데 끊긴다(바람 줄기용).</summary>
    static Color BrushPixel(float u, float v)
    {
        float d = Mathf.Abs(v * 2f - 1f);
        float bristle = Stretch(FbmU(u, v, 2.5f, 26f, 3)), patch = Stretch(FbmU(u, v + .5f, 7f, 4f, 3));
        float a = Smooth(1f, .5f, d) * (.16f + .84f * bristle * (.55f + .45f * patch));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(a * 1.15f));
    }
    /// <summary>바닥 충격파: 바깥은 날카롭고 안쪽으로 길게 끌리는 고리.</summary>
    static Color ShockPixel(float u, float v)
    {
        float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x) / (Mathf.PI * 2f) + .5f;
        float n = Stretch(FbmU(th, r, 18f, 7f, 3));
        float tail = Smooth(.42f, .9f, r) * Smooth(.985f, .93f, r);
        float edge = Smooth(.035f, 0f, Mathf.Abs(r - .92f));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(tail * tail * (.3f + .7f * n) * .75f + edge * (.55f + .45f * n)));
    }
    /// <summary>갈라진 틈: 들쭉날쭉한 검은 틈과 부서져 들뜬 가장자리. 끝으로 갈수록 가늘어진다.</summary>
    static Color CrackPixel(float u, float v)
    {
        float taper = Mathf.Pow(1f - u, .6f) * Smooth(0f, .03f, u), on = Smooth(0f, .05f, taper);
        float centre = .5f + (Fbm(u * 6f + 3f, .5f, 3) - .5f) * .55f;
        float half = Mathf.Max(.004f, (.07f + .2f * Stretch(Fbm(u * 15f + 9f, 1.5f, 3))) * taper);
        float d = Mathf.Abs(v - centre);
        float core = Smooth(half, half * .55f, d) * on;
        float lip = Smooth(half * 2.2f + .03f, half, d) * (1f - core) * on;
        float bright = lip * (.5f + .5f * Fbm(u * 60f, v * 14f, 2));
        return new Color(bright, bright, bright, Mathf.Clamp01(core + lip * .6f));
    }
    static float LineAt(float v, float at, float half) => Smooth(half * 1.7f, half * .6f, Mathf.Abs(v - at));
    /// <summary>종 표면 무늬: 위아래 겹띠, 아랫단 물결, 가운데 네모 틀과 돌기 아홉 개, 윗단 세로 눈금. 텍스처 한 장이 종 반 바퀴다.</summary>
    static Color BellPixel(float u, float v)
    {
        float a = .04f;
        a += LineAt(v, .035f, .014f) + LineAt(v, .09f, .005f) + LineAt(v, .3f, .005f) + LineAt(v, .335f, .005f) + LineAt(v, .64f, .005f) + LineAt(v, .675f, .005f) + LineAt(v, .9f, .006f) + LineAt(v, .955f, .012f);
        float wave = Mathf.Abs(Mathf.Repeat(u * 12f, 1f) - .5f) * 2f;
        a += Smooth(.022f, .008f, Mathf.Abs(v - (.13f + wave * .13f))) * .8f;
        float px = (Mathf.Repeat(u * 2f, 1f) - .5f) / .36f, py = (v - .4875f) / .115f, box = Mathf.Max(Mathf.Abs(px), Mathf.Abs(py));
        a += Smooth(.09f, .03f, Mathf.Abs(box - 1f)) * .85f;
        if (box < 1f)
        {
            float dx = (Mathf.Repeat(px * 1.5f + .5f, 1f) - .5f) * .393f, dy = (Mathf.Repeat(py * 1.5f + .5f, 1f) - .5f) * .24f;
            a += Smooth(.06f, .035f, Mathf.Sqrt(dx * dx + dy * dy));
        }
        a += Smooth(.08f, .03f, Mathf.Abs(Mathf.Repeat(u * 24f, 1f) - .5f)) * .5f * Smooth(.7f, .72f, v) * Smooth(.88f, .86f, v);
        return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
    }
    /// <summary>꼬인 삼 밧줄. 길이 방향으로 이어진다.</summary>
    static Color RopePixel(float u, float v)
    {
        float strand = Mathf.Sin(Mathf.Repeat(u * 4f + v * 1.3f, 1f) * Mathf.PI);
        float round = Mathf.Sqrt(Mathf.Clamp01(1f - (v * 2f - 1f) * (v * 2f - 1f)));
        float fibre = .78f + .22f * Stretch(FbmU(u, v, 40f, 12f, 2));
        float k = (.22f + .78f * Mathf.Pow(strand, .6f)) * (.3f + .7f * round) * fibre;
        return new Color(.56f * k, .45f * k, .3f * k, 1f);
    }
    /// <summary>부적 문양: 겹고리, 눈금, 팔괘, 안쪽 점 고리. 가운데는 글자 자리로 비워 둔다.</summary>
    static Color TalismanPixel(float u, float v)
    {
        const float aa = .0035f, turn = Mathf.PI * 2f;
        float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x); if (th < 0f) th += turn;
        float Band(float at, float half) { return Smooth(half + aa, half - aa, Mathf.Abs(r - at)); }
        float a = Band(.94f, .011f) + Band(.885f, .004f) + Band(.61f, .006f) + Band(.565f, .003f) * .8f;
        float tick = Mathf.Abs(Mathf.Repeat(th / turn * 96f, 1f) - .5f);
        a += Smooth(.17f, .12f, tick) * Band(.9125f, .013f) * .85f;
        float sector = th / turn * 8f; int group = Mathf.FloorToInt(sector) % 8;
        float arc = Mathf.Abs(sector - Mathf.Floor(sector) - .5f) * (turn / 8f) * r;
        for (int k = 0; k < 3; k++)
        {
            float bar = Band(.685f + k * .058f, .017f) * Smooth(.125f + aa, .125f - aa, arc);
            if ((Trigram[group] & (1 << k)) == 0) bar *= Smooth(.022f - aa, .022f + aa, arc);
            a += bar;
        }
        float dotArc = Mathf.Abs(Mathf.Repeat(th / turn * 32f, 1f) - .5f) * (turn / 32f) * r, dotR = r - .5875f;
        a += Smooth(.009f + aa, .009f - aa, Mathf.Sqrt(dotArc * dotArc + dotR * dotR)) * .9f;
        a += Mathf.Exp(-(r - .94f) * (r - .94f) * 900f) * .22f + Mathf.Exp(-(r - .61f) * (r - .61f) * 1500f) * .12f;
        return new Color(1f, 1f, 1f, Mathf.Clamp01(a * Smooth(1f, .985f, r)));
    }
    /// <summary>연기 덩어리 16종(4×4). R = 위에서 받은 빛, G = 짙은 정도, A = 모양.</summary>
    static Texture2D BakeSmoke()
    {
        const int cells = 4, cell = 256, size = cells * cell;
        var density = new float[size * size]; var fine = new float[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            int cx = x / cell, cy = y / cell; float seed = (cx + cy * cells) * 13.7f + 3f;
            float lx = ((x % cell) + .5f) / cell * 2f - 1f, ly = ((y % cell) + .5f) / cell * 2f - 1f, r = Mathf.Sqrt(lx * lx + ly * ly);
            float lump = Fbm(lx * 1.5f + seed, ly * 1.5f + seed * .7f, 4);
            float body = Smooth(1f, .25f, r + (lump - .5f) * 1.2f);
            float detail = Fbm(lx * 4.2f + seed * 1.3f, ly * 4.2f + 40f + seed, 4);
            density[y * size + x] = body * Mathf.Clamp01(.3f + 1.3f * detail) * Smooth(1f, .78f, r); fine[y * size + x] = detail;
        }
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            int i = y * size + x, above = Mathf.Min(y + 7, (y / cell) * cell + cell - 1);
            float d = density[i], shade = Mathf.Clamp01(.5f + (d - density[above * size + x]) * 3.2f + (fine[i] - .5f) * .5f);
            px[i] = new Color(shade, Mathf.Clamp01(d * 1.4f), 0f, Smooth(.05f, .62f, d));
        }
        return Finish(px, size, size, true, TextureWrapMode.Clamp);
    }
    /// <summary>오래된 문짝 나무: 세로로 긴 결, 넓은 나이테 띠, 갈라진 틈.</summary>
    static void BakeWood()
    {
        const int w = 512, h = 1024; var height = new float[w * h]; var px = new Color[w * h];
        Color dark = new Color(.1f, .065f, .045f), light = new Color(.33f, .215f, .135f), gap = new Color(.035f, .025f, .02f);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float u = (x + .5f) / w, v = (y + .5f) / h;
            float warp = Fbm(u * 2.5f + 5f, v * 1.2f + 2f, 3);
            float grain = Stretch(Fbm((u + warp * .06f) * 30f, v * 1.8f, 4)), fine = Fbm(u * 110f, v * 6f + 30f, 2);
            float band = .5f + .5f * Mathf.Sin((u * 9f + warp * 3.2f) * Mathf.PI * 2f);
            float tone = Mathf.Clamp01(.5f * grain + .12f * fine + .38f * band);
            float split = Smooth(.7f, .82f, Fbm(u * 70f + 11f, v * 1.1f + 7f, 3));
            px[y * w + x] = Color.Lerp(Color.Lerp(dark, light, tone), gap, split * .85f);
            height[y * w + x] = tone * .5f - split * .8f;
        }
        woodAlbedo = Finish(px, w, h, false, TextureWrapMode.Repeat); woodNormal = NormalMap(height, w, h, 3f);
    }
    /// <summary>두드려 만든 검은 쇠: 얽은 자국과 녹.</summary>
    static void BakeIron()
    {
        const int s = 512; var height = new float[s * s]; var px = new Color[s * s]; var mask = new Color[s * s];
        Color lo = new Color(.2f, .195f, .19f), hi = new Color(.38f, .37f, .36f), rustColor = new Color(.36f, .18f, .09f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float u = (x + .5f) / s, v = (y + .5f) / s;
            float n1 = Stretch(FbmUV(u, v, 5f, 4)), n2 = Stretch(FbmUV(u, v, 28f, 3));
            float pits = Smooth(.62f, .8f, FbmUV(Mathf.Repeat(u + .37f, 1f), Mathf.Repeat(v + .11f, 1f), 18f, 3));
            float rust = Smooth(.56f, .74f, FbmUV(Mathf.Repeat(u + .21f, 1f), Mathf.Repeat(v + .63f, 1f), 4f, 4)) * (.5f + .5f * n2);
            var c = Color.Lerp(Color.Lerp(lo, hi, n1 * .6f + n2 * .4f), rustColor, rust * .75f) * (1f - pits * .45f); c.a = 1f;
            px[y * s + x] = c; height[y * s + x] = n1 * .25f + n2 * .2f - pits * .7f + rust * .15f;
            mask[y * s + x] = new Color(Mathf.Lerp(.95f, .3f, rust), 0f, 0f, Mathf.Lerp(.58f, .2f, rust) * (1f - pits * .5f) * (.8f + .2f * n2));
        }
        ironAlbedo = Finish(px, s, s, false, TextureWrapMode.Repeat); ironNormal = NormalMap(height, s, s, 4f); ironMask = Finish(mask, s, s, true, TextureWrapMode.Repeat);
    }
    /// <summary>오래된 청동: 망치 자국, 파인 곳에 낀 푸른 녹.</summary>
    static void BakeBronze()
    {
        const int s = 512; var height = new float[s * s]; var px = new Color[s * s]; var mask = new Color[s * s];
        Color deep = new Color(.3f, .185f, .085f), body = new Color(.62f, .42f, .19f), bright = new Color(.84f, .63f, .32f), green = new Color(.25f, .42f, .36f);
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
        {
            float u = (x + .5f) / s, v = (y + .5f) / s;
            float dimple = Smooth(0f, .55f, Worley(u, v, 10, 7));
            float n = Stretch(FbmUV(u, v, 6f, 4)), fine = Stretch(FbmUV(u, v, 40f, 2));
            float patina = Smooth(.52f, .75f, FbmUV(Mathf.Repeat(u + .4f, 1f), Mathf.Repeat(v + .7f, 1f), 5f, 4)) * (.4f + .6f * (1f - dimple));
            var c = Color.Lerp(Color.Lerp(deep, body, n), bright, dimple * .55f); c = Color.Lerp(c, green, patina * .6f); c.a = 1f;
            px[y * s + x] = c; height[y * s + x] = dimple * .5f + n * .15f + fine * .05f;
            mask[y * s + x] = new Color(Mathf.Lerp(1f, .35f, patina), 0f, 0f, Mathf.Lerp(.72f, .25f, patina) * (.85f + .15f * fine));
        }
        bronzeAlbedo = Finish(px, s, s, false, TextureWrapMode.Repeat); bronzeNormal = NormalMap(height, s, s, 3.5f); bronzeMask = Finish(mask, s, s, true, TextureWrapMode.Repeat);
    }

    // ───────────── 재질 ─────────────

    static Material RibbonMaterial(Texture texture, Color color, float erode, float hot, float soft, Vector4 flow)
    {
        var m = new Material(ribbonShader);
        m.SetTexture("_MainTex", texture); m.SetTexture("_NoiseTex", texNoise); m.SetColor("_Color", color); m.SetFloat("_Intensity", 1f);
        m.SetFloat("_Erode", erode); m.SetFloat("_Hot", hot); m.SetFloat("_Soft", soft); m.SetVector("_Flow", flow);
        return m;
    }
    /// <summary>명암 없이 한 색으로 칠하는 알파 재질(검은 그림자, 갈라진 틈).</summary>
    static Material FlatMaterial(Texture texture, Color color, float shadeFloor)
    {
        var m = new Material(smokeShader);
        m.SetTexture("_MainTex", texture); m.SetColor("_Color", color); m.SetColor("_Glow", Color.black); m.SetFloat("_Shade", shadeFloor); m.SetFloat("_Soft", 0f);
        return m;
    }
    static Material Lit(Texture2D albedo, Texture2D normal, Texture2D mask, Color tint, float metallic, float smoothness, Vector2 tile, Vector2 offset, Color emission = default, float bump = 1f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", tint); m.SetTexture("_BaseMap", albedo); m.SetTextureScale("_BaseMap", tile); m.SetTextureOffset("_BaseMap", offset);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
        if (mask != null) { m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); }
        else { m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness); }
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        return m;
    }
    /// <summary>문짝 널빤지 한 장. 널빤지마다 결이 다른 자리를 쓴다.</summary>
    static Material WoodMaterial(float shade) => Lit(woodAlbedo, woodNormal, null, new Color(shade, shade, shade), 0f, .2f, new Vector2(.19f, 1f), new Vector2(Rand(0f, .8f), 0f), default, 1.1f);   // 나무결 그림은 이어지지 않으므로 가장자리를 넘지 않게 고른다
    static Material BronzeMaterial(Vector2 tile, Color emission = default) => Lit(bronzeAlbedo, bronzeNormal, bronzeMask, new Color(.74f, .68f, .62f), 1f, 1f, tile, new Vector2(Rand(0f, 1f), Rand(0f, 1f)), emission, 1.2f);
    static Material IronMaterial(Vector2 tile, Color emission = default) => Lit(ironAlbedo, ironNormal, ironMask, Color.white, 1f, 1f, tile, new Vector2(Rand(0f, 1f), Rand(0f, 1f)), emission, 1.3f);
    static Material RopeMaterial()
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", texRope); m.SetColor("_BaseColor", Color.white); m.SetTextureScale("_BaseMap", new Vector2(6f, 1f));
        return m;
    }
    /// <summary>가장자리가 빛나는 기운 몸체(잔상, 종 모양 기운). 게임의 기운 구슬 셰이더를 빌려 쓴다.</summary>
    static Material OrbMaterial(Color core, Color rim, float rimPower)
    {
        var m = new Material(Shader.Find("VisionStriker/QiOrb"));
        m.SetTexture("_NoiseTex", texNoise); m.SetColor("_CoreColor", core); m.SetColor("_RimColor", rim); m.SetFloat("_RimPower", rimPower);
        m.SetFloat("_NoiseScale", 2.4f); m.SetFloat("_Wobble", 0f); m.SetFloat("_Alpha", 1f);
        return m;
    }
    static Material SparkMaterial()
    {
        if (sparkMats.TryGetValue(spec.key, out var m)) return m;
        m = RibbonMaterial(assets.glow != null ? (Texture)assets.glow : Texture2D.whiteTexture, Hdr(Color.Lerp(spec.tint, Color.white, .25f), 3f), 0f, .7f, 0f, Vector4.zero);
        return sparkMats[spec.key] = m;
    }

    // ───────────── 도형 ─────────────

    /// <summary>회전체. profile 은 (높이, 반지름)을 아래에서 위로. 바깥쪽이 앞면이다.</summary>
    static GameObject Lathe(Vector3 basePosition, Vector2[] profile, Material material, int segments = 44, float tileU = 1f, float tileV = 1f)
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
        return MeshObject("Preview lathe", basePosition, vertices, normals, uvs, triangles, material);
    }
    /// <summary>고리(도넛). 축은 y 방향이다.</summary>
    static GameObject Torus(Vector3 position, float major, float minor, Material material, int segments = 40, int sides = 12, float tileU = 6f)
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
        return MeshObject("Preview torus", position, vertices, normals, uvs, triangles, material);
    }
    static GameObject MeshObject(string name, Vector3 position, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles, Material material)
    {
        var mesh = new Mesh { vertices = vertices, normals = normals, uv = uvs, triangles = triangles }; mesh.RecalculateBounds(); mesh.RecalculateTangents();
        var go = new GameObject(name); go.transform.position = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        bool lit = material != null && material.shader.name.EndsWith("/Lit");
        renderer.shadowCastingMode = lit ? ShadowCastingMode.On : ShadowCastingMode.Off; renderer.receiveShadows = lit;
        return go;
    }
    static GameObject Quad(Vector3 position, Quaternion rotation, float size, Material material)
    {
        var go = Part(null, PrimitiveType.Quad, position, Vector3.one * size, material); go.transform.rotation = rotation;
        var renderer = go.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return go;
    }

    // ───────────── 불티·물방울·충격파 ─────────────

    /// <summary>튀는 불티(쇠가 부딪칠 때), 물방울, 돌가루. 날아가는 방향으로 길게 늘어나고 떨어진다.</summary>
    static void Sparks(Vector3 position, Vector3 direction, int count, float speed, float life = .7f, float spread = 55f, float gravity = 1.6f, float size = .05f)
    {
        var go = new GameObject("Preview sparks");
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
        ps.Play(); Object.Destroy(go, life + .6f);
    }
    /// <summary>천천히 떠오르는 불씨·기운 알갱이.</summary>
    static void Embers(Vector3 center, Vector3 box, int count, float rise, float life, float size = .045f)
    {
        var go = new GameObject("Preview embers"); go.transform.position = center;
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
        ps.Play(); Object.Destroy(go, life + .6f);
    }
    /// <summary>바닥을 타고 퍼지는 충격파 고리.</summary>
    static void Shockwave(Vector3 center, float fromRadius, float toRadius, float seconds, float intensity = 2f)
    {
        var m = RibbonMaterial(texShock, Hdr(spec.tint, intensity), 0f, .5f, 0f, Vector4.zero);
        var quad = Quad(new Vector3(center.x, center.y + .035f, center.z), Quaternion.LookRotation(Vector3.down, Vector3.forward), fromRadius * 2f / .92f, m);
        Tween(seconds, u =>
        {
            if (!quad) return;
            quad.transform.localScale = Vector3.one * (Mathf.Lerp(fromRadius, toRadius, 1f - (1f - u) * (1f - u)) * 2f / .92f);
            m.SetFloat("_Intensity", 1f - u * u);
        }, () => { if (quad) Object.Destroy(quad); });
    }
}
