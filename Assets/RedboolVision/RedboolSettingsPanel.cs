using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Explicit local settings; opening the panel performs no camera work.</summary>
public sealed class RedboolSettingsPanel : MonoBehaviour
{
    static RedboolSettingsPanel instance;
    GameObject previousSelection;
    Text volumeLabel, fullscreenLabel, qualityLabel, cameraLabel, operatorLabel, message;
    public static bool IsOpen => instance != null;

    public static void Show()
    {
        if (instance != null) return;
        UIFactory.EnsureEventSystem();
        var root = new GameObject("REDBOOL Settings");
        instance = root.AddComponent<RedboolSettingsPanel>();
        instance.previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        instance.Build();
    }

    public static void Close()
    {
        if (instance == null) return;
        RedboolPauseMenu.ConsumeClosingInput();
        var old = instance; instance = null;
        old.gameObject.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(old.previousSelection != null && old.previousSelection.activeInHierarchy ? old.previousSelection : null);
        if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void Build()
    {
        var canvas = UIFactory.CreateCanvas("SettingsCanvas", transform, 1400);
        var shade = UIFactory.CreateImage("SettingsModal", canvas.transform, new Color(0, 0, 0, .78f), true);
        UIFactory.Stretch(shade.rectTransform);
        var panel = UIFactory.CreateImage("SettingsPanel", shade.transform, new Color(.045f, .055f, .055f, .99f), true);
        UIFactory.Place(panel.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(940, 820));
        var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = WuxiaTheme.Gold; outline.effectDistance = new Vector2(1, -1);
        Label(panel.transform, "Title", "설정", 38, 0, -28, 830, 60, TextAnchor.MiddleCenter);
        Label(panel.transform, "Version", "REDBOOL " + RedboolDemoSettings.Release, 20, 0, -85, 830, 28, TextAnchor.MiddleCenter);

        volumeLabel = Label(panel.transform, "Volume", "", 25, -385, -145, 300, 48);
        Button(panel.transform, "VolumeDown", "−", 20, -145, 90, () => { RedboolDemoSettings.MasterVolume -= .1f; Refresh(); });
        Button(panel.transform, "VolumeUp", "+", 130, -145, 90, () => { RedboolDemoSettings.MasterVolume += .1f; Refresh(); });
        Label(panel.transform, "VolumeHint", "음악과 효과음에 함께 적용됩니다.", 20, -385, -195, 750, 32);

        fullscreenLabel = Label(panel.transform, "Display", "", 25, -385, -245, 400, 48);
        Button(panel.transform, "ToggleFullscreen", "전환", 230, -245, 160, () => { RedboolDemoSettings.Fullscreen = !RedboolDemoSettings.Fullscreen; Refresh(); });
        qualityLabel = Label(panel.transform, "Quality", "", 25, -385, -315, 430, 48);
        Button(panel.transform, "ToggleQuality", "전환", 230, -315, 160, () => { RedboolDemoSettings.LowQuality = !RedboolDemoSettings.LowQuality; Refresh(); });
        Label(panel.transform, "QualityHint", "자동은 화면 부담을 조절하고, 낮음은 가벼운 3D 화면을 유지합니다.", 20, -385, -365, 800, 36);

        cameraLabel = Label(panel.transform, "Camera", "", 25, -385, -420, 350, 48);
        Button(panel.transform, "PreviousCamera", "−", 20, -420, 90, () => { RedboolDemoSettings.CameraIndex--; Refresh(); });
        Button(panel.transform, "NextCamera", "+", 130, -420, 90, () => { RedboolDemoSettings.CameraIndex++; Refresh(); });
        Button(panel.transform, "ApplyCamera", "카메라 적용·다시 연결", 30, -485, 530, () =>
        {
            RedboolDemoSettings.RequestCameraReconnect();
            message.text = "전투·수련에서는 카메라를 다시 연결합니다. 메인 메뉴에서는 다음 입장 때 적용돼요.";
        });
        Label(panel.transform, "CameraHint", "다른 카메라가 연결된 경우에만 바꾸세요. 바꾼 뒤 다시 보정하세요.", 20, -385, -540, 800, 36);

        operatorLabel = Label(panel.transform, "Operator", "", 24, -385, -590, 430, 48);
        Button(panel.transform, "ToggleOperator", "전환", 230, -590, 160, () => { RedboolDemoSettings.OperatorMode = !RedboolDemoSettings.OperatorMode; Refresh(); });
        Label(panel.transform, "OperatorHint", "운영자만 사용: 시험용 키를 허용합니다. 게임을 다시 켜면 꺼집니다.", 20, -385, -640, 800, 32);
        message = Label(panel.transform, "SettingsMessage", "", 20, -385, -680, 800, 50);
        message.horizontalOverflow = HorizontalWrapMode.Wrap;
        var close = Button(panel.transform, "CloseSettings", "닫기", 0, -750, 240, Close);
        Refresh();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(close.gameObject);
    }

    void Refresh()
    {
        volumeLabel.text = "전체 소리  " + Mathf.RoundToInt(RedboolDemoSettings.MasterVolume * 100) + "%";
        fullscreenLabel.text = "화면  ·  " + (RedboolDemoSettings.Fullscreen ? "전체 화면" : "창 모드");
        qualityLabel.text = "화면 품질  ·  " + (RedboolDemoSettings.LowQuality ? "낮음" : "자동");
        cameraLabel.text = "카메라  " + (RedboolDemoSettings.CameraIndex + 1) + (RedboolDemoSettings.CameraIndex == 0 ? " · 기본" : "");
        operatorLabel.text = "운영자 시험 기능  ·  " + (RedboolDemoSettings.OperatorMode ? "켜짐" : "꺼짐");
    }

    static Text Label(Transform parent, string name, string text, int size, float x, float y, float width, float height, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var label = WuxiaTheme.Text(name, parent, text, size, alignment, WuxiaTheme.Paper);
        // Left-aligned rows share a fixed left edge; centered titles retain their center.
        var pivot = alignment == TextAnchor.MiddleCenter ? new Vector2(.5f, 1) : new Vector2(0, 1);
        UIFactory.Place(label.rectTransform, new Vector2(.5f, 1), pivot, new Vector2(x, y), new Vector2(width, height));
        return label;
    }
    static Button Button(Transform parent, string name, string text, float x, float y, float width, UnityEngine.Events.UnityAction click)
    {
        var button = UIFactory.CreateButton(name, parent, text, 24, WuxiaTheme.Ink, click);
        WuxiaTheme.StyleButton(button);
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(x, y), new Vector2(width, 48));
        var mark = button.transform.Find("MenuMark"); if (mark != null) mark.gameObject.SetActive(false);
        return button;
    }
}
