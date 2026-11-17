using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Native Unity menu/parser checks; never opens a camera or joins a room.</summary>
public static class RedboolMenuSelfCheck
{
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
    static bool Reply(string json,IPEndPoint from,out string address)
    {
        object[] args={json==null?null:Encoding.UTF8.GetBytes(json),from,null};
        bool result=(bool)typeof(StartSceneUI).GetMethod("TryReadRoom",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        address=(string)args[2];return result;
    }
    public static void Run()
    {
        checks.Clear();var peer=new IPEndPoint(IPAddress.Parse("192.168.20.3"),5067);
        string valid="{\"service\":\"REDBOOL\",\"view_version\":"+RedboolViewProtocol.Version+",\"view_port\":5067,\"p2_port\":5066}";
        Check(Reply(valid,peer,out var address)&&address=="192.168.20.3","Compatible reply fills IPv4 only");
        foreach(var invalid in new[]{valid.Replace("REDBOOL","OTHER"),valid.Replace("\"view_version\":"+RedboolViewProtocol.Version,"\"view_version\":"+(RedboolViewProtocol.Version-1)),valid.Replace(":5067",":9999"),valid.Replace(":5066",":5065"),"{}","not json","",null,new string('x',513)})
            Check(!Reply(invalid,peer,out _),"Reject incompatible/malformed/bounded reply");
        Check(!Reply(valid,new IPEndPoint(peer.Address,5066),out _),"Reject wrong source port");
        Check(!Reply(valid,new IPEndPoint(IPAddress.IPv6Loopback,5067),out _),"Reject IPv6 reply");
        Check(!Reply(valid,null,out _),"Reject absent source");
        GameObject root=null;
        try
        {
            root=new GameObject("MenuCheck");var menu=root.AddComponent<StartSceneUI>();
            typeof(StartSceneUI).GetMethod("Start",Private).Invoke(menu,null);
            var canvas=root.transform.Find("StartCanvas");var panel=canvas.Find("TwoScreenMenu");
            Check(panel!=null&&!panel.gameObject.activeSelf,"PvP lobby starts closed");
            Check(canvas.Find("PvpButton").GetComponentInChildren<Text>().text=="PvP 대전","Main menu PvP label");
            foreach(var name in new[]{"NewGameButton","ContinueButton","CreditsButton","QuitButton"})
                Check(canvas.Find(name)!=null,"Preserved "+name);
            // 2026-11-16: 시작 화면에서 빠른 대전 버튼·설명 줄과 버전 글자를 뺐다.
            foreach(var name in new[]{"DuelTestButton","QuickDuelHelp","ReleaseVersion"})
                Check(canvas.Find(name)==null,"Removed "+name);
            bool active=RedboolPvp.Active;
            canvas.Find("PvpButton").GetComponent<Button>().onClick.Invoke();
            Check(panel.gameObject.activeSelf&&RedboolPvp.Active==active,"Opening lobby does not start or pair game");
            Check(panel.Find("Host").GetComponentInChildren<Text>().text=="방 만들기","Host label");
            Check(panel.Find("Join").GetComponentInChildren<Text>().text=="대전 참가","Join label");
            Check(panel.Find("FindRoom").GetComponentInChildren<Text>().text=="방 찾기","Discovery label");
            var input=panel.Find("HostIP").GetComponent<InputField>();
            input.text="999.999.1.1";panel.Find("Join").GetComponent<Button>().onClick.Invoke();
            Check(RedboolPvp.Active==active&&panel.Find("LobbyMessage").GetComponent<Text>().text.Contains("IPv4"),"Invalid address stays in lobby");
            Check(input.onValidateInput("",0,'x')=='\0'&&input.onValidateInput("",0,'.')=='.',"IPv4 field filters non-address characters");
            panel.Find("CloseLobby").GetComponent<Button>().onClick.Invoke();
            Check(!panel.gameObject.activeSelf,"Close returns to main menu");
        }
        finally {if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        CheckDiagnosticExport();
        CheckRecognitionExport();
        CheckLinkedDiagnosticRoot();
        string pythonFixture=Environment.GetEnvironmentVariable("REDBOOL_PYTHON_JOURNAL_FIXTURE_ROOT");
        if(!string.IsNullOrEmpty(pythonFixture))
        {
            if(!Application.isBatchMode)throw new Exception("Python journal fixture requires batch verification");
            Check(Directory.Exists(Path.Combine(pythonFixture,"CameraDiagnostics")),"Real Python journal fixture is available");
            string exported=RedboolPackageDiagnostics.BuildExport(pythonFixture,null);
            File.WriteAllText(Path.Combine(pythonFixture,"export-path.txt"),exported);
            Check(File.Exists(exported),"Real Python journal exported for offline exact replay");
        }
        string path=Environment.GetEnvironmentVariable("REDBOOL_MENU_CHECK_OUTPUT");
        string text="PASS "+checks.Count+" native menu/parser checks\n"+string.Join("\n",checks);
        if(!string.IsNullOrEmpty(path))File.WriteAllText(path,text);
        Debug.Log(text);
    }
    static void CheckDiagnosticExport()
    {
        string root=Path.Combine(Path.GetTempPath(),"REDBOOL_menu_check_"+Guid.NewGuid().ToString("N"));
        string session=Path.Combine(root,"CameraDiagnostics","20261113_010000");
        Directory.CreateDirectory(session);
        string summary=Path.Combine(session,"summary.json"),trace=Path.Combine(session,"trace.jsonl");
        string log=Path.Combine(root,"Player.log");
        File.WriteAllText(summary,"{\"camera\":false}");File.WriteAllText(trace,"{\"type\":\"synthetic\"}\n");
        File.WriteAllText(Path.Combine(session,"video.mp4"),"private-video");
        File.WriteAllText(Path.Combine(session,"frames.jsonl"),"private-poses");
        File.WriteAllText(Path.Combine(session,"secret.txt"),"private-unrelated");File.WriteAllText(log,"synthetic Unity log");
        try
        {
            string zip=RedboolPackageDiagnostics.BuildExport(root,log);
            using(var archive=ZipFile.OpenRead(zip))
            {
                var entries=archive.Entries.Select(e=>e.FullName).ToArray();
                Check(entries.Contains("camera/20261113_010000/summary.json"),"Diagnostic export includes allowed summary");
                Check(entries.Contains("camera/20261113_010000/trace.jsonl"),"Diagnostic export includes allowed trace");
                Check(entries.Contains("Unity.log")&&entries.Contains("READ_ME.txt")&&entries.Contains("export_report.json"),"Diagnostic export includes Unity log, scope and export coverage");
                Check(!entries.Any(n=>n.EndsWith("video.mp4")||n.EndsWith("frames.jsonl")||n.EndsWith("secret.txt")),"Diagnostic export excludes video, legacy raw files and unrelated files");
                Check(entries.Length==5,"Diagnostic fixture includes only allowed entries");
            }
            Check(File.ReadAllText(summary)=="{\"camera\":false}"&&File.ReadAllText(trace)=="{\"type\":\"synthetic\"}\n", "Export preserves source logs");
            Check(File.ReadAllText(Path.Combine(session,"video.mp4"))=="private-video", "Export preserves excluded source data");
        }
        finally {Directory.Delete(root,true);}
    }

    static void CheckRecognitionExport()
    {
        string root=Path.Combine(Path.GetTempPath(),"REDBOOL_recognition_export_"+Guid.NewGuid().ToString("N"));
        string folder=Path.Combine(root,"CameraDiagnostics","20261114_010000","recognition");
        Directory.CreateDirectory(folder);
        string chunk=Path.Combine(folder,"landmarks-000001.jsonl.gz");
        const string payload="{\"kind\":\"header\",\"seq\":1}\n{\"kind\":\"process\",\"seq\":2,\"capture_ns\":12345}\n";
        using(var file=File.Create(chunk))using(var gzip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest))
        {byte[] data=Encoding.UTF8.GetBytes(payload);gzip.Write(data,0,data.Length);}
        byte[] expected=File.ReadAllBytes(chunk);string hash;
        using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(expected)).Replace("-","").ToLowerInvariant();
        string valid="{\"file\":\"landmarks-000001.jsonl.gz\",\"bytes\":"+expected.Length+",\"sha256\":\""+hash+"\",\"first_seq\":1,\"last_seq\":2,\"rows\":2,\"frames\":1}";
        string corrupt=valid.Replace("000001","000002");
        string missing=valid.Replace("000001","000003");
        string traversal=valid.Replace("landmarks-000001.jsonl.gz","../secret.txt");
        string oversize=valid.Replace("000001","000004").Replace("\"bytes\":"+expected.Length,"\"bytes\":8388609");
        string index="{\"schema\":\"redbool.landmarks\",\"version\":1,\"compression\":\"gzip\",\"complete\":false,\"health\":{\"state\":\"incomplete\",\"active_part\":\"landmarks-000005.jsonl.gz.part\"},\"chunks\":["+string.Join(",",new[]{valid,corrupt,missing,traversal,oversize,valid})+"]}";
        File.WriteAllText(Path.Combine(folder,"landmarks.index.json"),index);
        File.WriteAllText(Path.Combine(folder,"landmarks-000002.jsonl.gz"),"bad fingerprint");
        File.WriteAllText(Path.Combine(folder,"landmarks-000005.jsonl.gz.part"),"unfinished");
        File.WriteAllText(Path.Combine(folder,"landmarks-999999.jsonl.gz"),"unindexed");
        File.WriteAllText(Path.Combine(folder,"private.mp4"),"excluded video");
        try
        {
            string zip=RedboolPackageDiagnostics.BuildExport(root,null);
            using(var archive=ZipFile.OpenRead(zip))
            {
                string prefix="camera/20261114_010000/recognition/";
                Check(archive.GetEntry(prefix+"landmarks.index.json")!=null,"Recognition index is included without truncation");
                var entry=archive.GetEntry(prefix+"landmarks-000001.jsonl.gz");
                Check(entry!=null,"Closed indexed recognition chunk is included");
                using(var source=entry.Open())using(var memory=new MemoryStream())
                {source.CopyTo(memory);Check(memory.ToArray().SequenceEqual(expected),"Export preserves compressed bytes and checksum exactly");}
                using(var source=entry.Open())using(var gzip=new GZipStream(source,CompressionMode.Decompress))using(var reader=new StreamReader(gzip))
                    Check(reader.ReadToEnd()==payload,"Exported recognition chunk decompresses with full row continuity");
                var entries=archive.Entries.Select(e=>e.FullName).ToArray();
                Check(entries.Count(n=>n.EndsWith(".jsonl.gz"))==1,"Corrupt, missing, oversized, duplicate and unindexed chunks are excluded");
                Check(!entries.Any(n=>n.Contains("..")||n.EndsWith(".part")||n.EndsWith(".mp4")),"Export excludes traversal, active files and camera video");
                string report;using(var reader=new StreamReader(archive.GetEntry("export_report.json").Open()))report=reader.ReadToEnd();
                foreach(string reason in new[]{"active_unfinished_chunk","missing_or_corrupt_chunk","invalid_chunk_name","invalid_chunk_size","duplicate_chunk"})
                    Check(report.Contains(reason),"Export explicitly reports "+reason);
                Check(report.Contains("\"source_complete\": false"),"Incomplete source is never presented as full replay coverage");
            }
            Check(File.ReadAllBytes(chunk).SequenceEqual(expected)&&File.ReadAllText(Path.Combine(folder,"landmarks.index.json"))==index,"Recognition export leaves originals unchanged");
        }
        finally {Directory.Delete(root,true);}
    }

