using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 게임에 넣은 적 필살기(EnemySpecial)를 실제 전투에서 확인한다(batchmode 전용). 적 AI 를 켠 채로 스토리 전투 11곳을 차례로 돌린다.
///   normal    : 적이 실제로 때려서 게이지가 차는지 보고, 가득 채운 뒤 스스로 발동해 끝까지 가는지, 끝난 뒤 화면·조명·시간이 원래대로인지, 다시 차지 않는지.
///   guard     : 주인공이 막고 있으면 피해가 줄고 부가 효과가 안 걸리는지.
///   interrupt : 모으는 중에 맞으면 끊기고 게이지 일부를 돌려받는지.
///   armoured  : 철문은 모으는 중에 맞아도 끊기지 않는지.
/// Unity -batchmode -executeMethod EnemySpecialPreview.PlayCheck -redbool-keyboard
/// 결과: Library/EnemySpecialPlayCheck/ (shots_done.txt, 적별 장면 사진)
/// </summary>
public static partial class EnemySpecialPreview
{
    const string CheckActive = "EnemySpecialPlayCheck.Active";
    static readonly float[] ShotAt = { .5f, 1.1f, .75f, .35f, 1.1f, .8f, .9f, 1.5f, 1.4f, .6f, .8f };
    static IEnumerator checkFlow;
    static int checkFrame = -1, checkFailures;
    static readonly List<string> checkLines = new List<string>();
    static string CheckOut => Path.GetFullPath(Env("PREVIEW_OUT") ?? "Library/EnemySpecialPlayCheck");

    [InitializeOnLoadMethod]
    static void ResumeCheck()
    {
        if (!SessionState.GetBool(CheckActive, false)) return;
        EditorApplication.update -= CheckTick; EditorApplication.update += CheckTick;
    }

