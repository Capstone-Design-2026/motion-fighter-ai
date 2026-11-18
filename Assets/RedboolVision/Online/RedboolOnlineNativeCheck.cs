using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

// Explicit command-line diagnostics only. Never starts a camera backend or
// bypasses preparation/game rules; also usable against the exact release binary.
public sealed class RedboolOnlineNativeCheck : MonoBehaviour
{
    [Serializable] sealed class Snapshot
    {
        public string role, room_code, error, status, utc;
        public bool supported, in_room, peer_present, game_configured, fighting, paused, resolved;
        public int local_input_port, connection_epoch, round, photon_rtt_ms;
        public long clock_ns, photon_bytes_in, photon_bytes_out;
        public double realtime_s;
    }
    RedboolPhotonSession session;
    string output;
    double nextWrite;
    bool onlineRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(Application.isEditor)return;
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"--redbool-external-camera")<0)return;
        bool host=Array.IndexOf(args,"--redbool-online-host")>=0;
        int join=Array.IndexOf(args,"--redbool-online-join");
        string output=Environment.GetEnvironmentVariable("REDBOOL_ONLINE_CHECK_STATUS");
        // Require BOTH opt-ins even for host/join automation. An ordinary game
        // launch does not create a diagnostic object, room or status file.
        if(string.IsNullOrWhiteSpace(output))return;
        var go=new GameObject("REDBOOL explicit native online check");
        DontDestroyOnLoad(go);
        var check=go.AddComponent<RedboolOnlineNativeCheck>();
        check.output=output;check.onlineRequested=host||join>=0;
        Application.runInBackground=true;
        if(check.onlineRequested) {
            check.session=RedboolPhotonSession.Ensure();
            check.session.RoomEntered+=check.RoomEntered;
            if(host)check.session.CreateRoom();
            else check.session.JoinRoom(join+1<args.Length?args[join+1]:"");
        }
        check.Write();
    }
    void RoomEntered()
    {
        if(session==null || !session.InRoom)return;
        RedboolOnlineRoomStrip.Ensure();
        if(session.IsHost)RedboolPvp.HostOnline();else RedboolPvp.JoinOnline();
    }
    void Update()
    {
        if(Time.realtimeSinceStartupAsDouble<nextWrite)return;
        nextWrite=Time.realtimeSinceStartupAsDouble+.25;Write();
    }
    void Write()
    {
        if(string.IsNullOrEmpty(output))return;
        var game=RedboolPvp.Instance;
        long t=Stopwatch.GetTimestamp(), f=Stopwatch.Frequency;
        var data=new Snapshot {
            role=RedboolPvp.IsClient?"P2":"P1",supported=session!=null&&session.Supported,
            in_room=session!=null&&session.InRoom,peer_present=session!=null&&session.PeerPresent,
            room_code=session!=null?session.RoomCode:"",error=session!=null?session.Error:"",
            status=session!=null?session.Status:"LAN",game_configured=game!=null&&game.Configured,
            local_input_port=onlineRequested&&RedboolPvp.IsClient?RedboolOnlineInputBridge.LocalPort:RedboolPvp.IsClient?5066:5065,
            connection_epoch=session!=null?session.ConnectionEpoch:0,round=game!=null?game.Round:0,
            fighting=game!=null&&game.Fighting,paused=game==null||game.Paused,resolved=game!=null&&game.Resolved,
            photon_rtt_ms=session!=null?session.RttMs:-1,realtime_s=Time.realtimeSinceStartupAsDouble,
            photon_bytes_in=session!=null?session.TrafficBytesIn:0,photon_bytes_out=session!=null?session.TrafficBytesOut:0,
            clock_ns=(t/f)*1000000000L+(long)((t%f)*(1000000000.0/f)),utc=DateTime.UtcNow.ToString("O")
        };
        try {
            string folder=Path.GetDirectoryName(Path.GetFullPath(output));Directory.CreateDirectory(folder);
            string temp=output+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(data));
            if(File.Exists(output))File.Replace(temp,output,null);else File.Move(temp,output);
        }
        catch(Exception e) {UnityEngine.Debug.LogWarning("[REDBOOL ONLINE CHECK] Status write: "+e.GetType().Name);}
    }
    void OnDestroy() {if(session!=null)session.RoomEntered-=RoomEntered;}
}
