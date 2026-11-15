using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Real loopback bind ownership; no camera, scene or gameplay simulation.</summary>
public static class RedboolViewBindingSelfCheck
{
    static readonly List<string> checks=new List<string>();
    const BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
    static FieldInfo Field(string name)=>typeof(RedboolViewLink).GetField(name,Fields);
    static void Check(bool ok,string label) {checks.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
    static void Retry(RedboolViewLink link,double now)=>typeof(RedboolViewLink).GetMethod("TryOpenSocket",Fields).Invoke(link,new object[]{now});
    static UdpClient Reserve(int port)
    {
        var socket=new UdpClient(AddressFamily.InterNetwork);
        try {socket.ExclusiveAddressUse=true;socket.Client.Bind(new IPEndPoint(IPAddress.Any,port));return socket;}
        catch {socket.Close();throw;}
    }
    static void DestroyFixture(GameObject obj)
    {
        if(obj==null)return;
        // This edit-mode fixture primes private state without Unity's Play-mode
        // Awake/Start lifecycle. Unity need not dispatch OnDestroy for such a
        // component, so exercise its real cleanup explicitly before destroying it.
        // The packaged lifecycle check covers automatic destruction in Play mode.
        var link=obj.GetComponent<RedboolViewLink>();
        if(link!=null)typeof(RedboolViewLink).GetMethod("OnDestroy",Fields).Invoke(link,null);
        UnityEngine.Object.DestroyImmediate(obj);
    }
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Run the binding check outside Play mode.");
        checks.Clear();GameObject hostObject=null,guestObject=null;UdpClient occupied=null;
        try
        {
            // If another real game already owns 5067 this fails safely before
            // touching it. The fixture only closes sockets it creates itself.
            occupied=Reserve(RedboolViewProtocol.Port);
            hostObject=new GameObject("View binding test host");var host=hostObject.AddComponent<RedboolViewLink>();
            Field("initialized").SetValue(host,true);
            // Deliberately prime old readiness fields: a failed local bind must
            // never authorize a match even if stale fields looked ready.
            Field("peer").SetValue(host,new IPEndPoint(IPAddress.Loopback,49999));
            Field("lastHeartbeat").SetValue(host,Time.realtimeSinceStartupAsDouble);
            Field("ack").SetValue(host,1UL);Field("sequence").SetValue(host,1UL);
            Retry(host,10);
            Check(!host.Bound&&host.PortConflict&&!string.IsNullOrEmpty(host.BindError),"Occupied5067 reports a specific local port conflict");
            Check(!host.PeerReady&&!host.Fresh,"A failed local bind cannot authorize play from stale readiness fields");
            Check(host.ConnectionIssue.Contains("다른 게임")&&host.ConnectionIssue.Contains("자동"),"Host UI explains other-game ownership and automatic recovery");
            Check((UdpClient)Field("socket").GetValue(host)==null&&occupied.Client.IsBound,"Failed candidate is not retained and foreign owner stays open");
            double scheduled=(double)Field("nextBindAttempt").GetValue(host);
            for(int i=0;i<100;i++)Retry(host,10.5);
            Check((double)Field("nextBindAttempt").GetValue(host)==scheduled,"Repeated rendered frames do not retry before the one-second deadline");
            Retry(host,11);
            Check((double)Field("nextBindAttempt").GetValue(host)==12&&!host.Bound,"Persistent conflict schedules only the next bounded retry");
            occupied.Close();occupied=null;
            Retry(host,11.5);
            Check(!host.Bound,"Release does not bypass the scheduled retry cadence");
            Retry(host,12);
            Check(host.Bound&&host.BindError==null&&!host.PortConflict&&host.ConnectionIssue==null,"Releasing5067 automatically recovers and clears the error");
            Check(!host.PeerReady&&Field("peer").GetValue(host)==null&&(ulong)Field("ack").GetValue(host)==0,"Recovered host waits for a real new peer heartbeat, never reviving old readiness");
            Check(!((UdpClient)Field("socket").GetValue(host)).Client.Blocking,"Recovered channel stays nonblocking");
            Check(host.GetComponents<RedboolViewMirror>().Length==0,"Bind retries do not duplicate replica setup");
            // A guest binds an ephemeral local port; an absent remote host is
            // ordinary waiting, never a fabricated local port conflict.
            guestObject=new GameObject("View binding test guest");var guest=guestObject.AddComponent<RedboolViewLink>();
            Field("client").SetValue(guest,true);Field("initialized").SetValue(guest,true);
            var remote=new IPEndPoint(IPAddress.Loopback,RedboolViewProtocol.Port);Field("peer").SetValue(guest,remote);
            // A newly opened local socket must not expose a completed old lobby
            // frame/feedback or partial special animation from a former lease.
            var assembler=(RedboolViewAssembler)Field("assembler").GetValue(guest);
            var oldSession=Guid.NewGuid();
            var oldActor=new RedboolViewActor {character=11,specialSerial=255,specialFlags=7,feedback=(byte)RedboolCombatFeedbackCode.SpecialStarted};
            assembler.Push(new RedboolViewPacket {kind=1,session=oldSession,seq=44,time=1,world=new RedboolViewWorld {stage=1,cursor1=11}},1);
            assembler.Push(new RedboolViewPacket {kind=2,session=oldSession,seq=44,time=1,actor=oldActor},1);
            var oldFrame=assembler.Push(new RedboolViewPacket {kind=3,session=oldSession,seq=44,time=1,actor=oldActor},1);
            var history=(List<RedboolViewFrame>)Field("history").GetValue(guest);history.Add(oldFrame);
            Field("lastFrame").SetValue(guest,Time.realtimeSinceStartupAsDouble);Field("ack").SetValue(guest,44UL);
            Retry(guest,20);
            Check(guest.Bound&&!guest.PortConflict&&guest.BindError==null&&!guest.Fresh,"Guest without snapshots is waiting, not a bind failure");
            Check(remote.Equals(Field("peer").GetValue(guest)),"Guest retry preserves the selected remote host");
            Check(guest.Latest==null&&history.Count==0&&(ulong)Field("ack").GetValue(guest)==0,"Reopened guest channel clears stale lobby/actor snapshots and old ACKs");
            DestroyFixture(hostObject);hostObject=null;
            using(var released=Reserve(RedboolViewProtocol.Port))Check(released.Client.IsBound,"Production cleanup releases only this host's5067 socket for the next game");
            string output=Environment.GetEnvironmentVariable("REDBOOL_VIEW_BINDING_CHECK_OUTPUT");
            if(!string.IsNullOrEmpty(output))File.WriteAllLines(output,checks);
            Debug.Log("REDBOOL VIEW BINDING PASS "+checks.Count+"; protocol "+RedboolViewProtocol.Version+" unchanged; camera closed");
        }
        finally
        {
            occupied?.Close();
            DestroyFixture(hostObject);
            DestroyFixture(guestObject);
        }
    }
}
