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
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes, locationPathName = output, target = target,
            options = BuildOptions.Development
        });
        var summary = report.summary;
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output), "build-messages.txt"),
            report.steps.SelectMany(step => step.messages.Select(message =>
                step.name + " | " + message.type + " | " + message.content)));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-result.txt"),
            "Result: " + summary.result + "\nScenes: " + scenes.Length +
            "\nErrors: " + summary.totalErrors + "\nWarnings: " + summary.totalWarnings +
            "\nBytes: " + summary.totalSize + "\nSeconds: " + summary.totalTime.TotalSeconds +
            "\nOutput: " + output + "\n" + String.Join("\n", scenes));
        if (summary.result != BuildResult.Succeeded) throw new Exception("Standalone build failed: " + summary.result);
    }
}
