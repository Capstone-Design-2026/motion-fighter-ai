using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PvP 수련(튜토리얼)의 설명 카드. 한 사람 몫이며 화면의 자기 쪽 절반에 뜬다.
///
/// 무엇을 몇 번 했는지는 RedboolPractice가 판단한다(카메라 쪽 개인 보정과 맞물려 있어 순서와 규칙을 바꾸지 않는다).
/// 이 카드는 그 진행 상황(step, hits)을 받아 "이 동작이 무엇이고, 어떻게 하고, 언제 쓰는지"를 단계별로 보여 준다:
///   ① 시범 보기 → ② 따라 하기(두 번) → ③ 돌아오기
/// 손님 화면도 같은 값(step, hits)을 받으므로 같은 카드가 뜬다.
/// 마지막 동작(필살기)은 고른 인물의 기술 이름과 설명으로 바뀐다.
/// </summary>
public sealed class RedboolPvpTutorialCard
{
    // RedboolPractice.Actions 와 같은 순서
    public static readonly string[] Names = { "잽", "스트레이트", "하이킥", "로우킥", "막기", "왼쪽 이동", "오른쪽 이동", "필살기" };
    public static readonly string[] How =
    {
        "왼손을 앞으로 빠르게 뻗었다가 바로 가드로 되돌리세요",
        "오른손을 앞으로 곧게 뻗었다가 가드로 되돌리세요",
        "왼손을 머리 위로 들어 올렸다가 내리세요",
        "오른손을 머리 위로 들어 올렸다가 내리세요",
        "두 손을 얼굴 앞으로 올려 잠깐 버틴 뒤 편하게 내리세요",
        "몸을 왼쪽으로 기울였다가 가운데로 돌아오세요",
        "몸을 오른쪽으로 기울였다가 가운데로 돌아오세요",
        "가슴 앞에서 두 팔을 X자로 겹쳐 잠깐 유지하세요",
    };
    public static readonly string[] Use =
    {
        "가장 빠른 공격. 거리를 재거나 상대의 공격을 끊을 때 씁니다",
        "잽보다 묵직한 한 방. 빗나가거나 막히면 틈이 깁니다",
        "머리를 노리는 발차기. 느리지만 멀리 닿습니다",
        "다리를 노리는 발차기. 상대가 얼굴만 막고 있을 때 씁니다",
        "일반 공격을 막습니다. 필살기는 막아도 일부 피해를 받을 수 있습니다",
        "기울이고 있는 동안 왼쪽으로 걷습니다. 거리를 조절할 때 씁니다",
        "기울이고 있는 동안 오른쪽으로 걷습니다. 거리를 조절할 때 씁니다",
        "",   // 필살기는 인물마다 다르다 (Show 에서 채운다)
    };
    public static readonly string[] Tips =
    {
        "왼손 주먹이 카메라에 가려지지 않게, 한 번 뻗고 바로 되돌려 보세요.",
        "왼손은 가드에 두고 오른손만 뻗어 보세요.",
        "실제로 다리를 들 필요는 없습니다. 왼손만 높이 올렸다가 내리세요.",
        "오른손만 높이 올렸다가 내리세요. 왼손은 가드에 둡니다.",
        "두 손이 모두 카메라에 보여야 합니다. 올린 채 잠깐 기다렸다가 내리세요.",
        "어깨를 조금 더 옆으로 옮겼다가, 가운데에서 잠깐 멈추세요.",
        "어깨를 조금 더 옆으로 옮겼다가, 가운데에서 잠깐 멈추세요.",
        "두 손목이 가슴 앞에서 엇갈리게 하고 약 1초 그대로 있으세요.",
    };
    const float DemoSeconds = 2.2f, TipAfter = 9f;
    const int Special = 7;
    static readonly Color[] SideColour = { new Color(.94f, .72f, .34f), new Color(.52f, .76f, 1f) };

