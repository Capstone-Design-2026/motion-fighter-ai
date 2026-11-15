using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bounded support export with complete compressed pose chunks, never camera images.</summary>
public sealed class RedboolPackageDiagnostics : MonoBehaviour
{
    const long MaxIndexBytes=4*1024*1024,MaxChunkBytes=8*1024*1024,MaxRecognitionBytes=128*1024*1024;
    static readonly Regex ChunkName=new Regex(@"\Alandmarks-[0-9]{6,12}\.jsonl\.gz\z",RegexOptions.CultureInvariant);
    [Serializable] sealed class Chunk {public string file,sha256;public long bytes,first_seq,last_seq,rows,frames;}
    [Serializable] sealed class Health {public string state,error,active_part;public long queue_dropped_rows,budget_omitted_rows;}
    [Serializable] sealed class Index {public string schema,compression;public int version;public bool complete;public Health health;public Chunk[] chunks;}
    [Serializable] sealed class Omitted {public string file,reason;}
    [Serializable] sealed class RecognitionExport
    {
        public string index,source_state;public bool source_complete;
        public long included_bytes;public List<string> included_chunks=new List<string>();
        public List<Omitted> omitted=new List<Omitted>();
    }
    [Serializable] sealed class ExportReport
    {
        public string schema="redbool.diagnostic_export";public int version=2;
        public bool includes_video=false;
        public string scope="Latest 3 camera runs; full-precision observations where complete chunks are available. Coverage/gaps and omitted files are explicit. No RGB/video/models. Not human-labelled motion ground truth.";
        public List<RecognitionExport> recognition=new List<RecognitionExport>();
        public List<string> warnings=new List<string>();
    }
    Task<string> export; string message="";
    static RedboolPackageDiagnostics instance;
    static Task<string> activeExport;
    // Completion includes faults/cancellation; never leave the menu locked
    // waiting for this component's next Update or a success-only callback.
    public static bool IsExporting => activeExport!=null&&!activeExport.IsCompleted;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetExportState(){activeExport=null;instance=null;}
    public static string ExportMessage => instance!=null ? instance.message : "";
    public static bool CanExport => instance!=null && SceneManager.GetActiveScene().name==SceneRoutes.Start
        && !IsExporting && instance.export==null
        && !(RedboolCameraRuntime.Instance!=null && RedboolCameraRuntime.Instance.DiagnosticsPending);
    public static bool RequestExport()
    {
        if(!CanExport)return false;
        string root=Application.persistentDataPath,log=Application.consoleLogPath;
        instance.export=Task.Run(()=>BuildExport(root,log));activeExport=instance.export;instance.message="";
        return true;
    }
    Task<StorageUsage> storageScan;
    CancellationTokenSource storageCancellation;
    StorageUsage storageUsage;
    bool storagePanel;
    public sealed class StorageUsage
    {
        public long camera_bytes, export_bytes;
        public int files, skipped_links;
        public bool partial, cancelled;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Application.isEditor || Application.isBatchMode || !RedboolCameraRuntime.Packaged)return;
        if(instance!=null)return;
        var go=new GameObject("REDBOOL Diagnostic Export");DontDestroyOnLoad(go);instance=go.AddComponent<RedboolPackageDiagnostics>();
    }
    void OnGUI()
    {
        if(SceneManager.GetActiveScene().name!=SceneRoutes.Start)return;
        if(!RedboolDemoSettings.OperatorMode)return;
        if(RedboolSettingsPanel.IsOpen||RedboolNewGameConfirmation.IsOpen||RedboolPauseMenu.IsOpen)return;
        bool pending=RedboolCameraRuntime.Instance!=null&&RedboolCameraRuntime.Instance.DiagnosticsPending;
        GUI.enabled=export==null&&!IsExporting&&!pending;
        string label=pending?"기록 마무리 중…":export==null?"진단 기록 저장":"기록 저장 중…";
        if(GUI.Button(new Rect(Screen.width-190,Screen.height-46,174,30),label))
            RequestExport();
        GUI.enabled=true;
        if(GUI.Button(new Rect(Screen.width-380,Screen.height-46,174,30),"기록 관리"))
        {
            storagePanel=!storagePanel;
            if(storagePanel&&!pending)StartStorageScan();else CancelStorageScan();
        }
        if(storagePanel)
        {
            var area=new Rect(Mathf.Max(10,Screen.width-510),Screen.height-275,495,190);
            GUI.Box(area,"");
            string size=storageScan!=null?"기록 용량 확인 중…":storageUsage==null?"기록 마무리 후 용량을 확인할 수 있어요.":
                "카메라 기록 "+SizeText(storageUsage.camera_bytes)+"  ·  내보낸 ZIP "+SizeText(storageUsage.export_bytes)
                +(storageUsage.partial?" (일부만 확인)":"");
            GUI.Label(new Rect(area.x+16,area.y+12,465,28),size);
            GUI.Label(new Rect(area.x+16,area.y+44,465,48),"원본 영상은 저장하지 않습니다. 기록은 자동 삭제되지 않아요.\n필요한 ZIP을 보관한 뒤 폴더에서 이전 기록을 확인하세요.");
            GUI.enabled=!pending;
            if(GUI.Button(new Rect(area.x+16,area.y+110,220,34),"기록 폴더 열기"))OpenRecordingFolder();
            GUI.enabled=!pending&&storageScan==null;
            if(GUI.Button(new Rect(area.x+252,area.y+110,220,34),"용량 새로 확인"))StartStorageScan();
            GUI.enabled=true;
            GUI.Label(new Rect(area.x+16,area.y+150,465,26),"관절 기록은 카메라 실행마다 최대 128MiB입니다.");
        }
        if(!string.IsNullOrEmpty(message))GUI.Label(new Rect(Screen.width-370,Screen.height-76,354,28),message);
    }
    void Update()
    {
        bool atMenu=SceneManager.GetActiveScene().name==SceneRoutes.Start;
        if((!atMenu||!RedboolDemoSettings.OperatorMode)&&storagePanel){storagePanel=false;CancelStorageScan();}
        if(storageScan!=null&&storageScan.IsCompleted)
        {
            if(storageScan.IsFaulted){storageUsage=new StorageUsage {partial=true};message="기록 용량을 확인하지 못했어요. 폴더에서 확인해주세요.";}
            else if(!storageScan.IsCanceled&&!storageScan.Result.cancelled)storageUsage=storageScan.Result;
            storageScan=null;storageCancellation?.Dispose();storageCancellation=null;
        }
        if(export!=null&&export.IsCompleted)
        {
            if(export.IsFaulted) {message="기록 저장에 실패했어요. 다시 시도해주세요.";Debug.LogWarning(export.Exception?.GetBaseException().Message);}
            else if(export.IsCanceled)message="기록 저장이 취소됐어요. 다시 시도해주세요.";
            else {message="진단 ZIP을 저장했어요. 열린 폴더에서 확인하세요.";if(atMenu)Application.OpenURL(new Uri(Path.GetDirectoryName(export.Result)).AbsoluteUri);}
            if(ReferenceEquals(activeExport,export))activeExport=null;
            export=null;
        }
    }
    static string SizeText(long bytes)=>bytes>=1024L*1024*1024?(bytes/(1024d*1024*1024)).ToString("F2")+" GiB":(bytes/(1024d*1024)).ToString("F1")+" MiB";
    void StartStorageScan()
    {
        if(storageScan!=null||SceneManager.GetActiveScene().name!=SceneRoutes.Start)return;
        if(RedboolCameraRuntime.Instance!=null&&RedboolCameraRuntime.Instance.DiagnosticsPending)return;
        string root=Application.persistentDataPath;
        storageCancellation=new CancellationTokenSource();var token=storageCancellation.Token;
        storageScan=Task.Run(()=>MeasureStorage(root,token));
    }
    void CancelStorageScan(){storageCancellation?.Cancel();}
    void OnDestroy(){CancelStorageScan();if(activeExport!=null&&activeExport.IsCompleted)activeExport=null;if(instance==this)instance=null;}
    void OpenRecordingFolder()
    {
        try
        {
            string folder=Path.Combine(Application.persistentDataPath,"CameraDiagnostics");
            if(Directory.Exists(folder)&&IsLink(folder)){message="연결된 폴더는 열지 않습니다. 저장 위치를 직접 확인해주세요.";return;}
            Directory.CreateDirectory(folder);Application.OpenURL(new Uri(folder).AbsoluteUri);
        }
        catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){message="기록 폴더를 열지 못했어요.";Debug.LogWarning(e.Message);}
    }
    /// <summary>Metadata-only bounded walk. Never reads payloads or removes files.</summary>
    public static StorageUsage MeasureStorage(string root,CancellationToken cancellation=default(CancellationToken))
    {
        var usage=new StorageUsage();var watch=System.Diagnostics.Stopwatch.StartNew();
        foreach(string leaf in new[]{"CameraDiagnostics","DiagnosticsExports"})
        {
            var pending=new Stack<string>();pending.Push(Path.Combine(root,leaf));
            while(pending.Count>0)
            {
                if(cancellation.IsCancellationRequested){usage.cancelled=true;return usage;}
                if(usage.files>=50000||watch.Elapsed.TotalSeconds>=5){usage.partial=true;return usage;}
                string directory=pending.Pop();
                try
                {
                    if(!Directory.Exists(directory))continue;
                    if(IsLink(directory)){usage.skipped_links++;usage.partial=true;continue;}
                    foreach(string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if(cancellation.IsCancellationRequested){usage.cancelled=true;return usage;}
                        if(usage.files>=50000||watch.Elapsed.TotalSeconds>=5){usage.partial=true;return usage;}
                        var attributes=File.GetAttributes(path);
                        if((attributes&FileAttributes.ReparsePoint)!=0){usage.skipped_links++;usage.partial=true;continue;}
                        if((attributes&FileAttributes.Directory)!=0){pending.Push(path);continue;}
                        long bytes=new FileInfo(path).Length;
                        if(leaf=="CameraDiagnostics")usage.camera_bytes+=bytes;else usage.export_bytes+=bytes;
                        usage.files++;
                    }
                }
                catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){usage.partial=true;}
            }
        }
        return usage;
    }
    public static string BuildExport(string root,string gameLog)
    {
        string output=Path.Combine(root,"DiagnosticsExports");Directory.CreateDirectory(output);
        string zip=Path.Combine(output,"REDBOOL_diagnostics_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".zip");
        string camera=Path.Combine(root,"CameraDiagnostics");
        string[] allowed={"trace.jsonl","summary.json","manifest.json","annotations.jsonl","personal_profile.json","backend.log"};
        var report=new ExportReport();
        using(var stream=new FileStream(zip,FileMode.CreateNew,FileAccess.Write))
        using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))
        {
            if(Directory.Exists(camera)&&IsLink(camera))report.warnings.Add("CameraDiagnostics: linked root excluded");
            else if(Directory.Exists(camera))
            {
                foreach(string session in Directory.GetDirectories(camera).Where(p=>!IsLink(p)).OrderByDescending(p=>p,StringComparer.Ordinal).Take(3))
                {
                    long recognitionBytes=0;
                    foreach(string file in PlainFiles(session))
                    {
                        string name="camera/"+file.Substring(camera.Length+1).Replace('\\','/');
                        if(Path.GetFileName(file)=="landmarks.index.json"&&Path.GetFileName(Path.GetDirectoryName(file))=="recognition")
                            AddRecognition(archive,file,name,report,ref recognitionBytes);
                        else if(allowed.Contains(Path.GetFileName(file)))
                        {
                            try {AddSnapshot(archive,file,name,64*1024*1024);}
                            catch(IOException e) {report.warnings.Add(name+": "+e.GetType().Name);}
                            catch(UnauthorizedAccessException e) {report.warnings.Add(name+": "+e.GetType().Name);}
                        }
                    }
                }
            }
            if(!string.IsNullOrEmpty(gameLog)&&File.Exists(gameLog))
            {
                if(IsLink(gameLog))report.warnings.Add("Unity.log: linked file excluded");
                else AddSnapshot(archive,gameLog,"Unity.log",8*1024*1024);
            }
            using(var writer=new StreamWriter(archive.CreateEntry("READ_ME.txt").Open(),Encoding.UTF8))
                writer.Write("Latest 3 integrated-camera runs; no RGB/video/models.\nrecognition/ contains closed, indexed, hash-verified full-rate landmark chunks when available. Read landmarks.index.json and export_report.json for gaps, omissions and completeness before replay.\nCompressed chunks are copied whole, never tail-truncated. Older text logs over the size limit contain their tail and a truncation note.\nSource files unchanged. Coordinates are model estimates, not human-labelled intended actions.\n");
            AddBytes(archive,"export_report.json",Encoding.UTF8.GetBytes(JsonUtility.ToJson(report,true)),System.IO.Compression.CompressionLevel.Optimal);
        }
        return zip;
    }
    static bool IsLink(string path) => (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;
    static IEnumerable<string> PlainFiles(string root)
    {
        // Do not follow links inside diagnostic folders into unrelated user data.
        foreach(string file in Directory.EnumerateFiles(root))if(!IsLink(file))yield return file;
        foreach(string dir in Directory.EnumerateDirectories(root))
            if(!IsLink(dir))foreach(string file in PlainFiles(dir))yield return file;
    }
    static byte[] WholeFile(string path,long limit)
    {
        if(IsLink(path))throw new IOException("Symbolic links are not diagnostic payloads");
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        {
            long length=stream.Length;
            if(length>limit)throw new IOException("Whole-file size limit exceeded");
            byte[] bytes=new byte[(int)length];int offset=0;
            while(offset<bytes.Length) {int n=stream.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
            return bytes;
        }
    }
    static void AddBytes(ZipArchive archive,string name,byte[] bytes,System.IO.Compression.CompressionLevel level)
    {using(var target=archive.CreateEntry(name,level).Open())target.Write(bytes,0,bytes.Length);}
    static void AddRecognition(ZipArchive archive,string path,string name,ExportReport report,ref long sessionBytes)
    {
        var result=new RecognitionExport {index=name};report.recognition.Add(result);
        byte[] metadata;Index index;
        try
        {
            metadata=WholeFile(path,MaxIndexBytes);
            AddBytes(archive,name,metadata,System.IO.Compression.CompressionLevel.Optimal);
            index=JsonUtility.FromJson<Index>(Encoding.UTF8.GetString(metadata));
            if(index==null||index.schema!="redbool.landmarks"||index.version!=1||index.compression!="gzip")throw new InvalidDataException("Unknown recognition schema");
        }
        catch(Exception e) when(e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is ArgumentException)
        {result.omitted.Add(new Omitted {file="landmarks.index.json",reason="invalid_or_unavailable_index: "+e.GetType().Name});return;}
        result.source_complete=index.complete;result.source_state=index.health?.state;
        if(!string.IsNullOrEmpty(index.health?.active_part))
            result.omitted.Add(new Omitted {file=index.health.active_part,reason="active_unfinished_chunk"});
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var chunk in index.chunks??new Chunk[0])
        {
            string reason=null;
            if(chunk==null||string.IsNullOrEmpty(chunk.file)||!ChunkName.IsMatch(chunk.file))reason="invalid_chunk_name";
            else if(!names.Add(chunk.file))reason="duplicate_chunk";
            else if(chunk.bytes<=0||chunk.bytes>MaxChunkBytes)reason="invalid_chunk_size";
            else if(chunk.rows<=0||chunk.first_seq>chunk.last_seq)reason="invalid_chunk_coverage";
            else if(chunk.sha256==null||chunk.sha256.Length!=64||!chunk.sha256.All(Uri.IsHexDigit))reason="invalid_chunk_hash";
            else if(sessionBytes+chunk.bytes>MaxRecognitionBytes)reason="export_session_byte_limit";
            if(reason!=null) {result.omitted.Add(new Omitted {file=chunk?.file,reason=reason});continue;}
            try
            {
                byte[] bytes=WholeFile(Path.Combine(Path.GetDirectoryName(path),chunk.file),MaxChunkBytes);
                string actual;
                using(var sha=SHA256.Create())actual=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                if(bytes.LongLength!=chunk.bytes||actual!=chunk.sha256.ToLowerInvariant())throw new InvalidDataException("Chunk fingerprint mismatch");
                // Already compressed on the camera worker; avoid recompressing it.
                string entry=name.Substring(0,name.LastIndexOf('/')+1)+chunk.file;
                AddBytes(archive,entry,bytes,System.IO.Compression.CompressionLevel.NoCompression);
                result.included_chunks.Add(chunk.file);result.included_bytes+=bytes.LongLength;sessionBytes+=bytes.LongLength;
            }
            catch(Exception e) when(e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is ArgumentException)
            {result.omitted.Add(new Omitted {file=chunk.file,reason="missing_or_corrupt_chunk: "+e.GetType().Name});}
        }
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
