using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>First-package support export; never includes camera images or raw poses.</summary>
public sealed class RedboolPackageDiagnostics : MonoBehaviour
{
    Task<string> export; string message="";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Application.isEditor || Application.isBatchMode || !RedboolCameraRuntime.Packaged)return;
        var go=new GameObject("REDBOOL Diagnostic Export");DontDestroyOnLoad(go);go.AddComponent<RedboolPackageDiagnostics>();
    }
    void OnGUI()
    {
        if(SceneManager.GetActiveScene().name!=SceneRoutes.Start)return;
        GUI.enabled=export==null;
        if(GUI.Button(new Rect(Screen.width-190,Screen.height-46,174,30),export==null?"진단 기록 저장":"기록 저장 중…"))
        {
            string root=Application.persistentDataPath,log=Application.consoleLogPath;
            export=Task.Run(()=>BuildExport(root,log));message="";
        }
        GUI.enabled=true;
        if(!string.IsNullOrEmpty(message))GUI.Label(new Rect(Screen.width-370,Screen.height-76,354,28),message);
    }
    void Update()
    {
        if(export==null || !export.IsCompleted)return;
        if(export.IsFaulted) {message="기록 저장에 실패했어요. 다시 시도해주세요.";Debug.LogWarning(export.Exception?.GetBaseException().Message);}
        else {message="진단 ZIP을 저장했어요. 열린 폴더에서 확인하세요.";Application.OpenURL(new Uri(Path.GetDirectoryName(export.Result)).AbsoluteUri);}
        export=null;
    }
    public static string BuildExport(string root,string gameLog)
    {
        string output=Path.Combine(root,"DiagnosticsExports");Directory.CreateDirectory(output);
        string zip=Path.Combine(output,"REDBOOL_diagnostics_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".zip");
        string camera=Path.Combine(root,"CameraDiagnostics");
        string[] allowed={"trace.jsonl","summary.json","manifest.json","annotations.jsonl","personal_profile.json","backend.log"};
        using(var stream=new FileStream(zip,FileMode.CreateNew,FileAccess.Write))
        using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))
        {
            if(Directory.Exists(camera))
            {
                foreach(string session in Directory.GetDirectories(camera).OrderByDescending(p=>p,StringComparer.Ordinal).Take(3))
                {
                    foreach(string file in Directory.EnumerateFiles(session,"*",SearchOption.AllDirectories))
                    {
                        if(!allowed.Contains(Path.GetFileName(file)) || (File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                        string name="camera/"+file.Substring(camera.Length+1).Replace('\\','/');
                        AddSnapshot(archive,file,name,64*1024*1024);
                    }
                }
            }
            if(!string.IsNullOrEmpty(gameLog)&&File.Exists(gameLog))AddSnapshot(archive,gameLog,"Unity.log",8*1024*1024);
            using(var writer=new StreamWriter(archive.CreateEntry("READ_ME.txt").Open(),Encoding.UTF8))
                writer.Write("Latest 3 integrated-camera sessions; no RGB/video/raw frame poses/models.\nFiles over the size limit contain their tail and a truncation note.\nSource files unchanged. This is diagnostic evidence, not full motion ground truth.\n");
        }
        return zip;
    }
    static void AddSnapshot(ZipArchive archive,string path,string name,long limit)
    {
        using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        {
            long length=source.Length,count=Math.Min(length,limit);
            if(length>limit)
            {
                using(var note=new StreamWriter(archive.CreateEntry(name+".truncated.txt").Open(),Encoding.UTF8))
                    note.Write("Tail only: original bytes="+length+", included maximum="+limit+". First JSONL line may be partial.\n");
                source.Seek(length-count,SeekOrigin.Begin);
            }
            using(var target=archive.CreateEntry(name,System.IO.Compression.CompressionLevel.Optimal).Open())
            {
                byte[] buffer=new byte[65536];
                while(count>0) {int n=source.Read(buffer,0,(int)Math.Min(buffer.Length,count));if(n==0)break;target.Write(buffer,0,n);count-=n;}
            }
        }
    }
}
