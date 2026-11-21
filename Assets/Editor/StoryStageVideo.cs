using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 전투 무대의 플레이 영상을 녹화한다 (batchmode 전용).
///   Record        : 지금 게임에 들어 있는 스토리 모드 전투 11곳 (실제 스토리 경로로 불러온다)
///   RecordHybrid  : 스토리 무대와 배경 그림을 합친 예시 무대 (HybridStageBuilder)
///   HybridStills  : 합친 예시 무대의 정지 화면 (분위기 확인용)
/// 스토리 진행 상태(RunState)로 그 전투를 고른 뒤 무대를 불러오므로 스테이지별 상대와 설정이 그대로 적용된다.
/// 주인공은 실제 입력 경로로 무대 오른쪽에서 왼쪽 끝까지 물러난 뒤 공격하고, 상대는 그 스테이지의 AI가 조종한다.
/// 끝나면 진행 중이던 저장 데이터를 원래대로 되돌린다.
/// Unity -batchmode -executeMethod StoryStageVideo.Record -redbool-keyboard
/// </summary>
[InitializeOnLoad]
public static class StoryStageVideo
{
    const string Active = "StoryStageVideo.Active", ModeKey = Active + ".Mode", SaveKey = "VisionStriker.Run";
    const int Fps = 30, LoadFrames = 45, SettleFrames = 75, PlayFrames = 360;
    // 스토리 지도에서 싸우는 순서
    static readonly string[] StoryIds = { "scout", "river", "ridge", "old_ferry", "gate", "quarry", "east", "camp", "rescue", "entrance", "final" };
    static string Mode => SessionState.GetString(ModeKey, "story");
    static bool Stills => Mode == "hybrid-stills";
    static int Width => Mode == "story" ? 1280 : 1920;
    static int Height => Mode == "story" ? 720 : 1080;
    public static string Out => Path.GetFullPath(Mode == "story" ? "Docs/StoryStages/Video" : Stills ? "Docs/HybridStages" : "Docs/HybridStages/Video");

    static IEnumerator flow;
    static int lastFrame = -1;
    static Camera cam; static Canvas canvas;
    static RenderTexture target; static Texture2D frame; static MediaEncoder all, single;
    static StoryStageVideo() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    public static void Record() { Begin("story", "Assets/Scenes/ScoutPass.unity"); }
    public static void RecordHybrid() { Begin("hybrid", HybridStageBuilder.ScenePath(HybridStageBuilder.ActiveStages()[0])); }
    public static void HybridStills() { Begin("hybrid-stills", HybridStageBuilder.ScenePath(HybridStageBuilder.ActiveStages()[0])); }

