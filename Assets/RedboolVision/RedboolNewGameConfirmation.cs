using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Nothing touches a saved run until the explicit confirmation callback.</summary>
public sealed class RedboolNewGameConfirmation : MonoBehaviour
{
    static RedboolNewGameConfirmation instance;
    Action confirmed;
    GameObject previousSelection;
    public static bool IsOpen => instance != null;

    public static void Show(Action onConfirm)
    {
        if (instance != null) return;
        UIFactory.EnsureEventSystem();
        var root = new GameObject("REDBOOL New Game Confirmation");
        instance = root.AddComponent<RedboolNewGameConfirmation>();
        instance.confirmed = onConfirm;
        instance.previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        instance.Build();
    }
    public static void Cancel() { Close(false); }
    static void Close(bool accept)
    {
        if (instance == null) return;
        RedboolPauseMenu.ConsumeClosingInput();
        var old = instance; instance = null;
        Action callback = old.confirmed; old.confirmed = null;
        old.gameObject.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(old.previousSelection != null && old.previousSelection.activeInHierarchy ? old.previousSelection : null);
        if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
        if (accept) callback?.Invoke();
    }
    void Build()
    {
        var canvas = UIFactory.CreateCanvas("NewGameConfirmationCanvas", transform, 1500);
        var shade = UIFactory.CreateImage("ConfirmModal", canvas.transform, new Color(0, 0, 0, .78f), true);
        UIFactory.Stretch(shade.rectTransform);
        var panel = UIFactory.CreateImage("ConfirmPanel", shade.transform, new Color(.045f, .055f, .055f, .99f), true);
        UIFactory.Place(panel.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(780, 360));
        var border = panel.gameObject.AddComponent<Outline>(); border.effectColor = WuxiaTheme.Gold; border.effectDistance = new Vector2(1, -1);
        var title = WuxiaTheme.Text("ConfirmTitle", panel.transform, "새 게임을 시작할까요?", 34, TextAnchor.MiddleCenter, WuxiaTheme.Gold);
        UIFactory.Place(title.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -30), new Vector2(700, 60));
        var text = WuxiaTheme.Text("ConfirmMessage", panel.transform, "진행 중인 스토리 저장을 새 게임으로 바꿉니다.\n계속하려면 이어하기를 선택하세요.", 25, TextAnchor.MiddleCenter, WuxiaTheme.Paper);
        UIFactory.Place(text.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(720, 100));
        var cancel = UIFactory.CreateButton("CancelNewGame", panel.transform, "취소", 26, WuxiaTheme.Ink, Cancel);
        var accept = UIFactory.CreateButton("ConfirmNewGame", panel.transform, "새 게임 시작", 26, WuxiaTheme.Ink, () => Close(true));
        WuxiaTheme.StyleButton(cancel); WuxiaTheme.StyleButton(accept);
        UIFactory.Place(cancel.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-170, 45), new Vector2(290, 60));
        UIFactory.Place(accept.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(170, 45), new Vector2(290, 60));
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancel.gameObject);
    }
    void OnDestroy() { if (instance == this) instance = null; }
}
