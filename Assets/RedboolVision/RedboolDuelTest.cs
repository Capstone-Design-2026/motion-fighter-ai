using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit, unsaved combat rehearsal. Normal campaign routing is unchanged.
/// 시작 화면의 단추는 없앴다. 자동 검사(Begin)와 실행 인자 --redbool-duel-test 로만 들어온다. 이 모드에서 F5 는 처음부터 다시.</summary>
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

    void Update()
    {
        if (Active && Input.GetKeyDown(KeyCode.F5)) Restart();
    }
}
