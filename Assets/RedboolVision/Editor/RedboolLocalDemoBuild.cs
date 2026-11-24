using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class RedboolLocalDemoBuild
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Use the isolated batch build project.");
        var output = Environment.GetEnvironmentVariable("REDBOOL_BUILD_OUTPUT");
        if (String.IsNullOrEmpty(output)) throw new Exception("REDBOOL_BUILD_OUTPUT is required.");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0 || scenes.Any(s => !File.Exists(s))) throw new Exception("Missing build scenes.");
        RedboolInputSelfCheck.Run();
        RedboolViewSelfCheck.Run();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var target = Environment.GetEnvironmentVariable("REDBOOL_BUILD_TARGET")=="windows" ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneOSX;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        // 흑운협 아이콘과 시작 화면 로고(Unity 로고 대신). 설정 파일이 어떤 상태여도 빌드마다 같게 넣는다.
        RedboolBranding.Apply();
        // REDBOOL_BUILD_RELEASE=1 이면 배포용으로 만든다: 화면 오른쪽 아래의 "Development Build" 글자가 없다.
        // 가짜 카메라 점검(--redbool-camera-check --redbool-camera-synthetic)은 개발용 빌드에서만 돌기 때문에 기본은 개발용 그대로다.
        bool release = Environment.GetEnvironmentVariable("REDBOOL_BUILD_RELEASE") == "1";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes, locationPathName = output, target = target,
            options = release ? BuildOptions.None : BuildOptions.Development
        });
        var summary = report.summary;
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output), "build-messages.txt"),
            report.steps.SelectMany(step => step.messages.Select(message =>
                step.name + " | " + message.type + " | " + message.content)));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-result.txt"),
            "Result: " + summary.result + "\nDevelopment: " + !release + "\nScenes: " + scenes.Length +
            "\nErrors: " + summary.totalErrors + "\nWarnings: " + summary.totalWarnings +
            "\nBytes: " + summary.totalSize + "\nSeconds: " + summary.totalTime.TotalSeconds +
            "\nOutput: " + output + "\n" + String.Join("\n", scenes));
        if (summary.result != BuildResult.Succeeded) throw new Exception("Standalone build failed: " + summary.result);
    }
}