    GameObject root, poseRoot;
    Text header, steps, phase, how, use, note, pips, waiting;
    TrainingPoseGraphic gamePose;
    int shownStep = -1; float stepStarted;

    public static RedboolPvpTutorialCard Create(Transform canvas, int slot)
    {
        var card = new RedboolPvpTutorialCard();
        var half = new Vector2(slot == 0 ? .25f : .75f, .5f);
        var panel = UIFactory.CreateRect("TutorialCard" + (slot + 1), canvas);
        card.root = panel.gameObject;
        UIFactory.Place(panel, half, new Vector2(.5f, 1), new Vector2(0, -132), new Vector2(780, 296));
        var ink = WuxiaTheme.Ornament("Ink", panel, WuxiaGraphic.Shape.Plaque, new Color(.03f, .045f, .055f, .92f)); ink.raycastTarget = false; UIFactory.Stretch(ink.rectTransform);
        var frame = WuxiaTheme.Ornament("Frame", panel, WuxiaGraphic.Shape.PlaqueFrame, new Color(SideColour[slot].r, SideColour[slot].g, SideColour[slot].b, .85f)); frame.raycastTarget = false; UIFactory.Stretch(frame.rectTransform);
        card.header = Line(panel, "Header", 32, WuxiaTheme.Gold, -12, 42);
        card.steps = Line(panel, "Steps", 17, WuxiaTheme.Paper, -54, 24);
        card.phase = Line(panel, "Phase", 21, WuxiaTheme.Paper, -82, 30);
        card.how = Line(panel, "How", 26, Color.white, -116, 38);
        card.use = Line(panel, "Use", 20, new Color(.72f, .88f, .80f), -158, 54);
        card.pips = Line(panel, "Pips", 25, WuxiaTheme.Gold, -216, 34);
        card.note = Line(panel, "Note", 19, new Color(.80f, .78f, .70f), -254, 32);
        card.waiting = Line(panel, "Waiting", 28, WuxiaTheme.Paper, -108, 80);

        // 가운데에는 RedboolPvp의 "나의 동작" 그림이 있다. 그 오른쪽에 게임 속 동작을 나란히 둔다.
        var pose = UIFactory.CreateRect("TutorialPose" + (slot + 1), canvas); card.poseRoot = pose.gameObject;
        UIFactory.Place(pose, half, new Vector2(.5f, .5f), Vector2.zero, new Vector2(760, 300));
        Caption(pose, "MyCaption", "나의 동작", -131); Caption(pose, "Arrow", "→", 0); Caption(pose, "GameCaption", "게임 속 동작", 131);
        var game = UIFactory.CreateRect("GamePose", pose);
        UIFactory.Place(game, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(131, 0), new Vector2(220, 240));
        card.gamePose = game.gameObject.AddComponent<TrainingPoseGraphic>(); card.gamePose.gameAction = true; card.gamePose.raycastTarget = false;
        card.Hide();
        return card;
    }

    static Text Line(Transform parent, string name, int size, Color colour, float y, float height)
    {
        var text = WuxiaTheme.Text(name, parent, "", size, TextAnchor.MiddleCenter, colour);
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false; text.supportRichText = true;
        UIFactory.Place(text.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(740, height));
        return text;
    }

