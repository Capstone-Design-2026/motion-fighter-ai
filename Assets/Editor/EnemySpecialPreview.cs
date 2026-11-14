using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 적 11명의 필살기 시안 미리보기 2판 (batchmode 전용). 게임에는 아무것도 적용하지 않는다:
/// 씬·프리팹·전투 코드를 고치지 않고, 실제 스토리 경로로 각 적의 무대를 불러와 이 도구가 직접 연출을 재생하고 녹화한다.
///
/// 규칙(시안): 적의 게이지는 적의 공격이 맞을 때마다 1칸 찬다. 5칸이 차면 AI가 스스로 필살기를 쓴다. 스테이지당 1회.
/// 미리보기에서는 3칸에서 시작해 적이 실제로 두 대를 맞히면(Fighter.AttackLanded) 가득 차고 바로 발동한다.
///
/// 연출은 무협의 기법에서 가져왔다(경공 잔상, 천근추, 사자후, 점혈, 금종조, 격산타우 등). 다른 작품의 고유 기술을 옮긴 것은 아니다.
/// 동작은 지금 있는 애니메이션 상태에 몸 전체의 뛰기·돌기·기울이기를 얹은 임시 연출이다.
/// Unity -batchmode -executeMethod EnemySpecialPreview.Record -redbool-keyboard
/// 결과: Docs/EnemySpecials/ (enemy_specials.mp4, 적별 정지 화면)
/// </summary>
[InitializeOnLoad]
public static partial class EnemySpecialPreview
{
    const string Active = "EnemySpecialPreview.Active", SaveKey = "VisionStriker.Run";
    // 2560×1440 으로 내보낸다. 가로세로 두 배(5120×2880)로 그린 뒤 줄여서 계단 현상을 없앤다.
    const int Fps = 30, Width = 2560, Height = 1440, LoadFrames = 45, SettleFrames = 70, GaugeMax = 5;
    // 시험용 환경 변수: PREVIEW_ONLY="5,9" 는 그 번호의 적만 찍고, PREVIEW_OUT 은 결과 폴더를, PREVIEW_SUPER 는 확대 배율을 바꾼다.
    static string Env(string name) { var value = Environment.GetEnvironmentVariable(name); return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    static int Super => int.TryParse(Env("PREVIEW_SUPER"), out int s) ? Mathf.Clamp(s, 1, 3) : 2;
    public static string Out => Path.GetFullPath(Env("PREVIEW_OUT") ?? "Docs/EnemySpecials");

    sealed class Spec
    {
        public string id, key, owner, hangul, hanja, summary;
        public Color tint, smoke;
        public float range = 3.2f, foeHealth = 1f;
        public int heroGauge;
        public Func<IEnumerator> run;
    }

    static readonly Spec[] Specs =
    {
        new Spec { id = "scout", key = "afterimage_kicks", owner = "산길 척후병", hangul = "잔영연환각", hanja = "殘影連環脚", summary = "경공으로 잔상 둘을 남긴다. 잔상이 먼저 차고 본체는 위에서 떨어진다", tint = new Color(.45f, 1f, .7f), smoke = new Color(.55f, .7f, .62f), run = AfterimageKicks },
        new Spec { id = "river", key = "dragon_rise", owner = "계곡 잠복병", hangul = "잠룡출수", hanja = "潛龍出水", summary = "물안개 속으로 가라앉아 물결로 다가온 뒤 물기둥과 함께 솟구친다", tint = new Color(.25f, .75f, .9f), smoke = new Color(.62f, .76f, .8f), run = DragonRise },
        new Spec { id = "ridge", key = "thousand_weight", owner = "고용 산적", hangul = "천근추지렬", hanja = "千斤墜地裂", summary = "높이 뛰어 온몸으로 내리찍으면 땅이 갈라지고 바위가 튄다", tint = new Color(1f, .55f, .15f), smoke = new Color(.42f, .32f, .22f), range = 3.6f, run = ThousandWeight },
        new Spec { id = "old_ferry", key = "rope_dart", owner = "옛 나루 약탈자", hangul = "승표탈기", hanja = "繩鏢奪氣", summary = "밧줄 표창으로 감아 끌어당기고, 모아 둔 필살기 게이지를 빼앗는다", tint = new Color(.8f, .35f, 1f), smoke = new Color(.12f, .05f, .16f), range = 3.4f, heroGauge = 5, run = RopeDart },
        new Spec { id = "gate", key = "golden_bell_gate", owner = "철문", hangul = "금종폐관", hanja = "金鐘閉關", summary = "금종조를 두르고 육중하게 내려앉으면 쇠문 두 짝이 솟아 닫힌다", tint = new Color(1f, .82f, .42f), smoke = new Color(.3f, .29f, .27f), run = GoldenBellGate },
        new Spec { id = "quarry", key = "flying_boulder", owner = "채석장 정예병", hangul = "격산비석", hanja = "隔山飛石", summary = "땅에서 바위를 띄워 주먹으로 쳐 날리고, 깨진 파편이 덮친다", tint = new Color(1f, .8f, .25f), smoke = new Color(.46f, .4f, .3f), range = 3.6f, run = FlyingBoulder },
        new Spec { id = "east", key = "whirlwind", owner = "능선 정예병", hangul = "선풍승룡", hanja = "旋風昇龍", summary = "제자리에서 돌아 회오리를 보내 띄우고, 뛰어올라 찍는다", tint = new Color(1f, .5f, .7f), smoke = new Color(.75f, .68f, .7f), run = Whirlwind },
        new Spec { id = "camp", key = "three_points", owner = "추포대 추적자", hangul = "삼점봉혈", hanja = "三點封穴", summary = "혈 자리 셋을 찔러 표식을 남기고, 한 박자 뒤 터뜨려 공격을 봉한다", tint = new Color(.75f, 1f, .15f), smoke = new Color(.12f, .18f, .05f), run = ThreePoints },
        new Spec { id = "rescue", key = "prison_roar", owner = "옥졸장", hangul = "진옥후", hanja = "震獄吼", summary = "고함의 음파로 밀어내고, 떨어지는 쇠창살로 가둔다", tint = new Color(1f, .3f, .1f), smoke = new Color(.12f, .04f, .03f), run = PrisonRoar },
        new Spec { id = "entrance", key = "shadow_bite", owner = "흑견", hangul = "흑영교", hanja = "黑影咬", summary = "그림자를 바닥으로 뻗어 발밑에서 물고, 기운을 빨아들여 체력을 회복한다", tint = new Color(.6f, .22f, .95f), smoke = new Color(.02f, 0f, .04f), foeHealth = .55f, run = ShadowBite },
        new Spec { id = "final", key = "black_cloud", owner = "무진", hangul = "흑운압성", hanja = "黑雲壓城", summary = "하늘이 어두워지고 먹구름이 끌어당긴 뒤, 구름 기둥과 벼락이 떨어진다", tint = new Color(1f, .82f, .35f), smoke = new Color(.17f, .16f, .22f), range = 3.8f, run = BlackCloud },
    };

    static IEnumerator flow;
    static int lastFrame = -1;
    static Camera cam; static Canvas hud; static Battle3DCamera rig;
    static RenderTexture big, target; static Texture2D frame; static MediaEncoder all;
    static PlayerController player; static EnemyAI enemy;
    static Fighter hero, foe; static Health heroHp, foeHp; static Fighter3DView foeView, heroView; static SpecialMove heroSpecial;
    static QiPalmAssets assets; static Spec spec; static int specIndex;
    static string still; static int stillIn;
    static System.Random rng;
    static Text gaugeText; static int gauge;
    // 그리기 직전에만 얹는 자세(전투 뷰가 매 프레임 되돌린다)와 카메라 물러나기
    static Vector3 foeLift, heroLift; static float foeSpin, foeTilt, heroSpin, heroTilt, camPull;
    static Light sun; static float sunBase, dimLevel, skyExposure; static Color skyBase, equatorBase, groundBase, flatBase, fogBase; static SphericalHarmonicsL2 probeBase; static Material skyMat;
    static GameObject rockPrefab; static Material darkMat, crackMat;
    static readonly List<Func<bool>> updaters = new List<Func<bool>>();
    static readonly Dictionary<(Material, string), Material> recolored = new Dictionary<(Material, string), Material>();
    static readonly Dictionary<string, Material> glowMats = new Dictionary<string, Material>(), smokeMats = new Dictionary<string, Material>(), softMats = new Dictionary<string, Material>();

    static EnemySpecialPreview() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    public static void Record()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        Directory.CreateDirectory(Out);
        foreach (var file in Directory.GetFiles(Out)) File.Delete(file);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        File.WriteAllText(Path.GetFullPath("Library/EnemySpecialPreview_save_backup.json"), PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 3000) { End("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = Capture(); if (!flow.MoveNext()) End(null); }
        catch (Exception e) { End(e.ToString()); }
    }

    // ───────────── 녹화 흐름 ─────────────

    static IEnumerator Capture()
    {
        yield return null; yield return null;
        Time.captureFramerate = Fps;
        big = new RenderTexture(Width * Super, Height * Super, 24) { filterMode = FilterMode.Bilinear }; big.Create();
        target = new RenderTexture(Width, Height, 0); target.Create();
        frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        all = NewEncoder(Out + "/enemy_specials.mp4");
        assets = QiPalmAssets.Load();
        if (assets == null) throw new Exception("QiPalmAssets missing");
        rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environments/ScoutPass/Prefabs/LowBoulder.prefab");
        if (rockPrefab == null) throw new Exception("LowBoulder prefab missing");
        BuildLook(); SetupPostFx();
        darkMat = FlatMaterial(Texture2D.whiteTexture, new Color(.012f, .01f, .016f, 1f), 1f);
        crackMat = FlatMaterial(texCrack, new Color(.36f, .29f, .23f, 1f), 0f);
        var only = new HashSet<int>();
        foreach (var part in (Env("PREVIEW_ONLY") ?? "").Split(',')) if (int.TryParse(part.Trim(), out int number)) only.Add(number);

        for (specIndex = 0; specIndex < Specs.Length; specIndex++)
        {
            if (only.Count > 0 && !only.Contains(specIndex + 1)) continue;
            spec = Specs[specIndex]; updaters.Clear(); still = null; rng = new System.Random(100 + specIndex); smokeFlash = 0f;
            foeLift = heroLift = Vector3.zero; foeSpin = foeTilt = heroSpin = heroTilt = camPull = 0f;
            StartStoryBattle(spec.id);
            string scene = SceneRoutes.PendingBattleScene; SceneRoutes.LoadBattle();
            for (int k = 0; k < LoadFrames; k++) yield return null;
            if (SceneManager.GetActiveScene().name != scene) throw new Exception(spec.id + ": expected scene " + scene + " but got " + SceneManager.GetActiveScene().name);

            player = Object.FindAnyObjectByType<PlayerController>(); enemy = Object.FindAnyObjectByType<EnemyAI>();
            if (player == null || enemy == null) throw new Exception(spec.id + ": fighters not found");
            foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>()) udp.enabled = false;
            cam = Camera.main; rig = cam.GetComponent<Battle3DCamera>(); if (rig != null) rig.allowMouseOrbit = false;
            var combatHud = Object.FindAnyObjectByType<CombatHUD>(); hud = combatHud != null ? combatHud.GetComponentInChildren<Canvas>() : null;
            if (hud != null)
            {
                var guide = hud.transform.Find("KeyGuide"); if (guide != null) guide.gameObject.SetActive(false);
                hud.renderMode = RenderMode.ScreenSpaceCamera; hud.worldCamera = cam; hud.planeDistance = cam.nearClipPlane + .1f;
            }
            cam.targetTexture = big; cam.aspect = (float)Width / Height; PrepareCamera();

            player.enabled = false; enemy.enabled = false;
            foreach (var fighter in Object.FindObjectsByType<Fighter>()) { fighter.CancelAttack(); fighter.InputLocked = false; fighter.IsBlocking = false; fighter.SetVisionPreparationLock(false); }
            hero = player.GetComponent<Fighter>(); foe = enemy.GetComponent<Fighter>();
            heroHp = player.GetComponent<Health>(); foeHp = enemy.GetComponent<Health>();
            foeView = enemy.GetComponent<Fighter3DView>(); heroView = player.GetComponent<Fighter3DView>(); heroSpecial = player.GetComponent<SpecialMove>();
            hero.Facing = 1; foe.Facing = -1;
            Place(player.gameObject, -1.3f); Place(enemy.gameObject, 1.3f);
            if (rig != null) rig.ResetOrbit();
            CaptureLighting();
            gauge = 3; Label();
            foe.AttackLanded += OnFoeLanded;
            for (int k = 0; k < SettleFrames; k++) yield return null;
            heroHp.SetCurrentHealth(heroHp.MaxHealth);
            if (spec.foeHealth < 1f) foeHp.SetCurrentHealth(Mathf.RoundToInt(foeHp.MaxHealth * spec.foeHealth));
            if (spec.heroGauge > 0 && heroSpecial != null) heroSpecial.AddCharge(spec.heroGauge);

            for (int k = 0; k < 12; k++) { yield return null; Grab(); }
            var sequence = Flatten(Whole());
            while (sequence.MoveNext()) { StepUpdaters(); yield return null; Grab(); }
            for (int k = 0; k < 36; k++) { StepUpdaters(); yield return null; Grab(); }
            QiPostFx.Set(0f); QiCameraFx.ResetAll();
            foe.AttackLanded -= OnFoeLanded;
            Debug.Log("[EnemySpecialPreview] " + (specIndex + 1).ToString("00") + " " + spec.owner + " · " + spec.hangul + " scene=" + scene + " enemy=" + foeHp.DisplayName + " gauge=" + gauge + " hero hp=" + heroHp.CurrentHealth + " foe hp=" + foeHp.CurrentHealth);
            cam.targetTexture = null;
        }
        all.Dispose(); all = null;
    }

    /// <summary>게이지 채우기(실제 공격 두 대) → 물러나기 → 필살기.</summary>
    static IEnumerator Whole()
    {
        // 적이 다가와 실제로 두 대를 맞힌다. 맞을 때마다 게이지가 1칸 찬다. 빗나가면 차지 않고, 거리를 고쳐 다시 친다.
        foreach (var move in new[] { CombatMove.Jab, CombatMove.Straight })
        {
            bool landed = false;
            foreach (float reach in new[] { .82f, .98f, .68f, 1.1f })
            {
                if (Mathf.Abs(FoeX - HeroX - reach) > .04f) { Slide(enemy.gameObject, HeroX + reach, .22f); yield return Hold(.26f); }
                int before = gauge; bool started = foe.TryAttack(move);
                for (int k = 0; k < 24 && gauge == before; k++) yield return null;
                for (int k = 0; k < 45 && foe.IsAttacking; k++) yield return null;
                if (gauge > before) { landed = true; break; }
                Debug.Log("[EnemySpecialPreview] " + spec.owner + ": " + move + " missed at " + reach + " (started=" + started + "), stepping and trying again");
            }
            if (!landed) { Debug.LogWarning("[EnemySpecialPreview] " + spec.owner + ": " + move + " never landed, the preview advanced the gauge itself"); AddGauge(); }
            yield return Hold(.2f);
        }
        foe.CancelAttack();
        // 게이지가 가득 찼다: 거리를 벌리고 스스로 발동한다.
        Slide(enemy.gameObject, HeroX + spec.range, .45f);
        yield return Hold(.65f);
        yield return spec.run();
    }
    static void OnFoeLanded(Fighter hit) { AddGauge(); }
    static void AddGauge()
    {
        gauge = Mathf.Min(GaugeMax, gauge + 1);
        if (gaugeText != null) gaugeText.text = GaugeLine();
    }
    static string GaugeLine()
    {
        string pips = new string('●', gauge) + new string('○', GaugeMax - gauge);
        return "적 게이지  " + pips + (gauge >= GaugeMax ? "   가득 참 → 스스로 발동" : "   공격이 맞으면 1칸") + "   ·   스테이지당 1회";
    }
    static void Note(string text) { if (gaugeText != null) gaugeText.text = text; }

    static IEnumerator Flatten(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator child) { stack.Push(child); continue; }
            yield return null;
        }
    }
    static IEnumerator Hold(float seconds) { int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps)); for (int i = 0; i < n; i++) yield return null; }
    static void StepUpdaters() { for (int i = updaters.Count - 1; i >= 0; i--) if (updaters[i]()) updaters.RemoveAt(i); }

    static void StartStoryBattle(string id)
    {
        var run = RunState.Instance;
        if (run == null) throw new Exception("RunState is missing");
        var data = new RunData { seed = 684, playerHealth = 100, playerMaxHealth = 100, pendingNodeId = id, currentNodeId = "cave", tutorialCompleted = true, tutorialVersion = 1, storyPresentationVersion = 1 };
        data.map = MapGenerator.Generate(run.Config.map, run.Config, data.seed);
        typeof(RunState).GetProperty("Data").SetValue(run, data);
    }

    // ───────────── 기본 도구 ─────────────

    static float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
    static void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var body = fighter.GetComponent<Rigidbody2D>(); if (body != null) { body.position = new Vector2(x, body.position.y); body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }
    static float HeroX => hero.transform.position.x;
    static float FoeX => foe.transform.position.x;
    /// <summary>카메라에서 먼 쪽(무대 안쪽)을 가리키는 방향.</summary>
    static Vector3 Far => cam != null && cam.transform.position.z > 0f ? Vector3.back : Vector3.forward;
    static Vector3 Hands()
    {
        var anim = foeView != null ? foeView.modelAnimator : null;
        if (anim != null && anim.isHuman)
        {
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand); var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (l && r) return (l.position + r.position) * .5f;
        }
        return foe.transform.position + Vector3.up * 1.15f + Vector3.left * .4f;
    }
    static Transform HeroBoneTransform(HumanBodyBones bone)
    {
        var anim = heroView != null ? heroView.modelAnimator : null;
        return anim != null && anim.isHuman ? anim.GetBoneTransform(bone) : null;
    }
    static Vector3 HeroBone(HumanBodyBones bone, float fallbackHeight)
    {
        var t = HeroBoneTransform(bone);
        return t ? t.position : new Vector3(HeroX, fallbackHeight, 0);
    }
    static Vector3 HeroChest => HeroBone(HumanBodyBones.Chest, 1.25f);
    static Vector3 Ground(float x) => new Vector3(x, 0f, 0f);
    static readonly Quaternion Left = Quaternion.LookRotation(Vector3.left, Vector3.up);
    static readonly Quaternion Down = Quaternion.LookRotation(Vector3.down, Vector3.right), Up = Quaternion.LookRotation(Vector3.up, Vector3.right);

    static void Tween(float seconds, Action<float> step, Action done = null)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps)), k = 0;
        updaters.Add(() => { k++; step(Mathf.Clamp01(k / (float)n)); if (k < n) return false; done?.Invoke(); return true; });
    }
    static void After(float seconds, Action action) => Tween(seconds, u => { }, action);
    static void Move(Transform t, Vector3 to, float seconds, Action done = null, int ease = 0)
    {
        Vector3 from = t.position;
        Tween(seconds, u => { if (!t) return; float e = ease == 1 ? u * u : ease == 2 ? 1 - (1 - u) * (1 - u) : u; t.position = Vector3.LerpUnclamped(from, to, e); }, done);
    }
    static void Slide(GameObject fighter, float toX, float seconds)
    {
        float from = fighter.transform.position.x;
        Tween(seconds, u => Place(fighter, Mathf.Lerp(from, toX, u)));
    }
    /// <summary>몸 전체가 포물선으로 뛴다(애니메이션 위에 얹는다).</summary>
    static void Jump(bool isFoe, float height, float seconds, Action landed = null)
    {
        Tween(seconds, u => { float y = 4 * height * u * (1 - u); if (isFoe) foeLift.y = y; else heroLift.y = y; }, () => { if (isFoe) foeLift.y = 0; else heroLift.y = 0; landed?.Invoke(); });
    }
    /// <summary>주인공이 떠올랐다 떨어진다. 뒤로 젖혀진다.</summary>
    static void Launch(float height, float seconds, float tilt)
    {
        Jump(false, height, seconds);
        Tween(seconds, u => heroTilt = tilt * Mathf.Sin(u * Mathf.PI), () => heroTilt = 0f);
    }
    /// <summary>카메라가 물러나 큰 기술을 한 화면에 담는다(그리기 직전에만 적용).</summary>
    static void Pull(float to, float seconds) { float from = camPull; Tween(seconds, u => camPull = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u))); }
    static void Shake(float amplitude, float decay = 6f) => QiCameraFx.Shake(amplitude * .02f, decay);
    static void Snap(string name, int afterFrames = 3) { still = name; stillIn = afterFrames; }
    static void ShowFoe(bool visible) { foreach (var r in foeView.visualRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = visible; }

    static void CaptureLighting()
    {
        sun = RenderSettings.sun;
        if (sun == null) foreach (var l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
        sunBase = sun != null ? sun.intensity : 1f; dimLevel = 1f;
        skyBase = RenderSettings.ambientSkyColor; equatorBase = RenderSettings.ambientEquatorColor; groundBase = RenderSettings.ambientGroundColor; flatBase = RenderSettings.ambientLight;
        fogBase = RenderSettings.fogColor; probeBase = RenderSettings.ambientProbe;
        // 하늘 재질은 복사본으로 바꿔 쓴다(원본 재질 파일을 건드리지 않기 위해).
        skyMat = RenderSettings.skybox != null ? new Material(RenderSettings.skybox) : null;
        if (skyMat != null) { RenderSettings.skybox = skyMat; skyExposure = skyMat.HasProperty("_Exposure") ? skyMat.GetFloat("_Exposure") : 1f; }
    }
    static void SetDim(float k)
    {
        dimLevel = k;
        if (sun != null) sun.intensity = sunBase * k;
        RenderSettings.ambientSkyColor = skyBase * k; RenderSettings.ambientEquatorColor = equatorBase * k; RenderSettings.ambientGroundColor = groundBase * k; RenderSettings.ambientLight = flatBase * k;
        RenderSettings.ambientProbe = probeBase * k;
        RenderSettings.fogColor = fogBase * Mathf.Lerp(.3f, 1f, k);
        if (skyMat != null && skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExposure * Mathf.Lerp(.25f, 1f, k));
    }
    /// <summary>주변을 어둡게(1 = 원래 밝기). 발동 순간과 큰 기술에서 쓴다.</summary>
    static void Dim(float to, float seconds) { float from = dimLevel; Tween(seconds, u => SetDim(Mathf.Lerp(from, to, u))); }

    // ───────────── 이펙트 도구 ─────────────

    static Color Recolor(Color c, Color tint)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        if (max <= 0f) return c;
        var hue = Color.Lerp(Color.white, tint, Mathf.Clamp01((1f - min / max) * 1.35f));
        return new Color(hue.r * max, hue.g * max, hue.b * max, c.a);
    }
    static Gradient Recolor(Gradient g)
    {
        if (g == null) return null;
        var keys = g.colorKeys; for (int i = 0; i < keys.Length; i++) keys[i].color = Recolor(keys[i].color, spec.tint);
        var result = new Gradient(); result.SetKeys(keys, g.alphaKeys); result.mode = g.mode; return result;
    }
    static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient c)
    {
        switch (c.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Recolor(c.color, spec.tint));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Recolor(c.colorMin, spec.tint), Recolor(c.colorMax, spec.tint));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Recolor(c.gradient));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(c.gradientMin), Recolor(c.gradientMax));
            default: return c;
        }
    }
    static void Tint(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i]; if (!m) continue;
                var key = (m, spec.key);
                if (!recolored.TryGetValue(key, out var n))
                {
                    n = new Material(m); string shader = m.shader.name;
                    if (shader.EndsWith("QiSmoke")) { var c = spec.smoke; c.a = m.GetColor("_Color").a; n.SetColor("_Color", c); }
                    else if (shader.EndsWith("QiOrb")) { n.SetColor("_CoreColor", Recolor(m.GetColor("_CoreColor"), spec.tint)); n.SetColor("_RimColor", Recolor(m.GetColor("_RimColor"), spec.tint)); }
                    else if (n.HasProperty("_Color")) n.SetColor("_Color", Recolor(m.GetColor("_Color"), spec.tint));
                    recolored[key] = n;
                }
                mats[i] = n;
            }
            r.sharedMaterials = mats;
        }
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.color = Recolor(l.color, spec.tint);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main; main.startColor = Recolor(main.startColor);
            var over = ps.colorOverLifetime; if (over.enabled) over.color = Recolor(over.color);
        }
        foreach (var trail in go.GetComponentsInChildren<TrailRenderer>(true)) trail.colorGradient = Recolor(trail.colorGradient);
    }
    static GameObject Fx(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f)
    {
        if (prefab == null) return null;
        var go = Object.Instantiate(prefab, position, rotation); go.transform.localScale = Vector3.one * scale; Tint(go); return go;
    }
    static GameObject Burst(Vector3 position, float scale) => Fx(assets.impact, position, Quaternion.identity, scale);
    /// <summary>기파 몸체만 빌려 쓴다(판정 스크립트는 끄고 이 도구가 직접 움직인다).</summary>
    static GameObject Wave(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = Object.Instantiate(assets.wave, position, rotation);
        var wave = go.GetComponent<QiWave>(); if (wave != null) wave.enabled = false;
        go.transform.localScale = scale; Tint(go); return go;
    }
    static void EndWave(GameObject go)
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
        Object.Destroy(go, .7f);
    }
    static void Strike(int damage, Vector2 knockback, float fxScale, float shake, float height = 1.15f)
    {
        Vector3 p = new Vector3(HeroX, height, 0f);
        heroHp.TakeDamage(damage, knockback, false, true);
        if (fxScale > 0f) Burst(p, fxScale);
        Shake(shake, 7f);
    }

    static Material Glow(float intensity = 2.4f)
    {
        string key = spec.key + intensity.ToString("0.00");
        if (!glowMats.TryGetValue(key, out var m))
        {
            // 띠 가운데만 하얗게 달아오르고 가장자리는 제 색으로 번진다. 5 이상은 벼락 속심처럼 통째로 하얗다.
            bool core = intensity >= 5f;
            m = RibbonMaterial(texRibbon, Hdr(spec.tint, core ? intensity : intensity * .8f), core ? 0f : .4f, core ? 1f : .6f, 0f, new Vector4(-1.7f, .12f, 3f, 1f));
            glowMats[key] = m;
        }
        return m;
    }
    static Material SmokeMaterial()
    {
        if (smokeMats.TryGetValue(spec.key, out var m)) return m;
        // 덩어리 안쪽에 명암이 있고, 바닥에 닿는 곳이 부드럽게 사라지며, 번쩍일 때 속이 밝아지는 연기
        m = new Material(smokeShader); m.SetTexture("_MainTex", texSmoke);
        var c = spec.smoke; c.a = .9f; m.SetColor("_Color", c); m.SetColor("_Glow", Color.black); m.SetFloat("_Shade", .42f); m.SetFloat("_Soft", 1.5f); m.SetFloat("_GroundFade", .45f);
        return smokeMats[spec.key] = m;
    }
    /// <summary>부드럽게 번지는 빛 덩이(구름 속 번쩍임 등).</summary>
    static Material SoftGlow()
    {
        if (softMats.TryGetValue(spec.key, out var m)) return m;
        m = RibbonMaterial(assets.glow != null ? (Texture)assets.glow : Texture2D.whiteTexture, Hdr(spec.tint, 1f), 0f, .3f, 1.2f, Vector4.zero);
        return softMats[spec.key] = m;
    }
    static void Puff(Vector3 position, float size, float life) => Smoke(position, Vector3.one * .05f, size, 0, .1f, life, 0f, 1, 1f, SoftGlow());
    /// <summary>연기·안개·먼지·먹구름. rate 는 초당 개수, burst 는 한 번에 터지는 개수.</summary>
    static GameObject Smoke(Vector3 center, Vector3 box, float size, float rate, float duration, float life, float rise, int burst = 0, float alpha = .6f, Material material = null)
    {
        var go = new GameObject("Preview smoke"); go.transform.position = center;
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
        ps.Play(); Object.Destroy(go, duration + life + .6f);
        return go;
    }
    static void Flash(Vector3 position, float intensity, float range, float life)
    {
        var go = new GameObject("Preview flash"); go.transform.position = position;
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = Color.Lerp(spec.tint, Color.white, .3f); l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
        smokeFlash = Mathf.Max(smokeFlash, intensity * .028f);   // 번쩍이는 순간 연기 속도 밝아진다
        Tween(life, u => { if (l) l.intensity = intensity * (1f - u); }, () => { if (go) Object.Destroy(go); });
    }
    /// <summary>빛나는 띠 재질은 가장자리가 번지며 사라지므로, 눈에 보이는 굵기가 비슷해지게 이만큼 넓혀 그린다.</summary>
    const float RibbonWidth = 2.3f;
    static LineRenderer Line(Material m, float width0, float width1, float life, bool loop = false, bool flat = false, bool fadeEnds = true)
    {
        var go = new GameObject("Preview line"); var lr = go.AddComponent<LineRenderer>();
        if (flat) { go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); lr.alignment = LineAlignment.TransformZ; }
        bool ribbon = m != null && m.shader == ribbonShader; float k = ribbon ? RibbonWidth : 1f;
        lr.sharedMaterial = m; lr.widthCurve = AnimationCurve.Linear(0, width0 * k, 1, width1 * k); lr.widthMultiplier = 1f; lr.numCapVertices = ribbon ? 0 : 2; lr.numCornerVertices = 3; lr.useWorldSpace = true; lr.loop = loop;
        lr.textureMode = LineTextureMode.Stretch;
        if (ribbon && !loop && fadeEnds)
        {
            var ends = new Gradient();
            ends.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(1f, .88f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = ends;
        }
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.positionCount = 0;
        Object.Destroy(go, life); return lr;
    }
    /// <summary>빠르게 스치는 한 줄기(바람 가르는 선, 물줄기).</summary>
    static void Streak(Vector3 from, Vector3 to, float width, float life, float intensity = 2.4f)
    {
        var lr = Line(Glow(intensity), width * .2f, width, life + .03f); lr.positionCount = 2;
        Tween(life, u => { if (!lr) return; lr.SetPosition(0, Vector3.Lerp(from, to, Mathf.Clamp01(u * 1.6f - .6f))); lr.SetPosition(1, Vector3.Lerp(from, to, Mathf.Clamp01(u * 2.2f))); lr.widthMultiplier = 1f - u * .7f; });
    }
    /// <summary>벼락 한 줄기: 꺾이는 굵은 줄 위에 흰 속심, 곁가지 둘.</summary>
    static void Bolt(Vector3 from, Vector3 to, float width, float life, int forks = 2)
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
            Vector3 a = main[rng.Next(1, main.Length - 2)];
            Draw(Strand(a, a + new Vector3(Rand(-1.3f, 1.3f), Rand(-1.5f, -.5f), Rand(-.5f, .5f)), .16f), width * .5f);
        }
    }
    /// <summary>
    /// 바닥을 타고 뻗는 금. 금은 바닥에 눕히고, 금 양옆으로 들뜬 돌 조각과 새어 나오는 빛을 세워
    /// 낮은 카메라에서도 땅이 깨진 것으로 읽히게 한다.
    /// </summary>
    static void Cracks(Vector3 origin, float dirX, float length, int count, float spreadDegrees, float grow, float life = 2.6f)
    {
        for (int c = 0; c < count; c++)
        {
            float angle = (count == 1 ? 0 : Mathf.Lerp(-spreadDegrees, spreadDegrees, c / (count - 1f))) + Rand(-5, 5);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * new Vector3(dirX, 0, 0), side = Vector3.Cross(Vector3.up, dir).normalized, p = origin;
            float len = length * Rand(.7f, 1f); int pts = Mathf.Max(6, Mathf.RoundToInt(len / .26f));
            var path = new Vector3[pts];
            for (int i = 0; i < pts; i++) { path[i] = p + Vector3.up * .03f; p += dir * (len / pts) + side * Rand(-.2f, .2f); }
            // 들쭉날쭉한 틈 무늬(끝으로 갈수록 가늘어지는 모양은 무늬에 들어 있다)와 틈 속에서 새는 빛
            foreach (var layer in new[] { (crackMat, .62f, .5f, 0f), (Glow(2.4f), .1f, .02f, .012f) })
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
                        if (rng.NextDouble() < .3) continue;
                        float size = Rand(.16f, .44f) * taper;
                        var rock = Rock(at + side * (k * Rand(.12f, .26f)) + Vector3.down * size * .28f, size); Vector3 full = rock.transform.localScale, rest = rock.transform.position;
                        rock.transform.localScale = full * .05f;
                        Tween(.09f, u => { if (!rock) return; rock.transform.localScale = full * Mathf.Lerp(.05f, 1f, u); rock.transform.position = rest + Vector3.up * (.16f * Mathf.Sin(u * Mathf.PI)); });
                        Object.Destroy(rock, Mathf.Max(.2f, life - delay));
                    }
                });
            }
        }
    }
    /// <summary>고리. normal 에 수직인 원을 그리고, 시간에 따라 반지름과 중심을 바꾼다.</summary>
    static LineRenderer Ring(Func<float, Vector3> center, Vector3 normal, Func<float, float> radius, float seconds, float width, float intensity = 2.4f, bool fade = true)
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
    /// <summary>옆에서 보이는 음파 호: 중심에서 -x 쪽으로 퍼지는 부채꼴 곡선.</summary>
    static void Arc(Func<float, Vector3> center, Func<float, float> radius, float halfDegrees, float seconds, float width, float intensity = 2.6f)
    {
        var lr = Line(Glow(intensity), width, width, seconds + .05f); const int n = 28; lr.positionCount = n;
        Tween(seconds, u =>
        {
            if (!lr) return; Vector3 c = center(u); float r = radius(u);
            for (int i = 0; i < n; i++) { float a = Mathf.Lerp(-halfDegrees, halfDegrees, i / (n - 1f)) * Mathf.Deg2Rad; lr.SetPosition(i, c + new Vector3(-Mathf.Cos(a), Mathf.Sin(a), 0) * r); }
            lr.widthMultiplier = 1f - Mathf.Clamp01((u - .5f) / .5f);
        });
    }
    /// <summary>허공에 뜨는 큰 글자 진(陣). 카메라가 낮아 바닥 글씨는 안 보이므로 상대 뒤에 세운다. 글자 둘레로 부적 문양이 천천히 돈다.</summary>
    static void Seal(string glyph, Vector3 center, float size, float life)
    {
        var go = new GameObject("Seal glyph"); go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(Far));
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = -5;
        ((RectTransform)go.transform).sizeDelta = new Vector2(400, 400);
        var text = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Text>(); text.transform.SetParent(go.transform, false);
        text.text = glyph; text.font = WuxiaTheme.MenuFont; text.fontSize = 300; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; text.rectTransform.sizeDelta = new Vector2(400, 400);
        var color = Color.Lerp(spec.tint, Color.white, .25f);
        var halo = text.gameObject.AddComponent<Outline>(); halo.effectColor = new Color(color.r, color.g, color.b, .35f); halo.effectDistance = new Vector2(2.5f, 2.5f);
        // 글자 뒤: 겹고리·눈금·팔괘가 그려진 부적 문양과, 넓게 번지는 빛
        const float discSize = 1.55f;
        var discMat = RibbonMaterial(texTalisman, Hdr(spec.tint, 1.15f), 0f, .2f, 0f, Vector4.zero);
        var disc = Quad(center + Far * .03f, Quaternion.LookRotation(Far), size * discSize, discMat);
        var hazeMat = RibbonMaterial(assets.glow != null ? (Texture)assets.glow : Texture2D.whiteTexture, Hdr(spec.tint, .16f), 0f, 0f, 0f, Vector4.zero);
        var haze = Quad(center + Far * .06f, Quaternion.LookRotation(Far), size * 2.1f, hazeMat);
        Tween(life, u =>
        {
            if (!go) return;
            float pop = Mathf.Lerp(1.5f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .14f))), fade = Mathf.Clamp01(u / .08f) * (1f - Mathf.Clamp01((u - .7f) / .3f));
            go.transform.localScale = Vector3.one * (size / 400f) * pop;
            text.color = new Color(color.r, color.g, color.b, .92f * fade);
            if (disc) { disc.transform.localScale = Vector3.one * (size * discSize * Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .2f)))); disc.transform.Rotate(0f, 0f, 16f / Fps, Space.Self); }
            discMat.SetFloat("_Intensity", fade); hazeMat.SetFloat("_Intensity", fade * .8f);
        }, () => { if (go) Object.Destroy(go); if (disc) Object.Destroy(disc); if (haze) Object.Destroy(haze); });
    }
    /// <summary>실제 바위 모델. 큰 것은 던지고, 작은 것은 파편으로 튄다.</summary>
    static GameObject Rock(Vector3 position, float size)
    {
        var go = Object.Instantiate(rockPrefab, position, Quaternion.Euler(Rand(0, 360), Rand(0, 360), Rand(0, 360)));
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Object.Destroy(c); }
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
    static void Toss(GameObject go, Vector3 velocity, float life)
    {
        Vector3 spin = new Vector3(Rand(-400, 400), Rand(-400, 400), Rand(-400, 400)); int bounces = 0;
        updaters.Add(() =>
        {
            if (!go) return true;
            velocity += Vector3.down * (16f / Fps); go.transform.position += velocity / Fps; go.transform.Rotate(spin / Fps, Space.World);
            if (go.transform.position.y < .06f && velocity.y < 0)
            {
                var p = go.transform.position; p.y = .06f; go.transform.position = p; velocity = new Vector3(velocity.x * .45f, -velocity.y * .32f, velocity.z * .45f);
                if (++bounces > 2 || velocity.y < .7f) return true;
            }
            return false;
        });
        Object.Destroy(go, life);
    }
    static void Debris(Vector3 origin, int count, float speed, float sizeMin, float sizeMax, float dirX = 0f)
    {
        for (int i = 0; i < count; i++)
            Toss(Rock(origin + new Vector3(Rand(-.3f, .3f), .12f, Rand(-.3f, .3f)), Rand(sizeMin, sizeMax)), new Vector3(dirX * Rand(.5f, 2.5f) + Rand(-1.6f, 1.6f), Rand(.6f, 1f) * speed, Rand(-1.2f, 1.2f)), 2.4f);
    }
    static GameObject Part(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        var collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Object.Destroy(collider); }
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = local; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
    /// <summary>적의 지금 모습을 본뜬 잔상(dark 면 검은 그림자 분신). 지정한 동작을 재생하고 사라진다.</summary>
    static GameObject Ghost(Vector3 position, Vector3 lookAt, string state, float life, bool dark = false, float speed = 1f)
    {
        var source = foeView.visualRoot.gameObject;
        // 꺼진 부모 밑에 복제해 전투 스크립트가 한 번도 깨어나지 않게 한 뒤 떼어 낸다.
        var holder = new GameObject("Ghost holder"); holder.SetActive(false);
        var go = Object.Instantiate(source, holder.transform);
        for (int pass = 0; pass < 2; pass++) foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb) Object.DestroyImmediate(mb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) Object.DestroyImmediate(c);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
        var look = lookAt - position; look.y = 0f;
        go.transform.SetParent(null, false);
        go.transform.SetPositionAndRotation(new Vector3(position.x, source.transform.position.y - foeLift.y, position.z), look.sqrMagnitude > .001f ? Quaternion.LookRotation(look) : source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        Object.Destroy(holder);
        var anim = go.GetComponentInChildren<Animator>(); if (anim != null) { anim.applyRootMotion = false; anim.speed = speed; anim.Play(state, 0, 0f); }
        void Paint(GameObject body, Material material)
        {
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true; var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = material; r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off; r.SetPropertyBlock(null);
            }
        }
        // 잔상은 가장자리가 빛나는 기운 몸체로 그린다. 그림자 분신은 검은 몸 위에, 같은 동작을 하는 빛 테두리를 겹친다.
        var orb = OrbMaterial(dark ? Color.black : Hdr(spec.tint, .2f), Hdr(spec.tint, dark ? 1.6f : 2.3f), dark ? 2.6f : 1.7f);
        if (dark)
        {
            Paint(go, darkMat);
            var rim = Object.Instantiate(go, go.transform); rim.transform.localPosition = Vector3.zero; rim.transform.localRotation = Quaternion.identity; rim.transform.localScale = Vector3.one;
            Paint(rim, orb);
            var rimAnim = rim.GetComponentInChildren<Animator>(); if (rimAnim != null) { rimAnim.applyRootMotion = false; rimAnim.speed = speed; rimAnim.Play(state, 0, 0f); }
        }
        else Paint(go, orb);
        Tween(life, u => orb.SetFloat("_Alpha", 1f - Mathf.Clamp01((u - .65f) / .35f)), () => { if (go) Object.Destroy(go); });
        return go;
    }
    /// <summary>한쪽에서 다른 쪽으로 빨려 가는 기운 알갱이.</summary>
    static void Drain(Func<Vector3> from, Func<Vector3> to, int count, float seconds)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 wobble = new Vector3(0, Rand(-.4f, .4f), Rand(-.3f, .3f));
            After(seconds * i / count, () =>
            {
                var orb = Wave(from(), Left, Vector3.one * .16f);
                Tween(.28f, u => { if (!orb) return; orb.transform.position = Vector3.Lerp(from(), to(), u) + wobble * Mathf.Sin(u * Mathf.PI); }, () => EndWave(orb));
            });
        }
    }

    static QiCutIn CutIn()
    {
        var cut = QiCutIn.Play(assets.brush, 1); cut.enabled = false;
        foreach (var t in cut.GetComponentsInChildren<Text>(true))
        {
            if (t.name == "Hanja") t.text = spec.hanja;
            else if (t.name == "Hangul") t.text = string.Join(" ", spec.hangul.ToCharArray());
            var outline = t.GetComponent<Outline>(); if (outline) outline.effectColor = new Color(spec.tint.r * .8f, spec.tint.g * .8f, spec.tint.b * .8f, .85f);
        }
        var c = cut.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + .08f;
        cut.Seek(0f); return cut;
    }
    static void Label()
    {
        var go = new GameObject("Preview label"); var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + .09f; c.sortingOrder = 500;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        Text Row(string text, int size, float y, Color color)
        {
            var t = new GameObject("Line", typeof(RectTransform)).AddComponent<Text>(); t.transform.SetParent(go.transform, false);
            t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAnchor.MiddleCenter; t.font = WuxiaTheme.MenuFont; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, 0f); r.sizeDelta = new Vector2(1800, size * 1.4f); r.anchoredPosition = new Vector2(0, y);
            var shadow = t.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .9f); shadow.effectDistance = new Vector2(3, -3);
            return t;
        }
        Row((specIndex + 1).ToString("00") + "  " + spec.owner + "  ·  " + spec.hangul + " " + spec.hanja, 46, 136, new Color(1f, .95f, .86f));
        Row(spec.summary, 27, 88, new Color(.9f, .86f, .8f));
        gaugeText = Row(GaugeLine(), 25, 44, Color.Lerp(spec.tint, Color.white, .45f));
    }

    /// <summary>발동: 주변이 어두워지고 컷인이 지나간 뒤(게임에서는 시간이 느려지는 구간) 캐릭터마다 다른 시간만큼 기를 모은다.</summary>
    static IEnumerator Gather(float seconds, float scale, string state = "SPECIAL_CHARGE")
    {
        if (!foe.BeginSpecial()) Debug.LogWarning("[EnemySpecialPreview] BeginSpecial refused for " + spec.owner);
        Note("필살기 발동   ·   이 스테이지에서는 다시 쓰지 않는다");
        QiPostFx.Set(1f); Dim(.6f, .25f);
        var cutIn = CutIn(); foeView.PlaySpecial(state);
        var charge = Fx(assets.charge, Hands(), Quaternion.identity, scale).GetComponent<QiChargeFx>();
        const int cutInFrames = 27;
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
        for (int i = 0; i < cutInFrames + n; i++)
        {
            if (charge != null) { charge.Follow(Hands(), Ground(FoeX)); charge.SetLevel(Mathf.Clamp01((i - cutInFrames) / (float)n)); charge.transform.localScale = Vector3.one * scale; }
            if (cutIn != null)
            {
                if (i < cutInFrames) cutIn.Seek(i * .95f / cutInFrames);
                else { Object.Destroy(cutIn.gameObject); cutIn = null; }
            }
            if (i == 9) Snap("cutin", 0);
            yield return null;
        }
        if (charge != null) charge.End();
    }
    static IEnumerator Settle()
    {
        yield return Hold(.35f);
        foeLift = Vector3.zero; foeSpin = foeTilt = 0f;
        foeView.PlaySpecial("IDLE"); foe.EndSpecial(); QiPostFx.Set(0f); Dim(1f, .5f); Pull(0f, .5f);
    }

    // ───────────── 필살기 11종 ─────────────

    // 01 산길 척후병 · 잔영연환각: 가볍고 잔혹한 정찰병. 경공으로 잔상을 남겨 어느 쪽이 진짜인지 모르게 한다.
    static IEnumerator AfterimageKicks()
    {
        yield return Gather(.3f, .8f);
        float hx = HeroX, start = FoeX; Vector3 heroGround = Ground(hx);
        Pull(.8f, .3f);
        // 본체가 사라지고, 양옆으로 갈라진 잔상이 차례로 찬다
        Vector3 near = new Vector3(hx + .95f, 0, 0) - Far * .5f, far = new Vector3(hx + .95f, 0, 0) + Far * .5f;
        Fx(assets.muzzle, new Vector3(start, 1.0f, 0), Left, .7f);
        Streak(new Vector3(start, 1.2f, 0), near + Vector3.up * 1.2f, .12f, .2f); Streak(new Vector3(start, .7f, 0), far + Vector3.up * .7f, .12f, .2f);
        Ghost(near, heroGround, "LOWKICK", .7f);
        ShowFoe(false); Place(enemy.gameObject, hx + 1.0f);
        yield return Hold(.2f); Strike(7, new Vector2(-.3f, 0), .5f, .04f, .4f);
        Ghost(far, heroGround, "HIGHKICK", .75f); Snap("action", 5);
        yield return Hold(.3f); Strike(8, new Vector2(-.3f, 0), .55f, .06f, 1.55f);
        yield return Hold(.12f);
        // 본체는 위에서 떨어진다
        ShowFoe(true); Place(enemy.gameObject, HeroX + .9f); foeLift.y = 2.3f; foeView.PlaySpecial("HIGHKICK");
        Streak(new Vector3(FoeX + .2f, 3.4f, 0), new Vector3(FoeX - .35f, 1.5f, 0), .16f, .22f);
        Tween(.18f, u => foeLift.y = Mathf.Lerp(2.3f, 0f, u * u));
        yield return Hold(.19f); Strike(13, new Vector2(-5.5f, 0f), 1f, .12f, 1.5f); Burst(Ground(FoeX), .5f); Snap("impact", 1);
        Shockwave(Ground(FoeX), .2f, 1.9f, .32f); Sparks(new Vector3(HeroX + .2f, 1.5f, 0), Vector3.left + Vector3.up * .4f, 26, 7f, .6f, 50f, 1.4f);
        yield return Hold(.4f);
        yield return Settle();
    }

    // 02 계곡 잠복병 · 잠룡출수: 물가에 숨어 기다리는 경계병. 물결만 보이다가 발밑에서 솟는다.
    static IEnumerator DragonRise()
    {
        yield return Gather(.45f, .8f, "GUARD_LOWKICK_HOLD");
        float hx = HeroX, start = FoeX;
        Smoke(new Vector3(start, .6f, 0), new Vector3(1.4f, 1f, 1.2f), 1.7f, 0, .3f, 1.4f, .3f, 26);
        Burst(new Vector3(start, .4f, 0), .7f);
        Tween(.24f, u => foeLift.y = Mathf.Lerp(0f, -2.3f, u * u), () => ShowFoe(false));
        yield return Hold(.32f);
        // 물살과 물결이 바닥을 타고 다가온다
        var wake = Wave(new Vector3(start, .14f, 0), Left, new Vector3(.25f, .25f, .7f));
        Move(wake.transform, new Vector3(hx + .75f, .14f, 0), .5f, () => EndWave(wake));
        for (int i = 0; i <= 5; i++)
        {
            float x = Mathf.Lerp(start, hx + .8f, i / 5f);
            Shockwave(new Vector3(x, 0f, 0), .15f, .95f, .55f, 1.7f);
            Smoke(new Vector3(x, .2f, 0), new Vector3(.5f, .1f, .5f), .7f, 0, .2f, .7f, .6f, 4);
            if (i == 3) Snap("action", 2);
            yield return Hold(.085f);
        }
        Place(enemy.gameObject, hx + .85f); Pull(1.4f, .3f);
        yield return Hold(.14f);
        // 물기둥과 함께 솟구쳐 올려 친다
        float px = hx + .6f;
        var pillar = Wave(new Vector3(px, .2f, 0), Up, new Vector3(.9f, .9f, 2.6f));
        Move(pillar.transform, new Vector3(px, 2.7f, 0), .26f, () => EndWave(pillar), 2);
        Smoke(new Vector3(px, 1.3f, 0), new Vector3(.9f, 2.4f, .9f), 1.3f, 0, .3f, 1.1f, 2.4f, 30);
        for (int k = 0; k < 7; k++) Streak(new Vector3(px + Rand(-.4f, .4f), 0, Rand(-.3f, .3f)), new Vector3(px + Rand(-.7f, .7f), Rand(2.2f, 3.6f), Rand(-.3f, .3f)), .07f, .3f);
        Burst(Ground(px), .9f); Flash(new Vector3(px, 1.2f, 0) - Far, 6f, 6f, .3f);
        Sparks(new Vector3(px, .3f, 0), Vector3.up, 70, 10f, .95f, 24f, 2.4f, .07f);   // 물보라
        ShowFoe(true); foeView.PlaySpecial("CROSS");
        Tween(.12f, u => foeLift.y = Mathf.Lerp(-2.3f, 0f, u), () => Jump(true, .9f, .45f));
        yield return Hold(.15f); Strike(26, new Vector2(-2.5f, 0f), 1.1f, .13f, 1.3f); Launch(1.5f, .7f, -38f); Snap("impact", 3);
        yield return Hold(.65f);
        yield return Settle();
    }

    // 03 고용 산적 · 천근추지렬: 힘과 몸무게만 믿는 거구. 뛰어올라 온몸으로 내리찍어 땅을 가른다.
    static IEnumerator ThousandWeight()
    {
        yield return Gather(.7f, 1.4f);
        float land = HeroX + 1.8f;   // 주인공 한 걸음 앞에 떨어진다. 맞히는 것은 주먹이 아니라 갈라지는 땅이다.
        foeView.PlaySpecial("SPECIAL_CHARGE"); Pull(2.8f, .35f);
        Slide(enemy.gameObject, land, .62f);
        Tween(.62f, u => foeTilt = Mathf.Lerp(-16f, 34f, u));
        bool landed = false; Jump(true, 2.1f, .62f, () => landed = true);
        yield return Hold(.31f); Snap("action", 0);
        while (!landed) yield return null;
        // 착지: 땅이 갈라지고 바위가 튄다
        foeTilt = 0f; foeView.PlaySpecial("LOWKICK"); Shake(.34f, 4f);
        Burst(Ground(land - .2f), 1.5f); Flash(new Vector3(land, .8f, 0) - Far, 7f, 7f, .35f);
        Smoke(new Vector3(land, .35f, 0), new Vector3(2.6f, .4f, 1.6f), 1.9f, 0, .3f, 1.5f, .6f, 30);
        Ring(u => new Vector3(land, .05f, 0), Vector3.up, u => Mathf.Lerp(.3f, 4.6f, u), .4f, .08f, 2.6f); Shockwave(Ground(land), .3f, 4.8f, .45f, 2.4f);
        Sparks(new Vector3(land - .2f, .15f, 0), new Vector3(-.5f, 1f, 0), 60, 9f, .9f, 70f, 2f); Embers(new Vector3(land - 1f, .5f, 0), new Vector3(3.6f, .6f, 1.4f), 40, 1.6f, 1.6f);
        Cracks(Ground(land - .35f), -1f, Mathf.Abs(land - HeroX) + 1.9f, 6, 30f, .22f);
        Debris(Ground(land - .5f), 7, 6.5f, .14f, .34f, -1f);
        yield return Hold(.2f);
        Debris(Ground(HeroX), 6, 7.5f, .16f, .4f);
        Smoke(new Vector3(HeroX, .4f, 0), new Vector3(1.4f, .5f, 1.2f), 1.5f, 0, .3f, 1.3f, .9f, 14);
        Strike(34, new Vector2(-6.5f, 0f), 1.3f, .2f, .6f); Launch(1.3f, .65f, -32f); Snap("impact", 2);
        yield return Hold(.65f);
        yield return Settle();
    }

    // 04 옛 나루 약탈자 · 승표탈기: 제 몫을 챙기는 약탈자. 밧줄 표창으로 감아 끌어오고, 모아 둔 게이지를 훔친다.
    static IEnumerator RopeDart()
    {
        yield return Gather(.3f, .7f);
        foeView.PlaySpecial("JAB");
        yield return Hold(.1f);
        var dart = Wave(Hands(), Left, new Vector3(.22f, .22f, .45f));
        // 쇠 표창 머리(창날과 고리)와 꼬인 삼 밧줄
        var head = Lathe(Hands(), new[] { new Vector2(0f, 0f), new Vector2(.2f, .05f), new Vector2(.24f, .026f), new Vector2(.3f, .026f), new Vector2(.31f, .044f), new Vector2(.34f, .044f), new Vector2(.36f, 0f) }, IronMaterial(new Vector2(2f, 1f), Hdr(spec.tint, .3f)), 14);
        head.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
        var rope = Line(RopeMaterial(), .05f, .05f, 4f); rope.textureMode = LineTextureMode.Tile; var glow = Line(Glow(1.6f), .016f, .016f, 4f);
        bool roped = true;
        updaters.Add(() =>
        {
            if (!roped || !rope || !dart) { if (rope) rope.positionCount = 0; if (glow) glow.positionCount = 0; if (head) Object.Destroy(head); return true; }
            Vector3 a = Hands(), b = dart.transform.position; const int n = 16;
            head.transform.position = b + Vector3.left * .16f;
            rope.positionCount = glow.positionCount = n;
            for (int i = 0; i < n; i++) { float t = i / (n - 1f); var p = Vector3.Lerp(a, b, t) + Vector3.down * (.22f * Mathf.Sin(t * Mathf.PI)); rope.SetPosition(i, p); glow.SetPosition(i, p); }
            return false;
        });
        Move(dart.transform, new Vector3(HeroX + .25f, 1.2f, 0), .14f); Snap("action", 3);
        yield return Hold(.14f); Strike(8, Vector2.zero, .5f, .05f); heroHp.ExtendStun(1.5f);
        // 허리에 감긴다
        float hx = HeroX;
        var wrapA = Ring(u => new Vector3(HeroX, 1.05f, 0), Vector3.up, u => .42f, 1.3f, .035f, 2.2f, false);
        var wrapB = Ring(u => new Vector3(HeroX, 1.3f, 0), Vector3.up, u => .40f, 1.3f, .035f, 2.2f, false);
        yield return Hold(.2f);
        // 끌어당긴다 (상대는 그녀 앞에 멈춘다)
        foeView.PlaySpecial("GUARD_HOLD");
        float stop = FoeX - 1.05f;
        Slide(player.gameObject, stop, .3f); Tween(.3f, u => { if (dart) dart.transform.position = new Vector3(Mathf.Lerp(hx, stop, u) + .25f, 1.2f, 0); heroTilt = 14f * Mathf.Sin(u * Mathf.PI); });
        yield return Hold(.32f);
        roped = false; EndWave(dart); if (wrapA) Object.Destroy(wrapA.gameObject); if (wrapB) Object.Destroy(wrapB.gameObject);
        // 기운을 빼앗는다: 주인공이 모아 둔 필살기 게이지가 비워진다
        heroHp.ExtendStun(1.1f);
        Drain(() => HeroChest, Hands, 14, .55f);
        yield return Hold(.3f);
        if (heroSpecial != null) heroSpecial.ResetCharge();
        Note("탈기: 주인공이 모아 둔 필살기 게이지를 비운다 (왼쪽 위 게이지)");
        Fx(assets.ready, Hands(), Quaternion.identity, 2.2f); Snap("impact", 2);
        yield return Hold(.4f);
        foeView.PlaySpecial("HIGHKICK");
        yield return Hold(.25f); Strike(16, new Vector2(-5f, 0f), 1.1f, .14f, 1.5f);
        yield return Hold(.3f);
        yield return Settle();
    }

    // 05 철문 · 금종폐관: 무거운 관문 지휘관. 금종조로 몸을 두르고(모으는 동안 맞아도 끊기지 않는다) 내려앉아 쇠문을 닫는다.
    static IEnumerator GoldenBellGate()
    {
        float fx = FoeX;
        // 금종조: 종 모양의 기운이 몸을 감싼다
        var profile = new[] { new Vector2(0f, 1.24f), new Vector2(.12f, 1.15f), new Vector2(.6f, 1.12f), new Vector2(1.1f, 1.07f), new Vector2(1.55f, .97f), new Vector2(1.92f, .8f), new Vector2(2.2f, .56f), new Vector2(2.4f, .28f), new Vector2(2.5f, .1f), new Vector2(2.62f, .1f) };
        // 가장자리가 빛나는 기운 껍데기 위에, 종 표면 무늬(띠·마름모 격자·돌기)가 천천히 돈다
        var shellMat = OrbMaterial(Hdr(spec.tint, .1f), Hdr(spec.tint, 1.5f), 2.2f); var bell = Lathe(Ground(fx), profile, shellMat, 56);
        var patternMat = RibbonMaterial(texBell, Hdr(spec.tint, .85f), 0f, .2f, 0f, Vector4.zero);
        var pattern = Lathe(Ground(fx), profile, patternMat, 56, 2f, 1f); pattern.transform.SetParent(bell.transform, false); pattern.transform.localPosition = Vector3.zero;
        Tween(2.6f, u =>
        {
            if (!bell) return;
            float open = Mathf.Lerp(.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 6f)));
            bell.transform.localScale = new Vector3(open, Mathf.Lerp(.6f, 1f, open), open) * (1f + .012f * Mathf.Sin(u * 60f));
            bell.transform.rotation = Quaternion.Euler(0f, u * 60f, 0f);
            float glow = (.8f + .2f * Mathf.Sin(u * 45f)) * (1f - Mathf.Clamp01((u - .8f) / .2f));
            shellMat.SetFloat("_Alpha", glow); patternMat.SetFloat("_Intensity", glow);
        }, () => { if (bell) Object.Destroy(bell); });
        foreach (var hoop in new[] { new Vector2(.03f, 1.24f), new Vector2(1.55f, .97f), new Vector2(2.2f, .56f), new Vector2(2.56f, .1f) })
            Ring(u => new Vector3(fx, hoop.x, 0), Vector3.up, u => hoop.y * Mathf.Lerp(.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 6f))), 2.5f, .035f, 1.8f);
        yield return Gather(.8f, 1.2f);
        // 육중한 몸이 짧게 떴다가 내려앉는다
        Pull(2.1f, .35f);
        foeView.PlaySpecial("SPECIAL_CHARGE"); bool landed = false; Jump(true, .9f, .44f, () => landed = true); Tween(.44f, u => foeTilt = Mathf.Lerp(-8f, 10f, u));
        while (!landed) yield return null;
        foeTilt = 0f; foeView.PlaySpecial("LOWKICK"); Shake(.28f, 4.5f); Burst(Ground(fx), 1.1f);
        Smoke(new Vector3(fx, .3f, 0), new Vector3(2.2f, .3f, 1.5f), 1.6f, 0, .3f, 1.3f, .5f, 22);
        Ring(u => new Vector3(fx, .05f, 0), Vector3.up, u => Mathf.Lerp(.3f, 3.8f, u), .36f, .07f, 2.6f); Shockwave(Ground(fx), .3f, 4f, .42f, 2.2f);
        Cracks(Ground(fx - .4f), -1f, 2.2f, 3, 24f, .16f); Cracks(Ground(fx + .4f), 1f, 1.5f, 2, 22f, .16f);
        yield return Hold(.14f);
        float hx = HeroX;
        Seal("閉", new Vector3(hx, 2.0f, 0) + Far * 1.3f, 3.0f, 1.5f);
        // 쇠문 두 짝이 땅에서 솟는다 → 한 박자 → 닫힌다 (문짝은 카메라를 보고 서서 양옆에서 닫힌다)
        // 널빤지는 결이 서로 다른 오래된 나무, 띠·못·문고리는 망치 자국과 푸른 녹이 있는 청동
        var band = BronzeMaterial(new Vector2(4f, .5f), Hdr(spec.tint, .05f)); var bronze = BronzeMaterial(new Vector2(1.5f, 1f), Hdr(spec.tint, .07f)); var rail = BronzeMaterial(new Vector2(.4f, 6f), Hdr(spec.tint, .05f));
        var leaves = new List<Transform>(); const float leafW = 1.5f, leafH = 3.3f, leafT = .3f, apart = 1.8f, shut = .96f; float face = -Far.z * leafT * .5f;
        foreach (int side in new[] { -1, 1 })
        {
            var leaf = new GameObject("Gate leaf").transform; leaf.position = new Vector3(hx + side * apart, -1.85f, 0);
            for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Cube, new Vector3((i - 2) * leafW / 5f, 0, (i % 2 == 0 ? .5f : -.5f) * face * .1f), new Vector3(leafW / 5f + .004f, leafH, leafT), WoodMaterial(Rand(.8f, 1.08f)));
            foreach (float y in new[] { -.38f, 0f, .38f })
            {
                Part(leaf, PrimitiveType.Cube, new Vector3(0, y * leafH, 0), new Vector3(leafW + .04f, .17f, leafT + .06f), band);
                for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Sphere, new Vector3((i - 2) * leafW / 5f, y * leafH, face * 1.22f), new Vector3(.115f, .115f, .08f), bronze);
            }
            foreach (float y in new[] { -.19f, .19f }) for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Sphere, new Vector3((i - 2) * leafW / 5f, y * leafH, face * 1.03f), new Vector3(.06f, .06f, .04f), bronze);   // 띠 사이의 작은 못
            foreach (int edge in new[] { -1, 1 }) Part(leaf, PrimitiveType.Cube, new Vector3(edge * leafW * .5f, 0, 0), new Vector3(.09f, leafH + .04f, leafT + .08f), rail);
            Vector3 knob = new Vector3(-side * leafW * .3f, .19f * leafH, face * 1.3f);
            Part(leaf, PrimitiveType.Sphere, knob, new Vector3(.27f, .27f, .12f), bronze);   // 문고리 받침
            var handle = Torus(Vector3.zero, .12f, .022f, bronze, 28, 10, 3f).transform; handle.SetParent(leaf, false); handle.localPosition = knob + new Vector3(0f, -.11f, face * .5f); handle.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 문고리
            Move(leaf, new Vector3(hx + side * apart, leafH * .5f, 0), .2f, null, 2); leaves.Add(leaf);
            Burst(Ground(hx + side * apart), .7f); Debris(Ground(hx + side * apart), 4, 5f, .1f, .24f);
            Smoke(new Vector3(hx + side * apart, .3f, 0), new Vector3(1.6f, .3f, 1f), 1.2f, 0, .3f, 1.1f, .6f, 12);
            Object.Destroy(leaf.gameObject, 3.4f);
        }
        Flash(new Vector3(hx, 1.6f, 0) - Far * 1.5f, 5f, 9f, 1.2f);
        Shake(.2f, 5f);
        yield return Hold(.46f); Snap("action", 0);
        foeView.PlaySpecial("SPECIAL_RELEASE");
        yield return Hold(.12f);
        foreach (var leaf in leaves) Move(leaf, new Vector3(hx + Mathf.Sign(leaf.position.x - hx) * shut, leafH * .5f, 0), .09f, null, 1);
        yield return Hold(.09f);
        Strike(30, Vector2.zero, 1.4f, .28f); heroHp.ExtendStun(.7f); Burst(new Vector3(hx, 2.4f, 0), .9f); Flash(new Vector3(hx, 1.4f, 0) - Far, 8f, 6f, .3f);
        for (int k = 0; k < 8; k++) Streak(new Vector3(hx, Rand(.3f, 3f), 0) - Far * .2f, new Vector3(hx + Rand(-1.4f, 1.4f), Rand(.2f, 3.4f), 0) - Far * Rand(.4f, 1.2f), .05f, .3f, 2.6f);
        for (int k = 0; k < 4; k++) Sparks(new Vector3(hx, .5f + k * .75f, 0) - Far * .25f, Vector3.up * .3f - Far, 22, 6.5f, .75f, 75f, 1.8f);   // 문짝이 맞물리며 튀는 불티
        Shockwave(Ground(hx), .3f, 3.2f, .4f, 2f);
        Smoke(new Vector3(hx, .3f, 0), new Vector3(2.6f, .3f, 1f), 1.3f, 0, .3f, 1.1f, .6f, 14);
        Snap("impact", 1);
        yield return Hold(.5f);
        foreach (var leaf in leaves) Move(leaf, new Vector3(leaf.position.x + Mathf.Sign(leaf.position.x - hx) * .5f, -1.9f, 0), .32f, null, 1);
        yield return Hold(.3f);
        yield return Settle();
    }

    // 06 채석장 정예병 · 격산비석: 말이 짧은 고참 석공. 바위를 띄워 주먹으로 쳐 보낸다.
    static IEnumerator FlyingBoulder()
    {
        yield return Gather(.45f, .9f);
        foeView.PlaySpecial("LOWKICK"); Pull(.9f, .3f);
        yield return Hold(.18f); Shake(.12f, 6f);
        float rx = FoeX - .95f;
        var rock = Rock(new Vector3(rx, -.7f, 0), 1.2f);
        Burst(Ground(rx), .7f); Debris(Ground(rx), 4, 4.5f, .08f, .18f); Cracks(Ground(rx + .2f), -1f, 1.1f, 2, 30f, .12f, 1.6f);
        Smoke(new Vector3(rx, .3f, 0), new Vector3(1.2f, .3f, 1f), 1.2f, 0, .3f, 1.1f, .7f, 12);
        Move(rock.transform, new Vector3(rx, 1.3f, 0), .22f, null, 2);
        Tween(.34f, u => { if (rock) rock.transform.Rotate(new Vector3(0, 0, 120f / Fps), Space.World); });
        yield return Hold(.34f); Snap("action", 0);
        // 주먹으로 친다
        foeView.PlaySpecial("CROSS");
        yield return Hold(.15f);
        Fx(assets.muzzle, new Vector3(rx + .3f, 1.3f, 0), Left, 1.1f); Shake(.12f, 6f); Flash(new Vector3(rx, 1.3f, 0) - Far, 6f, 5f, .25f);
        Sparks(new Vector3(rx + .2f, 1.3f, 0), Vector3.left, 40, 9f, .6f, 40f, 1.2f);
        Vector3 from = rock.transform.position; float hx = HeroX;
        bool hit = false;
        Tween(.2f, u => { if (!rock) return; rock.transform.position = Vector3.Lerp(from, new Vector3(hx + .4f, 1.2f, 0), u) + Vector3.up * (.35f * Mathf.Sin(u * Mathf.PI)); rock.transform.Rotate(new Vector3(0, 0, 900f / Fps), Space.World); }, () => hit = true);
        Streak(from, new Vector3(hx + .4f, 1.3f, 0), .2f, .26f);
        while (!hit) yield return null;
        // 깨지며 파편이 덮친다
        Object.Destroy(rock);
        Vector3 at = new Vector3(hx + .3f, 1.2f, 0);
        for (int i = 0; i < 11; i++) Toss(Rock(at + new Vector3(Rand(-.2f, .2f), Rand(-.2f, .2f), Rand(-.2f, .2f)), Rand(.16f, .44f)), new Vector3(Rand(-5.5f, 1.5f), Rand(1.5f, 5.5f), Rand(-1.8f, 1.8f)), 2.4f);
        Smoke(at, new Vector3(1.2f, 1.2f, 1f), 1.5f, 0, .3f, 1.2f, .5f, 18); Sparks(at, Vector3.left + Vector3.up * .5f, 50, 8f, .8f, 80f, 2f);
        Strike(30, new Vector2(-6f, 0f), 1.2f, .22f); Launch(.5f, .4f, -22f); Snap("impact", 1);
        yield return Hold(.55f);
        yield return Settle();
    }

    // 07 능선 정예병 · 선풍승룡: 바람 센 능선의 날카로운 정예. 회오리로 띄우고 뛰어올라 찍는다.
    static IEnumerator Whirlwind()
    {
        yield return Gather(.4f, .8f);
        foeView.PlaySpecial("HIGHKICK"); Pull(1.6f, .4f);
        Tween(.55f, u => foeSpin = 720f * u, () => foeSpin = 0f);
        // 회오리: 높이마다 다른 빠르기로 도는 바람 줄기들. 위로 갈수록 넓어진다.
        float x0 = FoeX - .7f, tornadoX = x0, clock = 0f, height = .4f; bool spinning = true;
        const int count = 24;
        var strokes = new LineRenderer[count]; var level = new float[count]; var speed = new float[count]; var span = new float[count]; var start = new float[count]; var wide = new float[count];
        // 바람 줄기는 빛줄기가 아니라 마른 붓으로 그은 듯한 결로 그린다
        var gust = RibbonMaterial(texBrush, Hdr(Color.Lerp(spec.tint, Color.white, .5f), 1.3f), .4f, 0f, 0f, new Vector4(-2.6f, .1f, 2f, 1f));
        var breeze = RibbonMaterial(texBrush, Hdr(Color.Lerp(spec.tint, Color.white, .5f), .8f), .4f, 0f, 0f, new Vector4(-2.6f, .1f, 2f, 1f));
        for (int i = 0; i < count; i++)
        {
            strokes[i] = Line(i % 4 == 0 ? gust : breeze, 0f, 0f, 4f);
            float w = Rand(.11f, .26f); strokes[i].widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.55f, w), new Keyframe(1f, 0f));
            level[i] = Mathf.Lerp(.08f, 3f, i / (count - 1f)) + Rand(-.06f, .06f); speed[i] = Rand(620f, 1050f); span[i] = Rand(100f, 220f); start[i] = Rand(0f, 360f); wide[i] = Rand(.85f, 1.2f);
        }
        var dust = Smoke(new Vector3(x0, .6f, 0), new Vector3(.8f, 1f, .8f), 1.2f, 36, 1.9f, .8f, 2.6f, 0, .5f);
        updaters.Add(() =>
        {
            if (!spinning) { foreach (var stroke in strokes) if (stroke) stroke.positionCount = 0; return true; }
            clock += 1f / Fps; height = Mathf.Min(3.1f, height + 7f / Fps);
            const int n = 16;
            for (int i = 0; i < count; i++)
            {
                var lr = strokes[i]; if (!lr) continue;
                if (level[i] > height) { lr.positionCount = 0; continue; }
                lr.positionCount = n; float radius = (.2f + .27f * level[i] + .05f * Mathf.Sin(clock * 9f + i)) * wide[i];
                for (int k = 0; k < n; k++)
                {
                    float t = k / (n - 1f), a = (start[i] + clock * speed[i] + t * span[i]) * Mathf.Deg2Rad;
                    lr.SetPosition(k, new Vector3(tornadoX + Mathf.Cos(a) * radius, level[i] + t * .28f, Mathf.Sin(a) * radius));
                }
            }
            if (dust) dust.transform.position = new Vector3(tornadoX, .6f, 0);
            return false;
        });
        yield return Hold(.3f);
        float x1 = HeroX;
        Tween(.42f, u => tornadoX = Mathf.Lerp(x0, x1, u));
        yield return Hold(.27f); Snap("action", 0);
        yield return Hold(.15f);
        // 휘말려 떠오른다
        Tween(.5f, u => { heroLift.y = Mathf.Lerp(0f, 1.7f, 1 - (1 - u) * (1 - u)); heroSpin = 600f * u; tornadoX = HeroX; });
        for (int k = 0; k < 3; k++) { Strike(6, Vector2.zero, .5f, .05f, 1.0f + heroLift.y); heroHp.ExtendStun(.9f); yield return Hold(.15f); }
        // 뛰어올라 찍는다
        Slide(enemy.gameObject, HeroX + .95f, .2f); foeView.PlaySpecial("HIGHKICK"); Jump(true, 1.8f, .46f);
        yield return Hold(.24f);
        spinning = false;
        Strike(14, new Vector2(-5.5f, 0f), 1.1f, .18f, 1.2f + heroLift.y); Snap("impact", 1);
        float top = heroLift.y;
        Tween(.13f, u => { heroLift.y = Mathf.Lerp(top, 0f, u * u); heroSpin = 0f; heroTilt = -40f * (1f - u); }, () => { heroTilt = 0f; Burst(Ground(HeroX), 1f); Shake(.16f, 6f); Smoke(new Vector3(HeroX, .3f, 0), new Vector3(1.4f, .3f, 1.2f), 1.3f, 0, .3f, 1.1f, .6f, 12); });
        yield return Hold(.55f);
        yield return Settle();
    }

    // 08 추포대 추적자 · 삼점봉혈: 초조한 사냥꾼. 혈 자리 셋을 찔러 두고, 한 박자 뒤에 터뜨려 공격을 봉한다.
    static IEnumerator ThreePoints()
    {
        yield return Gather(.4f, .75f);
        Streak(new Vector3(FoeX, 1.1f, 0), new Vector3(HeroX + 1.0f, 1.1f, 0), .12f, .16f);
        Slide(enemy.gameObject, HeroX + 1.0f, .12f);
        yield return Hold(.13f);
        var bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest, HumanBodyBones.RightUpperLeg };
        var marks = new List<GameObject>();
        for (int k = 0; k < 3; k++)
        {
            foeView.PlaySpecial(k % 2 == 0 ? "JAB" : "CROSS");
            yield return Hold(.1f);
            Vector3 point = HeroBone(bones[k], 1.4f - k * .35f);
            var needle = Wave(Hands(), Quaternion.LookRotation((point - Hands()).normalized, Vector3.up), new Vector3(.1f, .1f, .7f));
            Move(needle.transform, point, .05f, () => EndWave(needle));
            var mark = Fx(assets.ready, point, Quaternion.identity, .6f); var bone = HeroBoneTransform(bones[k]); if (bone) mark.transform.SetParent(bone, true); marks.Add(mark);
            Ring(u => point, Vector3.forward, u => Mathf.Lerp(.05f, .34f, u), .3f, .03f, 3f);
            float stay = 1.25f - k * .25f; var pin = bone;
            Ring(u => pin ? pin.position : point, Vector3.forward, u => .1f + .015f * Mathf.Sin(u * 40f), stay, .028f, 2.6f, false);
            Ring(u => pin ? pin.position : point, Vector3.forward, u => .17f, stay, .012f, 2f, false);
            heroHp.TakeDamage(3, Vector2.zero, false, true); heroHp.ExtendStun(1.2f); Shake(.04f, 8f);
            if (k == 2) Snap("action", 2);
            yield return Hold(.15f);
        }
        // 물러서서 지켜본다
        Slide(enemy.gameObject, HeroX + 2.3f, .22f); foeView.PlaySpecial("GUARD_HOLD");
        yield return Hold(.45f);
        Seal("封", new Vector3(HeroX, 2.0f, 0) + Far * 1.2f, 2.7f, 1.6f);
        yield return Hold(.12f);
        foreach (var mark in marks) { if (mark) { Burst(mark.transform.position, .5f); Sparks(mark.transform.position, Vector3.up, 18, 5f, .5f, 85f, .8f, .04f); Object.Destroy(mark); } }
        Strike(18, new Vector2(-1.5f, 0f), .9f, .16f); heroHp.ExtendStun(1.5f); Snap("impact", 2);
        // 봉혈: 몸에 고리가 남아 2초 동안 공격이 나가지 않는다
        Ring(u => new Vector3(HeroX, .9f, 0), Vector3.up, u => .46f, 1.3f, .035f, 2.2f); Ring(u => new Vector3(HeroX, 1.35f, 0), Vector3.up, u => .42f, 1.3f, .035f, 2.2f);
        Note("봉혈: 2초 동안 주인공의 공격과 필살기가 봉인된다");
        yield return Hold(.85f);
        yield return Settle();
    }

    // 09 옥졸장 · 진옥후: 통제를 잃을까 소리부터 지르는 감시관. 고함으로 밀어내고 창살로 가둔다.
    static IEnumerator PrisonRoar()
    {
        yield return Gather(.6f, 1.1f);
        foeView.PlaySpecial("SPECIAL_RELEASE"); Pull(1.2f, .35f);
        Tween(.5f, u => foeTilt = 12f * Mathf.Sin(u * Mathf.PI));
        yield return Hold(.15f);
        // 고함: 입에서 퍼지는 음파 여섯 겹
        Vector3 mouth = new Vector3(FoeX - .35f, 1.5f, 0);
        for (int i = 0; i < 6; i++)
        {
            Arc(u => mouth, u => Mathf.Lerp(.3f, 6.5f, u), 52f, .55f, Mathf.Lerp(.11f, .05f, i / 5f));
            Ring(u => mouth + Vector3.left * (6f * u), new Vector3(1f, 0f, .5f), u => Mathf.Lerp(.2f, 2.2f, u), .55f, .03f, 2f);
            Shake(.09f, 8f);
            if (i == 1) Smoke(new Vector3(mouth.x - 2f, .25f, 0), new Vector3(4f, .2f, 1.4f), 1.1f, 0, .3f, 1f, .5f, 16, .4f);
            if (i == 3) { Strike(12, new Vector2(-7f, 0), .7f, .12f); heroHp.ExtendStun(1.6f); heroTilt = -18f; After(.5f, () => heroTilt = 0f); Snap("action", 1); }
            yield return Hold(.06f);
        }
        yield return Hold(.42f);
        // 쇠창살이 떨어져 가둔다
        float hx = HeroX;
        Seal("獄", new Vector3(hx, 2.1f, 0) + Far * 1.3f, 2.7f, 1.5f);
        // 두드려 만든 검은 쇠창살: 아래는 뾰족하게 박히고 위는 창끝이며, 두 군데에 마디가 있다. 달아오른 듯 옅게 빛난다.
        var iron = IronMaterial(new Vector2(1f, 5f), Hdr(spec.tint, .2f));
        var shape = new[] { new Vector2(0f, 0f), new Vector2(.22f, .036f), new Vector2(.5f, .036f), new Vector2(.52f, .058f), new Vector2(.6f, .058f), new Vector2(.62f, .036f), new Vector2(2f, .036f), new Vector2(2.02f, .058f), new Vector2(2.1f, .058f), new Vector2(2.12f, .036f), new Vector2(2.32f, .036f), new Vector2(2.36f, .064f), new Vector2(2.44f, .046f), new Vector2(2.66f, 0f) };
        var bars = new List<Transform>();
        for (int i = 0; i < 9; i++)
        {
            float a = (i + .5f) * Mathf.PI * 2 / 9; Vector3 foot = new Vector3(hx + Mathf.Cos(a) * .68f, -.15f, Mathf.Sin(a) * .5f);
            var bar = Lathe(foot + Vector3.up * 6.5f, shape, iron, 12).transform; bars.Add(bar);
            Move(bar, foot, .12f, () => { Burst(new Vector3(foot.x, .05f, foot.z), .3f); Sparks(new Vector3(foot.x, .1f, foot.z), Vector3.up, 12, 5f, .5f, 70f, 1.6f, .04f); Shake(.06f, 8f); }, 1);
            Object.Destroy(bar.gameObject, 2.4f);
            yield return Hold(.03f);
        }
        // 마디를 지나는 쇠테 두 줄이 창살을 묶는다
        foreach (float y in new[] { .41f, 1.91f })
        {
            var hoop = Torus(new Vector3(hx, y, 0), .68f, .03f, iron, 48, 10, 8f).transform; hoop.localScale = new Vector3(1f, 1f, .5f / .68f); bars.Add(hoop);
            Object.Destroy(hoop.gameObject, 2.3f);
            Ring(u => new Vector3(hx, y, 0), Vector3.up, u => .72f, 1.3f, .05f, 2.4f);
        }
        Embers(new Vector3(hx, 1.2f, 0), new Vector3(1.3f, 2.2f, 1f), 40, .9f, 1.5f);
        yield return Hold(.14f);
        Strike(20, Vector2.zero, 1.1f, .22f); heroHp.ExtendStun(.9f); Flash(new Vector3(hx, 1.3f, 0) - Far, 7f, 5f, .3f); Snap("impact", 2);
        Note("가둠: 창살이 서 있는 동안 주인공은 움직이지 못한다");
        yield return Hold(.8f);
        foreach (var bar in bars) if (bar) Move(bar, bar.position + Vector3.down * 3.2f, .25f, null, 1);
        yield return Settle();
    }

    // 10 흑견 · 흑영교: 조용히 압박하며 위협을 즐기는 집행자. 그림자가 먼저 닿고, 기운을 빨아들인다.
    static IEnumerator ShadowBite()
    {
        yield return Gather(.5f, 1.2f, "GUARD_LOWKICK_HOLD");
        // 그림자가 몸에서 떨어져 나와 낮게 엎드려 달려든다
        float from = FoeX - .25f, to = HeroX + .7f;
        var shade = Ghost(Ground(from), Ground(HeroX), "WALK_FORWARD", 1.2f, true, 2.4f);
        shade.transform.rotation *= Quaternion.Euler(56f, 0f, 0f);
        var shadeAnim = shade.GetComponentInChildren<Animator>(); var head = shadeAnim != null && shadeAnim.isHuman ? shadeAnim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null) { var eyes = Fx(assets.ready, head.position, Quaternion.identity, .7f); eyes.transform.SetParent(head, true); }
        Smoke(new Vector3(from, .6f, 0), new Vector3(.8f, 1f, .7f), 1.1f, 0, .2f, .8f, .4f, 16, .85f);
        var fog = Smoke(new Vector3(from, .45f, 0), new Vector3(.5f, .6f, .5f), 1f, 60, .5f, .7f, .2f, 0, .85f);
        var leash = Line(darkMat, .05f, .17f, 1.4f); var leashGlow = Line(Glow(1.6f), .012f, .03f, 1.4f); float wriggle = 0f;
        Move(shade.transform, new Vector3(to, shade.transform.position.y, 0), .36f, null, 1);
        updaters.Add(() =>
        {
            if (!shade || !leash || !leashGlow) { if (leash) leash.positionCount = 0; if (leashGlow) leashGlow.positionCount = 0; return true; }
            const int n = 16; wriggle += 10f / Fps; leash.positionCount = leashGlow.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f); var p = new Vector3(Mathf.Lerp(FoeX - .1f, shade.transform.position.x, t), .07f + .04f * Mathf.Sin(t * 12f - wriggle), 0);
                leash.SetPosition(i, p); leashGlow.SetPosition(i, p + Vector3.up * .07f);
            }
            if (fog) fog.transform.position = new Vector3(shade.transform.position.x, .45f, 0);
            return false;
        });
        yield return Hold(.2f); Snap("action", 1);
        yield return Hold(.18f);
        // 그림자가 풀어지며 아가리가 된다: 발밑과 머리 위에서 이빨이 맞물린다
        float hx = HeroX; var fangs = new List<GameObject>();
        if (shade) Object.Destroy(shade);
        Smoke(new Vector3(hx + .2f, 1f, 0), new Vector3(1.3f, 1.8f, .9f), 1.3f, 24, 1.3f, .8f, .4f, 20, .85f);
        foreach (int jaw in new[] { -1, 1 })
        {
            var line = Line(Glow(2.2f), .035f, .035f, 1.5f); const int n = 20; line.positionCount = n; int sign = jaw;
            Tween(1.4f, u =>
            {
                if (!line) return; float gap = Mathf.Lerp(1.5f, .3f, Mathf.Clamp01(u * 14f)) + .04f * Mathf.Sin(u * 50f);
                for (int i = 0; i < n; i++) { float x = Mathf.Lerp(-.85f, .85f, i / (n - 1f)); line.SetPosition(i, new Vector3(hx + x, 1.15f + sign * (gap + .5f * (1f - x * x / .7225f)), 0)); }
                line.widthMultiplier = 1f - Mathf.Clamp01((u - .8f) / .2f);
            });
        }
        for (int i = -2; i <= 2; i++)
        {
            float lean = i * .09f;
            var lower = Wave(new Vector3(hx + i * .22f, -1.2f, 0), Up, new Vector3(.16f, .16f, 1.0f)); Move(lower.transform, new Vector3(hx + i * .22f - lean, .55f, 0), .09f, null, 1); fangs.Add(lower);
            var upper = Wave(new Vector3(hx + i * .22f, 3.8f, 0), Down, new Vector3(.16f, .16f, 1.0f)); Move(upper.transform, new Vector3(hx + i * .22f - lean, 1.8f, 0), .09f, null, 1); fangs.Add(upper);
        }
        yield return Hold(.09f);
        Strike(10, Vector2.zero, .8f, .12f); heroHp.ExtendStun(1.8f); Embers(new Vector3(hx, 1.1f, 0), new Vector3(1f, 1.7f, .7f), 34, .8f, 1.3f);
        yield return Hold(.3f);
        Strike(10, Vector2.zero, .6f, .1f);
        // 기운을 빨아들여 회복한다
        Drain(() => HeroChest, () => new Vector3(FoeX, 1.2f, 0), 18, .7f);
        int heal = Mathf.RoundToInt(foeHp.MaxHealth * .2f);
        After(.5f, () => { foeHp.Heal(heal); Fx(assets.ready, new Vector3(FoeX, 1.1f, 0), Quaternion.identity, 3.2f); Note("흡기: 빼앗은 기운으로 흑견의 체력이 회복된다 (오른쪽 위 체력)"); });
        yield return Hold(.5f); Snap("impact", 2);
        yield return Hold(.4f);
        foreach (var fang in fangs) EndWave(fang);
        Strike(10, new Vector2(-5f, 0f), 1.1f, .16f);
        yield return Hold(.35f);
        yield return Settle();
    }

    // 11 무진 · 흑운압성: 자기 논리가 정답이라 믿는 흑운협주. 먹구름을 끌어내려 누른다(구름을 깨는 파운장의 반대말).
    static IEnumerator BlackCloud()
    {
        // 먹구름이 몰려와 하늘을 덮고, 주인공을 끌어당긴다
        float mid = (HeroX + FoeX) * .5f;
        Pull(3.2f, .9f);
        Smoke(new Vector3(mid, 3.95f, 0) + Far * .5f, new Vector3(11f, .8f, 3.2f), 3.4f, 54, 3.7f, 2.2f, -.1f, 40, .9f);
        After(.5f, () => Dim(.22f, .9f));
        hero.InputLocked = true;
        Slide(player.gameObject, HeroX + .9f, 1.5f); Tween(1.5f, u => heroTilt = -14f * Mathf.Sin(u * Mathf.PI));
        for (int i = 0; i < 7; i++)
        {
            float delay = .7f + i * .2f;
            After(delay, () =>
            {
                Vector3 at = new Vector3(mid + Rand(-3.8f, 3.8f), 3.9f, 0) + Far * .3f;
                Puff(at, Rand(2.8f, 4f), .24f); Bolt(at + Vector3.up * .3f, at + new Vector3(Rand(-1.2f, 1.2f), Rand(-.9f, -.4f), 0), .06f, .12f, 1); Flash(at, 5f, 9f, .15f);
            });
        }
        yield return Gather(1.3f, 2.2f);
        hero.InputLocked = false; heroTilt = 0f;
        foeView.PlaySpecial("SPECIAL_RELEASE");
        yield return Hold(.18f);
        float hx = HeroX;
        Seal("滅", new Vector3(hx, 2.3f, 0) + Far * 1.5f, 3.6f, 1.7f);
        // 구름 기둥이 내려앉는다
        var column = Wave(new Vector3(hx, 8.5f, 0), Down, new Vector3(1.3f, 1.3f, 3.6f));
        Move(column.transform, new Vector3(hx, 1.7f, 0), .24f, null, 1);
        Smoke(new Vector3(hx, 2.4f, 0), new Vector3(1.7f, 3.8f, 1.3f), 2.1f, 70, .5f, 1f, -2.8f, 24, .9f); Snap("action", 5);
        yield return Hold(.24f);
        EndWave(column); Shake(.34f, 4f);
        Strike(26, Vector2.zero, 1.8f, .2f); heroHp.ExtendStun(1f);
        Ring(u => new Vector3(hx, .05f, 0), Vector3.up, u => Mathf.Lerp(.4f, 5.2f, u), .42f, .09f, 2.8f); Shockwave(Ground(hx), .4f, 5.4f, .46f, 2.6f);
        Embers(new Vector3(hx, .6f, 0), new Vector3(4f, .8f, 1.6f), 50, 1.8f, 1.7f);
        Cracks(Ground(hx - .2f), -1f, 3f, 4, 36f, .18f); Cracks(Ground(hx + .2f), 1f, 3f, 4, 36f, .18f);
        Debris(Ground(hx), 8, 7f, .14f, .36f);
        Smoke(new Vector3(hx, .4f, 0), new Vector3(3.2f, .4f, 1.6f), 1.9f, 0, .3f, 1.4f, .7f, 26, .8f);
        yield return Hold(.22f);
        // 벼락 세 줄
        for (int k = 0; k < 3; k++)
        {
            float x = HeroX + (k - 1) * .45f;
            Bolt(new Vector3(x + Rand(-.8f, .8f), 3.9f, 0), new Vector3(x, .1f, 0), .16f, .16f);
            Puff(new Vector3(x, 3.8f, 0), 3.4f, .22f); Burst(new Vector3(x, .9f, 0), 1f); Flash(new Vector3(x, 1.6f, 0) - Far, 12f, 10f, .2f); Shake(.22f, 6f);
            Sparks(new Vector3(x, .12f, 0), Vector3.up, 45, 9f, .7f, 60f, 2f);
            heroHp.TakeDamage(4, Vector2.zero, false, true); heroHp.ExtendStun(.6f);
            if (k == 1) Snap("impact", 0);
            yield return Hold(.12f);
        }
        Strike(8, new Vector2(-9f, 0f), 1.6f, .3f); Launch(1.2f, .65f, -36f);
        yield return Hold(.7f);
        yield return Settle();
    }

    // ───────────── 녹화 ─────────────

    static MediaEncoder NewEncoder(string path)
    {
        var h264 = new H264EncoderAttributes { gopSize = Fps, numConsecutiveBFrames = 2, profile = VideoEncodingProfile.H264High };
        var attributes = new VideoTrackEncoderAttributes(h264) { frameRate = new MediaRational(Fps), width = (uint)Width, height = (uint)Height, includeAlpha = false, targetBitRate = 24000000 };
        return new MediaEncoder(path, attributes);
    }
    /// <summary>뛰기·돌기·기울이기는 전투 뷰가 매 프레임 되돌리므로, 그리기 직전에만 얹는다.</summary>
    static void ApplyPose()
    {
        if (foeView != null && foeView.visualRoot != null)
        {
            var t = foeView.visualRoot; t.position += foeLift;
            if (foeSpin != 0f || foeTilt != 0f) t.rotation = Quaternion.Euler(0, foeSpin, 0) * t.rotation * Quaternion.Euler(foeTilt, 0, 0);
        }
        if (heroView != null && heroView.visualRoot != null)
        {
            var t = heroView.visualRoot; t.position += heroLift;
            if (heroSpin != 0f || heroTilt != 0f) t.rotation = Quaternion.Euler(0, heroSpin, 0) * t.rotation * Quaternion.Euler(heroTilt, 0, 0);
        }
    }
    static void Grab()
    {
        ApplyPose(); StepLook();
        Vector3 home = cam.transform.position;
        if (camPull > 0f) cam.transform.position = home - cam.transform.forward * camPull + Vector3.up * (camPull * .17f);
        Canvas.ForceUpdateCanvases(); cam.Render();
        cam.transform.position = home;
        Graphics.Blit(big, target);   // 두 배로 그린 화면을 네 점 평균으로 줄인다
        var previous = RenderTexture.active; RenderTexture.active = target;
        frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); frame.Apply(false);
        RenderTexture.active = previous;
        all.AddFrame(frame);
        if (still != null && --stillIn < 0)
        {
            File.WriteAllBytes(Out + "/" + (specIndex + 1).ToString("00") + "_" + spec.key + "_" + still + ".jpg", frame.EncodeToJPG(95));
            still = null;
        }
    }

    static void End(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        Time.captureFramerate = 0; Time.timeScale = 1;
        if (all != null) { all.Dispose(); all = null; }
        if (cam != null) cam.targetTexture = null;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        bool restored = PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", "");
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out + "/shots_done.txt", (failure == null ? "PASS" : "FAIL " + failure) + "\nsave restored: " + restored);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
