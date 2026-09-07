using UnityEngine.SceneManagement;

/// <summary>
/// 씬 이름을 한 곳에 모아둔다. 씬 이름을 바꾸면 여기만 고치면 된다.
/// 다른 스크립트에서 SceneManager.LoadScene("문자열") 을 직접 부르지 말 것.
/// </summary>
public static class SceneRoutes
{
    public const string Start  = "StartScene";
    public const string Map    = "MapScene";
    public const string Battle = "GameScene";

    public static void LoadStart()  { SceneManager.LoadScene(Start);  }
    public static void LoadMap()    { SceneManager.LoadScene(Map);    }
    public static void LoadBattle() { SceneManager.LoadScene(Battle); }
}
