using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 1:1 액션 테스트 관리자 (Boxer_Demo 씬).
/// - 상대(적 캐릭터)를 한 명씩만 켜서 킥복서와 맞붙여 봄 (Tab / F1~F4, 화면 버튼)
/// - 조종할 쪽을 킥복서 ↔ 상대로 바꿈 (X). 조종하지 않는 쪽은 키·버튼 입력을 받지 않음
/// - 조종 중인 캐릭터가 잽·크로스·하이킥·로우킥을 하면
///   1) 공격하는 쪽이 "딱 맞는 거리"까지 다가가고
///   2) 맞는 순간 상대가 피격(또는 가드) 반응을 함 (Z로 맞기/막기/반응없음 전환)
///   3) 실제로 주먹·발이 어디에 닿았는지 재서 거리를 스스로 보정함
/// - 동작이 끝나면 두 캐릭터가 기본 간격으로 돌아감 (Backspace = 바로 원위치)
/// </summary>
[DefaultExecutionOrder(100)]
public class FightTestManager : MonoBehaviour
{
    public enum Reaction { Hit, Guard, None }
    public enum Side { Player, Opponent }

    [System.Serializable]
    public class Opponent
    {
        public string label = "Enemy";
        public GameObject fighter;
        [Tooltip("킥복서가 이 상대를 때릴 때 두 캐릭터 사이 거리(m): 잽, 크로스, 하이킥, 로우킥")]
        public float[] playerAttackDistance = { 1.2f, 1.2f, 1.3f, 1.1f };
        [Tooltip("이 상대가 킥복서를 때릴 때 두 캐릭터 사이 거리(m): 잽, 크로스, 하이킥, 로우킥")]
        public float[] opponentAttackDistance = { 1.2f, 1.2f, 1.3f, 1.1f };
    }

    [Header("캐릭터")]
    public GameObject player;
    public List<Opponent> opponents = new List<Opponent>();
    public int activeOpponent = 0;

    [Header("테스트 설정")]
    public Side control = Side.Player;
    public Reaction reaction = Reaction.Hit;
    [Tooltip("평소(대기) 두 캐릭터 사이 거리")]
    public float neutralDistance = 1.6f;
    [Tooltip("공격할 때 맞는 거리까지 다가감")]
    public bool autoApproach = true;
    [Tooltip("맞은 위치를 재서 거리를 계속 보정 (Play 중에만 바뀌고, 저장은 안 됨)")]
    public bool learnDistance = true;
    [Range(0f, 1f)] public float learnRate = 0.6f;
    [Tooltip("동작이 끝나면 기본 간격으로 돌아감")]
    public bool autoReturn = true;
    public float returnDelay = 0.6f;
    public float returnSpeed = 1.5f;

    [Header("타이밍 (HanbokActionFeel의 impact와 같은 값)")]
    public float[] impact = { 0.27f, 0.32f, 0.31f, 0.33f };
    [Tooltip("킥 가드는 다리를 올리는 시간이 필요해서 이만큼(초) 먼저 시작")]
    public float kickGuardLead = 0.26f;
    [Tooltip("주먹 가드는 이만큼(초) 먼저 시작")]
    public float punchGuardLead = 0.08f;

    [Header("닿는 지점 보정 (m)")]
    public float fistTip = 0.07f, footTip = 0.05f, faceSurface = 0.09f, thighSurface = 0.08f;

    [Header("시연 UI")]
    public bool showPanel = true;
    [Tooltip("확인용: 맞는 순간 에디터를 일시정지 (화면으로 닿는 위치 확인)")]
    public bool pauseOnImpact = false;

    static readonly string[] StrikeStates = { "JAB", "CROSS", "HIGHKICK", "LOWKICK" };
    static readonly string[] StrikeKo = { "잽", "크로스", "하이킥", "로우킥" };

    Vector3 playerHome;
    Quaternion playerHomeRot;

    int exStrike = -1;
    bool exImpactDone, exGuardDone;
    float exStartNt;
    Vector3 exStartPos;
    float idleTimer;
    string lastInfo = "";

