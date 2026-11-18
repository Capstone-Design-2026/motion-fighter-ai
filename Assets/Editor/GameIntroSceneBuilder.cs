using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 게임 시작 연출 장면(BootScene)을 만들고 빌드 장면 목록 맨 앞에 넣는다(2026-11-16).
/// 장면에는 검은 바탕 카메라와 GameIntro 하나만 있다. 시작 화면(StartScene)은 그 다음 순서 그대로.
/// 메뉴: Tools > VisionStriker > Build Game Intro Scene / 배치: -executeMethod GameIntroSceneBuilder.BuildAndExit
/// </summary>
public static class GameIntroSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/BootScene.unity";

    [MenuItem("Tools/VisionStriker/Build Game Intro Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Boot Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;          // 3D 는 그리지 않는다. 연출은 화면 위 UI 로만 그린다.
        camera.depth = -10;
        new GameObject("Game Intro", typeof(GameIntro));
        EditorSceneManager.SaveScene(scene, ScenePath);

        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[REDBOOL INTRO] built " + ScenePath + " first of " + scenes.Count + " build scenes");
    }

    public static void BuildAndExit()
    {
        string output = Environment.GetEnvironmentVariable("REDBOOL_INTRO_BUILD_OUTPUT");
        int code = 0; string text;
        try
        {
            Build();
            var first = EditorBuildSettings.scenes[0];
            text = "PASS first=" + first.path + " enabled=" + first.enabled + " count=" + EditorBuildSettings.scenes.Length
                + " start_second=" + (EditorBuildSettings.scenes[1].path == "Assets/Scenes/StartScene.unity");
        }
        catch (Exception e) { text = "FAIL " + e; code = 1; }
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, text);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
