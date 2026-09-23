using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>최종 보스 승리 후 크레딧. 내용과 길이는 RunConfig에서 편집한다.</summary>
public class EndingSceneUI : MonoBehaviour
{
    private RectTransform roll;
    private RectTransform viewport;
    private float elapsed;
    private float duration;
    private bool leaving;
    private bool ready;
    private float finishedAt = -1f;

    IEnumerator Start()
    {
        Time.timeScale = 1f;
        UIFactory.EnsureEventSystem();
        var run = RunState.Instance;
        var config = run != null ? run.Config : null;
        duration = config != null ? Mathf.Max(5f, config.creditsDuration) : 32f;
        var canvas = UIFactory.CreateCanvas("EndingCanvas", transform, 200);
        var bg = UIFactory.CreateImage("Background", canvas.transform, new Color(0.025f, 0.035f, 0.06f), true);
        UIFactory.Stretch(bg.rectTransform);

        var title = UIFactory.CreateText("Title", canvas.transform,
            config != null ? config.endingTitle : "VISION STRIKER", 64,
            TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.4f));
        UIFactory.Place(title.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -35f), new Vector2(1500f, 100f));

        viewport = UIFactory.CreateRect("CreditsViewport", canvas.transform);
        UIFactory.Stretch(viewport, 100f, 140f, 100f, 155f);
        viewport.gameObject.AddComponent<RectMask2D>();
        string content = config != null ? config.creditsText : "제작진";
        if (config != null && !string.IsNullOrWhiteSpace(config.creditsAttributions))
            content += "\n\n음악 · 사운드 · 폰트 · 외부 에셋\n\n" + config.creditsAttributions;
        content += "\n\n플레이해 주셔서 감사합니다.\n\nTHANK YOU FOR PLAYING";
        // 한 Text의 높이 추정에 의존하지 않고 줄별 영역을 스크롤한다.
        roll = UIFactory.CreateRect("CreditsRoll", viewport);
        roll.anchorMin = new Vector2(0f, 0f);
        roll.anchorMax = new Vector2(1f, 0f);
        roll.pivot = new Vector2(.5f, 1f);
        roll.anchoredPosition = Vector2.zero;
        Font koreanFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 32);
        var lines = new List<Text>();
        foreach (var line in content.Replace("\r", "").Split('\n'))
        {
            var text = UIFactory.CreateText("Line_" + lines.Count, roll, line, 32, TextAnchor.UpperCenter);
            if (koreanFont != null) text.font = koreanFont;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(.5f, 1f);
            lines.Add(text);
        }

        var button = UIFactory.CreateButton("ReturnToStart", canvas.transform,
            "건너뛰기 / 시작 화면", 28, new Color(.18f, .2f, .28f), Finish);
        UIFactory.Place(button.GetComponent<RectTransform>(), new Vector2(.5f, 0f),
            new Vector2(.5f, 0f), new Vector2(0f, 38f), new Vector2(420f, 68f));

        // CanvasScaler와 viewport 크기가 확정된 다음 줄 높이와 이동 거리를 계산한다.
        yield return null;
        Canvas.ForceUpdateCanvases();
        float height = 0f;
        foreach (var text in lines)
        {
            float lineHeight = string.IsNullOrEmpty(text.text) ? 38f
                : Mathf.Max(64f, text.preferredHeight + 20f);
            text.rectTransform.sizeDelta = new Vector2(0f, lineHeight);
            text.rectTransform.anchoredPosition = new Vector2(0f, -height);
            height += lineHeight;
        }
        roll.sizeDelta = new Vector2(0f, height + 80f);
        Canvas.ForceUpdateCanvases();
        ready = true;
    }

    void Update()
    {
        if (leaving || !ready || roll == null) return;
        elapsed += Time.unscaledDeltaTime;
        roll.anchoredPosition = new Vector2(0f,
            Mathf.Lerp(0f, viewport.rect.height + roll.rect.height + 40f, Mathf.Clamp01(elapsed / duration)));
        // 마지막 줄의 영역까지 viewport 위를 완전히 지난 후 2초 기다린다.
        bool allPassed = roll.anchoredPosition.y - roll.rect.height >= viewport.rect.height;
        if (allPassed && finishedAt < 0f) finishedAt = elapsed;
        if (Input.GetKeyDown(KeyCode.Escape) || (finishedAt >= 0f && elapsed - finishedAt >= 2f))
            Finish();
    }

    private void Finish()
    {
        if (leaving) return;
        leaving = true;
        if (RunState.Instance != null) RunState.Instance.AbandonRun();
        SceneRoutes.LoadStart();
    }
}
