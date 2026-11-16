using System;
using UnityEngine;

/// <summary>Small player preferences. Never owns a camera or changes recognition rules.</summary>
public static class RedboolDemoSettings
{
    public const string Release = "OnlinePreview";
    const string Prefix = "REDBOOL.Settings.";
    static bool operatorMode;
    public static event Action CameraReconnectRequested;
    public static event Action Changed;

    public static float MasterVolume
    {
        get { return ValidVolume(PlayerPrefs.GetFloat(Prefix + "Volume", 1f)); }
        set
        {
            float volume = ValidVolume(value);
            PlayerPrefs.SetFloat(Prefix + "Volume", volume);
            AudioListener.volume = volume;
            Save();
        }
    }
    static float ValidVolume(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value); }

    public static int CameraIndex
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "CameraIndex", 0), 0, 4); }
        set { PlayerPrefs.SetInt(Prefix + "CameraIndex", Mathf.Clamp(value, 0, 4)); Save(); }
    }

    /// <summary>Consumed by the existing render-budget owner; no second URP writer.</summary>
    public static bool LowQuality
    {
        get { return PlayerPrefs.GetInt(Prefix + "LowQuality", 0) == 1; }
        set { PlayerPrefs.SetInt(Prefix + "LowQuality", value ? 1 : 0); Save(); }
    }

    public static bool Fullscreen
    {
        get { return PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1; }
        set
        {
            PlayerPrefs.SetInt(Prefix + "Fullscreen", value ? 1 : 0);
            if (Application.isPlaying && !Application.isEditor) ApplyDisplay(value);
            Save();
        }
    }
    static void ApplyDisplay(bool fullscreen)
    {
        // Borderless fullscreen keeps the display's refresh rate. Do not switch
        // graphics-quality assets, camera format or recognition resolution.
        Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    /// <summary>Deliberately session-only: guests never inherit debug keys after restart.</summary>
    public static bool OperatorMode
    {
        get { return operatorMode; }
        set { operatorMode = value; ApplyConsole(); Changed?.Invoke(); }
    }

    public static void RequestCameraReconnect() { CameraReconnectRequested?.Invoke(); }
    static void Save() { PlayerPrefs.Save(); Changed?.Invoke(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { operatorMode = false; CameraReconnectRequested = null; Changed = null; }

    static void ApplyConsole() { if (!Application.isEditor) Debug.developerConsoleEnabled = operatorMode; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySaved()
    {
        ApplyConsole();
        AudioListener.volume = MasterVolume;
        if (!Application.isEditor && PlayerPrefs.HasKey(Prefix + "Fullscreen")) ApplyDisplay(Fullscreen);
    }
}
