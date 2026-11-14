using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 적 필살기 이펙트 재료를 게임용 에셋으로 굽는다. 미리보기 도구가 실행 중에 만들던 것과 같은 텍스처·재질이다.
/// Unity -batchmode -executeMethod EnemySpecialPreview.BuildGameAssets
/// 결과: Assets/Effects/EnemySpecials/(Textures, Materials), Assets/Resources/EnemySpecials/EnemySpecialAssets.asset
/// </summary>
public static partial class EnemySpecialPreview
{
    const string GameFolder = "Assets/Effects/EnemySpecials", GameAsset = "Assets/Resources/EnemySpecials/EnemySpecialAssets.asset";

    [MenuItem("Tools/Vision Striker/Build Enemy Special Assets")]
    public static void BuildGameAssets()
    {
        string report = Path.GetFullPath("Library/EnemySpecialBuild");
        Directory.CreateDirectory(report);
        try
        {
            BuildLook();
            string textures = GameFolder + "/Textures", materials = GameFolder + "/Materials";
            Directory.CreateDirectory(textures); Directory.CreateDirectory(materials); Directory.CreateDirectory(Path.GetDirectoryName(GameAsset));
            AssetDatabase.Refresh();

            Texture2D Save(Texture2D source, string name, bool linear, bool normal, TextureWrapMode wrap, bool crisp)
            {
                string path = textures + "/" + name + ".png";
                File.WriteAllBytes(path, source.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !linear && !normal;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true; importer.wrapMode = wrap; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.textureCompression = crisp ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (saved == null) throw new Exception("Texture did not import: " + path);
                return saved;
            }
            Material Keep(string name, Material material)
            {
                string path = materials + "/" + name + ".mat";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(material, path);
                return material;
            }
            Material WithEmission(Material material)
            {
                // 런타임에 색만 바꿔 은은하게 빛나게 하므로, 빌드에 이 변형이 들어가도록 켜 둔다.
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                return material;
            }

            var repeat = TextureWrapMode.Repeat; var clamp = TextureWrapMode.Clamp;
            var asset = ScriptableObject.CreateInstance<EnemySpecialAssets>();
            asset.ribbon = ribbonShader; asset.smoke = smokeShader; asset.orb = Shader.Find("VisionStriker/QiOrb");
            asset.ribbonTex = Save(texRibbon, "special_ribbon", true, false, repeat, true);
            asset.brushTex = Save(texBrush, "special_brush", true, false, repeat, true);
            asset.noiseTex = Save(texNoise, "special_noise", true, false, repeat, true);
            asset.smokeTex = Save(texSmoke, "special_smoke", true, false, clamp, false);
            asset.talismanTex = Save(texTalisman, "special_talisman", true, false, clamp, true);
            asset.shockTex = Save(texShock, "special_shock", true, false, clamp, true);
            asset.crackTex = Save(texCrack, "special_crack", true, false, clamp, true);
            asset.bellTex = Save(texBell, "special_bell", true, false, repeat, true);
            asset.glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/QiPalm/Textures/qi_glow.png");
            var ropeTex = Save(texRope, "special_rope", false, false, repeat, false);
            var woodA = Save(woodAlbedo, "special_wood", false, false, repeat, false); var woodN = Save(woodNormal, "special_wood_normal", true, true, repeat, false);
            var ironA = Save(ironAlbedo, "special_iron", false, false, repeat, false); var ironN = Save(ironNormal, "special_iron_normal", true, true, repeat, false);
            var ironM = Save(ironMask, "special_iron_mask", true, false, repeat, false);
            var bronzeA = Save(bronzeAlbedo, "special_bronze", false, false, repeat, false); var bronzeN = Save(bronzeNormal, "special_bronze_normal", true, true, repeat, false);
            var bronzeM = Save(bronzeMask, "special_bronze_mask", true, false, repeat, false);

            asset.wood = Keep("M_SpecialWood", Lit(woodA, woodN, null, Color.white, 0f, .2f, new Vector2(.19f, 1f), Vector2.zero, default, 1.1f));
            asset.bronze = Keep("M_SpecialBronze", WithEmission(Lit(bronzeA, bronzeN, bronzeM, new Color(.74f, .68f, .62f), 1f, 1f, Vector2.one, Vector2.zero, default, 1.2f)));
            asset.iron = Keep("M_SpecialIron", WithEmission(Lit(ironA, ironN, ironM, Color.white, 1f, 1f, Vector2.one, Vector2.zero, default, 1.3f)));
            var rope = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            rope.SetTexture("_BaseMap", ropeTex); rope.SetColor("_BaseColor", Color.white); rope.SetTextureScale("_BaseMap", new Vector2(6f, 1f)); rope.SetFloat("_Cull", 0f);
            asset.rope = Keep("M_SpecialRope", rope);
            asset.rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environments/ScoutPass/Prefabs/LowBoulder.prefab");

            AssetDatabase.DeleteAsset(GameAsset); AssetDatabase.CreateAsset(asset, GameAsset);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            if (!asset.Complete) throw new Exception("EnemySpecialAssets is incomplete after the build");
            File.WriteAllText(report + "/shots_done.txt", "PASS\n" + GameAsset);
            Debug.Log("[EnemySpecialPreview] game assets built: " + GameAsset);
        }
        catch (Exception e)
        {
            File.WriteAllText(report + "/shots_done.txt", "FAIL " + e);
            Debug.LogError("[EnemySpecialPreview] game asset build failed: " + e);
            ExitSoon(1);
            return;
        }
        ExitSoon(0);
    }

    /// <summary>실행 메서드 안에서 바로 끝내면 유니티가 종료 도중 멈춘 적이 있어, 한 박자 뒤에 끝낸다.</summary>
    static void ExitSoon(int code) { if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(code); }
}