    // ───────── 캐릭터 헬퍼 ─────────
    GameObject Opp { get { return (activeOpponent >= 0 && activeOpponent < opponents.Count) ? opponents[activeOpponent].fighter : null; } }
    GameObject Attacker { get { return control == Side.Player ? player : Opp; } }
    GameObject Defender { get { return control == Side.Player ? Opp : player; } }

    static Animator A(GameObject g) { return g ? g.GetComponent<Animator>() : null; }
    static HitKnockback KB(GameObject g) { return g ? g.GetComponent<HitKnockback>() : null; }
    static Transform Bone(GameObject g, HumanBodyBones b) { var a = A(g); return a ? a.GetBoneTransform(b) : null; }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    float[] DistTable()
    {
        var o = opponents[activeOpponent];
        return control == Side.Player ? o.playerAttackDistance : o.opponentAttackDistance;
    }

    void Start()
    {
        if (!player) { enabled = false; return; }
        playerHome = player.transform.position;
        playerHomeRot = player.transform.rotation;
        SelectOpponent(Mathf.Clamp(activeOpponent, 0, opponents.Count - 1));
    }

    // ───────── 상대 선택 / 조종 전환 ─────────
    public void SelectOpponent(int i)
    {
        if (opponents.Count == 0) return;
        activeOpponent = (i % opponents.Count + opponents.Count) % opponents.Count;
        for (int j = 0; j < opponents.Count; j++)
            if (opponents[j].fighter) opponents[j].fighter.SetActive(j == activeOpponent);
        exStrike = -1;
        PlaceNeutral(true);
        LinkTargets();
        ApplyControl();
        lastInfo = "상대: " + opponents[activeOpponent].label;
    }

    void LinkTargets()
    {
        var opp = Opp;
        if (!opp) return;
        var pHead = Bone(player, HumanBodyBones.Head);
        var oHead = Bone(opp, HumanBodyBones.Head);
        foreach (var h in player.GetComponents<HeadLook>()) h.target = oHead;
        foreach (var p in player.GetComponents<PunchAim>()) p.target = oHead;
        foreach (var h in opp.GetComponents<HeadLook>()) h.target = pHead;
        foreach (var p in opp.GetComponents<PunchAim>()) p.target = pHead;
    }

    public void SetControl(Side s) { control = s; exStrike = -1; ApplyControl(); }

    void ApplyControl()
    {
        SetInput(player, control == Side.Player);
        SetInput(Opp, control == Side.Opponent);
    }

    static void SetInput(GameObject g, bool on)
    {
        if (!g) return;
        var d = g.GetComponent<HanbokDemoControl>(); if (d) d.enabled = on;
        var k = g.GetComponent<HitKnockback>(); if (k) k.acceptInput = on;
    }

    Vector3 OppHome { get { return playerHome + playerHomeRot * Vector3.forward * neutralDistance; } }

    void PlaceNeutral(bool instant)
    {
        var opp = Opp;
        if (instant)
        {
            player.transform.SetPositionAndRotation(playerHome, playerHomeRot);
            if (opp) opp.transform.SetPositionAndRotation(OppHome, Quaternion.LookRotation(-(playerHomeRot * Vector3.forward)));
        }
        var k = KB(player); if (k) k.SetHome(playerHome, playerHomeRot);
        k = KB(opp); if (k) k.SetHome(OppHome, Quaternion.LookRotation(-(playerHomeRot * Vector3.forward)));
    }

    // ───────── 입력 ─────────
    void ReadKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.tabKey.wasPressedThisFrame) SelectOpponent(activeOpponent + 1);
        if (kb.f1Key.wasPressedThisFrame) SelectOpponent(0);
        if (kb.f2Key.wasPressedThisFrame && opponents.Count > 1) SelectOpponent(1);
        if (kb.f3Key.wasPressedThisFrame && opponents.Count > 2) SelectOpponent(2);
        if (kb.f4Key.wasPressedThisFrame && opponents.Count > 3) SelectOpponent(3);
        if (kb.f5Key.wasPressedThisFrame && opponents.Count > 4) SelectOpponent(4);
        if (kb.f6Key.wasPressedThisFrame && opponents.Count > 5) SelectOpponent(5);
        if (kb.xKey.wasPressedThisFrame) SetControl(control == Side.Player ? Side.Opponent : Side.Player);
        if (kb.zKey.wasPressedThisFrame) reaction = (Reaction)(((int)reaction + 1) % 3);
        if (kb.backspaceKey.wasPressedThisFrame) { exStrike = -1; ReviveAll(); PlaceNeutral(true); }
        if (kb.pKey.wasPressedThisFrame) KO(Defender);
        if (kb.oKey.wasPressedThisFrame) KO(Attacker);
