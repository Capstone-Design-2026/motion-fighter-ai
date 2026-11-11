using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 지도 화면을 정해진 진행 상태로 띄워 PNG 로 저장한다 (batchmode 전용).
/// Unity -batchmode -executeMethod MapScreenshotTool.Run -mapShots &lt;폴더&gt;
/// 실제 저장 데이터는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class MapScreenshotTool
{
    const string Active = "MapScreenshotTool.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static MapScreenshotTool() { if (SessionState.GetBool(Active, false)) Subscribe(); }

    static string Output
    {
        get
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-mapShots");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Logs/MapShots");
        }
    }

    /// <summary>대화 컷씬 몇 개를 실제로 띄워 장면별로 캡처한다.</summary>
    public static void RunCutscenes() { SessionState.SetBool(Active + ".Cutscenes", true); Run(); }

    /// <summary>지도 표시 대안(0=현재 적용, 1~4=대안)을 같은 진행 상태로 차례로 캡처한다.</summary>
    /// <summary>현재 위치 표식·길 강조 대안(0=현재, 1~3)을 캡처한다.</summary>
    public static void RunMarkers() { SessionState.SetBool(Active + ".Markers", true); RunStyles(); }
    /// <summary>배경 어둡게 하기 대안(0=지금, 1~3)을 캡처한다.</summary>
    public static void RunDims() { SessionState.SetBool(Active + ".Dims", true); RunMarkers(); }
    public static void RunStyles() { SessionState.SetBool(Active + ".Styles", true); Run(); }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        AssetDatabase.DisallowAutoRefresh();
        EditorApplication.LockReloadAssemblies();
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 240) { Finish("Timeout"); return; }
        if (!Application.isPlaying) return;
        EditorApplication.isPaused = false;
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = SessionState.GetBool(Active + ".Cutscenes", false) ? CutsceneShots() : SessionState.GetBool(Active + ".Styles", false) ? StyleShots() : Shots(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static IEnumerator Wait(float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
    }

    // 진행 상태: (이름, 현재 위치, 지나온 곳들)
    static readonly (string name, string current, string[] cleared)[] States =
    {
        ("1_start", "cave", new[] { "cave" }),
        ("2_inn", "inn", new[] { "cave", "scout", "inn" }),
        ("3_herb", "herb", new[] { "cave", "scout", "inn", "bridge", "river", "gate", "herb" }),
        ("4_late", "tunnel", new[] { "cave", "scout", "inn", "river", "gate", "herb", "east", "pass", "quarry", "tunnel" }),
    };

    static IEnumerator Shots()
    {
        yield return null; yield return null;
        var run = RunState.Instance;
        foreach (var state in States)
        {
            run.StartNewRun(20261111);
            run.CompleteTutorial();
            run.Data.clearedNodeIds = new List<string>(state.cleared);
            run.Data.currentNodeId = state.current;
            foreach (var id in state.cleared)
                foreach (var phase in new[] { "_prologue", "_enter", "_visit", "_after" })
                    if (!run.Data.seenStoryIds.Contains(id + phase)) run.Data.seenStoryIds.Add(id + phase);
            run.RefreshStatus();
            SceneRoutes.LoadMap();
            yield return null; yield return null; yield return null;
            var wait = Wait(1.2f); while (wait.MoveNext()) yield return null;

            var ui = Object.FindAnyObjectByType<MapSceneUI>();
            if (ui == null) throw new Exception("MapSceneUI not found");
            var canvas = ui.GetComponentInChildren<Canvas>();
            var camGo = new GameObject("MapShotCamera"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.orthographic = true;
            var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; }
            Canvas.ForceUpdateCanvases();
            yield return null;
            var scroll = Object.FindAnyObjectByType<WorldMapScroll>();

            // 기본 배율
            Refocus(ui); yield return null; yield return null;
            wait = Wait(.4f); while (wait.MoveNext()) yield return null;
            Capture(cam, rt, state.name + "_a_default.png");
            // 정보 카드: 갈 수 있는 곳 하나와 지나온 곳 하나
            var show = typeof(MapSceneUI).GetMethod("ShowInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            MapNode fresh = run.Data.map.nodes.Find(n => n.status == NodeStatus.Available && n.IsBattle) ?? run.Data.map.nodes.Find(n => n.status == NodeStatus.Available);
            if (fresh != null) { show.Invoke(ui, new object[] { fresh }); yield return null; Capture(cam, rt, state.name + "_d_card_go.png"); }
            MapNode past = run.Data.map.nodes.Find(n => n.status == NodeStatus.Cleared && n.id != state.current);
            if (past != null) { show.Invoke(ui, new object[] { past }); yield return null; Capture(cam, rt, state.name + "_e_card_past.png"); }
            typeof(MapSceneUI).GetMethod("HideInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            // 단서 기록 화면
            var openJournal = typeof(MapSceneUI).GetMethod("ShowJournal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            openJournal.Invoke(ui, null); yield return null; yield return null;
            Capture(cam, rt, state.name + "_f_journal.png");
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (b.name == "JournalLocation_scout" || b.name == "JournalLocation_gate") { b.onClick.Invoke(); yield return null; yield return null; Capture(cam, rt, state.name + "_g_journal_" + b.name.Substring(16) + ".png"); }
            typeof(MapSceneUI).GetMethod("CloseJournal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
            yield return null;
            // 가까이
            if (scroll != null) { scroll.SetZoom(1.45f); Refocus(ui); yield return null; yield return null; }
            wait = Wait(.3f); while (wait.MoveNext()) yield return null;
            Capture(cam, rt, state.name + "_b_close.png");
            // 멀리
            if (scroll != null) { scroll.SetZoom(.5f); Refocus(ui); yield return null; yield return null; }
            wait = Wait(.3f); while (wait.MoveNext()) yield return null;
            Capture(cam, rt, state.name + "_c_far.png");

            cam.targetTexture = null; Object.Destroy(camGo); rt.Release();
        }
    }

    static IEnumerator StyleShots()
    {
        yield return null; yield return null;
        var run = RunState.Instance;
        bool markers = SessionState.GetBool(Active + ".Markers", false);
        for (int style = 0; style <= (markers ? 3 : 4); style++)
        {
            MapSceneUI.PreviewStyle = markers ? 0 : style;
            bool dims = SessionState.GetBool(Active + ".Dims", false);
            MapSceneUI.MarkerStyle = markers && !dims ? style : 0; MapSceneUI.RouteStyle = markers && !dims ? style : 0;
            MapSceneUI.DimStyle = dims ? style : MapSceneUI.DefaultDimStyle;
            string prefix = dims ? "dim" : markers ? "marker" : "style";
            foreach (var state in States)
            {
                if (state.name != "2_inn" && state.name != "3_herb") continue;
                run.StartNewRun(20261111);
                run.CompleteTutorial();
                run.Data.clearedNodeIds = new List<string>(state.cleared);
                run.Data.currentNodeId = state.current;
                foreach (var id in state.cleared)
                    foreach (var phase in new[] { "_prologue", "_enter", "_visit", "_after" })
                        if (!run.Data.seenStoryIds.Contains(id + phase)) run.Data.seenStoryIds.Add(id + phase);
                run.RefreshStatus();
                SceneRoutes.LoadMap();
                yield return null; yield return null; yield return null;
                var wait = Wait(1.2f); while (wait.MoveNext()) yield return null;
                var ui = Object.FindAnyObjectByType<MapSceneUI>();
                if (ui == null) throw new Exception("MapSceneUI not found");
                var camGo = new GameObject("MapShotCamera"); var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.orthographic = true;
                var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; }
                Canvas.ForceUpdateCanvases();
                yield return null;
                var scroll = Object.FindAnyObjectByType<WorldMapScroll>();
                Refocus(ui); yield return null; yield return null;
                wait = Wait(.4f); while (wait.MoveNext()) yield return null;
                Capture(cam, rt, prefix + style + "_" + state.name + "_default.png");
                if (markers)
                {
                    var show = typeof(MapSceneUI).GetMethod("ShowInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    MapNode fresh = run.Data.map.nodes.Find(n => n.status == NodeStatus.Available && n.IsBattle);
                    if (fresh != null) { show.Invoke(ui, new object[] { fresh }); yield return null; Capture(cam, rt, prefix + style + "_" + state.name + "_tooltip.png"); }
                    typeof(MapSceneUI).GetMethod("HideInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(ui, null);
                }
                if (scroll != null) { scroll.SetZoom(1.3f); Refocus(ui); yield return null; yield return null; }
                wait = Wait(.3f); while (wait.MoveNext()) yield return null;
                Capture(cam, rt, prefix + style + "_" + state.name + "_close.png");
                cam.targetTexture = null; Object.Destroy(camGo); rt.Release();
            }
        }
        MapSceneUI.PreviewStyle = 0; MapSceneUI.MarkerStyle = 0; MapSceneUI.RouteStyle = 0; MapSceneUI.DimStyle = MapSceneUI.DefaultDimStyle;
    }

    static IEnumerator CutsceneShots()
    {
        yield return null; yield return null;
        var run = RunState.Instance;
        run.StartNewRun(20261111);
        run.CompleteTutorial();
        run.Data.clearedNodeIds = new List<string> { "cave" };
        run.Data.currentNodeId = "cave";
        foreach (var phase in new[] { "_prologue", "_enter", "_visit", "_after" }) run.Data.seenStoryIds.Add("cave" + phase);
        run.RefreshStatus();
        SceneRoutes.LoadMap();
        yield return null; yield return null; yield return null;
        var wait = Wait(1.2f); while (wait.MoveNext()) yield return null;
        var ui = Object.FindAnyObjectByType<MapSceneUI>();
        if (ui == null) throw new Exception("MapSceneUI not found");
        var camGo = new GameObject("MapShotCamera"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.orthographic = true;
        var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        foreach (var key in new[] { "scout_visit", "inn_visit", "old_ferry_visit", "gate_visit", "herb_visit", "entrance_visit", "final_visit", "rescue_after" })
        {
            if (StoryCutscene.Load(key) == null) continue;
            ui.StartCoroutine(StoryCutscene.Play(run, key));
            wait = Wait(.8f); while (wait.MoveNext()) yield return null;
            var scene = Object.FindAnyObjectByType<StoryCutscene>();
            if (scene == null) continue;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = c.sortingOrder >= 200 ? .5f : 1f; }
            Canvas.ForceUpdateCanvases();
            for (int line = 0; line < 3; line++)
            {
                wait = Wait(.7f); while (wait.MoveNext()) yield return null;
                Capture(cam, rt, "cutscene_" + key + "_" + (line + 1) + ".png");
                typeof(StoryCutscene).GetField("next", flags).SetValue(scene, true);
                yield return null; yield return null;
                if (Object.FindAnyObjectByType<StoryCutscene>() == null) break;
            }
            scene = Object.FindAnyObjectByType<StoryCutscene>();
            if (scene != null) typeof(StoryCutscene).GetField("skip", flags).SetValue(scene, true);
            wait = Wait(.8f); while (wait.MoveNext()) yield return null;
        }
        cam.targetTexture = null; Object.Destroy(camGo); rt.Release();
    }

    static void Refocus(MapSceneUI ui)
    {
        var method = typeof(MapSceneUI).GetMethod("FocusOnCurrentRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        ui.StartCoroutine((IEnumerator)method.Invoke(ui, null));
    }

    static void Capture(Camera cam, RenderTexture rt, string file)
    {
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Output, file), img.EncodeToPNG());
        Object.Destroy(img);
    }

    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); SessionState.SetBool(Active + ".Styles", false); SessionState.SetBool(Active + ".Cutscenes", false); SessionState.SetBool(Active + ".Markers", false); SessionState.SetBool(Active + ".Dims", false); EditorApplication.update -= Tick;
        EditorApplication.UnlockReloadAssemblies(); AssetDatabase.AllowAutoRefresh();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(Output, "map_done.txt"), failure == null ? "PASS" : "FAIL " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
