using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
#if PHOTON_REALTIME_5_OR_NEWER
using Photon.Client;
using Photon.Realtime;
#endif

/// <summary>
/// Persistent two-player Photon Realtime 5 transport. Every public mutation and callback
/// runs on Unity's main thread. No credentials/SDK means unavailable, never offline success.
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class RedboolPhotonSession : MonoBehaviour
#if PHOTON_REALTIME_5_OR_NEWER
    , IConnectionCallbacks, IMatchmakingCallbacks, IInRoomCallbacks, IOnEventCallback
#endif
{
    public const string ProtocolVersion = "redbool-online-v1";
    public static RedboolPhotonSession Instance { get; private set; }
    public static RedboolPhotonSession Ensure()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<RedboolPhotonSession>();
        return existing != null ? existing : new GameObject("Redbool Online Session").AddComponent<RedboolPhotonSession>();
    }

    public bool Supported {
        get {
#if PHOTON_REALTIME_5_OR_NEWER
            return true;
#else
            return false;
#endif
        }
    }
    public bool InRoom { get; private set; }
    public bool IsHost { get; private set; }
    public bool PeerPresent { get; private set; }
    public string RoomCode { get; private set; } = "";
    public string Status { get; private set; } = "인터넷 대전 준비";
    public string Error { get; private set; } = "";
    /// <summary>RTT to the Photon server, NOT end-to-end peer or recognition latency.</summary>
    public int RttMs { get; private set; } = -1;
    public int ConnectionEpoch { get; private set; }
    public int RejectedOutgoingMessages { get; private set; }
    // SDK payload counters exclude transport headers; useful for diagnostics,
    // not a replacement for the Photon Dashboard's billable traffic total.
    public long TrafficBytesIn {
        get {
#if PHOTON_REALTIME_5_OR_NEWER
            return client!=null?(long)client.RealtimePeer.Stats.BytesIn:0;
#else
            return 0;
#endif
        }
    }
    public long TrafficBytesOut {
        get {
#if PHOTON_REALTIME_5_OR_NEWER
            return client!=null?(long)client.RealtimePeer.Stats.BytesOut:0;
#else
            return 0;
#endif
        }
    }
    public event Action<byte, byte[]> Message;
    public event Action RoomEntered;
    public event Action RoomLeft;

    [Serializable] sealed class LocalConfig { public string appId = ""; public string photonAppId = ""; }
    public static string AppId
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("REDBOOL_PHOTON_APP_ID");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            var asset = Resources.Load<TextAsset>("RedboolOnlineConfig");
            if (asset == null) return "";
            try
            {
                var config = JsonUtility.FromJson<LocalConfig>(asset.text);
                return config == null ? "" : ((!string.IsNullOrWhiteSpace(config.appId) ? config.appId : config.photonAppId) ?? "").Trim();
            }
            catch (Exception) { return ""; }
        }
    }

    int mainThread;
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        mainThread = Thread.CurrentThread.ManagedThreadId;
        DontDestroyOnLoad(gameObject);
        if (!Supported) Status = "Photon SDK 설치가 필요합니다";
    }

    void RequireMainThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThread)
            throw new InvalidOperationException("RedboolPhotonSession must be used on Unity's main thread.");
    }

    public void CreateRoom() { RequireMainThread(); Begin(true, ""); }
    public void JoinRoom(string code)
    {
        RequireMainThread();
        if (!RedboolPhotonWire.TryNormalizeRoomCode(code, out var normalized))
        { Error = "방 코드를 KR-XXXXXX 형식으로 입력해 주세요."; return; }
        Begin(false, normalized);
    }

    void Begin(bool create, string code)
    {
        Error = "";
#if PHOTON_REALTIME_5_OR_NEWER
        if (!Guid.TryParse(AppId, out var id) || id == Guid.Empty)
        { Error = "Photon Realtime App ID를 먼저 설정해 주세요."; Status = "연결 설정 필요"; return; }
        // Exactly one pending intent. Cancel invalidates any earlier join callbacks immediately.
        Leave();
        requested = true; requestedCreate = create; requestedCode = code;
        Status = create ? "서울 서버에 방 만드는 중…" : "서울 서버의 방에 참가하는 중…";
#else
        Error = "Photon Realtime 5 SDK가 없어 인터넷 연결을 시작할 수 없습니다.";
        Status = "Photon SDK 설치가 필요합니다";
#endif
    }

    public void Leave()
    {
        RequireMainThread();
#if PHOTON_REALTIME_5_OR_NEWER
        requested = false;
        if (client != null)
        {
            if (phase == Phase.Joining && !InRoom)
            { cancelledJoinCode = attemptCode; cancelledJoinUntil = Time.realtimeSinceStartup + 25f; }
            phase = Phase.Disconnecting;
            client.Disconnect();
            Flush();
        }
        else phase = Phase.Idle;
#endif
        ClearRoom();
        Status = Supported ? "인터넷 대전 준비" : "Photon SDK 설치가 필요합니다";
    }

    void ClearRoom()
    {
        bool wasInRoom = InRoom;
        InRoom = false; IsHost = false; PeerPresent = false; RoomCode = ""; RttMs = -1;
        unchecked { ConnectionEpoch++; }
#if PHOTON_REALTIME_5_OR_NEWER
        roomEpoch = null; hostActor = 0; peerActor = 0; receiver.Clear();
        Array.Clear(sendSequence, 0, sendSequence.Length);
#endif
        if (wasInRoom) RoomLeft?.Invoke();
    }

    public void Send(byte channel, byte[] payload, bool reliable = false)
    {
        RequireMainThread();
        if (payload == null || payload.Length == 0 || payload.Length > RedboolPhotonWire.MaxPayloadBytes)
        {
            RejectedOutgoingMessages++;
            Error = "전송할 메시지 크기가 허용 범위(1~8192바이트)를 벗어났습니다.";
            return;
        }
#if PHOTON_REALTIME_5_OR_NEWER
        if (!InRoom || !PeerPresent || phase != Phase.Room || client == null) return;
        try
        {
            // Do not build a stale application queue. Unreliable snapshots are disposable.
            // Reliable overflow terminates the match rather than silently losing an important event.
            if (client.RealtimePeer.QueuedOutgoingCommands >= MaxQueuedCommands ||
                client.RealtimePeer.ReliableCommandsInFlight >= MaxQueuedCommands)
            { RejectedOutgoingMessages++; if (reliable) Fail("인터넷 송신이 지연되어 연결을 종료했습니다. 다시 참가해 주세요."); return; }
            uint sequence = ++sendSequence[channel];
            if (sequence == 0) sequence = ++sendSequence[channel];
            byte[] envelope = RedboolPhotonWire.Encode(channel, sequence, roomEpoch, payload);
            var args = new RaiseEventArgs { TargetActors = new[] { peerActor }, CachingOption = EventCaching.DoNotCache };
            var options = new SendOptions {
                DeliveryMode = reliable ? DeliveryMode.Reliable : DeliveryMode.UnreliableUnsequenced,
                Encrypt = true, Channel = (byte)(reliable ? 1 : 0)
            };
            if (!client.OpRaiseEvent(WireEvent, envelope, args, options))
            { RejectedOutgoingMessages++; if (reliable) Fail("중요한 경기 메시지를 전송하지 못했습니다. 다시 참가해 주세요."); return; }
            Flush(); // Send-after-input; no extra 30 Hz timer and no Unity calls on a worker thread.
        }
        catch (Exception ex) { RejectedOutgoingMessages++; Fail("인터넷 송신 처리 오류: " + ex.GetType().Name); }
#endif
    }

    void OnDestroy()
    {
        if (Instance != this) return;
#if PHOTON_REALTIME_5_OR_NEWER
        if (client != null) { client.RemoveCallbackTarget(this); client.Disconnect(); client.SendOutgoingCommands(); }
#endif
        Instance = null;
    }

