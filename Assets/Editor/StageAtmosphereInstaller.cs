using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Idempotent installation on existing stages; does not regenerate their dressing.</summary>
public static class StageAtmosphereInstaller
{
    public const string Root = "Assets/Environments/SelectiveAtmosphere";
    public const string Output = @"C:\Users\sbpar\Documents\VisionStriker_AllStageAtmosphere_20261021";
    public static readonly string[] Ids = { "scout", "river", "ridge", "old_ferry", "gate", "quarry", "east", "camp", "rescue", "entrance", "final" };
    public static readonly string[] Scenes = { "ScoutPass", "RiverAmbush", "WindRidge", "OldFerry", "SealedGate", "HiddenQuarry", "EasternRidge", "SearchCamp", "PrisonStockade", "BlackCloudEntrance", "SluiceFortress" };
    public static string ScenePath(int i) => "Assets/Scenes/" + (i == 0 ? "" : "StoryStages/") + Scenes[i] + ".unity";
    public static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
    public static void ApplyBatch()
    {
        try { ApplyAll(); File.WriteAllText(Output+"/installed.txt", "PASS"); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output+"/install_error.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [MenuItem("Tools/VisionStriker/Apply Selective Atmosphere to Eleven Stages")]
    public static void ApplyAll()
    {
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene first.");
        Directory.CreateDirectory(Output); Directory.CreateDirectory(Root+"/Materials"); Directory.CreateDirectory(Root+"/Meshes");
        AssetDatabase.Refresh(); InstallRenderers();
        var previousScene = EditorSceneManager.GetActiveScene().path;
        var counts = new List<string> { "stage,selected_renderers,stone,roof,timber,preserved_renderers" };
        var listing = new List<string> { "stage,object,category,original_material" };
        for (int i=0; i<Ids.Length; i++)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath(i));
            var env = scene.GetRootGameObjects().First(g => i==0 ? g.name.StartsWith("Scout Pass") : g.name=="Story Stage · "+Ids[i]);
            ApplyCurrent(Ids[i], env.transform);
            var settings = Camera.main.GetComponent<SelectiveSceneryAtmosphere>();
            var all = env.GetComponentsInChildren<Renderer>(true);
            if (all.Any(r=> (r.renderingLayerMask & SelectiveSceneryAtmosphere.ReservedLayers)!=0))
                throw new Exception("Reserved rendering layer already in use: "+Ids[i]);
            foreach (var s in settings.surfaces)
            {
                if (s.renderer is SkinnedMeshRenderer || s.renderer.GetComponentInParent<Fighter>()) throw new Exception("Fighter selected");
                listing.Add(Ids[i]+","+Hierarchy(s.renderer.transform).Replace(",",";")+","+s.kind+","+s.originalMaterials[0].name);
            }
            counts.Add(Ids[i]+","+settings.surfaces.Length+","+string.Join(",",Enumerable.Range(0,3).Select(k=>settings.surfaces.Count(s=>s.kind==k)))+","+(all.Length-settings.surfaces.Length));
            if (env.GetComponentsInChildren<Collider2D>(true).Length!=0) throw new Exception("Scenery must not add combat colliders");
            EditorSceneManager.SaveScene(scene);
        }
        File.WriteAllLines(Output+"/selection_counts.csv", counts); File.WriteAllLines(Output+"/targets.csv", listing);
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)) EditorSceneManager.OpenScene(previousScene);
    }
    public static void InstallRenderers()
    {
        foreach (var path in new[]{"Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset"})
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!data) throw new FileNotFoundException(path);
            var feature = data.rendererFeatures.OfType<SelectiveSceneryFeature>().FirstOrDefault();
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<SelectiveSceneryFeature>(); feature.name="Selective Scenery Atmosphere";
                AssetDatabase.AddObjectToAsset(feature, data); data.rendererFeatures.Add(feature);
            }
            feature.maskShader = AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/SelectiveSceneryMask.shader");
            feature.compositeShader = AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/SelectiveSceneryComposite.shader");
            if (!feature.maskShader || !feature.compositeShader || ShaderUtil.ShaderHasError(feature.maskShader) || ShaderUtil.ShaderHasError(feature.compositeShader))
                throw new Exception("Atmosphere shader missing/invalid: "+string.Join(" | ",new[]{feature.maskShader,feature.compositeShader}.Where(s=>s).SelectMany(ShaderUtil.GetShaderMessages).Select(m=>m.message+" line "+m.line)));
            feature.SetActive(true); feature.Create(); EditorUtility.SetDirty(feature); data.SetDirty(); EditorUtility.SetDirty(data);
        }
    }
    public static void ApplyCurrent(string id, Transform environment)
    {
        if (id=="entrance") EntranceFacadeLayout.Apply(environment);
        var camera = Camera.main;
        var settings = camera.GetComponent<SelectiveSceneryAtmosphere>();
        if (!settings) settings = camera.gameObject.AddComponent<SelectiveSceneryAtmosphere>();
        // Restore only this system's previous assignments before classifying again.
        foreach (var s in settings.surfaces) if(s.renderer && s.originalMaterials!=null) s.renderer.sharedMaterials=s.originalMaterials;
        var surfaces = new List<SelectiveSceneryAtmosphere.Surface>();
        foreach (var r in environment.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is SkinnedMeshRenderer) continue;
            var original = r.sharedMaterials;
            if (original.Length==0) continue;
            var kinds = original.Select(m=>Category(r,m)).ToArray();
            if (kinds.All(k=>k<0)) continue;
            // A renderer list treats a whole renderer. Mixed protected/treated submeshes must not leak.
            if (kinds.Any(k=>k!=kinds[0])) throw new Exception("Mixed architecture submeshes need an explicit split: "+Hierarchy(r.transform));
            surfaces.Add(new SelectiveSceneryAtmosphere.Surface { renderer=r, kind=kinds[0], originalMaterials=original });
            r.sharedMaterials = original.Select(Treated).ToArray();
        }
        settings.surfaces = surfaces.ToArray();
        settings.radiusAt1600=3.5f; settings.softening=.9f; settings.desaturation=.08f; settings.haze=.07f;
        bool night = new[]{"old_ferry","camp","rescue","entrance","final"}.Contains(id);
        settings.hazeColor=night?new Color(.38f,.42f,.47f):new Color(.52f,.55f,.53f);
        EditorUtility.SetDirty(settings);
    }
    static int Category(Renderer r, Material m)
    {
        if (!m) return -1;
        string p=Hierarchy(r.transform).ToLowerInvariant(), n=m.name.ToLowerInvariant();
        string[] keep={"gatedoors","bankoutcrop","militarytent","prisoncage","cargoboat","quarryhoist","sluicewinch","evergreenshrub","cliffrock","lowboulder","bentpine","pathstones","ground edging","fighting apron","arena360ground"};
        if (keep.Any(p.Contains)) return -1;
        bool architecture = new[]{"fortress wall","stockade","watchtower","joined stone gate","joined prison gate","corner pier","gatehall","bastionhall","gallery","twostoreykeep","gorgecornerhall","citadel foundations","monumental sluices","terrace rails","tiled gate pavilion","quay retaining","cut granite terraces","prison overhead","braced mine entrance","mountain lookout","convoy shelter","stored timber"}.Any(p.Contains);
        if (!architecture) return -1;
        if (m.shader.name.Contains("Story Stone")) return 0;
        if (n.Contains("weatheredroof") || n.Contains("lookoutthatch")) return 1;
        if (n.Contains("weatheredtimber") || n.Contains("aged red timber") || n.Contains("checkpointtimber")) return 2;
        return -1;
    }
    static Material Treated(Material source)
    {
        var guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
        if(string.IsNullOrEmpty(guid)) throw new Exception("Source material must be an asset: "+source.name);
        string path=Root+"/Materials/"+source.name+"_"+guid.Substring(0,8)+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(source); AssetDatabase.CreateAsset(m,path); }
        else EditorUtility.CopySerialized(source,m);
        m.name=source.name+" · local atmosphere";
        if(m.HasProperty("_Blocks"))m.SetFloat("_Blocks",source.GetFloat("_Blocks")*.65f);
        if(m.HasProperty("_BaseColor"))
        {
            var c=source.GetColor("_BaseColor");var muted=Color.Lerp(c,new Color(c.grayscale,c.grayscale,c.grayscale,c.a),.12f);
            m.SetColor("_BaseColor",new Color(muted.r*.97f,muted.g*.98f,muted.b*.96f,c.a));
        }
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",Mathf.Min(source.GetFloat("_Smoothness"),.17f));
        EditorUtility.SetDirty(m); return m;
    }
}
