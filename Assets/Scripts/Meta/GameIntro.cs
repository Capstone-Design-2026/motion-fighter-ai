using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 시작 연출(2026-11-16 시안 3종). BootScene(빌드 첫 장면)에 하나 있다.
/// 연출을 보여 주는 동안 시작 화면을 뒤에서 불러오고, 마지막에 시작 화면 위에서 연출을 걷어 낸다.
/// 그래서 예전처럼 로고 뒤에 빈 화면이 2초쯤 남지 않는다. 아무 키나 누르면 바로 걷어 낸다.
///
///   Clouds : 먹구름 속에서 금빛 붓이 黑雲峽 을 쓰고, 붉은 낙관이 찍힌 뒤 구름이 좌우로 갈라진다.
///   Slash  : 어두운 화면을 금빛 검기가 가르고, 제목이 뜬 뒤 화면이 그 선을 따라 두 쪽으로 밀려난다.
///   Ink    : 한지에 먹물 한 방울이 떨어져 번지고, 먹빛 속에서 제목이 금색으로 피어난 뒤 밝아진다.
///   Drop   : (기본, 2026-11-16 사용자 선택) 한지에 먹 한 방울이 떨어지면 번지는 먹 자국 안으로 시작 화면이 바로 드러난다.
///
/// 고르기: 명령줄 --redbool-intro drop|clouds|slash|ink|none (없으면 DefaultStyle).
/// 다른 명령줄 실행(--redbool-pvp, --redbool-duel-test, 카메라 점검 등)과 배치 모드에서는 연출 없이 바로 넘어간다.
/// 그림은 Resources/UI/Intro (Tools/Developer/make_intro_art.py), 제목은 Resources/UI/Wuxia/HeukUnHyeopTitle.
/// </summary>
public sealed class GameIntro : MonoBehaviour
{
    public enum Style { Clouds, Slash, Ink, Drop, None }
    public const string SceneName = "BootScene";
    public static Style DefaultStyle = Style.Drop;

    static readonly Color InkBlack = new Color(.043f, .035f, .035f, 1f);
    const float TitleWidth = 1120f;

    RectTransform root;
    CanvasGroup group;
    AudioSource voice;
    QiPalmAssets sounds;
    Texture2D title;
    AsyncOperation loading;
    bool skip;

