using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Explicit, unsaved combat rehearsal. Normal campaign routing is unchanged.</summary>
public sealed class RedboolDuelTest : MonoBehaviour
{
    public static bool Active { get; private set; }
    // Use the first authored campaign arena, not the empty combat sandbox.
    public const string ArenaScene = "ScoutPass";
    static RedboolDuelTest instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMode() { Active = false; instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CommandLineEntry()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--redbool-duel-test") >= 0) Begin();
    }

    public static void End() { Active=false; if(instance!=null) Destroy(instance.gameObject); instance=null; }

    public static void ReturnToMenu()
    {
        if (!Active) return;
        // Direct duel never owns a campaign run. Leave it through the normal
        // menu route so the integrated camera can finish its diagnostics.
        HitImpact.ReleaseTimeControl();
        Time.timeScale = 1f; Time.fixedDeltaTime = .02f;
        End();
        SceneRoutes.LoadStart();
    }

    public static void Begin()
    {
        Active = true;
        if (instance == null)
        {
            var go = new GameObject("REDBOOL Direct Duel Test");
            instance = go.AddComponent<RedboolDuelTest>();
            DontDestroyOnLoad(go);
        }
        Restart();
    }

    public static void Restart()
    {
        if (!Active) return;
        HitImpact.ReleaseTimeControl();
        Time.timeScale = 1f; Time.fixedDeltaTime = .02f;
        if (!Application.CanStreamedLevelBeLoaded(ArenaScene))
            throw new InvalidOperationException("Direct duel arena missing from build: " + ArenaScene);
        SceneManager.LoadScene(ArenaScene);
        Debug.Log("[REDBOOL DUEL TEST] Direct combat; campaign save is protected.");
    }

    void Start()
    {
        UIFactory.EnsureEventSystem();
        var canvas = UIFactory.CreateCanvas("DuelTestHelp", transform, 210);
        var exit = UIFactory.CreateButton("DuelExitToMenu", canvas.transform, "메뉴로 · Esc", 20,
            WuxiaTheme.Ink, ReturnToMenu);
        UIFactory.Place(exit.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(24, -124), new Vector2(180, 42));
        WuxiaTheme.StyleButton(exit);
        var label = WuxiaTheme.Text("Help", canvas.transform,
            "결투 테스트 · 진행 저장 안 함  |  F5: 다시 시작  |  I: 필살기 충전  |  Esc: 메뉴", 18,
            TextAnchor.MiddleCenter, WuxiaTheme.Paper);
        UIFactory.Place(label.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(0, 24), new Vector2(1100, 40));
    }

    void Update()
    {
        if (!Active) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { ReturnToMenu(); return; }
        if (Input.GetKeyDown(KeyCode.F5)) Restart();
    }
}
