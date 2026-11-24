// Test-only deterministic callback harness. Not an SDK, never imported into Assets,
// never shipped, and never evidence that the real Photon SDK or Cloud was validated.
#if REDBOOL_PHOTON_CONTRACT_TEST
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static T FindAnyObjectByType<T>() where T : class => null; public static void Destroy(object o) {} public static void DontDestroyOnLoad(object o) {} }
    public class MonoBehaviour : Object { public GameObject gameObject; }
    public class GameObject { public GameObject(string s) {} public T AddComponent<T>() where T : new() => new T(); }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int n) {} }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string s) where T : class => null; }
    public static class JsonUtility { public static T FromJson<T>(string s) => default(T); }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace Photon.Client
{
    public class PhotonHashtable : Dictionary<object, object> { public new object this[object key] { get => TryGetValue(key, out var v) ? v : null; set => base[key] = value; } }
    public enum ConnectionProtocol { Udp }
    public enum DeliveryMode { Reliable, UnreliableUnsequenced }
    public struct SendOptions { public DeliveryMode DeliveryMode; public bool Encrypt; public byte Channel; }
    public class EventData { public byte Code; public int Sender; public object CustomData; }
    public class PhotonStats { public long BytesIn,BytesOut; public float RoundtripTime = 20; }
    public class PhotonPeer { public int QueuedOutgoingCommands, SentReliableCommandsCount, ReliableCommandsInFlight, QueuedIncomingCommands; public PhotonStats Stats=new PhotonStats(); }
}
namespace Photon.Realtime
{
    using Photon.Client;
    public interface IConnectionCallbacks {
        void OnConnected(); void OnConnectedToMaster(); void OnDisconnected(DisconnectCause cause);
        void OnRegionListReceived(RegionHandler handler); void OnCustomAuthenticationResponse(Dictionary<string, object> data); void OnCustomAuthenticationFailed(string message);
    }
    public interface IMatchmakingCallbacks {
        void OnCreatedRoom(); void OnJoinedRoom(); void OnLeftRoom(); void OnCreateRoomFailed(short code, string message);
        void OnJoinRoomFailed(short code, string message); void OnJoinRandomFailed(short code, string message); void OnFriendListUpdate(List<FriendInfo> friends);
    }
    public interface IInRoomCallbacks { void OnPlayerEnteredRoom(Player p); void OnPlayerLeftRoom(Player p); void OnMasterClientSwitched(Player p); void OnRoomPropertiesUpdate(PhotonHashtable p); void OnPlayerPropertiesUpdate(Player p, PhotonHashtable h); }
    public interface IOnEventCallback { void OnEvent(EventData e); }
    public enum DisconnectCause { DisconnectByClientLogic }
    public class RegionHandler {} public class FriendInfo {}
    public class AuthenticationValues { public string UserId { get; private set; } public AuthenticationValues(string id) { UserId = id; } }
    public enum AuthModeOption { AuthOnceWss }
    public class AppSettings { public string AppIdRealtime, AppVersion, FixedRegion; public bool UseNameServer, EnableProtocolFallback; public ConnectionProtocol Protocol; public AuthModeOption AuthMode; }
    public class Player { public int ActorNumber; public bool IsInactive; }
    public class RoomOptions { public bool IsVisible, IsOpen, CleanupCacheOnLeave, BroadcastPropsChangeToAll; public int MaxPlayers, PlayerTtl, EmptyRoomTtl; public PhotonHashtable CustomRoomProperties; }
    public class EnterRoomArgs { public string RoomName; public RoomOptions RoomOptions; }
    public enum EventCaching { DoNotCache }
    public struct RaiseEventArgs { public int[] TargetActors; public EventCaching CachingOption; }
    public static class ErrorCode { public const short GameIdAlreadyExists = 32766, GameFull = 32765; }
    public class Room {
        public RealtimeClient Owner; public string Name; public int MaxPlayers = 2, MasterClientId = 1; public bool IsVisible, IsOpen;
        public Dictionary<int, Player> Players = new Dictionary<int, Player>(); public PhotonHashtable CustomProperties;
        public bool SetCustomProperties(PhotonHashtable changed) {
            foreach (var p in changed) CustomProperties[p.Key] = p.Value;
            Owner.Callbacks.Enqueue(() => (Owner.Target as IInRoomCallbacks)?.OnRoomPropertiesUpdate(changed)); return true;
        }
    }
    public class RealtimeClient {
        public static RealtimeClient Last; public object Target; public AuthenticationValues AuthValues;
        public PhotonPeer RealtimePeer = new PhotonPeer(); public Player LocalPlayer = new Player { ActorNumber = 1 }; public Room CurrentRoom;
        public Queue<Action> Callbacks = new Queue<Action>(); public AppSettings LastSettings; public EnterRoomArgs LastCreate;
        public SendOptions LastSend; public int[] LastTargets; public bool JoinOnly; public int RaiseCount;
        public RealtimeClient() { Last = this; }
        public void AddCallbackTarget(object o) { Target = o; } public void RemoveCallbackTarget(object o) { Target = null; }
        public bool ConnectUsingSettings(AppSettings s) { LastSettings = s; Callbacks.Enqueue(() => (Target as IConnectionCallbacks)?.OnConnectedToMaster()); return true; }
        public bool DispatchIncomingCommands() { if (Callbacks.Count == 0) return false; Callbacks.Dequeue()(); return Callbacks.Count > 0; }
        public bool SendOutgoingCommands() => false;
        public void Disconnect() { Callbacks.Enqueue(() => (Target as IConnectionCallbacks)?.OnDisconnected(DisconnectCause.DisconnectByClientLogic)); }
        public bool OpCreateRoom(EnterRoomArgs args) {
            LastCreate = args; CurrentRoom = new Room { Owner = this, Name = args.RoomName, CustomProperties = args.RoomOptions.CustomRoomProperties };
            CurrentRoom.Players[1] = LocalPlayer; Callbacks.Enqueue(() => (Target as IMatchmakingCallbacks)?.OnJoinedRoom()); return true;
        }
        public bool OpJoinRoom(EnterRoomArgs args) {
            JoinOnly = true; LocalPlayer.ActorNumber = 2;
            CurrentRoom = new Room { Owner = this, Name = args.RoomName, CustomProperties = new PhotonHashtable {
                { "authoritativeHost", 1 }, { "roomEpoch", RedboolPhotonWire.EpochText(RedboolPhotonWire.NewEpoch()) }, { "protocol", RedboolPhotonSession.ProtocolVersion }
            } };
            CurrentRoom.Players[1] = new Player { ActorNumber = 1 }; CurrentRoom.Players[2] = LocalPlayer;
            Callbacks.Enqueue(() => (Target as IMatchmakingCallbacks)?.OnJoinedRoom()); return true;
        }
        public bool OpRaiseEvent(byte code, object payload, RaiseEventArgs args, SendOptions options) { RaiseCount++; LastSend = options; LastTargets = args.TargetActors; return true; }
    }
}
#endif