    public static void PlayCheck()
    {
        if (!Application.isBatchMode) throw new Exception("Run with Unity -batchmode.");
        EditorSceneManager.OpenScene("Assets/Scenes/ScoutPass.unity");
        Directory.CreateDirectory(CheckOut);
        foreach (var file in Directory.GetFiles(CheckOut)) File.Delete(file);
        SessionState.SetBool(CheckActive + ".HadSave", PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(CheckActive + ".Save", PlayerPrefs.GetString(SaveKey, ""));
        File.WriteAllText(Path.GetFullPath("Library/EnemySpecialPlayCheck_save_backup.json"), PlayerPrefs.GetString(SaveKey, ""));
        SessionState.SetFloat(CheckActive + ".Start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(CheckActive, true); ResumeCheck();
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static void CheckTick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(CheckActive + ".Start", 0) > 1500) { CheckEnd("Timeout"); return; }
        if (!Application.isPlaying || EditorApplication.isCompiling) return;
        Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
        if (checkFrame == Time.frameCount) return;
        checkFrame = Time.frameCount;
        try { if (checkFlow == null) checkFlow = Flatten(CheckFlow()); if (!checkFlow.MoveNext()) CheckEnd(null); }
        catch (Exception e) { CheckEnd(e.ToString()); }
    }

    static void CheckEnd(string failure)
    {
        SessionState.SetBool(CheckActive, false); EditorApplication.update -= CheckTick;
        Time.captureFramerate = 0; Time.timeScale = 1;
        if (SessionState.GetBool(CheckActive + ".HadSave", false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(CheckActive + ".Save", ""));
        else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        bool restored = PlayerPrefs.GetString(SaveKey, "") == SessionState.GetString(CheckActive + ".Save", "");
        bool pass = failure == null && checkFailures == 0;
        Directory.CreateDirectory(CheckOut);
        File.WriteAllText(CheckOut + "/shots_done.txt", (pass ? "PASS" : "FAIL " + (failure ?? checkFailures + " check(s) failed")) + "\nsave restored: " + restored + "\n" + string.Join("\n", checkLines));
        EditorApplication.Exit(pass ? 0 : 1);
    }

    static void Report(bool ok, string line)
    {
        if (!ok) checkFailures++;
        checkLines.Add((ok ? "ok    " : "FAIL  ") + line);
        Debug.Log("[EnemySpecialPlayCheck] " + (ok ? "ok " : "FAIL ") + line);
    }

    static IEnumerator CheckFlow()
    {
        yield return null; yield return null;
        Time.captureFramerate = Fps;
        var rt = new RenderTexture(1600, 900, 24); rt.Create();
        var shot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var only = new HashSet<int>();
        foreach (var part in (Env("PREVIEW_ONLY") ?? "").Split(',')) if (int.TryParse(part.Trim(), out int number)) only.Add(number);
        for (int i = 0; i < Specs.Length; i++)
            if (only.Count == 0 || only.Contains(i + 1)) yield return CheckStage(i, "normal", rt, shot);
        if (only.Count == 0)
        {
            yield return CheckStage(0, "guard", rt, shot);
            yield return CheckStage(0, "interrupt", rt, shot);
            yield return CheckStage(4, "armoured", rt, shot);
        }
    }

    static IEnumerator CheckStage(int index, string mode, RenderTexture rt, Texture2D shot)
    {
        var entry = Specs[index]; string tag = (index + 1).ToString("00") + " " + entry.owner + " " + entry.hangul + " [" + mode + "]";
        const float dt = 1f / Fps;
        StartStoryBattle(entry.id);
        string scene = SceneRoutes.PendingBattleScene; SceneRoutes.LoadBattle();
        for (int k = 0; k < LoadFrames; k++) yield return null;
        var playerController = Object.FindAnyObjectByType<PlayerController>(); var enemyAi = Object.FindAnyObjectByType<EnemyAI>();
        if (playerController == null || enemyAi == null) { Report(false, tag + ": fighters not found in " + scene); yield break; }
        foreach (var udp in Object.FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) udp.enabled = false;
        var heroFighter = playerController.GetComponent<Fighter>(); var heroHealth = playerController.GetComponent<Health>();
        var foeFighter = enemyAi.GetComponent<Fighter>(); var foeHealth = enemyAi.GetComponent<Health>(); var special = enemyAi.GetComponent<EnemySpecial>();
        for (int k = 0; k < SettleFrames; k++) yield return null;
        if (special == null || !special.Available) { Report(false, tag + ": the enemy has no usable special (component " + (special != null) + ")"); yield break; }
        if (special.MoveName != entry.hangul) { Report(false, tag + ": wrong move " + special.MoveName); yield break; }
        var camera = Camera.main;
        var sunLight = RenderSettings.sun; float sunBefore = sunLight != null ? sunLight.intensity : -1f; Color ambientBefore = RenderSettings.ambientLight; var skyBefore = RenderSettings.skybox;
        Text gaugeLabel = null;
        var vitals = GameObject.Find("EnemyVitals");
        if (vitals != null) foreach (var text in vitals.GetComponentsInChildren<Text>(true)) if (text.name == "GaugeLabel") gaugeLabel = text;
        string Label() => gaugeLabel != null ? gaugeLabel.text : "(no label)";

        bool guarding = mode == "guard";
        if (guarding) playerController.enabled = false;

        // 적이 실제로 때려서 게이지가 차는지
        int natural = -1; float waited = 0f;
        if (mode == "normal")
        {
            while (special.Charge < 1 && waited < 12f) { waited += dt; yield return null; }
            natural = special.Charge;
        }
        string labelCharging = Label();
        // 부가 효과 확인 준비: 승표탈기는 주인공 게이지를 채워 두고, 흑영교는 적 체력을 깎아 둔다
        var heroGauge = playerController.GetComponent<SpecialMove>(); int heroGaugeBefore = -1; bool sawSeal = false;
        if (mode == "normal" && index == 3 && heroGauge != null) { heroGauge.AddCharge(heroGauge.HitsToCharge); heroGaugeBefore = heroGauge.Charge; }
        if (mode == "normal" && index == 9) foeHealth.SetCurrentHealth(Mathf.RoundToInt(foeHealth.MaxHealth * .55f));
        heroHealth.SetCurrentHealth(heroHealth.MaxHealth);
        special.AddCharge(special.HitsToCharge);
        string labelReady = Label();
        float fired = 0f;
        while (!special.IsPlaying && fired < 8f) { if (guarding) heroFighter.TryGuard(false); fired += dt; yield return null; }
        if (!special.IsPlaying) { Report(false, tag + ": did not fire within 8 s (charge " + special.Charge + ", label '" + Label() + "')"); yield break; }

        int heroBefore = heroHealth.CurrentHealth, foeBefore = foeHealth.CurrentHealth;
        bool sawSlow = false, struck = false, shotA = false, shotB = false; float acting = 0f, duration = 0f; string labelPlaying = Label();
        while (special.IsPlaying && duration < 16f)
        {
            if (guarding) heroFighter.TryGuard(false);
            if (Time.timeScale < .5f) sawSlow = true;
            if (heroFighter.IsAttackSealed) sawSeal = true;
            if ((mode == "interrupt" || mode == "armoured") && special.CurrentPhase == EnemySpecial.Phase.Gather && !struck) { struck = true; foeHealth.TakeDamage(5, Vector2.zero, false, true); }
            if (special.CurrentPhase == EnemySpecial.Phase.Acting)
            {
                acting += dt;
                if (mode == "normal" && camera != null)
                {
                    if (!shotA && acting >= ShotAt[index]) { shotA = true; CheckShot(camera, rt, shot, (index + 1).ToString("00") + "_" + entry.key + "_a"); }
                    if (!shotB && acting >= ShotAt[index] + .4f) { shotB = true; CheckShot(camera, rt, shot, (index + 1).ToString("00") + "_" + entry.key + "_b"); }
                }
            }
            duration += dt; yield return null;
        }
        bool ended = !special.IsPlaying; int heroAfter = heroHealth.CurrentHealth; string labelAfter = Label();
        bool used = special.Used; int chargeAfter = special.Charge; int dealt = special.LastDamage; bool guardedResult = special.LastGuarded;
        playerController.enabled = false; enemyAi.enabled = false;   // 뒷정리 확인 동안 적이 더 때리지 않게
        for (int k = 0; k < Fps * 5; k++) yield return null;

        var root = GameObject.Find("EnemySpecial FX"); int leftovers = root != null ? root.transform.childCount : 0;
        bool timeOk = Mathf.Approximately(Time.timeScale, 1f);
        bool lightOk = (sunLight == null || Mathf.Abs(sunLight.intensity - sunBefore) < .001f) && RenderSettings.ambientLight == ambientBefore && RenderSettings.skybox == skyBefore;
        bool freed = !foeFighter.IsSpecialActive;
        special.AddCharge(special.HitsToCharge); int refill = special.Charge;

        string facts = "natural charge " + natural + " in " + waited.ToString("0.0") + "s, fired after " + fired.ToString("0.00") + "s, lasted " + duration.ToString("0.0") + "s, slow " + sawSlow
            + ", damage " + dealt + " (hero " + heroBefore + "→" + heroAfter + "), guarded " + guardedResult + ", used " + used + ", charge after " + chargeAfter + ", refill " + refill
            + ", foe hp " + foeBefore + "→" + foeHealth.CurrentHealth + ", leftovers " + leftovers + ", time " + timeOk + ", light " + lightOk + ", freed " + freed
            + ", labels '" + labelCharging + "' / '" + labelReady + "' / '" + labelPlaying + "' / '" + labelAfter + "'";
        bool extra = true;
        if (mode == "normal" && index == 3) { int gaugeAfter = heroGauge != null ? heroGauge.Charge : -1; extra = heroGaugeBefore > 0 && gaugeAfter == 0; facts += ", hero gauge " + heroGaugeBefore + "→" + gaugeAfter; }
        if (mode == "normal" && index == 7) { extra = sawSeal; facts += ", attacks sealed " + sawSeal; }
        if (mode == "normal" && index == 9) { extra = foeHealth.CurrentHealth > foeBefore; facts += ", healed " + (foeHealth.CurrentHealth - foeBefore); }
        bool common = ended && timeOk && lightOk && freed && leftovers == 0 && extra;
        bool ok;
        switch (mode)
        {
            case "guard": ok = common && used && guardedResult && dealt > 0 && dealt <= 12 && refill == 0; break;
            case "interrupt": ok = common && struck && !used && chargeAfter == 2 && duration < 3f; break;
            case "armoured": ok = common && struck && used && dealt > 0; break;
            default: ok = common && natural >= 1 && sawSlow && used && dealt >= 20 && heroBefore - heroAfter == dealt && refill == 0 && labelAfter.Contains("사용함") && labelPlaying.Contains("발동"); break;
        }
        Report(ok, tag + ": " + facts);
    }

    static void CheckShot(Camera camera, RenderTexture rt, Texture2D shot, string name)
    {
        var previousTarget = camera.targetTexture; camera.targetTexture = rt; camera.Render(); camera.targetTexture = previousTarget;
        var previous = RenderTexture.active; RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); shot.Apply(false);
        RenderTexture.active = previous;
        File.WriteAllBytes(CheckOut + "/" + name + ".jpg", shot.EncodeToJPG(90));
    }
}
