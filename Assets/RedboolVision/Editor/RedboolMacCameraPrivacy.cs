using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Camera-free checks. Unity writes/signs the app; these callbacks never edit a
// signed bundle. The final packaging step independently checks the same key.
public sealed class RedboolMacCameraPrivacy : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 10000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneOSX) return;
        ValidateSettings();
    }

    public static void ValidateSettings()
    {
        // Unity's shared cameraUsageDescription player setting is exposed by
        // this API for Apple builds, including the macOS Player Settings UI.
        if (String.IsNullOrWhiteSpace(PlayerSettings.iOS.cameraUsageDescription))
            throw new BuildFailedException("REDBOOL macOS camera usage description is missing. Set Camera Usage Description before building.");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.StandaloneOSX)
            ValidateApp(report.summary.outputPath);
    }

    public static void ValidateApp(string appPath)
    {
        ValidatePlist(Path.Combine(appPath, "Contents", "Info.plist"));
    }

    public static void ValidatePlist(string path)
    {
        if (!File.Exists(path)) throw new BuildFailedException("Missing macOS Info.plist: " + path);
        using (var reader = XmlReader.Create(path, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
        {
            var doc = XDocument.Load(reader);
            var root = doc.Root;
            var dict = root != null && root.Name.LocalName == "plist" ? root.Element("dict") : null;
            var keys = dict == null ? new XElement[0] : dict.Elements("key")
                .Where(key => key.Value == "NSCameraUsageDescription").ToArray();
            var value = keys.Length == 1 ? keys[0].ElementsAfterSelf().FirstOrDefault() : null;
            if (value == null || value.Name.LocalName != "string" || String.IsNullOrWhiteSpace(value.Value))
                throw new BuildFailedException("REDBOOL macOS app is missing a non-empty NSCameraUsageDescription string. Do not distribute this build.");
        }
    }
}