#else
        if (Input.GetKeyDown(KeyCode.Tab)) SelectOpponent(activeOpponent + 1);
        if (Input.GetKeyDown(KeyCode.F1)) SelectOpponent(0);
        if (Input.GetKeyDown(KeyCode.F2) && opponents.Count > 1) SelectOpponent(1);
        if (Input.GetKeyDown(KeyCode.F3) && opponents.Count > 2) SelectOpponent(2);
        if (Input.GetKeyDown(KeyCode.F4) && opponents.Count > 3) SelectOpponent(3);
        if (Input.GetKeyDown(KeyCode.F5) && opponents.Count > 4) SelectOpponent(4);
        if (Input.GetKeyDown(KeyCode.F6) && opponents.Count > 5) SelectOpponent(5);
        if (Input.GetKeyDown(KeyCode.X)) SetControl(control == Side.Player ? Side.Opponent : Side.Player);
        if (Input.GetKeyDown(KeyCode.Z)) reaction = (Reaction)(((int)reaction + 1) % 3);
        if (Input.GetKeyDown(KeyCode.Backspace)) { exStrike = -1; ReviveAll(); PlaceNeutral(true); }
        if (Input.GetKeyDown(KeyCode.P)) KO(Defender);
        if (Input.GetKeyDown(KeyCode.O)) KO(Attacker);