    public static Style Chosen(out bool otherCommandLine)
    {
        var args = Environment.GetCommandLineArgs();
        otherCommandLine = false;
        var style = DefaultStyle;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--redbool-intro")
            {
                if (i + 1 < args.Length && Enum.TryParse(args[i + 1], true, out Style chosen)) style = chosen;
                i++;
            }
            else if (args[i].StartsWith("--redbool-", StringComparison.Ordinal)) otherCommandLine = true;
        }
        return style;
    }

    IEnumerator Start()
    {
        var style = Chosen(out bool commandLine);
        if (commandLine || Application.isBatchMode || style == Style.None)
        {
            // PvP·빠른 대전은 같은 프레임에 스스로 장면을 불러온다. 그 밖에는 바로 시작 화면.
            yield return null;
            if (!RedboolPvp.Active && !RedboolDuelTest.Active && SceneManager.GetActiveScene().name == SceneName)
                SceneManager.LoadScene(SceneRoutes.Start);
            Destroy(gameObject);
            yield break;
        }
        DontDestroyOnLoad(gameObject);
        Build();
        Debug.Log("[REDBOOL INTRO] style=" + style);
        yield return null;
        loading = SceneManager.LoadSceneAsync(SceneRoutes.Start);
        loading.allowSceneActivation = false;

        switch (style)
        {
            case Style.Clouds: yield return Clouds(); break;
            case Style.Slash: yield return Slash(); break;
            case Style.Drop: yield return Drop(); break;
            default: yield return Ink(); break;
        }
        Destroy(gameObject);
    }

    void Update() { if (!skip && (Input.anyKeyDown || Input.GetMouseButtonDown(0))) skip = true; }

    // 연출 앞부분이 끝나면 시작 화면을 켠다. 켜는 첫 프레임이 무거워 잠깐 멈추는데, 그동안 연출의 마지막 그림이 덮고 있다.
    IEnumerator OpenStartScene(float minimumHold)
    {
        float held = 0f;
        while ((held < minimumHold && !skip) || loading.progress < .9f) { held += Time.unscaledDeltaTime; yield return null; }
        loading.allowSceneActivation = true;
        while (SceneManager.GetActiveScene().name != SceneRoutes.Start) yield return null;
        yield return null; yield return null;
    }

    // ─────────────────────── 1. 먹구름 + 붓글씨 + 낙관 ───────────────────────

    IEnumerator Clouds()
    {
        var back = Fill("InkBack", InkBlack);
        var left = Picture("CloudLeft", Tex("CloudLeft"), new Vector2(1500, 1290), new Vector2(-470, 0));
        var right = Picture("CloudRight", Tex("CloudRight"), new Vector2(1500, 1290), new Vector2(470, 0));
        var titleRoot = Rect("Title", root, new Vector2(TitleWidth, TitleWidth * title.height / title.width), new Vector2(0, 40));
        var chars = TitleCharacters(titleRoot, Image.FillMethod.Vertical);
        var tip = Picture("BrushTip", Tex("Glow"), new Vector2(150, 150), Vector2.zero, titleRoot, new Color(1f, .82f, .45f, 0f));
        var seal = Picture("Seal", Tex("Seal"), new Vector2(118, 249), new Vector2(TitleWidth * .5f + 40, -60), titleRoot, new Color(1, 1, 1, 0));

        // 구름이 천천히 숨 쉬듯 흐른다.
        float t = 0f;
        Action drift = () =>
        {
            float s = Mathf.Sin(Time.unscaledTime * .8f) * 14f;
            left.rectTransform.anchoredPosition = new Vector2(-470 + s, 0);
            right.rectTransform.anchoredPosition = new Vector2(470 - s, 0);
        };
        // 黑 → 雲 → 峽 을 위에서 아래로 써 내려간다(획이 지나간 자리를 금빛 붓끝이 따라간다).
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i]; float dur = .42f; t = 0f;
            var rect = c.rectTransform;
            while (t < dur && !skip)
            {
                t += Time.unscaledDeltaTime; float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / dur));
                c.fillAmount = u;
                tip.color = new Color(1f, .82f, .45f, .9f * Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI));
                tip.rectTransform.anchoredPosition = rect.anchoredPosition + new Vector2(0, rect.sizeDelta.y * (.5f - u));
                drift(); yield return null;
            }
            c.fillAmount = 1f;
        }
        tip.color = new Color(1, 1, 1, 0);
        foreach (var c in chars) c.fillAmount = 1f;

        // 낙관이 쾅 찍히며 화면이 살짝 흔들린다.
        Play(sounds != null ? sounds.impactClip : null, .8f);
        t = 0f;
        while (t < .5f)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / .12f);
            seal.color = new Color(1, 1, 1, u);
            seal.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.9f, 1f, 1 - (1 - u) * (1 - u));
            float shake = t < .3f ? (1 - t / .3f) * 9f : 0f;
            root.anchoredPosition = new Vector2(Mathf.Sin(t * 90f) * shake, Mathf.Cos(t * 77f) * shake);
            drift(); yield return null;
        }
        root.anchoredPosition = Vector2.zero;

        yield return OpenStartScene(.7f);

        // 구름이 좌우로 갈라지며 시작 화면이 드러난다.
        t = 0f; const float reveal = 1.15f;
        while (t < reveal)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / reveal);
            float e = u * u * (3 - 2 * u);
            left.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-470, -1750, e), 0);
            right.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(470, 1750, e), 0);
            back.color = new Color(InkBlack.r, InkBlack.g, InkBlack.b, 1 - Mathf.Clamp01((u - .05f) / .55f));
            float fade = 1 - Mathf.Clamp01(u / .55f);
            foreach (var c in chars) c.color = new Color(1, 1, 1, fade);
            seal.color = new Color(1, 1, 1, fade);
            titleRoot.localScale = Vector3.one * Mathf.Lerp(1f, 1.12f, e);
            yield return null;
        }
    }

    // ─────────────────────── 2. 검기로 가르기 ───────────────────────

    IEnumerator Slash()
    {
        const float angle = -24f;
        var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        var normal = new Vector2(-dir.y, dir.x);
        // 화면을 선 위·아래 두 쪽으로 나눠 그린다. 처음에는 두 쪽이 맞붙어 한 화면처럼 보인다.
        var halves = new RectTransform[2];
        var titles = new RawImage[2];
        for (int i = 0; i < 2; i++)
        {
            var half = Rect(i == 0 ? "UpperHalf" : "LowerHalf", root, new Vector2(6000, 3000), Vector2.zero);
            half.pivot = new Vector2(.5f, i == 0 ? 0f : 1f);
            half.anchoredPosition = Vector2.zero;
            half.localRotation = Quaternion.Euler(0, 0, angle);
            var maskImage = half.gameObject.AddComponent<Image>(); maskImage.raycastTarget = false;
            half.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect("Content", half, new Vector2(2600, 1700), Vector2.zero);
            content.anchorMin = content.anchorMax = half.pivot;
            content.anchoredPosition = Vector2.zero;
            content.localRotation = Quaternion.Euler(0, 0, -angle);
            Picture("Back", Texture2D.whiteTexture, new Vector2(2600, 1700), Vector2.zero, content, InkBlack);
            Picture("CloudL", Tex("CloudLeft"), new Vector2(1500, 1290), new Vector2(-620, 0), content, new Color(1, 1, 1, .55f));
            Picture("CloudR", Tex("CloudRight"), new Vector2(1500, 1290), new Vector2(620, 0), content, new Color(1, 1, 1, .55f));
            titles[i] = Picture("Title", title, new Vector2(TitleWidth, TitleWidth * title.height / title.width), new Vector2(0, 30), content, new Color(1, 1, 1, 0));
            halves[i] = half;
        }
        var streak = Picture("Slash", Tex("Slash"), new Vector2(2600, 74), Vector2.zero, root, Color.white);
        var streakRect = streak.rectTransform;
        streakRect.pivot = new Vector2(0f, .5f);
        streakRect.localRotation = Quaternion.Euler(0, 0, angle);
        streakRect.anchoredPosition = -dir * 1300f;
        streakRect.localScale = new Vector3(0, 1, 1);
        var flash = Fill("Flash", new Color(1f, .95f, .85f, 0f));

        float t = 0f;
        while (t < .25f && !skip) { t += Time.unscaledDeltaTime; yield return null; }
        // 검기가 대각선으로 지나간다.
        Play(sounds != null ? sounds.release : null, .9f);
        t = 0f;
        while (t < .14f) { t += Time.unscaledDeltaTime; streakRect.localScale = new Vector3(Mathf.Clamp01(t / .14f), 1.6f, 1); yield return null; }
        streakRect.localScale = new Vector3(1, 1.6f, 1);
        // 번쩍인 뒤 제목이 뜬다.
        t = 0f;
        while (t < .55f)
        {
            t += Time.unscaledDeltaTime;
            flash.color = new Color(1f, .95f, .85f, .75f * (1 - Mathf.Clamp01(t / .3f)));
            float a = Mathf.Clamp01((t - .1f) / .25f);
            foreach (var copy in titles) { copy.color = new Color(1, 1, 1, a); copy.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, a); }
            streakRect.localScale = new Vector3(1, Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(t / .4f)), 1);
            streak.color = new Color(1, 1, 1, Mathf.Lerp(1f, .55f, Mathf.Clamp01(t / .5f)));
            yield return null;
        }
        flash.color = new Color(1, 1, 1, 0);

        yield return OpenStartScene(.9f);

        // 화면이 검기 선을 따라 두 쪽으로 밀려나며 시작 화면이 드러난다.
        Play(sounds != null ? sounds.impactClip : null, .45f);
        t = 0f; const float reveal = .8f;
        while (t < reveal)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / reveal); float e = u * u;
            halves[0].anchoredPosition = normal * Mathf.Lerp(0, 1300, e);
            halves[1].anchoredPosition = -normal * Mathf.Lerp(0, 1300, e);
            streak.color = new Color(1, 1, 1, .55f * (1 - Mathf.Clamp01(u / .4f)));
            yield return null;
        }
    }

    // ─────────────────────── 3. 먹 한 방울 ───────────────────────

    IEnumerator Ink()
    {
        Picture("Paper", Tex("Paper"), new Vector2(1920, 1080), Vector2.zero);
        var drop = Picture("Drop", Tex("InkDrop"), new Vector2(46, 86), new Vector2(0, 640));
        // 번진 먹 자국은 별 모양이라 화면 모서리에 한지가 남는다. 번지는 뒤쪽 절반 동안 먹빛을 화면 전체에 함께 깐다.
        var wash = Fill("InkWash", new Color(InkBlack.r, InkBlack.g, InkBlack.b, 0f));
        var blot = Picture("Blot", Tex("InkBlot"), new Vector2(1400, 1400), Vector2.zero, root, Color.white);
        blot.rectTransform.localScale = Vector3.zero;
        var titleImage = Picture("Title", title, new Vector2(TitleWidth, TitleWidth * title.height / title.width), new Vector2(0, 30), root, new Color(.08f, .07f, .07f, 0));

        float t = 0f;
        while (t < .3f && !skip) { t += Time.unscaledDeltaTime; yield return null; }
        // 방울이 떨어진다(점점 빨라지며 길게 늘어난다).
        t = 0f;
        while (t < .42f && !skip)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / .42f);
            drop.rectTransform.anchoredPosition = new Vector2(0, Mathf.Lerp(640, 0, u * u));
            drop.rectTransform.localScale = new Vector3(1f - u * .15f, 1f + u * .5f, 1);
            yield return null;
        }
        drop.color = new Color(1, 1, 1, 0);
        Play(sounds != null ? sounds.readyClip : null, .7f);
        // 한지에 번져 화면 전체가 먹빛이 된다.
        t = 0f;
        while (t < 1.15f && !skip)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / 1.15f);
            float e = 1 - Mathf.Pow(1 - u, 3);
            blot.rectTransform.localScale = Vector3.one * Mathf.Lerp(.04f, 3.4f, e);
            blot.rectTransform.localRotation = Quaternion.Euler(0, 0, e * 14f);
            wash.color = new Color(InkBlack.r, InkBlack.g, InkBlack.b, Mathf.Clamp01((u - .45f) / .45f));
            yield return null;
        }
        blot.rectTransform.localScale = Vector3.one * 3.4f;
        wash.color = InkBlack;
        // 먹빛 속에서 제목이 금색으로 피어난다.
        t = 0f;
        while (t < 1.0f && !skip)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 1.0f));
            titleImage.color = Color.Lerp(new Color(.08f, .07f, .07f, 0), Color.white, u);
            titleImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, u);
            yield return null;
        }
        titleImage.color = Color.white; titleImage.rectTransform.localScale = Vector3.one;

        yield return OpenStartScene(.7f);

        t = 0f; const float reveal = 1.0f;
        while (t < reveal)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / reveal);
            group.alpha = 1 - u * u * (3 - 2 * u);
            titleImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.08f, u);
            yield return null;
        }
    }

    // ─────────────────────── 4. 먹 한 방울 → 시작 화면 ───────────────────────

    IEnumerator Drop()
    {
        // 한지가 화면을 덮고 있다가, 먹 자국 모양의 구멍(IntroHole 셰이더)이 번지며 뒤의 시작 화면을 드러낸다.
        var paper = Picture("Paper", Tex("Paper"), Vector2.zero, Vector2.zero);
        var rect = paper.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
        var shader = Shader.Find("REDBOOL/IntroHole");
        Material hole = shader != null ? new Material(shader) : null;
        if (hole != null)
        {
            hole.SetTexture("_MaskTex", Tex("InkBlot"));
            hole.SetVector("_Hole", new Vector4(.5f, .53f, 0f, 0f));
            hole.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f);
            paper.material = hole;
        }
        var drop = Picture("Drop", Tex("InkDrop"), new Vector2(46, 86), new Vector2(0, 470));
        drop.rectTransform.localScale = Vector3.zero;
        var splat = Picture("Splat", Tex("InkBlot"), new Vector2(1400, 1400), new Vector2(0, 1080 * .03f), root, Color.white);
        splat.rectTransform.localScale = Vector3.zero;

        // 시작 화면을 불러오는 동안 붓끝에 먹 방울이 맺힌다.
        float t = 0f;
        while (t < .55f || loading.progress < .9f)
        {
            t += Time.unscaledDeltaTime;
            float grow = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .5f));
            float wobble = 1f + Mathf.Sin(t * 9f) * .04f * grow;
            drop.rectTransform.localScale = new Vector3(grow / wobble, grow * wobble, 1);
            if (skip && t > .1f) break;
            yield return null;
        }
        yield return OpenStartScene(0f);

        // 떨어진다(점점 빨라지며 길게 늘어난다).
        t = 0f;
        while (t < .26f && !skip)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / .26f);
            drop.rectTransform.anchoredPosition = new Vector2(0, Mathf.Lerp(470, 1080 * .03f, u * u));
            drop.rectTransform.localScale = new Vector3(1f - u * .2f, 1f + u * .6f, 1);
            yield return null;
        }
        drop.color = new Color(1, 1, 1, 0);
        Play(sounds != null ? sounds.readyClip : null, .7f);
        // 먹이 톡 퍼지고, 그 자국 안으로 시작 화면이 쓱 번져 나온다.
        t = 0f;
        while (t < .09f)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / .09f);
            splat.rectTransform.localScale = Vector3.one * Mathf.Lerp(.02f, .14f, u);
            yield return null;
        }
        t = 0f; const float spread = .9f;
        while (t < spread)
        {
            t += Time.unscaledDeltaTime; float u = Mathf.Clamp01(t / spread);
            float e = 1 - Mathf.Pow(1 - u, 3);
            float scale = Mathf.Lerp(.12f, 3.7f, e);                       // 화면 높이 기준 구멍 크기
            if (hole != null) hole.SetVector("_Hole", new Vector4(.5f, .53f, scale, e * .35f));
            else group.alpha = 1 - e;
            splat.color = new Color(1, 1, 1, 1 - Mathf.Clamp01(u / .25f));
            splat.rectTransform.localScale = Vector3.one * Mathf.Lerp(.14f, .5f, e);
            yield return null;
        }
        if (hole != null) Destroy(hole);
    }

    // ─────────────────────── 화면 만들기 ───────────────────────

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true; group.interactable = false;
        root = Rect("Stage", transform, Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
        title = Resources.Load<Texture2D>("UI/Wuxia/HeukUnHyeopTitle");
        sounds = QiPalmAssets.Load();
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 0f;
    }

    void Play(AudioClip clip, float volume) { if (clip != null) voice.PlayOneShot(clip, volume * RedboolDemoSettings.MasterVolume); }

    static Texture2D Tex(string name) => Resources.Load<Texture2D>("UI/Intro/" + name);

    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        return rect;
    }

    RawImage Picture(string name, Texture texture, Vector2 size, Vector2 position, Transform parent = null, Color? color = null)
    {
        var rect = Rect(name, parent != null ? parent : root, size, position);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false;
        image.color = color ?? Color.white;
        return image;
    }

    RawImage Fill(string name, Color color)
    {
        var image = Picture(name, Texture2D.whiteTexture, Vector2.zero, Vector2.zero, null, color);
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = new Vector2(400, 400);
        return image;
    }

    // 제목 그림을 글자 세 칸(黑 / 雲 / 峽)으로 나눠 하나씩 채워 그릴 수 있게 한다.
    Image[] TitleCharacters(RectTransform parent, Image.FillMethod method)
    {
        float[] cuts = { 0f, 648f, 1196f, title.width };
        float scale = parent.sizeDelta.x / title.width;
        var result = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            float x0 = cuts[i], w = cuts[i + 1] - cuts[i];
            var sprite = Sprite.Create(title, new Rect(x0, 0, w, title.height), new Vector2(.5f, .5f), 100f);
            var rect = Rect("Char" + i, parent, new Vector2(w * scale, title.height * scale), new Vector2((x0 + w * .5f - title.width * .5f) * scale, 0));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.raycastTarget = false;
            image.type = Image.Type.Filled; image.fillMethod = method;
            image.fillOrigin = (int)Image.OriginVertical.Top; image.fillAmount = 0f;
            result[i] = image;
        }
        return result;
    }
}