    static void Begin(string mode, string firstScene)
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        SessionState.SetString(ModeKey, mode);
        EditorSceneManager.OpenScene(firstScene);
        Directory.CreateDirectory(Out);
        foreach (var file in Directory.GetFiles(Out)) File.Delete(file);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        File.WriteAllText(Path.GetFullPath("Library/StoryStageVideo_save_backup.json"), PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 1800) { Finish("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = Capture(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static IEnumerator Capture()
    {
        yield return null; yield return null;
        // 영상 한 장이 게임 시간 1/30초가 되게 고정한다.
        Time.captureFramerate = Fps;
        target = new RenderTexture(Width, Height, 24); target.Create();
        frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        if (!Stills) all = NewEncoder(Out + "/all_stages.mp4", Mode == "story" ? 4000000u : 4800000u);

        bool story = Mode == "story";
        int count = story ? StoryIds.Length : HybridStageBuilder.ActiveStages().Length;
        for (int i = 0; i < count; i++)
        {
            var hybrid = story ? null : HybridStageBuilder.ActiveStages()[i];
            string id = story ? StoryIds[i] : hybrid.id;
            string name = story ? (i + 1).ToString("00") + "_" + id : (i + 1) + "_" + hybrid.key;
            StartStoryBattle(id);
            string scene;
            if (story) { scene = SceneRoutes.PendingBattleScene; SceneRoutes.LoadBattle(); }
            else
            {
                scene = hybrid.sceneName;
                EditorSceneManager.LoadSceneInPlayMode(HybridStageBuilder.ScenePath(hybrid), new LoadSceneParameters(LoadSceneMode.Single));
            }
            for (int k = 0; k < LoadFrames; k++) yield return null;
            if (SceneManager.GetActiveScene().name != scene) throw new Exception(id + ": expected scene " + scene + " but got " + SceneManager.GetActiveScene().name);

            var player = Object.FindAnyObjectByType<PlayerController>(); var enemy = Object.FindAnyObjectByType<EnemyAI>();
            if (player == null || enemy == null) throw new Exception(id + ": fighters not found");
            foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>()) udp.enabled = false;
            cam = Camera.main; var rig = cam.GetComponent<Battle3DCamera>();
            if (rig != null) rig.allowMouseOrbit = false;
            var hud = Object.FindAnyObjectByType<CombatHUD>(); canvas = hud != null ? hud.GetComponentInChildren<Canvas>() : null;
            if (canvas != null)
            {
                var guide = canvas.transform.Find("KeyGuide"); if (guide != null) guide.gameObject.SetActive(false);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = cam.nearClipPlane + .1f;
            }
            cam.targetTexture = target; cam.aspect = (float)Width / Height;

            player.enabled = false; enemy.enabled = false;
            foreach (var fighter in Object.FindObjectsByType<Fighter>()) { fighter.CancelAttack(); fighter.InputLocked = false; fighter.IsBlocking = false; }
            player.GetComponent<Fighter>().Facing = 1; enemy.GetComponent<Fighter>().Facing = -1;
            var playerHealth = player.GetComponent<Health>(); var enemyHealth = enemy.GetComponent<Health>();

            if (Stills)
            {
                // 가운데 대치, 오른쪽 구석, 왼쪽 구석
                Place(player.gameObject, -2.1f); Place(enemy.gameObject, 2.1f); if (rig != null) rig.ResetOrbit();
                for (int k = 0; k < 100; k++) yield return null;
                Save(Out + "/" + name + "_fight.jpg");
                if (canvas != null) canvas.gameObject.SetActive(false);
                Place(player.gameObject, 2.4f); Place(enemy.gameObject, 6.2f);
                for (int k = 0; k < 100; k++) yield return null;
                Save(Out + "/" + name + "_right.jpg");
                Place(player.gameObject, -6.2f); Place(enemy.gameObject, -2.4f);
                for (int k = 0; k < 100; k++) yield return null;
                Save(Out + "/" + name + "_left.jpg");
                cam.targetTexture = null;
                continue;
            }

            // 무대 오른쪽에서 시작한다. 카메라가 자리 잡을 때까지는 녹화하지 않는다.
            Place(player.gameObject, 2.4f); Place(enemy.gameObject, 5.6f);
            if (rig != null) rig.ResetOrbit();
            for (int k = 0; k < SettleFrames; k++) yield return null;

            single = NewEncoder(Out + "/" + name + ".mp4", Mode == "story" ? 4000000u : 10000000u);
            player.enabled = true; enemy.enabled = true;
            for (int f = 0; f < PlayFrames; f++)
            {
                Drive(player, f);
                // 녹화 도중 한쪽이 쓰러져 지도로 넘어가지 않게 체력이 바닥나기 전에 채운다.
                foreach (var health in new[] { playerHealth, enemyHealth })
                    if (health != null && health.CurrentHealth < health.MaxHealth * .3f) health.SetCurrentHealth(health.MaxHealth);
                yield return null;
                Grab();
            }
            single.Dispose(); single = null;
            player.ClearExternalInput();
            Debug.Log("[StoryStageVideo] " + name + " scene=" + scene + " enemy=" + enemyHealth.DisplayName + " behaviour=" + enemy.BehaviorType
                + " player x=" + player.transform.position.x.ToString("0.0") + " enemy x=" + enemy.transform.position.x.ToString("0.0")
                + " hp=" + playerHealth.CurrentHealth + "/" + enemyHealth.CurrentHealth);
            cam.targetTexture = null;
        }
        if (all != null) { all.Dispose(); all = null; }
    }

    // 스토리 지도에서 그 전투를 고른 상태를 만든다 (StoryStagesReview 와 같은 방식).
    static void StartStoryBattle(string id)
    {
        var run = RunState.Instance;
        if (run == null) throw new Exception("RunState is missing");
        var data = new RunData { seed = 684, playerHealth = 100, playerMaxHealth = 100, pendingNodeId = id, currentNodeId = "cave", tutorialCompleted = true, tutorialVersion = 1, storyPresentationVersion = 1 };
        data.map = MapGenerator.Generate(run.Config.map, run.Config, data.seed);
        typeof(RunState).GetProperty("Data").SetValue(run, data);
    }

    // 주인공 조작 순서 (30프레임 = 1초)
    static void Drive(PlayerController player, int f)
    {
        if (f == 15) player.ExternalMovement(-1f);      // 왼쪽 끝까지 물러난다. 상대 AI가 쫓아온다.
        if (f == 282) player.ExternalMovement(0f);
        if (f == 288) player.ExternalAction("JAB");
        if (f == 304) player.ExternalAction("STRAIGHT");
        if (f == 322) player.ExternalAction("LOW_KICK");
        if (f == 340) player.ExternalAction("HIGH_KICK");
    }

    static void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var body = fighter.GetComponent<Rigidbody2D>(); if (body != null) { body.position = new Vector2(x, body.position.y); body.linearVelocity = Vector2.zero; }
        Physics2D.SyncTransforms();
    }

    static MediaEncoder NewEncoder(string path, uint bitRate)
    {
        var h264 = new H264EncoderAttributes { gopSize = Fps, numConsecutiveBFrames = 2, profile = VideoEncodingProfile.H264High };
        var attributes = new VideoTrackEncoderAttributes(h264)
        {
            frameRate = new MediaRational(Fps), width = (uint)Width, height = (uint)Height, includeAlpha = false, targetBitRate = bitRate
        };
        return new MediaEncoder(path, attributes);
    }

    static void Read()
    {
        Canvas.ForceUpdateCanvases(); cam.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); frame.Apply(false);
        RenderTexture.active = previous;
    }

    static void Grab()
    {
        Read();
        all.AddFrame(frame);
        if (single != null) single.AddFrame(frame);
    }

    static void Save(string path) { Read(); File.WriteAllBytes(path, frame.EncodeToJPG(93)); }

    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        Time.captureFramerate = 0; Time.timeScale = 1;
        if (single != null) { single.Dispose(); single = null; }
        if (all != null) { all.Dispose(); all = null; }
        if (cam != null) cam.targetTexture = null;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        bool restored = PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(Active + ".Save", "");
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out + "/record_done.txt", (failure == null ? "PASS" : "FAIL " + failure) + "\nsave restored: " + restored);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
