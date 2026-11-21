using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 엔딩 크레딧 미리보기. EndingScene 을 Play 로 열어 정해진 시각의 화면을 PNG 로 저장한다(-creditsReport 폴더).
/// 배치: -executeMethod EndingCreditsPreview.Capture -creditsReport &lt;폴더&gt;
/// CaptureStart 는 시작 화면(제작진 단추 확인용)을 한 장 저장한다.
/// </summary>
[InitializeOnLoad]
public static class EndingCreditsPreview
{
    const string Flag = "EndingCreditsPreview.Active", Mode = "EndingCreditsPreview.Mode";
    static IEnumerator flow; static int last = -1;
    static string Out { get { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-creditsReport"); return i >= 0 && i + 1 < a.Length ? a[i + 1] : Path.Combine(Application.temporaryCachePath, "EndingCredits"); } }
    static EndingCreditsPreview() { if (SessionState.GetBool(Flag, false)) Subscribe(); }

    [MenuItem("Tools/VisionStriker/Preview Ending Credits")]
    public static void Capture() { Begin("credits", "Assets/Scenes/EndingScene.unity"); }
    public static void CaptureStart() { Begin("start", "Assets/Scenes/StartScene.unity"); }

    static void Begin(string mode, string scene)
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene(scene);
        SessionState.SetString(Mode, mode); SessionState.SetBool(Flag, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.isPaused = false; Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == last) return; last = Time.frameCount;
        try { if (flow == null) flow = SessionState.GetString(Mode, "credits") == "start" ? RunStart() : Run(); if (!flow.MoveNext()) Finish("PASS"); }
        catch (Exception e) { Debug.LogException(e); Finish("FAIL " + e.Message); }
    }
    static void Finish(string result)
    {
        File.WriteAllText(Path.Combine(Out, "credits_done.txt"), result);
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick; flow = null;
        if (Application.isBatchMode) EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1); else EditorApplication.ExitPlaymode();
    }

    static Camera cam; static RenderTexture rt; static Texture2D img;
    static void Shoot(string file)
    {
        if (cam == null)
        {
            cam = new GameObject("PreviewCaptureCam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt; img = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        }
        foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
            if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f + canvas.sortingOrder * -.0001f; }
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); img.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(Out, file), img.EncodeToPNG());
    }

    static IEnumerator Run()
    {
        EndingSceneUI ui = null;
        while (ui == null || !ui.Ready) { ui = UnityEngine.Object.FindAnyObjectByType<EndingSceneUI>(); yield return null; }
        Shoot("warmup.png"); File.Delete(Path.Combine(Out, "warmup.png"));
        yield return null; yield return null;
        float roll = ui.RollSeconds;
        File.WriteAllText(Path.Combine(Out, "credits_info.txt"), $"rollSeconds={roll:F1}");
        foreach (float t in new[] { 3f, roll * .14f, roll * .28f, roll * .42f, roll * .56f, roll * .70f, roll * .84f, roll * .96f, roll + 3f })
        {
            ui.Seek(t);
            Shoot($"credits_{t:000.0}s.png");
            yield return null;
        }
    }

    static IEnumerator RunStart()
    {
        float until = Time.realtimeSinceStartup + 2.5f;
        while (Time.realtimeSinceStartup < until) yield return null;
        Shoot("start_screen.png");
    }
}
