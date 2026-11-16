using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Explicit editor setup only; never changes gameplay or a running build.</summary>
public static class RedboolApplicationIcon
{
    public const string AssetPath = "Assets/Branding/HeukUnHyeopIcon.png";
    static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Unknown, NamedBuildTarget.Standalone };

    [MenuItem("REDBOOL/Build/Apply Approved Application Icon")]
    public static void Apply()
    {
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        var icon = LoadIcon();
        foreach (var target in Targets)
        {
            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Any);
            if (sizes.Length == 0) throw new Exception("No application icon slots: " + target.TargetName);
            PlayerSettings.SetIcons(target, Enumerable.Repeat(icon, sizes.Length).ToArray(), IconKind.Any);
        }
        AssetDatabase.SaveAssets();
        Validate();
    }

    [MenuItem("REDBOOL/Build/Validate Application Icon")]
    public static void Validate()
    {
        var icon = LoadIcon();
        foreach (var target in Targets)
        {
            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Any);
            var icons = PlayerSettings.GetIcons(target, IconKind.Any);
            if (sizes.Length == 0 || icons.Length != sizes.Length || icons.Any(value => value != icon))
                throw new Exception("Approved application icon missing from " + target.TargetName);
            Debug.Log("[REDBOOL ICON] " + (target == NamedBuildTarget.Unknown ? "Default" : target.TargetName) +
                " sizes=" + String.Join(",", sizes) + " asset=" + AssetPath);
        }
    }

    static Texture2D LoadIcon()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
        if (icon == null || icon.width != 1024 || icon.height != 1024)
            throw new Exception("Expected approved 1024px application icon: " + AssetPath);
        var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
        if (importer == null || importer.mipmapEnabled || !importer.alphaIsTransparency ||
            importer.textureCompression != TextureImporterCompression.Uncompressed)
            throw new Exception("Application icon must retain alpha without compression or mipmaps.");
        return icon;
    }
}
