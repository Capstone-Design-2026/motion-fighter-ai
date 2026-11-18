using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Edit-mode native comparison; never enters Play or opens a camera.</summary>
public static class RedboolPreparationAllocationCheck
{
    const string Scout="Assets/Scenes/ScoutPass.unity";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [Serializable] sealed class Trial { public string scene,variant; public int iterations,roots,fighters; public int allocation_count; public double elapsed_ms; }
    [Serializable] sealed class Report { public bool passed; public string error,unity,scene_path; public string allocation_measurement="Native GC.Alloc event count; sample.Value is not interpreted as bytes"; public bool camera_opened,play_mode_entered; public List<string> checks; public List<Trial> trials; }
    static readonly List<string> checks=new List<string>();
    static readonly List<Trial> trials=new List<Trial>();
    static void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
    static RedboolBattlePreparation Component(Scene scene)
    {
        var go=new GameObject("Allocation fixture preparation");
        SceneManager.MoveGameObjectToScene(go,scene);go.SetActive(false);
        return go.AddComponent<RedboolBattlePreparation>();
    }
    // Exact old selection algorithm retained only in this fixture.
    static void Legacy(Scene scene,List<Fighter> result)
    {
        result.Clear();
        foreach(var root in scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<Fighter>());
    }
    static Action Refresh(RedboolBattlePreparation preparation)
    {return (Action)Delegate.CreateDelegate(typeof(Action),preparation,typeof(RedboolBattlePreparation).GetMethod("RefreshFighters",Private));}
    static List<Fighter> Result(RedboolBattlePreparation preparation)
    {return (List<Fighter>)typeof(RedboolBattlePreparation).GetField("fighters",Private).GetValue(preparation);}
    static List<Fighter> Compare(RedboolBattlePreparation preparation,string label)
    {
        var expected=new List<Fighter>();Legacy(preparation.gameObject.scene,expected);Refresh(preparation)();
        var actual=Result(preparation);Check(actual.Count==expected.Count,label+" count");
        for(int i=0;i<actual.Count;i++)Check(ReferenceEquals(actual[i],expected[i]),label+" ordered identity "+i);
        Check(((List<GameObject>)typeof(RedboolBattlePreparation).GetField("refreshRoots",Private).GetValue(preparation)).Count==0,label+" releases root scratch references");
        Check(((List<Fighter>)typeof(RedboolBattlePreparation).GetField("refreshFighters",Private).GetValue(preparation)).Count==0,label+" releases component scratch references");
        return new List<Fighter>(actual);
    }
    static GameObject Root(Scene scene,string name)
    {var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
    static Fighter Child(GameObject parent,string name,bool active=true)
    {var go=new GameObject(name);go.transform.SetParent(parent.transform);go.SetActive(active);return go.AddComponent<Fighter>();}
    static Trial RecordAllocations(Action action,int count,int capacity)
    {
        var watch=new Stopwatch();
        // Unity's Boehm Mono reports zero for GC.GetAllocatedBytesForCurrentThread.
        // Native GC.Alloc records are collected synchronously, without a frame sum.
        using(var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",capacity,ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
        {
            if(!recorder.Valid)throw new Exception("Native GC.Alloc recorder unavailable");
            watch.Start();
            for(int i=0;i<count;i++)action();
            watch.Stop();recorder.Stop();
            if(recorder.Count>=capacity)throw new Exception("GC.Alloc recorder capacity exhausted");
            return new Trial{iterations=count,allocation_count=recorder.Count,elapsed_ms=watch.Elapsed.TotalMilliseconds};
        }
    }
    static void ValidateAllocationRecorder()
    {
        Action small=()=>GC.KeepAlive(new byte[4096]);
        Action large=()=>GC.KeepAlive(new byte[8192]);
        small();large(); // JIT and warm each call outside the measured interval.
        var a=RecordAllocations(small,4,128);var b=RecordAllocations(large,4,128);
        Check(a.allocation_count==4&&b.allocation_count==4,"Native recorder observes every control allocation");
        // GC.Alloc is a marker; sample.Value does not expose its byte metadata.
        // Compare allocation counts rather than incorrectly labeling timer values as bytes.
    }
    static Trial Measure(Scene scene,Action action,string variant,int count,int fighterCount)
    {
        var trial=RecordAllocations(action,count,count*(scene.rootCount+16)+1024);
        trial.scene=scene.name;trial.variant=variant;trial.roots=scene.rootCount;trial.fighters=fighterCount;
        return trial;
    }
    static void Benchmark(Scene scene,RedboolBattlePreparation preparation,int count)
    {
        var expected=new List<Fighter>();Action baseline=()=>Legacy(scene,expected);Action candidate=Refresh(preparation);
        for(int i=0;i<40;i++){baseline();candidate();}
        int fighterCount=Result(preparation).Count;
        // AB/BA order controls simple first-call/JIT effects. Timings are evidence,
        // not hard real-time acceptance limits or a claim to fix recorded stalls.
        var a1=Measure(scene,baseline,"legacy_A",count,fighterCount);var b1=Measure(scene,candidate,"reuse_B",count,fighterCount);
        var b2=Measure(scene,candidate,"reuse_B2",count,fighterCount);var a2=Measure(scene,baseline,"legacy_A2",count,fighterCount);
        trials.AddRange(new[]{a1,b1,b2,a2});
        Check(a1.allocation_count>0&&a2.allocation_count>0,scene.name+" legacy allocates traversal arrays");
        Check(b1.allocation_count<=a1.allocation_count/10&&b2.allocation_count<=a2.allocation_count/10,scene.name+" reused traversal reduces measured allocations at least90percent");
        Compare(preparation,scene.name+" after benchmark");
    }
    [Serializable] sealed class SceneDependencies { public string scene; public int dependencies; public List<string> lfs_pointers; }
    [Serializable] sealed class DependencyReport { public List<SceneDependencies> scenes; public int unique_lfs_pointers; }
    static void AuditDependencies(string output)
    {
        var rows=new List<SceneDependencies>();var all=new HashSet<string>();
        foreach(var item in EditorBuildSettings.scenes)
        {
            if(!item.enabled)continue;
            var dependencies=AssetDatabase.GetDependencies(item.path,true);var pointers=new List<string>();
            foreach(string path in dependencies)
            {
                if(!File.Exists(path)||new FileInfo(path).Length>512)continue;
                if(File.ReadAllText(path).StartsWith("version https://git-lfs.github.com/spec/v1\n"))
                {pointers.Add(path);all.Add(path);}
            }
            rows.Add(new SceneDependencies{scene=item.path,dependencies=dependencies.Length,lfs_pointers=pointers});
        }
        File.WriteAllText(Path.Combine(output,"enabled_scene_lfs_dependencies.json"),JsonUtility.ToJson(new DependencyReport{scenes=rows,unique_lfs_pointers=all.Count},true));
        Check(all.Count==0,"Enabled build scenes contain no unresolved LFS pointer dependencies");
    }
    public static void Run()
    {
        checks.Clear();trials.Clear();string error=null;
        string output=Environment.GetEnvironmentVariable("REDBOOL_PREPARATION_ALLOCATION_OUTPUT");
        if(string.IsNullOrEmpty(output))throw new Exception("Set REDBOOL_PREPARATION_ALLOCATION_OUTPUT");
        Directory.CreateDirectory(output);
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Check(Application.isBatchMode&&!Application.isPlaying,"Batch edit mode only; no gameplay or camera startup");
            Check(Application.unityVersion=="6000.4.5f1","Expected editor version");
            ValidateAllocationRecorder();
            AuditDependencies(output);
            var scout=EditorSceneManager.OpenScene(Scout,OpenSceneMode.Single);
            var prep=Component(scout);var original=Compare(prep,"Actual ScoutPass");
            Check(original.Count>=2,"Actual ScoutPass contains authored player and enemy fighters");
            Benchmark(scout,prep,200);
            var fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);prep=Component(fixture);
            for(int i=0;i<128;i++)Root(fixture,"EmptyRoot"+i);
            var r1=Root(fixture,"RootOne");var first=r1.AddComponent<Fighter>();
            var one=Child(r1,"Active child");var hidden=Child(r1,"Inactive child",false);
            var disabled=Child(r1,"Disabled component");disabled.enabled=false;
            var r2=Root(fixture,"Inactive root");r2.SetActive(false);var inactiveRoot=r2.AddComponent<Fighter>();
            Child(r2,"Hidden descendant");
            var initial=Compare(prep,"Constructed active/inactive hierarchy");
            Check(initial.Contains(first)&&initial.Contains(one)&&initial.Contains(disabled),"Active objects include disabled Fighter components");
            Check(!initial.Contains(hidden)&&initial.Contains(inactiveRoot),"Inactive descendants excluded while inactive root component follows original Unity semantics");
            hidden.gameObject.SetActive(true);Check(Compare(prep,"Enabled child").Contains(hidden),"Newly enabled child appears at next refresh");
            r2.SetActive(true);Compare(prep,"Enabled root subtree");
            var spawn=Child(r1,"Spawned fighter");Check(Compare(prep,"Spawn").Contains(spawn),"New fighter appears at next refresh");
            Object.DestroyImmediate(one.gameObject);Check(!Compare(prep,"Destroyed child").Contains(one),"Destroyed fighter is removed at next refresh");
            disabled.transform.SetParent(r2.transform);Compare(prep,"Reparent");
            r2.transform.SetAsFirstSibling();Compare(prep,"Root sibling order");
            SceneManager.MoveGameObjectToScene(r2,scout);Compare(prep,"Moved out of scene");
            Compare(Component(scout),"Moved into ScoutPass");
            Benchmark(fixture,prep,400);
            Check(!Application.isPlaying&&RedboolCameraRuntime.Instance==null,"Native checks never entered Play or created a camera runtime");
        }
        catch(Exception failure){error=failure.ToString();Debug.LogException(failure);}
        finally
        {
            // No scene/save/config writes; discard only this test's in-memory fixtures.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
        var report=new Report{passed=error==null,error=error,unity=Application.unityVersion,scene_path=Scout,camera_opened=false,play_mode_entered=false,checks=checks,trials=trials};
        File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(report,true));
        File.WriteAllText(Path.Combine(output,"checks.txt"),string.Join("\n",checks));
        Debug.Log("[PREPARATION ALLOCATION CHECK] "+(error==null?"PASS "+checks.Count:"FAIL"));
        EditorApplication.Exit(error==null?0:1);
    }
}