#if PHOTON_REALTIME_5_OR_NEWER
    enum Phase { Idle, Connecting, Creating, Joining, JoinRetryWait, PublishingHost, Room, Disconnecting }
    const byte WireEvent = 42;
    const int MaxQueuedCommands = 64;
    const string EpochKey = "roomEpoch", HostKey = "authoritativeHost", ProtocolKey = "protocol";
    RealtimeClient client;
    Phase phase;
    bool requested, requestedCreate, attemptCreate;
    string requestedCode, attemptCode;
    byte[] roomEpoch;
    int hostActor, peerActor, createRetries;
    float attemptStarted, cancelledJoinUntil, nextJoinRetryAt;
    string cancelledJoinCode;
    int joinRetries;
    readonly uint[] sendSequence = new uint[256];
    readonly RedboolPhotonSequenceGate receiver = new RedboolPhotonSequenceGate();
    // Stable within this Session only, including cancel/retry. This identifies the
    // same anonymous participant; it is not authentication and is never persisted.
    readonly string anonymousUserId = Guid.NewGuid().ToString("N");

    void Update()
    {
        if (client != null)
        {
            var current = client;
            // Bound work to prevent a burst of remote messages from blocking the UI.
            try
            {
                if (phase != Phase.Disconnecting && current.RealtimePeer.QueuedIncomingCommands > 512)
                { Fail("수신 메시지가 누적되어 연결을 종료했습니다. 다시 참가해 주세요."); return; }
                for (int i = 0; i < 128 && client == current && current.DispatchIncomingCommands(); i++) { }
                Flush();
                if (client != null && InRoom) RttMs = Math.Max(0, (int)Math.Round((double)client.RealtimePeer.Stats.RoundtripTime));
            }
            catch (Exception ex) { Fail("인터넷 연결 처리 오류: " + ex.GetType().Name); }
        }
        if (client != null && phase == Phase.JoinRetryWait && Time.realtimeSinceStartup >= nextJoinRetryAt)
            RequestJoin();
        if (client == null && requested) StartPending();
        if (client != null && phase != Phase.Room && phase != Phase.Disconnecting &&
            Time.realtimeSinceStartup - attemptStarted > 25f)
            Fail("서버 연결 시간이 초과되었습니다. 인터넷 상태와 App ID를 확인해 주세요.");
    }

    void LateUpdate() { Flush(); }

    void StartPending()
    {
        requested = false;
        attemptCreate = requestedCreate;
        attemptCode = attemptCreate ? RedboolPhotonWire.NewRoomCode() : requestedCode;
        roomEpoch = attemptCreate ? RedboolPhotonWire.NewEpoch() : null;
        createRetries = 0; joinRetries = 0;
        attemptStarted = Time.realtimeSinceStartup;
        phase = Phase.Connecting;
        client = new RealtimeClient();
        client.AddCallbackTarget(this);
        // Anonymous device/session identity only; this is not account authentication.
        client.AuthValues = new AuthenticationValues(anonymousUserId);
        var settings = new AppSettings {
            AppIdRealtime = AppId, AppVersion = ProtocolVersion,
            FixedRegion = "kr", UseNameServer = true, Protocol = ConnectionProtocol.Udp,
            AuthMode = AuthModeOption.AuthOnceWss, EnableProtocolFallback = false
        };
        if (!client.ConnectUsingSettings(settings))
        {
            client.RemoveCallbackTarget(this); client.Disconnect(); client = null;
            phase = Phase.Idle;
            Error = "Photon 연결을 시작하지 못했습니다. App ID와 SDK 설정을 확인해 주세요.";
            Status = "연결 실패";
        }
    }

    void Flush()
    {
        var current = client;
        if (current == null) return;
        for (int i = 0; i < 3 && current.SendOutgoingCommands(); i++) { }
    }

    void Fail(string reason)
    {
        requested = false;
        Error = reason;
        phase = Phase.Disconnecting;
        ClearRoom();
        Status = "인터넷 연결 종료";
        if (client != null) client.Disconnect();
        else phase = Phase.Idle;
    }

    public void OnConnected() { }
    public void OnConnectedToMaster()
    {
        if (phase != Phase.Connecting) return;
        if (attemptCreate) RequestCreate();
        else RequestJoin();
    }

    void RequestJoin()
    {
        phase = Phase.Joining;
        if (!client.OpJoinRoom(new EnterRoomArgs { RoomName = attemptCode }))
            Fail("방 참가 요청을 보내지 못했습니다.");
    }

    void RequestCreate()
    {
        phase = Phase.Creating;
        var properties = new PhotonHashtable {
            { EpochKey, RedboolPhotonWire.EpochText(roomEpoch) }, { HostKey, 0 }, { ProtocolKey, ProtocolVersion }
        };
        var options = new RoomOptions {
            IsVisible = false, IsOpen = false, MaxPlayers = 2,
            PlayerTtl = 0, EmptyRoomTtl = 0, CleanupCacheOnLeave = true,
            BroadcastPropsChangeToAll = true, CustomRoomProperties = properties
        };
        if (!client.OpCreateRoom(new EnterRoomArgs { RoomName = attemptCode, RoomOptions = options }))
            Fail("방 만들기 요청을 보내지 못했습니다.");
    }

    public void OnCreatedRoom() { }
    public void OnJoinedRoom()
    {
        if (phase != Phase.Creating && phase != Phase.Joining)
        { if (client != null) client.Disconnect(); return; }
        if (client.CurrentRoom == null || client.CurrentRoom.Name != attemptCode)
        { Fail("요청한 방과 서버 응답이 다릅니다."); return; }
        if (attemptCreate)
        {
            phase = Phase.PublishingHost;
            hostActor = client.LocalPlayer.ActorNumber;
            if (!client.CurrentRoom.SetCustomProperties(new PhotonHashtable { { HostKey, hostActor } }))
                Fail("호스트 정보를 저장하지 못했습니다.");
        }
        else ValidateAndEnter();
    }

    bool ReadIdentity(out int authority, out byte[] epoch)
    {
        authority = 0; epoch = null;
        var room = client?.CurrentRoom;
        if (room == null || room.MaxPlayers != 2 || room.IsVisible || room.Name != attemptCode) return false;
        var props = room.CustomProperties;
        if (!(props[ProtocolKey] is string protocol) || protocol != ProtocolVersion ||
            !(props[HostKey] is int actor) || actor <= 0 ||
            !(props[EpochKey] is string text) || !RedboolPhotonWire.TryReadEpoch(text, out epoch)) return false;
        if (!room.Players.TryGetValue(actor, out var host) || host.IsInactive || room.MasterClientId != actor) return false;
        authority = actor;
        return true;
    }

    void ValidateAndEnter()
    {
        if (!ReadIdentity(out int authority, out var epoch))
        { Fail("방의 버전 또는 호스트 정보가 유효하지 않습니다. 새 방을 만들어 주세요."); return; }
        if (attemptCreate && (authority != hostActor || RedboolPhotonWire.EpochText(epoch) != RedboolPhotonWire.EpochText(roomEpoch)))
        { Fail("호스트 정보가 변경되어 연결을 종료했습니다."); return; }
        if (!attemptCreate && authority == client.LocalPlayer.ActorNumber)
        { Fail("기존 호스트가 없는 방에는 참가할 수 없습니다."); return; }
        hostActor = authority; roomEpoch = epoch;
        IsHost = client.LocalPlayer.ActorNumber == hostActor;
        InRoom = true; RoomCode = attemptCode; phase = Phase.Room;
        cancelledJoinCode = null; cancelledJoinUntil = 0;
        unchecked { ConnectionEpoch++; }
        RefreshPeer();
        Error = "";
        Status = PeerPresent ? "상대와 연결되었습니다" : "상대가 참가하기를 기다리는 중";
        if (IsHost) client.CurrentRoom.IsOpen = !PeerPresent;
        RoomEntered?.Invoke();
    }

    void RefreshPeer()
    {
        peerActor = 0;
        foreach (var pair in client.CurrentRoom.Players)
            if (pair.Key != client.LocalPlayer.ActorNumber && !pair.Value.IsInactive)
            { peerActor = pair.Key; break; }
        PeerPresent = peerActor > 0;
    }

    public void OnPlayerEnteredRoom(Player player)
    {
        if (!InRoom || phase != Phase.Room) return;
        if (client.CurrentRoom.Players.Count > 2) { Fail("방 정원 정보가 유효하지 않습니다."); return; }
        RefreshPeer();
        if (IsHost) client.CurrentRoom.IsOpen = false;
        Status = "상대와 연결되었습니다";
    }
    public void OnPlayerLeftRoom(Player player)
    {
        if (!InRoom) return;
        // No takeover, no stale match state, no second opponent in a continuing match.
        Fail(player.ActorNumber == hostActor ? "호스트의 연결이 종료되었습니다. 새 방에 참가해 주세요." :
            "상대의 연결이 종료되었습니다. 새 방을 만들어 주세요.");
    }
    public void OnMasterClientSwitched(Player player)
    {
        if (phase == Phase.Room || phase == Phase.PublishingHost)
            if (player == null || player.ActorNumber != hostActor) Fail("호스트가 떠나 대전이 종료되었습니다.");
    }
    public void OnRoomPropertiesUpdate(PhotonHashtable changed)
    {
        if (phase == Phase.PublishingHost) { ValidateAndEnter(); return; }
        if (!InRoom) return;
        if (!ReadIdentity(out int authority, out var epoch) || authority != hostActor ||
            RedboolPhotonWire.EpochText(epoch) != RedboolPhotonWire.EpochText(roomEpoch))
            Fail("방 정보가 변경되어 안전하게 대전을 종료했습니다.");
    }
    public void OnPlayerPropertiesUpdate(Player player, PhotonHashtable changed) { }

    public void OnEvent(EventData ev)
    {
        if (ev.Code != WireEvent || !InRoom || !PeerPresent || phase != Phase.Room ||
            ev.Sender != peerActor || ev.Sender == client.LocalPlayer.ActorNumber) return;
        if (!client.CurrentRoom.Players.TryGetValue(ev.Sender, out var sender) || sender.IsInactive) return;
        if (!RedboolPhotonWire.TryDecode(ev.CustomData as byte[], roomEpoch, out byte channel, out uint sequence, out var payload) ||
            !receiver.Accept(channel, sequence)) return;
        Message?.Invoke(channel, payload);
    }

    public void OnCreateRoomFailed(short code, string message)
    {
        if (phase != Phase.Creating) return;
        if (code == ErrorCode.GameIdAlreadyExists && createRetries++ < 3)
        { attemptCode = RedboolPhotonWire.NewRoomCode(); RequestCreate(); return; }
        Fail("방을 만들지 못했습니다. (Photon " + code + ")");
    }
    public void OnJoinRoomFailed(short code, string message)
    {
        if (phase != Phase.Joining) return;
        // A cancelled Master->Game hop may temporarily count against room capacity.
        // Only an explicit retry of that same recently cancelled room gets this bounded
        // grace period. Normal full/closed/missing rooms still fail immediately.
        if (code == ErrorCode.GameFull && attemptCode == cancelledJoinCode &&
            Time.realtimeSinceStartup < cancelledJoinUntil && joinRetries++ < 23)
        {
            phase = Phase.JoinRetryWait;
            nextJoinRetryAt = Time.realtimeSinceStartup + 1f;
            Status = "취소한 연결을 정리한 뒤 다시 참가하는 중…";
            return;
        }
        Fail("방이 없거나 닫혔거나 이미 가득 찼습니다. 코드를 확인해 주세요. (Photon " + code + ")");
    }
    public void OnJoinRandomFailed(short code, string message) { Fail("지원하지 않는 임의 참가 응답입니다."); }
    public void OnFriendListUpdate(List<FriendInfo> friends) { }
    public void OnLeftRoom()
    {
        if (phase != Phase.Disconnecting) Fail("방 연결이 종료되었습니다.");
    }
    public void OnDisconnected(DisconnectCause cause)
    {
        bool expected = phase == Phase.Disconnecting;
        if (client != null) client.RemoveCallbackTarget(this);
        client = null; phase = Phase.Idle;
        ClearRoom();
        if (!expected)
        { requested = false; Error = "Photon 연결이 끊겼습니다: " + cause; Status = "연결 실패"; }
    }
    public void OnRegionListReceived(RegionHandler handler) { }
    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
    public void OnCustomAuthenticationFailed(string message) { Fail("Photon 인증에 실패했습니다. 대시보드의 인증 설정을 확인해 주세요."); }
#endif
}
