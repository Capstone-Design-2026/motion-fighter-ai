using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
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
        const string valid="{\"service\":\"REDBOOL\",\"view_version\":29,\"view_port\":5067,\"p2_port\":5066}";
        Check(Reply(valid,peer,out var address)&&address=="192.168.20.3","Compatible reply fills IPv4 only");
        foreach(var invalid in new[]{valid.Replace("REDBOOL","OTHER"),valid.Replace(":29",":28"),valid.Replace(":5067",":9999"),valid.Replace(":5066",":5065"),"{}","not json","",null,new string('x',513)})
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
            foreach(var name in new[]{"NewGameButton","ContinueButton","DuelTestButton","CreditsButton","QuitButton"})
                Check(canvas.Find(name)!=null,"Preserved "+name);
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
                Check(entries.Contains("Unity.log")&&entries.Contains("READ_ME.txt"),"Diagnostic export includes Unity log and scope");
                Check(!entries.Any(n=>n.EndsWith("video.mp4")||n.EndsWith("frames.jsonl")||n.EndsWith("secret.txt")),"Diagnostic export excludes video, poses, unrelated files");
                Check(entries.Length==4,"Diagnostic fixture includes only allowed entries");
            }
            Check(File.ReadAllText(summary)=="{\"camera\":false}"&&File.ReadAllText(trace)=="{\"type\":\"synthetic\"}\n", "Export preserves source logs");
            Check(File.ReadAllText(Path.Combine(session,"video.mp4"))=="private-video", "Export preserves excluded source data");
        }
        finally {Directory.Delete(root,true);}
    }

}