#endif
    }

    // 지금(또는 넘어가는 중인) 공격 상태와 진행도
    static int CurrentStrike(Animator a, out float nt, out float len)
    {
        nt = 0f; len = 1f;
        if (!a) return -1;
        var cur = a.GetCurrentAnimatorStateInfo(0);
        bool tr = a.IsInTransition(0);
        var next = tr ? a.GetNextAnimatorStateInfo(0) : cur;
        for (int s = 0; s < StrikeStates.Length; s++)
        {
            if (tr && next.IsName(StrikeStates[s])) { nt = next.normalizedTime; len = next.length; return s; }
            if (cur.IsName(StrikeStates[s]) && !(tr && !next.IsName(StrikeStates[s]) && cur.normalizedTime > 0.9f)) { nt = cur.normalizedTime; len = cur.length; return s; }
        }
        return -1;
    }

    static bool IsIdle(Animator a)
    {
        if (!a || a.IsInTransition(0)) return false;
        var c = a.GetCurrentAnimatorStateInfo(0);
        return c.IsName("IDLE") || c.IsName("HIGHGUARD_V2");
    }

    // ───────── 매 프레임 ─────────
    void Update()
    {
        ReadKeys();
        var att = Attacker; var def = Defender;
        if (!att || !def) return;

        // 쓰러진 사람이 있으면 공격 처리·마주 보기·원위치 이동 안 함 (Backspace로 일으키기)
        if (Defeated(player) || Defeated(Opp)) { exStrike = -1; idleTimer = 0f; return; }

        // 서로 마주 보기
        FaceEachOther(player, Opp);

        float nt, len;
        int s = CurrentStrike(A(att), out nt, out len);
        if (s >= 0 && exStrike < 0) BeginExchange(s, nt);
        if (exStrike >= 0 && s != exStrike) exStrike = -1;

        if (exStrike >= 0)
        {
            idleTimer = 0f;
            Vector3 dir = Flat(def.transform.position - att.transform.position).normalized;
            if (autoApproach && !exImpactDone && dir.sqrMagnitude > 0.5f)
            {
                float d = DistTable()[exStrike];
                Vector3 goal = def.transform.position - dir * d;
                goal.y = att.transform.position.y;
                float k = Mathf.Clamp01((nt - exStartNt) / Mathf.Max(0.05f, impact[exStrike] - exStartNt));
                k = k * k * (3f - 2f * k);
                att.transform.position = Vector3.Lerp(exStartPos, goal, k);
            }
            // 가드는 맞기 직전에 시작
            if (reaction == Reaction.Guard && !exGuardDone)
            {
                float speed = Mathf.Max(0.05f, A(att).speed);
                float timeToImpact = (impact[exStrike] - nt) * len / speed;
                float lead = exStrike >= 2 ? kickGuardLead : punchGuardLead;
                if (timeToImpact <= lead) { exGuardDone = true; TriggerGuard(def, exStrike); }
            }
        }
        else if (autoReturn)
        {
            bool calm = IsIdle(A(player)) && IsIdle(A(Opp)) && !(KB(player) && KB(player).IsPushing) && !(KB(Opp) && KB(Opp).IsPushing);
            idleTimer = calm ? idleTimer + Time.deltaTime : 0f;
            if (idleTimer > returnDelay)
            {
                // 부드럽게 감속하며 돌아감 (갑자기 미끄러지지 않게)
                float k = 1f - Mathf.Exp(-returnSpeed * Time.deltaTime);
                player.transform.position = Vector3.Lerp(player.transform.position, playerHome, k);
                Opp.transform.position = Vector3.Lerp(Opp.transform.position, OppHome, k);
            }
        }
    }

    void BeginExchange(int s, float nt)
    {
        exStrike = s;
        exImpactDone = false;
        exGuardDone = reaction != Reaction.Guard;
        exStartNt = Mathf.Min(nt, impact[s] - 0.05f);
        exStartPos = Attacker.transform.position;
    }

    void LateUpdate()
    {
        if (exStrike < 0 || exImpactDone) return;
        var att = Attacker; var def = Defender;
        if (!att || !def) return;
        float nt, len;
        int s = CurrentStrike(A(att), out nt, out len);
        if (s != exStrike || nt < impact[exStrike]) return;
        exImpactDone = true;

        // 실제로 닿은 위치 측정 → 거리 보정
        Vector3 dir = Flat(def.transform.position - att.transform.position).normalized;
        Vector3 tip, surf;
        Measure(att, def, exStrike, dir, out tip, out surf);
        float err = Vector3.Dot(tip - surf, dir); // + = 너무 가까움(파고듦), - = 못 미침
        var table = DistTable();
        float before = table[exStrike];
        // 로우킥은 옆에서 휘둘러 맞히는 동작이라 앞뒤 거리로 재면 부정확 → 보정 안 함. 가드 때는 막는 자세라 얼굴 위치가 달라서 보정 안 함
        if (learnDistance && exStrike < 3 && reaction == Reaction.Hit) table[exStrike] = Mathf.Clamp(before + err * learnRate, 0.5f, 2.4f);
        string who = control == Side.Player ? "킥복서 → " + opponents[activeOpponent].label : opponents[activeOpponent].label + " → 킥복서";
        lastInfo = who + " " + StrikeKo[exStrike] + " | 거리 " + Flat(def.transform.position - att.transform.position).magnitude.ToString("F2") + "m, 닿은 오차 " + (err * 100f).ToString("+0;-0") + "cm";

        if (reaction == Reaction.Hit) TriggerHit(def, exStrike);
        // 로우킥을 정강이로 막으면 차던 다리가 튕겨 나옴 (막는 다리를 뚫고 지나가지 않게)
        if (reaction == Reaction.Guard && exStrike == 3) { var feel = att.GetComponent<HanbokActionFeel>(); if (feel) feel.Rebound(); }
#if UNITY_EDITOR
        if (pauseOnImpact) UnityEditor.EditorApplication.isPaused = true;
#endif
    }

    void Measure(GameObject att, GameObject def, int s, Vector3 dir, out Vector3 tip, out Vector3 surf)
    {
        if (s == 0) tip = Bone(att, HumanBodyBones.LeftHand).position + dir * fistTip;
        else if (s == 1) tip = Bone(att, HumanBodyBones.RightHand).position + dir * fistTip;
        else if (s == 2) tip = Bone(att, HumanBodyBones.RightFoot).position + dir * footTip;
        else tip = Vector3.Lerp(Bone(att, HumanBodyBones.RightLowerLeg).position, Bone(att, HumanBodyBones.RightFoot).position, 0.6f);

        if (s < 3) surf = Bone(def, HumanBodyBones.Head).position - dir * faceSurface;
        else
        {
            // 공격하는 쪽에 더 가까운 다리(앞다리) 허벅지 바깥
            var lu = Bone(def, HumanBodyBones.LeftUpperLeg); var ll = Bone(def, HumanBodyBones.LeftLowerLeg);
            var ru = Bone(def, HumanBodyBones.RightUpperLeg); var rl = Bone(def, HumanBodyBones.RightLowerLeg);
            Vector3 l = Vector3.Lerp(lu.position, ll.position, 0.55f), r = Vector3.Lerp(ru.position, rl.position, 0.55f);
            Vector3 lead = Vector3.Dot(l - r, dir) < 0f ? l : r;
            surf = lead - dir * thighSurface;
        }
    }

    static void TriggerHit(GameObject def, int s)
    {
        var k = KB(def);
        if (k)
        {
            if (s == 0) k.HitJabKB();
            else if (s == 3) k.HitLegKB();
            else k.HitHeadKB();
            return;
        }
        var a = A(def);
        if (a) a.SetTrigger(s == 0 ? "HitJab" : s == 3 ? "HitLeg" : "HitHead");
    }

    static void TriggerGuard(GameObject def, int s)
    {
        var k = KB(def);
        if (k)
        {
            if (s == 0) k.GuardJabKB();
            else if (s == 1) k.GuardCrossKB();
            else if (s == 2) k.KickGuardKB();
            else k.KickGuardLowKB();
            return;
        }
        var a = A(def);
        if (a) a.SetTrigger(s == 0 ? "GuardJab" : s == 1 ? "GuardCross" : s == 2 ? "KickGuard" : "KickGuardLow");
    }

    static void FaceEachOther(GameObject a, GameObject b)
    {
        if (!a || !b) return;
        Vector3 d = Flat(b.transform.position - a.transform.position);
        if (d.sqrMagnitude < 1e-4f) return;
        a.transform.rotation = Quaternion.LookRotation(d.normalized);
        b.transform.rotation = Quaternion.LookRotation(-d.normalized);
    }

    // ───────── 패배(KO) ─────────
    public void KO(GameObject g)
    {
        if (!g) return;
        exStrike = -1;
        var d = g.GetComponent<DefeatPose>();
        if (d) d.Defeat(); else { var a = A(g); if (a) a.SetTrigger("Defeat"); }
        lastInfo = (g == player ? "킥복서" : opponents[activeOpponent].label) + " 패배";
    }

    public void ReviveAll()
    {
        foreach (var g in new[] { player, Opp })
        {
            if (!g) continue;
            var d = g.GetComponent<DefeatPose>();
            if (d && d.IsDefeated) d.Revive();
        }
    }

    static bool Defeated(GameObject g) { var d = g ? g.GetComponent<DefeatPose>() : null; return d && d.IsDefeated; }

    // ───────── 거리 자동 맞추기 ─────────
    [HideInInspector] public bool calibrating;
    public int calibrationRepeats = 3;

    public void StartCalibration() { if (!calibrating && Application.isPlaying) StartCoroutine(Calibrate()); }

    System.Collections.IEnumerator Calibrate()
    {
        calibrating = true;
        var prevR = reaction; var prevC = control; int prevO = activeOpponent; float prevTs = Time.timeScale;
        bool prevLearn = learnDistance;
        reaction = Reaction.Hit; learnDistance = true; Time.timeScale = 2f;
        string[] trig = { "Jab", "Cross", "HighKick", "LowKick" };
        for (int o = 0; o < opponents.Count; o++)
        {
            if (!opponents[o].fighter) continue;
            SelectOpponent(o);
            for (int side = 0; side < 2; side++)
            {
                SetControl((Side)side);
                for (int st = 0; st < 4; st++)
                    for (int rep = 0; rep < calibrationRepeats; rep++)
                    {
                        exStrike = -1; PlaceNeutral(true);
                        yield return new WaitForSeconds(0.35f);
                        A(Attacker).SetTrigger(trig[st]);
                        float t = 0f;
                        while (exStrike < 0 && t < 1.5f) { t += Time.deltaTime; yield return null; }
                        while (exStrike >= 0) yield return null;
                        t = 0f;
                        while (t < 5f && !(IsIdle(A(player)) && IsIdle(A(Opp)) && !KB(player).IsPushing && !KB(Opp).IsPushing)) { t += Time.deltaTime; yield return null; }
                    }
            }
        }
        Time.timeScale = prevTs; reaction = prevR; learnDistance = prevLearn;
        SelectOpponent(prevO); SetControl(prevC);
        Debug.Log("[FightTest] 거리 맞추기 완료\n" + DistanceReport());
        lastInfo = "거리 맞추기 완료";
        calibrating = false;
    }

    public string DistanceReport()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in opponents)
        {
            sb.Append(o.label).Append(" | 킥복서→상대 ");
            foreach (var d in o.playerAttackDistance) sb.Append(d.ToString("F3")).Append(' ');
            sb.Append("| 상대→킥복서 ");
            foreach (var d in o.opponentAttackDistance) sb.Append(d.ToString("F3")).Append(' ');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ───────── 화면 패널 (오른쪽) ─────────
    void OnGUI()
    {
        if (!showPanel || opponents.Count == 0) return;
        float s = Mathf.Clamp(Screen.height / 900f, 0.4f, 1.25f);
        float w = 280 * s, h = 40 * s, gap = 6 * s;
        float x = Screen.width - w - 20 * s, y = 20 * s;
        GUI.skin.button.fontSize = Mathf.RoundToInt(18 * s);
        GUI.skin.box.fontSize = Mathf.RoundToInt(16 * s);
        GUI.skin.label.fontSize = Mathf.RoundToInt(15 * s);

        GUI.Box(new Rect(x, y, w, 32 * s), "1:1 액션 테스트");
        y += 32 * s + gap;
        GUI.Label(new Rect(x, y, w, 24 * s), "상대 선택 (Tab, F1~F" + opponents.Count + ")");
        y += 24 * s;
        for (int i = 0; i < opponents.Count; i++)
        {
            string t = (i == activeOpponent ? "▶ " : "") + opponents[i].label + " (F" + (i + 1) + ")";
            if (GUI.Button(new Rect(x, y, w, h), t)) SelectOpponent(i);
            y += h + gap;
        }
        y += gap;
        string c = control == Side.Player ? "조종: 킥복서 (X로 바꾸기)" : "조종: " + opponents[activeOpponent].label + " (X로 바꾸기)";
        if (GUI.Button(new Rect(x, y, w, h), c)) SetControl(control == Side.Player ? Side.Opponent : Side.Player);
        y += h + gap;
        string r = reaction == Reaction.Hit ? "맞는 쪽 반응: 맞기 (Z)" : reaction == Reaction.Guard ? "맞는 쪽 반응: 막기 (Z)" : "맞는 쪽 반응: 없음 (Z)";
        if (GUI.Button(new Rect(x, y, w, h), r)) reaction = (Reaction)(((int)reaction + 1) % 3);
        y += h + gap;
        if (GUI.Button(new Rect(x, y, w, h), "원위치·일으키기 (Backspace)")) { exStrike = -1; ReviveAll(); PlaceNeutral(true); }
        y += h + gap;
        if (GUI.Button(new Rect(x, y, w * 0.49f, h), "상대 패배 (P)")) KO(Defender);
        if (GUI.Button(new Rect(x + w * 0.51f, y, w * 0.49f, h), "조종 캐릭 패배 (O)")) KO(Attacker);
        y += h + gap;
        if (GUI.Button(new Rect(x, y, w, h), calibrating ? "거리 맞추는 중..." : "거리 자동 맞추기")) StartCalibration();
        y += h + gap * 2;
        GUI.Box(new Rect(x, y, w, 118 * s), "J 잽 / K 크로스 / U 하이킥 / I 로우킥\n= 조종 중인 캐릭터가 공격,\n상대는 맞는 위치에서 반응\n\n" + lastInfo);
    }
}
