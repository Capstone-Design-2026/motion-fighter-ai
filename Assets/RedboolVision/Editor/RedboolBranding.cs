using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 흑운협 아이콘과 시작 화면(2026-11-16). 게임을 켤 때 Unity 로고 대신 제목 붓글씨 로고가 나오고,
/// 실행 파일·작업 표시줄 아이콘은 금빛 "黑" 문양이 된다. 그림은 Assets/Branding/ 에 있고
/// 제목 그림(Resources/UI/Wuxia/HeukUnHyeopTitle.png)에서 만들었다.
/// 빌드할 때마다 RedboolLocalDemoBuild 가 Apply 를 불러, 설정 파일 상태와 상관없이 같은 아이콘·시작 화면이 들어간다.
/// 메뉴: Tools > VisionStriker > Apply Game Icon And Splash
/// </summary>
public static class RedboolBranding
{
    public const string IconPath = "Assets/Branding/HeukUnHyeopIcon.png";
    public const string SplashPath = "Assets/Branding/HeukUnHyeop_SplashLogo.png";
    // 시작 화면 바탕: 제목 화면의 먹색과 같은 계열
    static readonly Color Background = new Color(.078f, .068f, .063f, 1f);

    [MenuItem("Tools/VisionStriker/Apply Game Icon And Splash")]
    public static void Apply()
    {
        Import(IconPath, false);
        Import(SplashPath, true);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        var logo = AssetDatabase.LoadAssetAtPath<Sprite>(SplashPath);
        if (icon == null || logo == null) throw new Exception("Branding images are missing: " + IconPath + ", " + SplashPath);

        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        PlayerSettings.SetIcons(NamedBuildTarget.Standalone, new[] { icon }, IconKind.Any);

        // 2026-11-16: 빌드 첫 장면(BootScene)의 시작 연출(GameIntro)이 로고 역할을 하므로 Unity 기본 시작 화면은 끈다.
        // 로고 설정은 남겨 두어, 연출을 빼고 다시 켜면(show = true) 흑운협 로고가 나온다.
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
        PlayerSettings.SplashScreen.backgroundColor = Background;
        PlayerSettings.SplashScreen.background = null;
        PlayerSettings.SplashScreen.overlayOpacity = 0f;
        PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2.4f, logo) };
        AssetDatabase.SaveAssets();
        Debug.Log("[REDBOOL BRANDING] icon=" + IconPath + " splash_logo=" + SplashPath
            + " show_unity_logo=" + PlayerSettings.SplashScreen.showUnityLogo
            + " logos=" + PlayerSettings.SplashScreen.logos.Length);
    }

    static void Import(string path, bool sprite)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = false;
        if (sprite && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
        { importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
        if (!sprite && importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
        if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
        if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
        if (changed) importer.SaveAndReimport();
    }

    /// <summary>배치 실행용: Apply 후 결과를 REDBOOL_BRANDING_OUTPUT 파일에 적고 끝낸다.</summary>
    public static void ApplyAndExit()
    {
        string output = Environment.GetEnvironmentVariable("REDBOOL_BRANDING_OUTPUT");
        int code = 0; string text;
        try
        {
            Apply();
            text = "PASS show_unity_logo=" + PlayerSettings.SplashScreen.showUnityLogo
                + " logos=" + PlayerSettings.SplashScreen.logos.Length
                + " icons=" + PlayerSettings.GetIcons(NamedBuildTarget.Standalone, IconKind.Any).Length;
            if (PlayerSettings.SplashScreen.showUnityLogo) { text = "FAIL Unity logo could not be turned off (license?) " + text; code = 1; }
        }
        catch (Exception e) { text = "FAIL " + e; code = 1; }
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, text);
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
