using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 좌우 이동 중 발이 바닥에 붙어 있는지 잰다 (batchmode 전용 검사).
/// 12명(주인공 + 스토리 상대 11명)을 스토리 전투 경로와 PvP 경로(왼쪽·오른쪽 자리)에서 앞뒤로 걸리고,
/// 프레임마다 두 발의 발바닥 높이(스킨 메시를 그 프레임 자세로 구워서 발 뼈에 붙은 정점 중 가장 낮은 점),
/// 두 발이 모두 뜬 시간 비율, 가장 크게 뜬 높이, 골반 위아래 흔들림, 딛은 발이 바닥에서 미끄러진 빠르기를 기록한다.
/// 옆에서 본 사진(바닥선 위치는 frames.csv 의 floorRow)과 게임 카메라 사진도 저장한다.
/// Unity -batchmode -executeMethod FootGroundingProbe.Run  (환경 변수 FOOT_PROBE_OUT = 결과 폴더,
///   FOOT_PROBE_ONLY = "story" | "pvp" (생략하면 둘 다), FOOT_PROBE_IMAGES = "0" 이면 사진 생략)
/// 결과: metrics.csv(구간별), frames.csv(프레임별), checks.txt, notes.txt, side/…jpg, game/…jpg, done.txt
/// 저장 데이터(VisionStriker.Run)는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class FootGroundingProbe
{
    const string Active = "FootGroundingProbe.Active", SaveKey = "VisionStriker.Run";
    // 발이 "바닥에 닿았다"고 보는 높이(발바닥 가장 낮은 점 기준).
    const float ContactCm = 1.0f;
    static readonly string[] StoryNodes = { "scout", "river", "ridge", "old_ferry", "gate", "quarry", "east", "camp", "rescue", "entrance", "final" };
    static IEnumerator flow;
    static int lastFrame = -1;
    static readonly List<string> checks = new List<string>(), errors = new List<string>(), notes = new List<string>();
    static StreamWriter frameCsv, metricCsv;
    static Camera side, game; static RenderTexture sideRt, gameRt; static Texture2D sideImg, gameImg;
    static bool images = true;

    static FootGroundingProbe() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    static void Subscribe()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }
    static string Out => SessionState.GetString(Active + ".Out", Path.GetFullPath("Logs/FootProbe"));
    static string Only => SessionState.GetString(Active + ".Only", "");

    public static void Ping()
    {
        string o = Environment.GetEnvironmentVariable("FOOT_PROBE_OUT") ?? Path.GetFullPath("Logs/FootProbe");
        Directory.CreateDirectory(o);
        File.WriteAllText(Path.Combine(o, "done.txt"), "PING " + Application.unityVersion);
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    public static void Run()
    {
        string o = Environment.GetEnvironmentVariable("FOOT_PROBE_OUT");
        if (string.IsNullOrEmpty(o)) o = Path.GetFullPath("Logs/FootProbe");
        Directory.CreateDirectory(o);
        SessionState.SetString(Active + ".Out", o);
        SessionState.SetString(Active + ".Only", Environment.GetEnvironmentVariable("FOOT_PROBE_ONLY") ?? "");
        SessionState.SetBool(Active + ".Images", Environment.GetEnvironmentVariable("FOOT_PROBE_IMAGES") != "0");
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/StoryStages/SealedGate.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Log(string m, string s, LogType t)
    {
        // 장면을 바꿀 때 파운장 화면 효과가 남는다는 기존 경고(걷기와 무관). 따로 적어 둔다.
        if (m.StartsWith("Some objects were not cleaned up when closing the scene")) { if (!notes.Contains("KNOWN " + m)) notes.Add("KNOWN " + m); return; }
        if (m.StartsWith("SOCKET ERROR")) { if (!notes.Contains("ENV " + m)) notes.Add("ENV " + m); return; }   // UDP 포트를 다른 Unity/게임이 쓰는 중
        if ((t == LogType.Error || t == LogType.Exception) && !m.Contains("MCP") && !s.Contains("IvanMurzak")) errors.Add(m + "\n" + s);
        if (t == LogType.Warning && m.Contains("could not be found")) { if (notes.Count(x => x.StartsWith("WARN")) < 40) notes.Add("WARN " + m); }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 1500) { Finish("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (flow == null) { images = SessionState.GetBool(Active + ".Images", true); flow = Flow(); }
            if (!flow.MoveNext()) Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static void Check(bool ok, string label) { checks.Add((ok ? "PASS " : "FAIL ") + label); }

    static void Finish(string failure)
    {
        Time.captureFramerate = 0;
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        frameCsv?.Dispose(); metricCsv?.Dispose(); frameCsv = metricCsv = null;
        if (failure != null) errors.Add("FLOW " + failure);
        File.WriteAllLines(Path.Combine(Out, "checks.txt"), checks);
        File.WriteAllLines(Path.Combine(Out, "notes.txt"), notes);
        File.WriteAllLines(Path.Combine(Out, "runtime_errors.txt"), errors);
        int fail = checks.Count(c => c.StartsWith("FAIL"));
        bool ok = failure == null && fail == 0 && errors.Count == 0;
        File.WriteAllText(Path.Combine(Out, "done.txt"), (ok ? "PASS " : "FAIL ") + (checks.Count - fail) + " pass / " + fail + " fail / errors " + errors.Count
            + (failure != null ? " " + failure.Split('\n')[0] : ""));
        int code = ok ? 0 : 1;
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }

    // ───────────────────────────── measurement ─────────────────────────────

    /// <summary>한 사람의 발 측정 도구. 가중치가 가장 큰 뼈가 발(또는 그 아래 뼈)인 정점만 발바닥 후보로 쓴다.</summary>
    sealed class Rig
    {
        public Fighter3DView view; public Rigidbody2D body; public Fighter fighter;
        public Transform hips, footL, footR;
        readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        readonly List<int[]> left = new List<int[]>(), right = new List<int[]>();
        readonly Mesh baked = new Mesh();
        readonly List<Vector3> verts = new List<Vector3>();
        public string source = "";

        public static Rig Build(Fighter3DView view)
        {
            var r = new Rig { view = view, body = view.GetComponent<Rigidbody2D>(), fighter = view.GetComponent<Fighter>() };
            var a = view.modelAnimator;
            r.hips = a.GetBoneTransform(HumanBodyBones.Hips);
            r.footL = a.GetBoneTransform(HumanBodyBones.LeftFoot); r.footR = a.GetBoneTransform(HumanBodyBones.RightFoot);
            foreach (var smr in view.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                BoneWeight[] w = null;
                try { w = smr.sharedMesh.boneWeights; } catch { w = null; }
                var bones = smr.bones;
                if (w == null || w.Length == 0 || bones == null || bones.Length == 0) continue;
                var L = new List<int>(); var R = new List<int>();
                for (int v = 0; v < w.Length; v++)
                {
                    var bw = w[v]; int idx = bw.boneIndex0; float best = bw.weight0;
                    if (bw.weight1 > best) { best = bw.weight1; idx = bw.boneIndex1; }
                    if (bw.weight2 > best) { best = bw.weight2; idx = bw.boneIndex2; }
                    if (bw.weight3 > best) { best = bw.weight3; idx = bw.boneIndex3; }
                    if (idx < 0 || idx >= bones.Length || bones[idx] == null) continue;
                    var b = bones[idx];
                    if (b == r.footL || b.IsChildOf(r.footL)) L.Add(v);
                    else if (b == r.footR || b.IsChildOf(r.footR)) R.Add(v);
                }
                if (L.Count + R.Count == 0) continue;
                r.skins.Add(smr); r.left.Add(L.ToArray()); r.right.Add(R.ToArray());
                r.source += smr.name + "(" + L.Count + "/" + R.Count + ") ";
            }
            if (r.skins.Count == 0) r.source = "NO SKIN WEIGHTS (ankle height only)";
            return r;
        }

        /// <summary>두 발의 발바닥 가장 낮은 점 y, 그 점 근처(1cm 안) 정점들의 x 평균.</summary>
        public void Soles(out float yL, out float yR, out float xL, out float xR)
        {
            yL = yR = float.PositiveInfinity;
            if (skins.Count == 0)
            {
                yL = footL.position.y; yR = footR.position.y; xL = footL.position.x; xR = footR.position.x; return;
            }
            float sumL = 0, sumR = 0; int nL = 0, nR = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int s = 0; s < skins.Count; s++)
                {
                    var smr = skins[s];
                    if (pass == 0 || skins.Count > 1) { smr.BakeMesh(baked); baked.GetVertices(verts); }
                    var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                        foreach (int i in sideIndex == 0 ? left[s] : right[s])
                        {
                            Vector3 p = m.MultiplyPoint3x4(verts[i]);
                            if (pass == 0) { if (sideIndex == 0) yL = Mathf.Min(yL, p.y); else yR = Mathf.Min(yR, p.y); }
                            else if (sideIndex == 0) { if (p.y < yL + .01f) { sumL += p.x; nL++; } }
                            else if (p.y < yR + .01f) { sumR += p.x; nR++; }
                        }
                }
            xL = nL > 0 ? sumL / nL : footL.position.x; xR = nR > 0 ? sumR / nR : footR.position.x;
        }
        public float Ground => view.Walk != null ? view.Walk.GroundY : view.visualRoot.position.y;
    }

    struct Sample
    {
        public float t, x, hips, hL, hR, cxL, cxR, axL, axR, speed, ik;
        public int planted; public float reach, contact, shift;
        public string state;
    }

    sealed class Segment
    {
        public string path, who, slot, dir; public int facing;
        public readonly List<Sample> s = new List<Sample>();
        public float idleLow = float.NaN, expected;
        public string clip = "";
    }

    static Sample Take(Rig r, float t0)
    {
        r.Soles(out float yL, out float yR, out float xL, out float xR);
        float g = r.Ground;
        return new Sample
        {
            t = Time.time - t0, x = r.body.position.x, hips = (r.hips.position.y - g) * 100f,
            hL = (yL - g) * 100f, hR = (yR - g) * 100f, cxL = xL, cxR = xR, axL = r.footL.position.x, axR = r.footR.position.x,
            speed = r.view.modelAnimator.speed, state = r.view.CurrentState,
            ik = r.view.Walk != null ? r.view.Walk.Weight : 0, planted = r.view.Walk != null ? r.view.Walk.PlantedFoot : -1,
            reach = r.view.Walk != null ? r.view.Walk.ReachError * 100f : 0, contact = r.view.Walk != null ? r.view.Walk.ContactError * 100f : 0,
            shift = ShiftOf(r.view) * 100f
        };
    }

    // 걷기 접지가 없는 예전 코드에서도 돌도록 리플렉션으로 읽는다.
    static readonly PropertyInfo shiftProperty = typeof(Fighter3DView).GetProperty("GroundShift");
    static float ShiftOf(Fighter3DView v) => shiftProperty != null ? (float)shiftProperty.GetValue(v) : 0f;
    static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "nan" : v.ToString("F2", CultureInfo.InvariantCulture);
    static string P(float v) => v.ToString("F4", CultureInfo.InvariantCulture);

    /// <summary>구간 하나를 정리해 metrics.csv 에 한 줄 쓰고 확인 항목을 남긴다.</summary>
    static void Summarize(Segment g, float settle)
    {
        var s = g.s.Where(x => x.t >= settle).ToList();
        string id = $"{g.path} {g.who} {g.slot} {g.dir}";
        if (s.Count < 10) { Check(false, $"{id}: too few samples {s.Count}"); return; }
        float dur = s[s.Count - 1].t - s[0].t;
        float travel = Mathf.Abs(s[s.Count - 1].x - s[0].x) / Mathf.Max(.01f, dur);
        int both1 = 0, both2 = 0; float maxLow = float.MinValue, minLow = float.MaxValue, sumLow = 0, hipMin = float.MaxValue, hipMax = float.MinValue;
        float slipC = 0, slipA = 0, plantedTime = 0, ikSum = 0, highMax = float.MinValue, shiftAbs = 0, shiftMax = 0;
        var states = new Dictionary<string, int>(); float pbSum = 0;
        foreach (var x in s)
        {
            float low = Mathf.Min(x.hL, x.hR);
            if (low > ContactCm) both1++;
            if (low > 2f) both2++;
            maxLow = Mathf.Max(maxLow, low); minLow = Mathf.Min(minLow, low); sumLow += low;
            hipMin = Mathf.Min(hipMin, x.hips); hipMax = Mathf.Max(hipMax, x.hips);
            highMax = Mathf.Max(highMax, Mathf.Max(x.hL, x.hR));
            shiftAbs += Mathf.Abs(x.shift); shiftMax = Mathf.Max(shiftMax, Mathf.Abs(x.shift));
            string key = x.state ?? "null";
            states[key] = (states.TryGetValue(key, out int c) ? c : 0) + 1; pbSum += x.speed; ikSum += x.ik;
        }
        // 딛은 발 미끄럼: 발바닥이 0.8cm 안으로 내리 닿아 있는 구간(5프레임 이상)에서 처음·끝 2프레임(딛는 순간·떼는 순간)을 빼고
        // 발목이 옆으로 움직인 거리. 두 발을 따로 센다.
        for (int foot = 0; foot < 2; foot++)
        {
            int start = -1;
            for (int k = 0; k <= s.Count; k++)
            {
                bool down = k < s.Count && (foot == 0 ? s[k].hL : s[k].hR) < .8f;
                if (down && start < 0) start = k;
                if (down || start < 0) continue;
                for (int j = start + 3; j <= k - 3; j++)
                {
                    slipA += Mathf.Abs(foot == 0 ? s[j].axL - s[j - 1].axL : s[j].axR - s[j - 1].axR);
                    slipC += Mathf.Abs(foot == 0 ? s[j].cxL - s[j - 1].cxL : s[j].cxR - s[j - 1].cxR);
                    plantedTime += s[j].t - s[j - 1].t;
                }
                start = -1;
            }
        }
        float n = s.Count;
        string state = string.Join("+", states.OrderByDescending(k => k.Value).Select(k => k.Key));
        float slipCs = plantedTime > 0 ? slipC / plantedTime * 100f : float.NaN, slipAs = plantedTime > 0 ? slipA / plantedTime * 100f : float.NaN;
        metricCsv.WriteLine(string.Join(",", g.path, g.who, g.slot, g.facing, g.dir, state, F(pbSum / n), F(travel), F(g.expected), F(g.idleLow),
            F(both1 / n * 100f), F(both2 / n * 100f), F(maxLow), F(sumLow / n), F(minLow), F(hipMax - hipMin), F(slipCs), F(slipAs),
            F(travel > .01f ? slipAs / 100f / travel : float.NaN), F(plantedTime / Mathf.Max(.01f, dur) * 100f), F(ikSum / n), F(highMax), F(shiftAbs / n), F(shiftMax), s.Count, g.clip));
        metricCsv.Flush();
        Check(travel > .3f, $"{id}: actually travels ({travel:F2} m/s, expected {g.expected:F2})");
        Check(!state.Split('+')[0].StartsWith("IDLE"), $"{id}: walking state plays ({state})");
        Check(both1 / n <= .10f, $"{id}: both feet off the floor (>1cm) at most 10% of frames ({both1 / n * 100f:F0}%)");
        Check(maxLow <= 2.5f, $"{id}: lowest sole never more than 2.5cm above floor (max {maxLow:F2}cm)");
        Check(minLow >= -2.0f, $"{id}: sole never sinks more than 2cm into floor (min {minLow:F2}cm)");
        Check(!(slipAs > 3f), $"{id}: planted foot slides at most 3cm/s ({slipAs:F2}cm/s at {travel * 100f:F0}cm/s travel)");
        Check(hipMax - hipMin <= 7f, $"{id}: hips bob at most 7cm ({hipMax - hipMin:F1}cm)");
    }

    // ───────────────────────────── images ─────────────────────────────

    static void SetupCameras()
    {
        if (side != null) return;
        var go = new GameObject("FootProbe Side Camera"); Object.DontDestroyOnLoad(go);
        side = go.AddComponent<Camera>(); side.enabled = false; side.orthographic = true; side.orthographicSize = .62f;
        side.nearClipPlane = 1.5f; side.farClipPlane = 60f; side.clearFlags = CameraClearFlags.SolidColor; side.backgroundColor = new Color(.62f, .66f, .72f);
        sideRt = new RenderTexture(800, 450, 24, RenderTextureFormat.ARGB32); sideImg = new Texture2D(800, 450, TextureFormat.RGB24, false);
        gameRt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); gameImg = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    }
    static void Render(Camera cam, RenderTexture rt, Texture2D img, string file)
    {
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = null;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, img.EncodeToJPG(88));
    }
    /// <summary>옆(카메라가 +z 를 본다)에서 다리 쪽을 찍는다. 돌려주는 값은 사진 위에서부터 센 바닥 줄.</summary>
    static int SideShot(Rig r, string file)
    {
        float g = r.Ground; float cx = r.body.position.x;
        side.transform.SetPositionAndRotation(new Vector3(cx, g + .52f, -2f), Quaternion.identity);
        Render(side, sideRt, sideImg, file);
        float bottom = g + .52f - side.orthographicSize;
        return Mathf.RoundToInt(sideRt.height - (g - bottom) / (2 * side.orthographicSize) * sideRt.height);
    }

    // ───────────────────────────── scene control ─────────────────────────────

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    static void Quiet()
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;
            if (n == "UdpActionReceiver" || n == "RedboolDemoReceiver") mb.enabled = false;
            if (n == "BattleIntroSequence" || n == "RedboolBattlePreparation" || n == "BattleSceneBridge") Object.Destroy(mb);
        }
        foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
        {
            f.SetRoundIntroLock(false); f.SetVisionPreparationLock(false); f.InputLocked = false; f.CancelAttack(); f.IsBlocking = false;
            var h = f.GetComponent<Health>(); if (h != null) h.DamageLocked = true;
        }
    }
    static void Place(Component c, float x)
    {
        var b = c.GetComponent<Rigidbody2D>();
        b.position = new Vector2(x, b.position.y); b.linearVelocity = Vector2.zero;
        c.transform.position = new Vector3(x, b.position.y, c.transform.position.z);
        Physics2D.SyncTransforms();
    }
    static void TestRun(string id)
    {
        var run = RunState.Instance;
        var data = new RunData { seed = 684, playerHealth = 100, playerMaxHealth = 100, pendingNodeId = id, currentNodeId = "cave", tutorialCompleted = true, tutorialVersion = 1, storyPresentationVersion = 1 };
        data.map = MapGenerator.Generate(run.Config.map, run.Config, data.seed);
        typeof(RunState).GetProperty("Data").SetValue(run, data);
    }
    static string ClipName(Fighter3DView v)
    {
        var info = v.modelAnimator.GetCurrentAnimatorClipInfo(0);
        return info.Length > 0 && info[0].clip != null ? info[0].clip.name : "";
    }
    static void Record(Segment g, Rig r, float t0, int frame, string key, bool gameShot)
    {
        var smp = Take(r, t0); g.s.Add(smp);
        if (string.IsNullOrEmpty(g.clip) && smp.t > .3f) g.clip = ClipName(r.view);
        int row = -1;
        if (images && smp.t >= .3f && frame % 2 == 0) row = SideShot(r, Path.Combine(Out, "side", key, "s_" + frame.ToString("000") + ".jpg"));
        if (images && gameShot && game != null && frame == 48) Render(game, gameRt, gameImg, Path.Combine(Out, "game", key + ".jpg"));
        frameCsv.WriteLine(string.Join(",", key, frame, F(smp.t), smp.state, F(smp.speed), P(smp.x), F(smp.hips), F(smp.hL), F(smp.hR),
            P(smp.cxL), P(smp.cxR), P(smp.axL), P(smp.axR), F(smp.ik), smp.planted, F(smp.reach), F(smp.contact), F(smp.shift), row));
    }

    /// <summary>한 사람을 정해진 방식으로 걸리며 측정한다.</summary>
    static IEnumerator Walk(Segment g, Rig r, Action drive, Action stop, float seconds, string key)
    {
        float t0 = Time.time; int frame = 0;
        while (Time.time - t0 < seconds)
        {
            drive();
            yield return null;
            Record(g, r, t0, frame, key, true);
            frame++;
        }
        stop();
        Summarize(g, .3f);
        var w = Wait(.5f); while (w.MoveNext()) yield return null;
    }
    static string WalkInternals(Fighter3DView v)
    {
        var w = v.Walk; if (w == null) return "no walk";
        var t = typeof(GroundedBoxingWalk); const BindingFlags B = BindingFlags.Instance | BindingFlags.NonPublic;
        var legs = (Array)t.GetField("legs", B).GetValue(w);
        string legInfo = "";
        if (legs != null) foreach (var l in legs) if (l != null) legInfo += " sole=" + ((float)l.GetType().GetField("soleHeight").GetValue(l) * 100f).ToString("F2") + "cm";
        var r = Rig.Build(v); r.Soles(out float yL, out float yR, out _, out _);
        return $"calibrated={t.GetField("calibrated", B).GetValue(w)} pelvisH={t.GetField("pelvisHeight", B).GetValue(w)} legLen={t.GetField("legLength", B).GetValue(w)} stride={t.GetField("strideScale", B).GetValue(w)}{legInfo}"
            + $" | true ankle-sole L={(r.footL.position.y - yL) * 100f:F2} R={(r.footR.position.y - yR) * 100f:F2}cm";
    }
    static float IdleLow(Rig r)
    {
        r.Soles(out float yL, out float yR, out _, out _);
        return (Mathf.Min(yL, yR) - r.Ground) * 100f;
    }

    static IEnumerator Flow()
    {
        yield return null; yield return null;
        Directory.CreateDirectory(Out);
        frameCsv = new StreamWriter(Path.Combine(Out, "frames.csv"), false, new UTF8Encoding(false));
        frameCsv.WriteLine("seg,frame,t,state,animSpeed,bodyX,hipsCm,soleLcm,soleRcm,contactLx,contactRx,ankleLx,ankleRx,ikWeight,ikPlanted,reachErrCm,contactErrCm,groundShiftCm,floorRow");
        metricCsv = new StreamWriter(Path.Combine(Out, "metrics.csv"), false, new UTF8Encoding(false));
        metricCsv.WriteLine("path,who,slot,facing,dir,state,animSpeed,travel_mps,expected_mps,idleLowest_cm,bothOff1cm_pct,bothOff2cm_pct,maxLowest_cm,meanLowest_cm,minLowest_cm,hipsBob_cm,plantedSlip_cms,plantedAnkleSlip_cms,slipPerTravel,plantedTime_pct,ikWeight,swingPeak_cm,groundShiftMean_cm,groundShiftMax_cm,frames,clip");
        Time.captureFramerate = 60;
        var cfg = RunState.Instance.Config;
        notes.Add("RunConfig.heroMovement=" + cfg.heroMovement + " heroWalkSpeed=" + cfg.heroWalkSpeed);
        SetupCameras();
        if (Only != "pvp")
            foreach (var id in StoryNodes)
            {
                var e = Story(id); while (e.MoveNext()) yield return e.Current;
            }
        if (Only != "story")
        {
            var e = Pvp(); while (e.MoveNext()) yield return e.Current;
        }
        Time.captureFramerate = 0;
    }

    static IEnumerator Story(string id)
    {
        TestRun(id); SceneRoutes.LoadBattle();
        var w = Wait(1.0f); while (w.MoveNext()) yield return null;
        var player = Object.FindAnyObjectByType<PlayerController>(); var enemy = Object.FindAnyObjectByType<EnemyAI>();
        if (player == null || enemy == null) { Check(false, id + ": fighters found"); yield break; }
        string scene = SceneManager.GetActiveScene().name;
        Quiet();
        var foeSpeedField = typeof(EnemyAI).GetField("moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
        float foeSpeed = (float)foeSpeedField.GetValue(enemy) * Fighter3DView.BoxingTravelScale;
        enemy.enabled = false;
        var driver = enemy.gameObject.AddComponent<WalkingPhysicsDriver>();
        player.useKeyboard = false;
        var heroView = player.GetComponent<Fighter3DView>(); var foeView = enemy.GetComponent<Fighter3DView>();
        player.GetComponent<Fighter>().Facing = 1; enemy.GetComponent<Fighter>().Facing = -1;
        game = Camera.main;
        Place(player, -2.4f); Place(enemy, 2.6f);
        w = Wait(1.0f); while (w.MoveNext()) yield return null;
        var hero = Rig.Build(heroView); var foe = Rig.Build(foeView);
        var heroMove = player.GetComponent<HeroAuthoredMovement>();
        notes.Add($"story {id} scene={scene} foe visual={foeView.visualRoot.name} foeSpeed={foeSpeed:F3} foeController={foeView.modelAnimator.runtimeAnimatorController.name} foeAuthoredSteps={foeView.authoredSteps}"
            + $" heroController={heroView.modelAnimator.runtimeAnimatorController.name} heroAuthoredMovementActive={(heroMove != null && heroMove.isActiveAndEnabled)}"
            + $" heroScale={heroView.visualRoot.lossyScale.y:F3} foeScale={foeView.visualRoot.lossyScale.y:F3} heroSkins={hero.source} foeSkins={foe.source}"
            + $" bodyInterp={player.GetComponent<Rigidbody2D>().interpolation} ground={hero.Ground:F3}/{foe.Ground:F3} heroX={player.transform.position.x:F2} foeX={enemy.transform.position.x:F2}");
        float heroIdle = IdleLow(hero), foeIdle = IdleLow(foe);
        float fwdExpected = player.moveSpeed * .55f, backExpected = fwdExpected * .8f;
        IEnumerator e;
        // 주인공: 비전 이동 입력(축 ±1). 같은 겉모습이라 세 무대에서만 잰다.
        if (id == "scout" || id == "gate" || id == "final")
        {
            var g = new Segment { path = "story", who = "hero(" + id + ")", slot = "L", facing = 1, dir = "fwd", expected = fwdExpected, idleLow = heroIdle };
            e = Walk(g, hero, () => player.ExternalMovement(1f), () => player.ExternalMovement(0), 1.4f, $"story_{id}_hero_fwd"); while (e.MoveNext()) yield return null;
            g = new Segment { path = "story", who = "hero(" + id + ")", slot = "L", facing = 1, dir = "back", expected = backExpected, idleLow = heroIdle };
            e = Walk(g, hero, () => player.ExternalMovement(-1f), () => player.ExternalMovement(0), 1.4f, $"story_{id}_hero_back"); while (e.MoveNext()) yield return null;
            if (id == "gate")
            {
                // 키보드 빠르기(0.4 배율)와 같은 느린 걸음
                float slow = .4f / .55f;
                g = new Segment { path = "story", who = "hero(" + id + ",slow)", slot = "L", facing = 1, dir = "fwd", expected = fwdExpected * slow, idleLow = heroIdle };
                e = Walk(g, hero, () => player.ExternalMovement(slow), () => player.ExternalMovement(0), 1.4f, $"story_{id}_heroSlow_fwd"); while (e.MoveNext()) yield return null;
                g = new Segment { path = "story", who = "hero(" + id + ",slow)", slot = "L", facing = 1, dir = "back", expected = backExpected * slow, idleLow = heroIdle };
                e = Walk(g, hero, () => player.ExternalMovement(-slow), () => player.ExternalMovement(0), 1.4f, $"story_{id}_heroSlow_back"); while (e.MoveNext()) yield return null;
            }
            Place(player, -2.4f);
            w = Wait(.4f); while (w.MoveNext()) yield return null;
        }
        // 상대: EnemyAI 와 같은 빠르기(moveSpeed × 0.4)
        {
            var g = new Segment { path = "story", who = id, slot = "R", facing = -1, dir = "fwd", expected = foeSpeed, idleLow = foeIdle };
            e = Walk(g, foe, () => driver.speed = -foeSpeed, () => driver.speed = 0, 1.4f, $"story_{id}_foe_fwd"); while (e.MoveNext()) yield return null;
            g = new Segment { path = "story", who = id, slot = "R", facing = -1, dir = "back", expected = foeSpeed, idleLow = foeIdle };
            e = Walk(g, foe, () => driver.speed = foeSpeed, () => driver.speed = 0, 1.4f, $"story_{id}_foe_back"); while (e.MoveNext()) yield return null;
        }
        if (id == "gate")
        {
            var r1 = Release("story gate foe", foeView, () => driver.speed = -foeSpeed, () => { driver.speed = 0; enemy.GetComponent<Fighter>().TryAttack(CombatMove.Jab); }, null);
            while (r1.MoveNext()) yield return null;
            var r2 = Release("story gate hero", heroView, () => player.ExternalMovement(1f), () => { player.ExternalMovement(0); player.GetComponent<Fighter>().TryAttack(CombatMove.Straight); }, null);
            while (r2.MoveNext()) yield return null;
            var r3 = Release("story gate foe guard", foeView, () => driver.speed = foeSpeed, () => { driver.speed = 0; enemy.GetComponent<Fighter>().TryGuard(false); }, () => enemy.GetComponent<Fighter>().ReleaseGuard());
            while (r3.MoveNext()) yield return null;
        }
        Check(heroIdle > -1.5f && heroIdle < 1.5f, $"story {id}: hero idle sole on floor ({heroIdle:F2}cm)");
        Check(foeIdle > -1.5f && foeIdle < 1.5f, $"story {id}: foe idle sole on floor ({foeIdle:F2}cm)");
    }

    /// <summary>걷다가 바로 공격·막기로 넘어갈 때 걷기 접지가 0.1초 안에 완전히 풀리는지(그 동작 자세가 예전과 같은지) 본다.</summary>
    static IEnumerator Release(string label, Fighter3DView view, Action walk, Action act, Action after)
    {
        float t0 = Time.time;
        while (Time.time - t0 < .6f) { walk(); yield return null; }
        act();
        float start = Time.time, worst = 0f; string state = "";
        while (Time.time - start < .5f)
        {
            yield return null;
            if (Time.time - start > .1f) { worst = Mathf.Max(worst, Mathf.Abs(ShiftOf(view))); if (state == "") state = view.CurrentState; }
        }
        after?.Invoke();
        Check(worst < 1e-5f && state != "IDLE" && !state.StartsWith("WALK"), $"{label}: grounding fully released 0.1s into {state} after walking (max shift {worst * 100f:F3}cm)");
        var w = Wait(.9f); while (w.MoveNext()) yield return null;
    }

    static IEnumerator Pvp()
    {
        SceneManager.LoadScene("UndergroundPit");
        var w = Wait(1.0f); while (w.MoveNext()) yield return null;
        var first = Object.FindAnyObjectByType<PlayerController>(); var enemy = Object.FindAnyObjectByType<EnemyAI>();
        if (first == null || enemy == null) { Check(false, "pvp: arena fighters found"); yield break; }
        Vector3 p2 = enemy.transform.position; enemy.gameObject.SetActive(false);
        // RedboolPvp.SceneLoaded 와 같게: 주인공 겉모습을 하나 간직하고, 1P 를 복제해 2P 를 만든다.
        var firstView = first.GetComponent<Fighter3DView>();
        var holder = new GameObject("FootProbe hero template"); holder.SetActive(false);
        var heroTemplate = Object.Instantiate(firstView.visualRoot.gameObject, holder.transform, false); heroTemplate.name = firstView.visualRoot.name;
        var second = Object.Instantiate(first.gameObject, p2, first.transform.rotation).GetComponent<PlayerController>();
        var players = new[] { first, second };
        for (int i = 0; i < 2; i++) { players[i].name = "FootProbe Player " + (i + 1); players[i].useKeyboard = false; players[i].SetOpponent(players[1 - i].transform); }
        foreach (var cam in Object.FindObjectsByType<Battle3DCamera>(FindObjectsSortMode.None)) { cam.player = first.transform; cam.enemy = second.transform; }
        Quiet();
        game = Camera.main;
        var cfg = RunState.Instance.Config;
        for (int c = 0; c < RedboolPvpRoster.Count; c++)
        {
            var ch = RedboolPvpRoster.Get(c);
            var prefab = ch.node == null ? heroTemplate : cfg.GetEnemyVisual(ch.node);
            for (int slot = 0; slot < 2; slot++)
            {
                var view = players[slot].GetComponent<Fighter3DView>();
                bool keep = c == 0 && slot == 0;
                if (!keep && !view.ApplyVisual(prefab)) Check(false, "pvp " + ch.node + " slot " + slot + ": visual applied");
            }
            Quiet();
            Place(first, -2.4f); Place(second, 1.2f);
            w = Wait(.9f); while (w.MoveNext()) yield return null;
            var rigs = new[] { Rig.Build(first.GetComponent<Fighter3DView>()), Rig.Build(second.GetComponent<Fighter3DView>()) };
            string key = ch.node ?? "hero";
            var hm = first.GetComponent<HeroAuthoredMovement>();
            notes.Add($"pvp {key} visual={rigs[1].view.visualRoot.name} controller={rigs[1].view.modelAnimator.runtimeAnimatorController.name} authoredSteps={rigs[1].view.authoredSteps}"
                + $" heroAuthoredMovementActive={(hm != null && hm.isActiveAndEnabled)} facing={players[0].GetComponent<Fighter>().Facing}/{players[1].GetComponent<Fighter>().Facing}"
                + $" scale={rigs[1].view.visualRoot.lossyScale.y:F3} skins={rigs[1].source} ground={rigs[0].Ground:F3}");
            notes.Add($"pvp {key} walk L: {WalkInternals(rigs[0].view)} || R: {WalkInternals(rigs[1].view)}");
            float idle0 = IdleLow(rigs[0]), idle1 = IdleLow(rigs[1]);
            float fwd = first.moveSpeed * .55f, back = fwd * .8f;
            // 두 사람을 같은 방향으로 함께 걸린다. 오른쪽으로: 1P 앞·2P 뒤, 왼쪽으로: 1P 뒤·2P 앞.
            for (int pass = 0; pass < 2; pass++)
            {
                float axis = pass == 0 ? 1f : -1f;
                var a = new Segment { path = "pvp", who = key, slot = "L", facing = 1, dir = pass == 0 ? "fwd" : "back", expected = pass == 0 ? fwd : back, idleLow = idle0 };
                var b = new Segment { path = "pvp", who = key, slot = "R", facing = -1, dir = pass == 0 ? "back" : "fwd", expected = pass == 0 ? back : fwd, idleLow = idle1 };
                var e = WalkPair(a, b, rigs, players, axis, 1.4f, $"pvp_{key}_L_{a.dir}", $"pvp_{key}_R_{b.dir}");
                while (e.MoveNext()) yield return null;
            }
            Check(idle0 > -1.5f && idle0 < 1.5f, $"pvp {key} L: idle sole on floor ({idle0:F2}cm)");
            Check(idle1 > -1.5f && idle1 < 1.5f, $"pvp {key} R: idle sole on floor ({idle1:F2}cm)");
        }
    }

    static IEnumerator WalkPair(Segment a, Segment b, Rig[] rigs, PlayerController[] players, float axis, float seconds, string keyA, string keyB)
    {
        float t0 = Time.time; int frame = 0;
        while (Time.time - t0 < seconds)
        {
            players[0].ExternalMovement(axis); players[1].ExternalMovement(axis);
            yield return null;
            Record(a, rigs[0], t0, frame, keyA, true);
            Record(b, rigs[1], t0, frame, keyB, false);
            frame++;
        }
        players[0].ExternalMovement(0); players[1].ExternalMovement(0);
        Summarize(a, .3f); Summarize(b, .3f);
        var w = Wait(.5f); while (w.MoveNext()) yield return null;
    }
}
