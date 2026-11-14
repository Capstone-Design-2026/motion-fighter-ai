using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PvP 수련(튜토리얼)의 설명 카드. 한 사람 몫이며 화면의 자기 쪽 절반에 뜬다.
///
/// 무엇을 몇 번 했는지는 RedboolPractice가 판단한다(카메라 쪽 개인 보정과 맞물려 있어 순서와 규칙을 바꾸지 않는다).
/// 이 카드는 그 진행 상황(step, hits)을 받아 "이 동작이 무엇이고, 어떻게 하고, 언제 쓰는지"를 단계별로 보여 준다:
///   ① 시범 보기 → ② 따라 하기(두 번) → ③ 돌아오기
/// 손님 화면도 같은 값(step, hits)을 받으므로 같은 카드가 뜬다.
/// </summary>
public sealed class RedboolPvpTutorialCard
{
    // RedboolPractice.Actions 와 같은 순서
    public static readonly string[] Names = { "잽", "스트레이트", "하이킥", "로우킥", "막기", "왼쪽 이동", "오른쪽 이동", "필살기 · 파운장" };
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
        "막고 있는 동안에는 피해를 받지 않습니다. 상대가 움직이면 올리세요",
        "기울이고 있는 동안 왼쪽으로 걷습니다. 거리를 조절할 때 씁니다",
        "기울이고 있는 동안 오른쪽으로 걷습니다. 거리를 조절할 때 씁니다",
        "공격을 맞혀 기를 다 모으면 쓸 수 있는 큰 기술입니다. 지금은 연습이라 바로 됩니다",
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
        "두 손목이 가슴 앞에서 엇갈리게 하고 0.7초쯤 그대로 있으세요.",
    };
    const float DemoSeconds = 2.2f, TipAfter = 9f;

    GameObject root, poseRoot;
    Text header, phase, how, use, note, pips, waiting;
    TrainingPoseGraphic gamePose;
    int shownStep = -1; float stepStarted;

    public static RedboolPvpTutorialCard Create(Transform canvas, int slot)
    {
        var card = new RedboolPvpTutorialCard();
        var half = new Vector2(slot == 0 ? .25f : .75f, .5f);
        var panel = UIFactory.CreateImage("TutorialCard" + (slot + 1), canvas, new Color(.02f, .04f, .055f, .88f));
        card.root = panel.gameObject;
        UIFactory.Place(panel.rectTransform, half, new Vector2(.5f, 1), new Vector2(0, -126), new Vector2(760, 266));
        card.header = Line(panel.transform, "Header", 32, WuxiaTheme.Gold, -10, 42);
        card.phase = Line(panel.transform, "Phase", 21, WuxiaTheme.Paper, -52, 30);
        card.how = Line(panel.transform, "How", 26, Color.white, -88, 38);
        card.use = Line(panel.transform, "Use", 21, new Color(.72f, .88f, .80f), -130, 54);
        card.pips = Line(panel.transform, "Pips", 26, WuxiaTheme.Gold, -186, 34);
        card.note = Line(panel.transform, "Note", 19, new Color(.80f, .78f, .70f), -224, 36);
        card.waiting = Line(panel.transform, "Waiting", 28, WuxiaTheme.Paper, -100, 60);

        // 가운데에는 RedboolPvp의 "나의 동작" 그림이 있다. 그 오른쪽에 게임 속 동작을 나란히 둔다.
        var pose = UIFactory.CreateRect("TutorialPose" + (slot + 1), canvas); card.poseRoot = pose.gameObject;
        UIFactory.Place(pose, half, new Vector2(.5f, .5f), Vector2.zero, new Vector2(760, 300));
        Caption(pose, "MyCaption", "나의 동작", 0); Caption(pose, "Arrow", "→", 135); Caption(pose, "GameCaption", "게임 속 동작", 262);
        var game = UIFactory.CreateRect("GamePose", pose);
        UIFactory.Place(game, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(262, 0), new Vector2(220, 240));
        card.gamePose = game.gameObject.AddComponent<TrainingPoseGraphic>(); card.gamePose.gameAction = true; card.gamePose.raycastTarget = false;
        card.Hide();
        return card;
    }

    static Text Line(Transform parent, string name, int size, Color colour, float y, float height)
    {
        var text = WuxiaTheme.Text(name, parent, "", size, TextAnchor.MiddleCenter, colour);
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false; text.supportRichText = true;
        UIFactory.Place(text.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(730, height));
        return text;
    }

    static void Caption(Transform parent, string name, string value, float x)
    {
        var text = WuxiaTheme.Text(name, parent, value, name == "Arrow" ? 44 : 20, TextAnchor.MiddleCenter, new Color(.85f, .82f, .72f));
        text.raycastTarget = false;
        UIFactory.Place(text.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(x, name == "Arrow" ? 0 : 138), new Vector2(200, 40));
    }

    public void Hide() { if (root.activeSelf) root.SetActive(false); if (poseRoot.activeSelf) poseRoot.SetActive(false); shownStep = -1; }

    /// <summary>step: 지금 익히는 동작 번호(8이면 끝), hits: 이번 동작을 성공한 횟수(0~2), live: 카메라 신호가 들어오는가</summary>
    public void Show(int step, int hits, bool live)
    {
        if (!root.activeSelf) root.SetActive(true);
        bool done = step >= Names.Length;
        if (poseRoot.activeSelf == done) poseRoot.SetActive(!done);
        foreach (var line in new[] { header, phase, how, use, pips, note }) if (line.gameObject.activeSelf == done) line.gameObject.SetActive(!done);
        if (waiting.gameObject.activeSelf != done) waiting.gameObject.SetActive(done);
        if (done) { waiting.text = "수련 완료!\n상대가 끝나면 대전이 시작됩니다"; return; }

        if (step != shownStep) { shownStep = step; stepStarted = Time.unscaledTime; gamePose.step = step; gamePose.SetVerticesDirty(); }
        float since = Time.unscaledTime - stepStarted;
        // 이미 한 번 해냈으면 시범을 건너뛰고 '따라 하기'로 본다.
        int stage = hits >= 2 ? 2 : hits >= 1 || since >= DemoSeconds ? 1 : 0;
        header.text = "수련  " + (step + 1) + " / " + Names.Length + "     " + Names[step];
        phase.text = Mark("① 시범 보기", stage == 0) + "     " + Mark("② 따라 하기", stage == 1) + "     " + Mark("③ 돌아오기", stage == 2);
        how.text = How[step];
        use.text = "쓰임  ·  " + Use[step];
        pips.text = "성공   " + (hits >= 1 ? "●" : "○") + "  " + (hits >= 2 ? "●" : "○");
        note.text = !live ? "카메라 신호를 기다리는 중입니다 · 카메라 앞에 서서 C로 중앙 가드 보정"
            : stage == 2 ? (step == 4 ? "잘했어요! 팔을 편하게 내리세요" : step == 5 || step == 6 ? "잘했어요! 몸을 가운데로 돌려 주세요" : "잘했어요! 다음 동작으로 넘어갑니다")
            : hits == 1 ? "좋아요! 같은 동작을 한 번 더"
            : stage == 0 ? "그림을 먼저 보세요. 곧 따라 할 차례입니다"
            : since > TipAfter ? "도움말  ·  " + Tips[step]
            : "지금 따라 해 보세요";
    }

    static string Mark(string label, bool on) => on ? "<color=#F2C46B><b>" + label + "</b></color>" : "<color=#8C8C86>" + label + "</color>";
}
