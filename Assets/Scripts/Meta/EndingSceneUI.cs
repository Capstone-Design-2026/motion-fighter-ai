using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 엔딩 크레딧. 최종 보스 승리 뒤에 나오고, 시작 화면의 '제작진'으로도 볼 수 있다.
/// 구성(다른 게임들의 크레딧을 참고):
///   제목 → 만든 팀 → 팀원(이름 한 번 + 맡은 일) → 출처 · 도구 → 안내 문구 → 제목과 감사 인사
///   왼쪽에는 등장인물 초상이 차례로 지나가고, 오른쪽으로 크레딧이 올라간다.
/// 내용은 RunConfig.creditsText 에서 편집한다. 줄 머리 표시:
///   [logo]   흑운협 제목 그림
///   # 분야    금색 머리말(작은 글씨 + 금색 선)
///   ## 이름   크게 쓰는 이름
///   = 직함    이름 아래 금색 직함
///   - 설명    작은 회색 글씨(맡은 일, 출처)
///   ---      먹 구름 구분 장식
///   그 밖    가운데 정렬 일반 문장(빈 줄은 간격)
/// 엔딩 음악은 RunConfig.endingMusic.
/// </summary>
public class EndingSceneUI : MonoBehaviour
{
    const float Speed = 66f;          // 기준 해상도(1080p)에서 초당 올라가는 거리
    const float EndCardTime = 6f;     // 마지막 제목 화면을 보여 주는 시간
    const float CastSeconds = 4.2f;   // 등장인물 한 명을 보여 주는 최소 시간
    static readonly Color Gold = new Color(.86f, .69f, .40f);
    static readonly Color Name = new Color(.96f, .93f, .86f);
    static readonly Color Note = new Color(.70f, .72f, .76f);
    static readonly string[] CastOrder = { "Seojin", "Doyun", "Yeonhwa", "Herbalist", "Physician",
        "Enemy01", "Enemy02", "Enemy03", "Enemy04", "Enemy05", "Enemy06", "Enemy07", "Enemy08", "Enemy09", "Enemy10", "Enemy11" };

    /// <summary>시작 화면의 '제작진'으로 들어왔는지. 그때는 진행 중인 판을 지우지 않는다.</summary>
    public static bool OpenedFromMenu;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { OpenedFromMenu = false; }

    RectTransform roll, viewport, backdropRect, castRect;
    CanvasGroup endCard, rollGroup;
    RawImage castImage;
    Text castName, castLabel;
    readonly List<StoryActorDefinition> cast = new List<StoryActorDefinition>();
    int castIndex = -1;
    AudioSource music;
    float elapsed, travel;
    bool leaving, ready, fromMenu;

