using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// 전투 씬을 실제로 재생해 화면을 저장한다 (batchmode 전용). 카메라 구도나 맵을 바꾼 뒤 눈으로 확인할 때 쓴다.
/// Unity -batchmode -executeMethod BattleShotTool.Shots -shotScene &lt;씬 경로&gt; -shotOut &lt;폴더&gt; -shotTag &lt;이름&gt; -redbool-keyboard
/// 저장 데이터는 시작 전에 보관했다가 끝나면 되돌린다.
/// </summary>
[InitializeOnLoad]
public static class BattleShotTool
{
    const string Active = "BattleShotTool.Active", SaveKey = "VisionStriker.Run";
    static IEnumerator flow;
    static int lastFrame = -1;
    static BattleShotTool() { if (SessionState.GetBool(Active, false)) Subscribe(); }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    public static void Shots()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        AssetDatabase.DisallowAutoRefresh(); EditorApplication.LockReloadAssemblies();
        string outDir = Arg("-shotOut", Path.GetFullPath("Logs/BattleShots")); Directory.CreateDirectory(outDir);
        SessionState.SetString(Active + ".Out", outDir);
        SessionState.SetString(Active + ".Tag", Arg("-shotTag", "battle"));
        SessionState.SetBool(Active + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Active + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(Active + ".Start", (float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(Arg("-shotScene", "Assets/Scenes/StoryStages/SealedGate.unity"));
        SessionState.SetBool(Active, true); Subscribe();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".Start", 0) > 200) { Finish("Timeout"); return; }
        if (!Application.isPlaying) return;
        EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (flow == null) flow = Capture(); if (!flow.MoveNext()) Finish(null); }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static IEnumerator Wait(float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < until) yield return null;
    }

    static IEnumerator Capture()
    {
        yield return null; yield return null;
        string outDir = SessionState.GetString(Active + ".Out", ""), tag = SessionState.GetString(Active + ".Tag", "battle");
        var wait = Wait(1.5f); while (wait.MoveNext()) yield return null;
        var player = Object.FindAnyObjectByType<PlayerController>(); var enemy = Object.FindAnyObjectByType<EnemyAI>();
        if (player == null || enemy == null) throw new Exception("fighters not found");
        player.enabled = false; enemy.enabled = false;
        foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) udp.enabled = false;
        foreach (var bridge in Object.FindObjectsByType<BattleSceneBridge>(FindObjectsSortMode.None)) Object.Destroy(bridge);
        Place(player.gameObject, -1.25f); Place(enemy.gameObject, 1.25f);
        wait = Wait(3.0f); while (wait.MoveNext()) yield return null;

        var cam = Camera.main;
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Shot(cam, rt, Path.Combine(outDir, tag + "_1_close.png"));
        var hero = player.GetComponent<Fighter>(); var foe = enemy.GetComponent<Fighter>();
        if (hero != null) hero.TryAttack(CombatMove.Straight);
        wait = Wait(.30f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, Path.Combine(outDir, tag + "_2_punch.png"));
        wait = Wait(1.4f); while (wait.MoveNext()) yield return null;
        if (foe != null) foe.TryAttack(CombatMove.HighKick);
        wait = Wait(.38f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, Path.Combine(outDir, tag + "_3_kick.png"));
        wait = Wait(1.6f); while (wait.MoveNext()) yield return null;
        Place(player.gameObject, -2.3f); Place(enemy.gameObject, 2.3f);
        wait = Wait(2.2f); while (wait.MoveNext()) yield return null;
        Shot(cam, rt, Path.Combine(outDir, tag + "_4_far.png"));
        rt.Release();
    }

    static void Place(GameObject fighter, float x)
    {
        var p = fighter.transform.position; p.x = x; fighter.transform.position = p;
        var body = fighter.GetComponent<Rigidbody2D>(); if (body != null) { body.position = new Vector2(x, body.position.y); body.linearVelocity = Vector2.zero; }
    }

    static void Shot(Camera cam, RenderTexture rt, string path)
    {
        RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt });
        var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(path, img.EncodeToPNG()); Object.Destroy(img);
    }

    static void Finish(string failure)
    {
        SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
        EditorApplication.UnlockReloadAssemblies(); AssetDatabase.AllowAutoRefresh();
        if (SessionState.GetBool(Active + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Active + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(SessionState.GetString(Active + ".Out", "."), "shots_done.txt"), failure == null ? "PASS" : "FAIL " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