    static void CheckLinkedDiagnosticRoot()
    {
        // The Mac Editor check exercises a real link; production also rejects
        // Windows directory junctions via FileAttributes.ReparsePoint.
        if(Application.platform!=RuntimePlatform.OSXEditor)return;
        string root=Path.Combine(Path.GetTempPath(),"REDBOOL_link_check_"+Guid.NewGuid().ToString("N"));
        string outside=root+"_outside",link=Path.Combine(root,"CameraDiagnostics");
        Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(outside,"20261114_000000"));
        File.WriteAllText(Path.Combine(outside,"20261114_000000","trace.jsonl"),"unrelated data");
        try
        {
            using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName="/bin/ln",Arguments="-s \""+outside+"\" \""+link+"\"",UseShellExecute=false,CreateNoWindow=true}))
            {process.WaitForExit();Check(process.ExitCode==0,"Linked diagnostic fixture created");}
            using(var archive=ZipFile.OpenRead(RedboolPackageDiagnostics.BuildExport(root,null)))
            {
                Check(!archive.Entries.Any(e=>e.FullName.StartsWith("camera/")),"Linked diagnostic root cannot export unrelated folders");
                using(var reader=new StreamReader(archive.GetEntry("export_report.json").Open()))
                    Check(reader.ReadToEnd().Contains("linked root excluded"),"Linked root exclusion is reported");
            }
            Check(File.ReadAllText(Path.Combine(outside,"20261114_000000","trace.jsonl"))=="unrelated data","Linked target remains unchanged");
        }
        finally
        {
            if(Directory.Exists(link))Directory.Delete(link);
            Directory.Delete(root,true);Directory.Delete(outside,true);
        }
    }

}
