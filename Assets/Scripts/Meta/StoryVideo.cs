using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Space advances one dialogue line and its matching shot. Waiting never advances the story.</summary>
public class StoryVideo : MonoBehaviour
{
    public bool Completed { get; private set; }
    public int CurrentLineIndex { get; private set; }
    public bool IsWaitingForInput { get; private set; }
    public bool IsHolding { get; private set; }
    readonly VideoPlayer[] players = new VideoPlayer[2];
    readonly RenderTexture[] textures = new RenderTexture[2];
    GameObject root;
    RawImage image;
    Text speaker, body, hint, loading;
    int active = -1;
    bool advance;
    string failure;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) RequestAdvance();
    }

    // Shared by keyboard input and runtime checks; loading input is deliberately discarded.
    public void RequestAdvance()
    {
        if (IsWaitingForInput) advance = true;
    }

    public IEnumerator Present(StoryCutsceneDefinition data)
    {
        Completed = false;
        CurrentLineIndex = 0;
        if (data.lines == null || data.lines.Length == 0) yield break;
        foreach (var line in data.lines)
        {
            if (!Exists(line.videoFileName) || !Exists(line.holdVideoFileName))
            {
                Debug.LogWarning("[StoryVideo] Missing dialogue clip; using Space dialogue fallback.");
                yield break;
            }
        }
        Build();
        try
        {
            for (CurrentLineIndex = 0; CurrentLineIndex < data.lines.Length; CurrentLineIndex++)
            {
                var line = data.lines[CurrentLineIndex];
                IsWaitingForInput = false;
                IsHolding = false;
                advance = false;
                hint.text = "";
                // Keep the previous picture and text until the next video has a decoded frame.
                yield return SwitchClip(line.videoFileName, false);
                if (failure != null) yield break;
                speaker.text = line.speaker;
                body.text = line.text;
                hint.text = CurrentLineIndex == data.lines.Length - 1 ? "Space  ·  여정 시작" : "Space  ·  다음";
                IsWaitingForInput = true;
                float lastProgress = Time.realtimeSinceStartup;
                long lastFrame = -1;
                while (!advance && failure == null)
                {
                    var p = players[active];
                    // Play each dramatic beat once, then breathe in a silent seamless loop.
                    if (!IsHolding && !p.isPlaying)
                    {
                        yield return SwitchClip(line.holdVideoFileName, true);
                        IsHolding = failure == null;
                        lastProgress = Time.realtimeSinceStartup;
                        lastFrame = -1;
                    }
                    p = players[active];
                    if (p.frame != lastFrame)
                    {
                        lastFrame = p.frame;
                        lastProgress = Time.realtimeSinceStartup;
                    }
                    if (Time.realtimeSinceStartup - lastProgress > 15) failure = "Playback stalled.";
                    yield return null;
                }
                if (failure != null) yield break;
            }
            IsWaitingForInput = false;
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime)
            {
                root.GetComponent<CanvasGroup>().alpha = 1 - t / .18f;
                yield return null;
            }
            Completed = true;
        }
        finally
        {
            if (failure != null) Debug.LogWarning("[StoryVideo] " + failure + " Using dialogue fallback.");
            Release();
        }
    }

    static bool Exists(string path)
    {
        return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(Application.streamingAssetsPath, path));
    }

    void Build()
    {
        var blocker = UIFactory.CreateImage("VideoInputBlocker", transform, Color.black, true);
        UIFactory.Stretch(blocker.rectTransform);
        root = blocker.gameObject;
        root.AddComponent<CanvasGroup>();
        var rect = UIFactory.CreateRect("CinematicImage", root.transform);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var fit = rect.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 16f / 9f;
        image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
        loading = UIFactory.CreateText("VideoLoading", root.transform, "불러오는 중…", 24, TextAnchor.MiddleCenter);
        UIFactory.Stretch(loading.rectTransform);
        // Attach subtitles to the fitted picture, keeping them readable on wide/tall windows.
        var panel = UIFactory.CreateImage("VideoDialogue", rect, new Color(.02f, .025f, .03f, .96f));
        panel.rectTransform.anchorMin = Vector2.zero;
        panel.rectTransform.anchorMax = new Vector2(1, .167f);
        panel.rectTransform.offsetMin = panel.rectTransform.offsetMax = Vector2.zero;
        speaker = UIFactory.CreateText("VideoSpeaker", panel.transform, "", 25, TextAnchor.MiddleLeft, new Color(.87f, .76f, .56f));
        Position(speaker.rectTransform, new Vector2(.05f,.63f), new Vector2(.8f,.96f));
        body = UIFactory.CreateText("VideoDialogueText", panel.transform, "", 30, TextAnchor.MiddleLeft, new Color(.94f,.93f,.88f));
        Position(body.rectTransform, new Vector2(.05f,.08f), new Vector2(.78f,.68f));
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.supportRichText = false;
        hint = UIFactory.CreateText("VideoAdvanceHint", panel.transform, "", 20, TextAnchor.MiddleRight, new Color(.72f,.69f,.62f));
        Position(hint.rectTransform, new Vector2(.8f,.1f), new Vector2(.97f,.5f));
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject("CinematicPlayer" + i);
            go.transform.SetParent(root.transform, false);
            var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0;
            audio.ignoreListenerPause = true;
            var p = go.AddComponent<VideoPlayer>();
            players[i] = p;
            p.playOnAwake = false;
            p.waitForFirstFrame = true;
            p.skipOnDrop = false; // Windows Unity 6000.4: catch-up can freeze playback.
            p.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            p.source = VideoSource.Url;
            p.renderMode = VideoRenderMode.RenderTexture;
            textures[i] = new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32);
            textures[i].Create();
            p.targetTexture = textures[i];
            p.audioOutputMode = VideoAudioOutputMode.AudioSource;
            p.controlledAudioTrackCount = 1;
            p.SetTargetAudioSource(0,audio);
            p.errorReceived += OnError;
        }
    }

    static void Position(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    IEnumerator SwitchClip(string path, bool loop)
    {
        int next = active == 0 ? 1 : 0;
        var p = players[next];
        p.Stop();
        p.isLooping = loop;
        p.audioOutputMode = loop ? VideoAudioOutputMode.None : VideoAudioOutputMode.AudioSource;
        p.GetComponent<AudioSource>().mute = true;
        p.url = new Uri(Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,path))).AbsoluteUri;
        try { p.Prepare(); } catch (Exception e) { failure = e.Message; }
        float deadline = Time.realtimeSinceStartup + 15;
        while (!p.isPrepared && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
        if (!p.isPrepared || failure != null) { failure = failure ?? "Prepare timeout."; yield break; }
        p.Play();
        deadline = Time.realtimeSinceStartup + 15;
        // Never expose an empty or stale render target during a clip change.
        while (p.frame < 1 && failure == null && Time.realtimeSinceStartup < deadline) yield return null;
        if (p.frame < 1 || failure != null) { failure = failure ?? "First frame timeout."; yield break; }
        if (active >= 0) players[active].Stop();
        active = next;
        image.texture = textures[next];
        image.enabled = true;
        loading.gameObject.SetActive(false);
        p.GetComponent<AudioSource>().mute = loop;
    }

    void OnError(VideoPlayer source, string message) { failure = message; }
    void OnDestroy() { Release(); }
    void Release()
    {
        IsWaitingForInput = false;
        for (int i=0;i<2;i++)
        {
            if (players[i] != null)
            {
                players[i].errorReceived -= OnError;
                players[i].Stop();
                players[i].targetTexture = null;
                players[i] = null;
            }
            if (textures[i] != null) { textures[i].Release(); Destroy(textures[i]); textures[i] = null; }
        }
        if (root != null) { root.SetActive(false); Destroy(root); root = null; }
    }
}