    static void Caption(Transform parent, string name, string value, float x)
    {
        var text = WuxiaTheme.Text(name, parent, value, name == "Arrow" ? 44 : 20, TextAnchor.MiddleCenter, new Color(.85f, .82f, .72f));
        text.raycastTarget = false;
        UIFactory.Place(text.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, name == "Arrow" ? 0 : 138), new Vector2(200, 40));
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); if (poseRoot.activeSelf) poseRoot.SetActive(false); shownStep = -1; }

    /// <summary>
    /// step: 지금 익히는 동작 번호(8이면 끝), hits: 이번 동작을 성공한 횟수(0~2), live: 카메라 신호가 들어오는가.
    /// who: 고른 인물, specialName: 실제로 나갈 필살기 이름(인물의 기술을 쓰지 못하면 파운장),
    /// personal: 개인 동작 범위 상태(0 기본 인식, 1 확인 중, 2 적용됨).
    /// </summary>
    public void Show(int step, int hits, bool live, RedboolPvpCharacter who, string specialName, int personal)
    {
        if (!root.activeSelf) root.SetActive(true);
        bool done = step >= Names.Length;
        if (poseRoot.activeSelf == done) poseRoot.SetActive(!done);
        foreach (var line in new[] { header, steps, phase, how, use, pips, note }) if (line.gameObject.activeSelf == done) line.gameObject.SetActive(!done);
        if (waiting.gameObject.activeSelf != done) waiting.gameObject.SetActive(done);
        if (done) { waiting.text = "<color=#F2C46B>수련 완료!</color>\n상대가 끝나면 대전이 시작됩니다"; return; }

        if (step != shownStep) { shownStep = step; stepStarted = Time.unscaledTime; gamePose.step = step; gamePose.SetVerticesDirty(); }
        float since = Time.unscaledTime - stepStarted;
        // 이미 한 번 해냈으면 시범을 건너뛰고 '따라 하기'로 본다.
        int stage = hits >= 2 ? 2 : hits >= 1 || since >= DemoSeconds ? 1 : 0;
        bool special = step == Special;
        header.text = "수련  " + (step + 1) + " / " + Names.Length + "     " + (special ? "필살기 · " + specialName : Names[step]);
        var dots = new System.Text.StringBuilder();
        for (int i = 0; i < Names.Length; i++)
            dots.Append(i < step ? "<color=#B8935A>●</color>" : i == step ? "<color=#FFFFFF>●</color>" : "<color=#5C5F60>○</color>").Append(i < Names.Length - 1 ? "  " : "");
        steps.text = dots.ToString();
        phase.text = Mark("① 시범 보기", stage == 0) + "     " + Mark("② 따라 하기", stage == 1) + "     " + Mark("③ 돌아오기", stage == 2);
        how.text = How[step];
        use.text = special ? SpecialUse(who, specialName) : "쓰임  ·  " + Use[step];
        string range = personal == 2 ? "개인 동작 범위 적용" : personal == 1 ? "동작과 복귀를 확인 중" : "기본 동작 인식";
        pips.text = "성공   " + (hits >= 1 ? "●" : "○") + "  " + (hits >= 2 ? "●" : "○") + "<size=17><color=#8C8C86>        " + range + "</color></size>";
        note.text = !live ? "카메라 입력 확인 중 · 보정 상태는 내 동작 화면을 확인하세요"
            : stage == 2 ? (step == 4 ? "잘했어요! 팔을 편하게 내리세요" : step == 5 || step == 6 ? "잘했어요! 몸을 가운데로 돌려 주세요" : special ? "잘했어요! 팔을 풀면 수련이 끝납니다" : "잘했어요! 다음 동작으로 넘어갑니다")
            : hits == 1 ? "좋아요! 같은 동작을 한 번 더"
            : stage == 0 ? "그림을 보며 바로 따라 해도 됩니다"
            : since > TipAfter ? "도움말  ·  " + Tips[step]
            : special ? "수련에서는 자세만 확인합니다. 대전에서는 공격을 맞혀 기를 다 모아야 나갑니다"
            : "지금 따라 해 보세요";
    }

    // 필살기 칸의 설명: 그 인물의 기술이 무엇을 하는지. 인물의 기술을 쓰지 못해 파운장으로 나가면 파운장 설명을 쓴다.
    static string SpecialUse(RedboolPvpCharacter who, string specialName)
    {
        var owner = who != null && who.signature == specialName ? who : RedboolPvpRoster.Get(RedboolPvpRoster.Hero);
        return (who != null ? who.name + "의 기술  ·  " : "") + owner.specialLine;
    }

    static string Mark(string label, bool on) => on ? "<color=#F2C46B><b>" + label + "</b></color>" : "<color=#8C8C86>" + label + "</color>";
}
