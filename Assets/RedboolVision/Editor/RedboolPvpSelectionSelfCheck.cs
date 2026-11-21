using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Actual host/client menu callbacks and codec, with unbound sockets and
/// no remote peer on the client: never opens a port, game or physical camera.</summary>
public static class RedboolPvpSelectionSelfCheck
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    static readonly List<string> checks=new List<string>();
    static string Output=>Environment.GetEnvironmentVariable("REDBOOL_SELECTION_CHECK_OUTPUT");
    static void Check(bool ok,string why) {checks.Add((ok?"PASS ":"FAIL ")+why);if(!ok)throw new Exception(why);}
    static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Private).SetValue(obj,value);
    static object Get(object obj,string field)=>obj.GetType().GetField(field,Private).GetValue(obj);
    static object Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,Private).Invoke(obj,args);
    static void Role(bool client,bool dual) {
        typeof(RedboolPvp).GetField("<IsClient>k__BackingField",Static).SetValue(null,client);
        typeof(RedboolPvp).GetField("<RequireView>k__BackingField",Static).SetValue(null,dual);
    }
    static void Codec()
    {
        var p=new RedboolViewPacket {kind=6,session=Guid.NewGuid(),nonce=Guid.NewGuid(),seq=7,time=1,
            selectionRun=Guid.NewGuid(),selectionRound=4,selectionRevision=uint.MaxValue,selectionCharacter=11,selectionLocked=true};
        byte[] bytes=RedboolViewProtocol.Encode(p);
        Check(RedboolViewProtocol.Version==33&&bytes.Length<1200&&RedboolViewProtocol.TryDecode(bytes,out var q),"Selection packet has dedicated v33 and stays below MTU");
        RedboolViewProtocol.TryDecode(bytes,out q);
        Check(q.selectionRun==p.selectionRun&&q.selectionRevision==p.selectionRevision&&q.selectionRound==4&&q.selectionCharacter==11&&q.selectionLocked,"All selection identities and absolute choice round trip");
        for(int i=0;i<bytes.Length;i++)Check(!RedboolViewProtocol.TryDecode(new ArraySegment<byte>(bytes,0,i).ToArray(),out _),"Selection truncation rejected at "+i);
        var malformed=(byte[])bytes.Clone();malformed[malformed.Length-1]=2;
        Check(!RedboolViewProtocol.TryDecode(malformed,out _),"Invalid confirm boolean rejected");
        p.selectionCharacter=12;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(p),out _),"Out of roster choice rejected");p.selectionCharacter=2;
        p.selectionRun=Guid.Empty;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(p),out _),"Missing selection epoch rejected");p.selectionRun=Guid.NewGuid();
        p.selectionRound=-1;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(p),out _),"Negative round rejected");
        bytes[4]=32;Check(!RedboolViewProtocol.TryDecode(bytes,out _),"Prior incompatible release rejected");
        var model=new RedboolPvpSelection();model.Begin();Guid run=model.Run;
        Check(model.Apply(0,run,0,3,false,12)&&model.Revision[0]==1,"Choose changes exactly one revision");
        Check(!model.Apply(0,run,0,3,true,12)&&!model.Locked[0],"Old confirm cannot lock newer selection");
        Check(!model.Apply(0,run,1,4,true,12),"Confirm cannot also change the visible character");
        Check(model.Apply(0,run,1,3,true,12)&&model.Locked[0],"Exact visible revision confirms");
        Check(!model.Apply(0,run,1,3,true,12)&&model.Revision[0]==2,"Retried old command is a harmless rejection");
        Check(!model.Apply(0,run,2,4,false,12),"Locked selection must unlock before changing character");
        Check(model.Apply(0,run,2,3,false,12),"Explicit unlock retains character");
        uint revision=model.Revision[0];model.ParticipantChanged(0);
        Check(!model.Apply(0,run,revision,3,true,12),"Person/C change invalidates in-flight confirm");
        model.Begin();Check(!model.Apply(0,run,0,3,false,12),"Reselection has a new epoch");
    }
    sealed class Menu
    {
        public GameObject root;public RedboolPvp game;public RedboolPvpLobby lobby;public RedboolViewLink link;public Canvas canvas;public UdpClient socket;
    }
    static Menu MenuCreate(string name,bool client)
    {
        var m=new Menu();m.root=new GameObject(name);m.game=m.root.AddComponent<RedboolPvp>();m.lobby=m.root.AddComponent<RedboolPvpLobby>();m.link=m.root.AddComponent<RedboolViewLink>();
        Set(m.game,"<Lobby>k__BackingField",m.lobby);Set(m.game,"<ViewLink>k__BackingField",m.link);Set(m.game,"configured",true);
        Set(m.lobby,"game",m.game);Set(m.link,"game",m.game);Set(m.link,"client",client);Set(m.link,"initialized",true);
        m.socket=new UdpClient();Set(m.link,"socket",m.socket); // Unbound; no Initialize or Send peer.
        Role(client,true);Call(m.lobby,"BuildUi");Call(m.lobby,"Enter",RedboolPvpLobby.Stage.Select);m.canvas=(Canvas)Get(m.lobby,"canvas");
        return m;
    }
    static RedboolViewFrame HostFrame(Menu host,Guid session,ulong seq)
    {
        var l=host.lobby;var f=new RedboolViewFrame {session=session,seq=seq,time=1,arrival=Time.realtimeSinceStartupAsDouble,
            world=new RedboolViewWorld {stage=1,round=host.game.Round,selectionRun=l.SelectionRun,selectionRevision0=l.SelectionRevision[0],selectionRevision1=l.SelectionRevision[1],
                cursor0=(byte)l.Cursor[0],cursor1=(byte)l.Cursor[1],locked=(byte)((l.Locked[0]?1:0)|(l.Locked[1]?2:0)),fov=60,rotation=Quaternion.identity}};
        for(int i=0;i<2;i++)f.actors[i]=new RedboolViewActor {live=false,character=(byte)l.Cursor[i],step=0};return f;
    }
    static void Publish(Menu guest,RedboolViewFrame frame)
    {
        var assembler=(RedboolViewAssembler)Get(guest.link,"assembler");
        for(byte kind=1;kind<=3;kind++)assembler.Push(new RedboolViewPacket {kind=kind,session=frame.session,seq=frame.seq,time=frame.time,world=frame.world,actor=kind>1?frame.actors[kind-2]:null},frame.arrival);
        Set(guest.link,"ack",frame.seq);Set(guest.link,"lastFrame",Time.realtimeSinceStartupAsDouble);
        Call(guest.link,"ObserveSelectionReply",frame);guest.lobby.ShowRemote(frame);
    }
    static void FreshHost(Menu host,ulong seq) {Set(host.link,"sequence",seq);Set(host.link,"ack",seq);Set(host.link,"lastHeartbeat",Time.realtimeSinceStartupAsDouble);}
    static bool Dispatch(Menu host,RedboolViewPacket p,IPEndPoint from) {Role(false,true);return (bool)Call(host.link,"ApplySelectionPacket",p,from);}
    static RedboolViewPacket Pending(Menu guest)=>(RedboolViewPacket)Get(guest.link,"selectionRequest");
    static void Readiness()
    {
        var s=new RedboolCameraRuntime.CameraStatus {camera_connected=true,calibrated=true,ready=true,tracking="VALID"};
        Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("동작 준비됨"),"Fresh ready is shown separately from calibration");
        s.tracking="STALE";Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("입력 복구")&&!RedboolCameraPanel.CalibrationSummary(s,true).Contains("동작 준비"),"Calibrated STALE never claims ready even if stale ready bit remains true");
        s.tracking="VALID";s.framing=new RedboolCameraRuntime.Framing {code="OUTSIDE"};
        Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("양팔 확인"),"Clipped arms do not claim ready from shoulder-only tracking");
        foreach(var code in new[]{"UNCERTAIN","NO_POSE"}) {
            s.framing.code=code;
            Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("양팔 확인")&&RedboolCameraPanel.CameraInputGuidance(s,true).Contains("팔꿈치"),"Uncertain or absent arms never claim ready: "+code);
        }
        s.framing=null;s.ready=false;Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("자세 확인"),"Calibrated preparing is not labeled ready");
        Check(RedboolCameraPanel.CalibrationSummary(s,false).Contains("연결 확인"),"Old status cannot advertise readiness");
        s.calibrated=false;s.phase="calibrating";Check(RedboolCameraPanel.CalibrationSummary(s,true).StartsWith("보정 중"),"Active calibration is distinct from complete");
        s.phase="waiting_calibration";Check(RedboolCameraPanel.CalibrationSummary(s,true).Contains("C로 시작"),"Unstarted calibration gives a single action");
    }
    public static void RunAll() {Run();RedboolViewSelfCheck.Run();RedboolInputSelfCheck.Run();RedboolPracticeSelfCheck.Run();RedboolPvpPracticeSelfCheck.Run();}
    public static void Run()
    {
        if(!Application.isBatchMode||string.IsNullOrEmpty(Output))throw new Exception("Isolated graphical batch and REDBOOL_SELECTION_CHECK_OUTPUT required");
        Directory.CreateDirectory(Output);checks.Clear();bool oldClient=RedboolPvp.IsClient,oldDual=RedboolPvp.RequireView;
        Scene scene=default(Scene);Menu host=null,guest=null;string failure=null;
        try
        {
            Codec();Readiness();scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            host=MenuCreate("Host picker",false);
            var session=Guid.NewGuid();var nonce=Guid.NewGuid();var peer=new IPEndPoint(IPAddress.Loopback,12345);
            Set(host.link,"session",session);Set(host.link,"peerNonce",nonce);Set(host.link,"peer",peer);FreshHost(host,10);
            Role(false,true);Call(host.lobby,"Refresh",new object[]{null});
            Check(!host.game.BothPresent&&host.lobby.SelectKey(KeyCode.RightArrow)&&host.lobby.Cursor[0]==1,"Host arrows work with hands lowered and no camera presence");
            var hostCards=(Button[])Get(host.lobby,"pickButtons");hostCards[3].onClick.Invoke();
            Check(host.lobby.Cursor[0]==3&&host.lobby.Cursor[1]==5,"Host mouse changes only P1");
            var confirm=((Button[])Get(host.lobby,"confirmButtons"))[0];
            var data=new BaseEventData(EventSystem.current);((RedboolPvpSelectionButton)confirm).OnSubmit(data);
            Check(!host.lobby.Locked[0],"EventSystem Submit does not duplicate Lobby Enter handling");
            Check(host.lobby.SelectKey(KeyCode.Return)&&host.lobby.Locked[0],"One Enter confirms once");
            ((RedboolPvpSelectionButton)confirm).OnSubmit(data);Check(host.lobby.Locked[0],"Submit after Enter cannot undo confirmation");
            Check(!host.lobby.ChooseLocal(2),"Locked host cannot change character");
            Check(host.lobby.SelectKey(KeyCode.Backspace)&&!host.lobby.Locked[0],"Backspace explicitly unlocks");
            Call(host.lobby,"OnCamera",0,"LEFT_PUNCH",1f,"RIGHT",1f);Check(host.lobby.Cursor[0]==3&&!host.lobby.Locked[0],"Camera movement and punch never select or confirm");
            var camera=new GameObject("Picker render camera").AddComponent<Camera>();camera.backgroundColor=new Color(.02f,.03f,.04f);camera.clearFlags=CameraClearFlags.SolidColor;
            Call(host.lobby,"Refresh",new object[]{null});
            Check(((Text[])Get(host.lobby,"bigName"))[0].text==RedboolPvpRoster.Get(3).name,"Rendered host presentation reflects the most recent mouse choice");
            Shot(host.canvas,camera,"host_picker");
            // Each real laptop creates one active menu. Build the guest only
            // after the host snapshot instead of toggling an unrendered Canvas
            // inactive/active within the same Editor call.
            host.root.SetActive(false);guest=MenuCreate("Guest picker",true);
            Set(guest.link,"session",session);Set(guest.link,"nonce",nonce);
            Role(true,true);Publish(guest,HostFrame(host,session,10));
            Check(!((Button[])Get(guest.lobby,"confirmButtons"))[0].gameObject.activeSelf&&((Button[])Get(guest.lobby,"confirmButtons"))[1].gameObject.activeSelf,"Guest has only P2 confirmation control");
            var guestCards=(Button[])Get(guest.lobby,"pickButtons");guestCards[7].onClick.Invoke();var choose=Pending(guest);
            Check(choose!=null&&choose.selectionCharacter==7&&!choose.selectionLocked&&guest.lobby.Cursor[1]==5,"Guest mouse queues absolute request, never predicts authoritative choice");
            Check(!guest.lobby.SelectKey(KeyCode.Return),"Confirm waits for selected-character acknowledgment");
            FreshHost(host,10);
            Check(!Dispatch(host,choose,new IPEndPoint(IPAddress.Loopback,12346)),"Foreign source endpoint cannot choose");
            var savedNonce=choose.nonce;choose.nonce=Guid.NewGuid();Check(!Dispatch(host,choose,peer),"Wrong nonce rejected");choose.nonce=savedNonce;
            var savedRound=choose.selectionRound;choose.selectionRound++;Check(!Dispatch(host,choose,peer),"Old or foreign round rejected");choose.selectionRound=savedRound;
            var savedSession=choose.session;choose.session=Guid.NewGuid();Check(!Dispatch(host,choose,peer),"Foreign host session rejected");choose.session=savedSession;
            ulong savedSeq=choose.seq;choose.seq=1;Check(!Dispatch(host,choose,peer),"Command nine snapshots behind cannot apply");choose.seq=11;
            Check(!Dispatch(host,choose,peer),"Future command sequence cannot apply");choose.seq=savedSeq;
            Check(Dispatch(host,choose,peer)&&host.lobby.Cursor[1]==7&&host.lobby.Cursor[0]==3,"Connected guest choice is applied only to P2");
            Check(!Dispatch(host,choose,peer),"Repeated prior-revision choose cannot change state twice");
            FreshHost(host,11);Role(true,true);Publish(guest,HostFrame(host,session,11));
            Check(!guest.link.SelectionPending&&guest.lobby.Cursor[1]==7,"Authoritative acknowledgment releases pending UI");
            Check(guest.lobby.SelectKey(KeyCode.LeftArrow)&&Pending(guest).selectionCharacter==6,"Guest keyboard uses same revision-bound command path");
            var move=Pending(guest);Check(Dispatch(host,move,peer),"Guest arrow choice reaches host");
            FreshHost(host,12);Role(true,true);Publish(guest,HostFrame(host,session,12));
            ((Button[])Get(guest.lobby,"confirmButtons"))[1].onClick.Invoke();var lockRequest=Pending(guest);
            Check(lockRequest!=null&&lockRequest.selectionCharacter==6&&lockRequest.selectionLocked,"Guest explicit confirm captures currently displayed character and revision");
            Role(false,true);host.lobby.ParticipantChanged(1);
            Check(!Dispatch(host,lockRequest,peer)&&!host.lobby.Locked[1],"C/person change invalidates in-flight guest confirmation");
            FreshHost(host,13);Role(true,true);Publish(guest,HostFrame(host,session,13));
            Check(!guest.link.SelectionPending,"New person revision cancels old request without rebasing it");
            Check(guest.lobby.SelectKey(KeyCode.Return),"Guest can explicitly confirm new-person revision");var final=Pending(guest);
            Check(Dispatch(host,final,peer)&&host.lobby.Locked[1]&&!Dispatch(host,final,peer),"Guest confirm applies once, duplicate cannot unlock");
            FreshHost(host,14);Role(true,true);Publish(guest,HostFrame(host,session,14));
            Check(guest.lobby.Locked[1]&&!guest.link.SelectionPending,"Confirmed host lock appears on guest");
            Shot(guest.canvas,camera,"guest_picker_confirmed");
            Set(guest.link,"lastFrame",Time.realtimeSinceStartupAsDouble);
            Check(guest.lobby.SelectKey(KeyCode.Backspace),"Guest unlock is explicit");var unlock=Pending(guest);
            Set(host.link,"lastHeartbeat",-10d);Check(!Dispatch(host,unlock,peer)&&host.lobby.Locked[1],"Disconnected view cannot apply queued unlock");
            Set(guest.link,"lastFrame",-10d);Role(true,true);Call(guest.link,"SendSelectionRequest",Time.realtimeSinceStartupAsDouble);
            Check(!guest.link.SelectionPending&&!guest.lobby.ChooseLocal(2),"Client loss cancels retry and freezes picker");
            FreshHost(host,15);Publish(guest,HostFrame(host,session,15));Check(guest.lobby.SelectKey(KeyCode.Backspace),"Fresh reconnect permits a new explicit request");
            Check(Dispatch(host,Pending(guest),peer),"Reconnect unlock applies without reviving old click");
            var obsolete=Pending(guest);
            FreshHost(host,16);Role(true,true);Publish(guest,HostFrame(host,session,16));
            Check(guest.lobby.ChooseLocal(8),"Guest can queue a new choice after unlock acknowledgment");
            Set(guest.link,"selectionRequestUntil",-1d);Call(guest.link,"SendSelectionRequest",Time.realtimeSinceStartupAsDouble);
            Check(!guest.link.SelectionPending&&host.lobby.Cursor[1]==6,"Retry timeout leaves authoritative selection unchanged");
            Role(false,true);Call(host.lobby,"Enter",RedboolPvpLobby.Stage.Select);
            Check(!Dispatch(host,obsolete,peer),"Reselection epoch rejects old-round UI request");
            Call(host.lobby,"Enter",RedboolPvpLobby.Stage.Done);Check(!Dispatch(host,obsolete,peer)&&!host.lobby.ChooseLocal(1),"No picker mutation after tutorial phase starts");
            Role(false,false);Call(host.lobby,"Enter",RedboolPvpLobby.Stage.Select);Check(host.lobby.SelectLocalSlot(1)&&host.lobby.ChooseLocal(9)&&host.lobby.ConfirmLocal(true),"Single-screen mode retains P2 mouse/keyboard selection");
            Check(host.lobby.SelectKey(KeyCode.Tab)&&host.lobby.LocalSlot==0&&host.lobby.ChooseLocal(2)&&host.lobby.ConfirmLocal(true),"Single-screen Tab switches owner and can explicitly confirm both");
            Check(host.socket.Client.LocalEndPoint==null&&guest.socket.Client.LocalEndPoint==null,"Neither fixture socket was bound or sent a packet");
            Check(host.game.Receivers[0]==null&&guest.game.Receivers[0]==null&&RedboolCameraRuntime.Instance==null,"No receiver or physical camera was started");
        }
        catch(Exception e){failure=e.ToString();}
        finally
        {
            if(scene.IsValid())foreach(var go in scene.GetRootGameObjects())Object.DestroyImmediate(go);
            host?.socket?.Close();guest?.socket?.Close();Role(oldClient,oldDual);
            File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);File.WriteAllText(Path.Combine(Output,"result.txt"),failure==null?"PASS "+checks.Count:failure);
        }
        if(failure!=null)throw new Exception(failure);
        Debug.Log("REDBOOL SELECTION UI PASS "+checks.Count+"; unbound sockets, no camera or game input");
    }
    static void Shot(Canvas canvas,Camera camera,string name)
    {
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        canvas.GetComponent<CanvasScaler>().enabled=false;canvas.scaleFactor=2f/3f;
        var previous=RenderTexture.active;Texture2D image=null;
        try {
            // EditMode executes the whole fixture within one call, with no
            // intervening PlayerLoop canvas rebuild after SetActive. Dirty all
            // active graphics so unchanged headers/ornaments render too.
            foreach(var graphic in canvas.GetComponentsInChildren<Graphic>())graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
            if(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline!=null)UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target});else camera.Render();
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        }finally {RenderTexture.active=previous;camera.targetTexture=null;if(image)Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);}
    }
}
