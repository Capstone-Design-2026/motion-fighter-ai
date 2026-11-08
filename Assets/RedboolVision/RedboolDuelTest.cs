using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Explicit, unsaved combat rehearsal. Normal campaign routing is unchanged.</summary>
public sealed class RedboolDuelTest : MonoBehaviour
{
    public static bool Active { get; private set; }
    static RedboolDuelTest instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMode() { Active = false; instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CommandLineEntry()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--redbool-duel-test") >= 0) Begin();
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
        SceneManager.LoadScene(SceneRoutes.Battle);
        Debug.Log("[REDBOOL DUEL TEST] Direct combat; campaign save is protected.");
    }

    void Start()
    {
        var canvas = UIFactory.CreateCanvas("DuelTestHelp", transform, 210);
        var label = WuxiaTheme.Text("Help", canvas.transform,
            "결투 테스트 · 진행 저장 안 함  |  F5: 처음부터 다시  |  I: 필살기 충전", 18,
            TextAnchor.MiddleCenter, WuxiaTheme.Paper);
        UIFactory.Place(label.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(0, 24), new Vector2(1100, 40));
    }

    void Update()
    {
        if (Active && Input.GetKeyDown(KeyCode.F5)) Restart();
    }
}