    IEnumerator Start()
    {
        Time.timeScale = 1f;
        fromMenu = OpenedFromMenu; OpenedFromMenu = false;
        UIFactory.EnsureEventSystem();
        var run = RunState.Instance;
        var config = run != null ? run.Config : null;
        var canvas = UIFactory.CreateCanvas("EndingCanvas", transform, 200);

        // 배경: 어두운 먹색 위에 지도 그림을 흐리게 깔고 아주 천천히 당긴다.
        var bg = UIFactory.CreateImage("Background", canvas.transform, new Color(.018f, .02f, .03f), true);
        UIFactory.Stretch(bg.rectTransform);
        var map = Resources.Load<Texture2D>("UI/Wuxia/JourneyWorldMap");
        if (map != null)
        {
            var backdrop = new GameObject("Backdrop", typeof(RectTransform)).AddComponent<RawImage>();
            backdrop.transform.SetParent(canvas.transform, false);
            backdrop.texture = map; backdrop.color = new Color(.30f, .28f, .26f, 1f); backdrop.raycastTarget = false;
            backdropRect = backdrop.rectTransform;
            backdropRect.anchorMin = backdropRect.anchorMax = new Vector2(.5f, .5f);
            var fit = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = map.width / (float)map.height;
        }
        var shade = UIFactory.CreateImage("Shade", canvas.transform, new Color(0f, 0f, 0f, .38f));
        UIFactory.Stretch(shade.rectTransform);

        // 왼쪽: 등장인물 초상. 위아래 어둠(Edge)보다 먼저 만들어 발치가 어둠에 묻히게 한다.
        foreach (var id in CastOrder)
        {
            var actor = Resources.Load<StoryActorDefinition>("Story/Actors/" + id);
            if (actor != null && actor.artwork != null) cast.Add(actor);
        }
        if (cast.Count > 0)
        {
            castImage = new GameObject("CastPortrait", typeof(RectTransform)).AddComponent<RawImage>();
            castImage.transform.SetParent(canvas.transform, false); castImage.raycastTarget = false; castImage.color = Color.clear;
            castRect = castImage.rectTransform;
            castRect.anchorMin = castRect.anchorMax = new Vector2(.22f, 0f); castRect.pivot = new Vector2(.5f, 0f);
        }
        Edge(canvas.transform, "TopFade", true); Edge(canvas.transform, "BottomFade", false);
        if (cast.Count > 0)
        {
            castLabel = UIFactory.CreateText("CastLabel", canvas.transform, "등장인물", 20, TextAnchor.MiddleCenter, Color.clear);
            castLabel.font = WuxiaTheme.MenuFont;
            UIFactory.Place(castLabel.rectTransform, new Vector2(.22f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 128f), new Vector2(600f, 30f));
            castName = UIFactory.CreateText("CastName", canvas.transform, "", 42, TextAnchor.MiddleCenter, Color.clear);
            castName.font = WuxiaTheme.MenuFont;
            var castShadow = castName.gameObject.AddComponent<Shadow>(); castShadow.effectColor = new Color(0f, 0f, 0f, .8f); castShadow.effectDistance = new Vector2(2f, -2f);
            UIFactory.Place(castName.rectTransform, new Vector2(.22f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 68f), new Vector2(600f, 60f));
        }

        // 오른쪽: 올라가는 크레딧. 초상이 없으면 화면 전체를 쓴다.
        viewport = UIFactory.CreateRect("CreditsViewport", canvas.transform);
        viewport.anchorMin = new Vector2(cast.Count > 0 ? .42f : 0f, 0f); viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();
        roll = UIFactory.CreateRect("CreditsRoll", viewport);
        roll.anchorMin = new Vector2(0f, 0f); roll.anchorMax = new Vector2(1f, 0f); roll.pivot = new Vector2(.5f, 1f);
        roll.anchoredPosition = Vector2.zero;
        rollGroup = roll.gameObject.AddComponent<CanvasGroup>(); rollGroup.blocksRaycasts = false;

        string content = config != null && !string.IsNullOrWhiteSpace(config.creditsText) ? config.creditsText : "# 만든 사람들\n## 팀 RedBool";
        if (config != null && !string.IsNullOrWhiteSpace(config.creditsAttributions))
            content += "\n---\n# 음악 · 효과음 · 외부 에셋\n" + config.creditsAttributions;
        var items = Build(content);

        endCard = BuildEndCard(canvas.transform, config != null ? config.endingTitle : "흑운협", config != null ? config.creditsCopyright : "");

        var button = UIFactory.CreateButton("ReturnToStart", canvas.transform, fromMenu ? "돌아가기  Esc" : "건너뛰기  Esc", 22, new Color(.08f, .09f, .12f, .75f), Finish);
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 30f), new Vector2(220f, 52f));
        var label = button.GetComponentInChildren<Text>(); if (label != null) { label.font = WuxiaTheme.Font; label.color = Note; }

        if (config != null && config.endingMusic != null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.clip = config.endingMusic; music.loop = true; music.volume = 0f; music.playOnAwake = false; music.Play();
        }

        // CanvasScaler 와 viewport 크기가 정해진 다음 줄 높이를 계산해 쌓는다.
        yield return null;
        Canvas.ForceUpdateCanvases();
        float y = 0f;   // 맨 위(제목 그림)부터 화면 아래에서 올라온다
        foreach (var item in items)
        {
            y += item.gapBefore;
            float h = item.height > 0f ? item.height : (item.text != null ? item.text.preferredHeight + 6f : 0f);
            item.rect.anchoredPosition = new Vector2(0f, -y);
            if (item.text != null) item.rect.sizeDelta = new Vector2(item.rect.sizeDelta.x, h);
            y += h;
        }
        roll.sizeDelta = new Vector2(0f, y + 40f);
        travel = viewport.rect.height + roll.rect.height;
        Canvas.ForceUpdateCanvases();
        ready = true;
    }

    // ── 내용 만들기 ──────────────────────────────────────────────

    sealed class Item { public RectTransform rect; public Text text; public float gapBefore, height; }

    List<Item> Build(string content)
    {
        var items = new List<Item>();
        var logo = Resources.Load<Texture2D>("UI/Wuxia/HeukUnHyeopTitle");
        string previous = "";
        foreach (var raw in content.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line == "[logo]")
            {
                if (logo == null) continue;
                var img = new GameObject("Logo", typeof(RectTransform)).AddComponent<RawImage>();
                img.transform.SetParent(roll, false); img.texture = logo; img.raycastTarget = false;
                float w = 700f, h = w * logo.height / logo.width;
                var r = img.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1f); r.pivot = new Vector2(.5f, 1f); r.sizeDelta = new Vector2(w, h);
                items.Add(new Item { rect = r, height = h, gapBefore = 20f });
            }
            else if (line == "---")
            {
                var orn = WuxiaTheme.Ornament("Divider", roll, WuxiaGraphic.Shape.Cloud, new Color(Gold.r, Gold.g, Gold.b, .55f));
                var r = orn.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1f); r.pivot = new Vector2(.5f, 1f); r.sizeDelta = new Vector2(150f, 40f);
                items.Add(new Item { rect = r, height = 40f, gapBefore = 64f });
            }
            else if (line.StartsWith("## "))
            {
                // 같은 묶음 안에서 다음 사람으로 넘어갈 때는 넉넉히 띄운다.
                bool nextPerson = previous.StartsWith("- ") || previous.StartsWith("= ");
                items.Add(Line(line.Substring(3), WuxiaTheme.MenuFont, 54, Name, FontStyle.Bold, nextPerson ? 52f : 14f));
            }
            else if (line.StartsWith("# "))
            {
                var item = Line(line.Substring(2), WuxiaTheme.MenuFont, 30, Gold, FontStyle.Normal, 56f);
                items.Add(item);
                var rule = UIFactory.CreateImage("Rule", roll, new Color(Gold.r, Gold.g, Gold.b, .5f));
                var r = rule.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1f); r.pivot = new Vector2(.5f, 1f); r.sizeDelta = new Vector2(120f, 2f);
                items.Add(new Item { rect = r, height = 2f, gapBefore = 8f });
            }
            else if (line.StartsWith("= "))
                items.Add(Line(line.Substring(2), WuxiaTheme.MenuFont, 28, Gold, FontStyle.Normal, 10f));
            else if (line.StartsWith("- "))
                items.Add(Line(line.Substring(2), WuxiaTheme.Font, 25, Note, FontStyle.Normal, previous.StartsWith("= ") ? 10f : 6f));
            else if (line.Length == 0)
            {
                var r = UIFactory.CreateRect("Space", roll);
                items.Add(new Item { rect = r, height = 26f });
            }
            else
                items.Add(Line(line, WuxiaTheme.MenuFont, 34, Name, FontStyle.Normal, 10f));
            if (line.Length > 0) previous = line;
        }
        return items;
    }

    Item Line(string value, Font font, int size, Color color, FontStyle style, float gap)
    {
        var text = UIFactory.CreateText("Line", roll, value, size, TextAnchor.UpperCenter, color);
        text.font = font; text.fontStyle = style; text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0f, 0f, 0f, .7f); shadow.effectDistance = new Vector2(2f, -2f);
        var r = text.rectTransform; r.anchorMin = new Vector2(.5f, 1f); r.anchorMax = new Vector2(.5f, 1f); r.pivot = new Vector2(.5f, 1f);
        r.sizeDelta = new Vector2(1020f, size * 1.4f);
        return new Item { rect = r, text = text, gapBefore = gap };
    }

    CanvasGroup BuildEndCard(Transform parent, string title, string copyright)
    {
        var root = UIFactory.CreateRect("EndCard", parent); UIFactory.Stretch(root);
        var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false;
        var logo = Resources.Load<Texture2D>("UI/Wuxia/HeukUnHyeopTitle");
        float top = 60f;
        if (logo != null)
        {
            var img = new GameObject("Logo", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(root, false); img.texture = logo; img.raycastTarget = false;
            float w = 900f; UIFactory.Place(img.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, top), new Vector2(w, w * logo.height / logo.width));
        }
        else
        {
            var t = UIFactory.CreateText("Title", root, title, 96, TextAnchor.MiddleCenter, Gold); t.font = WuxiaTheme.MenuFont;
            UIFactory.Place(t.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, top), new Vector2(1200f, 160f));
        }
        var thanks = UIFactory.CreateText("Thanks", root, "플레이해 주셔서 감사합니다", 34, TextAnchor.MiddleCenter, Name);
        thanks.font = WuxiaTheme.MenuFont;
        UIFactory.Place(thanks.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, -230f), new Vector2(1200f, 60f));
        var en = UIFactory.CreateText("ThanksEn", root, "THANK YOU FOR PLAYING", 22, TextAnchor.MiddleCenter, new Color(.84f, .80f, .72f, .85f));
        en.font = WuxiaTheme.DisplayFont;
        UIFactory.Place(en.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, -280f), new Vector2(1200f, 40f));
        if (!string.IsNullOrWhiteSpace(copyright))
        {
            var c = UIFactory.CreateText("Copyright", root, copyright, 20, TextAnchor.MiddleCenter, Note);
            c.font = WuxiaTheme.Font;
            UIFactory.Place(c.rectTransform, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 44f), new Vector2(1200f, 30f));
        }
        return group;
    }

    static void Edge(Transform parent, string name, bool top)
    {
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int i = 0; i < 64; i++) { float a = Mathf.SmoothStep(0f, 1f, i / 63f); tex.SetPixel(0, top ? i : 63 - i, new Color(0f, 0f, 0f, a)); }
        tex.Apply();
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
        img.transform.SetParent(parent, false); img.texture = tex; img.raycastTarget = false;
        var r = img.rectTransform; r.anchorMin = new Vector2(0f, top ? 1f : 0f); r.anchorMax = new Vector2(1f, top ? 1f : 0f);
        r.pivot = new Vector2(.5f, top ? 1f : 0f); r.sizeDelta = new Vector2(0f, 220f); r.anchoredPosition = Vector2.zero;
    }

    // ── 진행 ──────────────────────────────────────────────────

    void Update()
    {
        if (leaving || !ready || roll == null) return;
        elapsed += Time.unscaledDeltaTime;
        if (backdropRect != null) backdropRect.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, Mathf.Clamp01(elapsed / 90f));
        if (music != null) music.volume = Mathf.Clamp01(elapsed / 2f) * .8f;

        // 마지막 줄이 화면 위쪽(약 55% 높이)에 이르면 크레딧을 흐리게 지우며 마지막 제목 화면을 띄운다.
        roll.anchoredPosition = new Vector2(0f, Mathf.Min(travel, elapsed * PixelsPerSecond));
        float since = elapsed - RollSeconds;
        float rollAlpha = 1f - Mathf.Clamp01(since / 1.5f);
        if (rollGroup != null) rollGroup.alpha = rollAlpha;
        UpdateCast(rollAlpha);
        endCard.alpha = Mathf.Clamp01((since - .6f) / 1.5f);
        if (music != null && since > EndCardTime - 2f) music.volume = Mathf.Min(music.volume, Mathf.Clamp01((EndCardTime - since) / 2f) * .8f);

        if (Input.GetKeyDown(KeyCode.Escape) || since >= EndCardTime) Finish();
    }

    /// <summary>등장인물 초상을 크레딧 길이에 맞춰 차례로 보여 준다. 청운만 오른쪽을 보고 있어 나머지는 좌우를 뒤집는다.</summary>
    void UpdateCast(float rollAlpha)
    {
        if (castImage == null || cast.Count == 0) return;
        float per = Mathf.Clamp(RollSeconds / cast.Count, CastSeconds, 7f);
        int index = Mathf.Clamp((int)(elapsed / per), 0, cast.Count - 1);
        float local = elapsed - index * per;
        if (index != castIndex)
        {
            castIndex = index;
            var actor = cast[index];
            Rect crop = actor.crop;
            bool flip = actor.name != "Seojin";
            castImage.texture = actor.artwork;
            castImage.uvRect = flip ? new Rect(crop.x + crop.width, crop.y, -crop.width, crop.height) : crop;
            float aspect = (actor.artwork.width * crop.width) / (actor.artwork.height * crop.height);
            castRect.sizeDelta = new Vector2(860f * aspect, 860f);
            castName.text = actor.displayName;
        }
        bool last = index == cast.Count - 1;
        float a = Mathf.Clamp01(local / .7f) * (last ? 1f : Mathf.Clamp01((per - local) / .7f)) * rollAlpha;
        castRect.anchoredPosition = new Vector2(Mathf.Lerp(-36f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(local / 1.1f))), 0f);
        castImage.color = new Color(.93f, .93f, .93f, a);
        castName.color = new Color(Name.r, Name.g, Name.b, a);
        castLabel.color = new Color(Gold.r, Gold.g, Gold.b, a * .9f);
    }

    /// <summary>검사·미리보기용: 지정한 시간만큼 흐른 모습으로 맞춘다.</summary>
    public void Seek(float seconds) { elapsed = Mathf.Max(0f, seconds - Time.unscaledDeltaTime); Update(); }
    public bool Ready => ready;
    /// <summary>마지막 줄이 화면 위쪽에 이르기까지 올라가야 하는 거리.</summary>
    float RollDistance => Mathf.Max(0f, travel - viewport.rect.height * .45f);
    /// <summary>올라가는 빠르기. 등장인물이 모두 한 번씩(각 CastSeconds 이상) 지나가도록, 내용이 짧으면 더 천천히 올린다.</summary>
    float PixelsPerSecond
    {
        get
        {
            float speed = Speed * Mathf.Max(.5f, viewport.rect.height / 1080f);
            return cast.Count > 0 ? Mathf.Min(speed, RollDistance / (cast.Count * CastSeconds)) : speed;
        }
    }
    /// <summary>크레딧이 다 올라가 마지막 제목 화면으로 넘어가는 시각(초).</summary>
    public float RollSeconds => ready ? RollDistance / PixelsPerSecond : 0f;

    private void Finish()
    {
        if (leaving) return;
        leaving = true;
        if (music != null) music.Stop();
        // 엔딩으로 왔을 때만 끝난 판을 정리한다. 시작 화면에서 열어 본 경우에는 저장을 건드리지 않는다.
        if (!fromMenu && RunState.Instance != null) RunState.Instance.AbandonRun();
        SceneRoutes.LoadStart();
    }
}
