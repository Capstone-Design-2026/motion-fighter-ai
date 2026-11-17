using System;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class RedboolViewSelfCheck
{
    static int checks;
    static void Check(bool ok,string why) {checks++;if(!ok) throw new Exception("View protocol: "+why);}
    static RedboolViewActor Actor()
    {
        var a=new RedboolViewActor {root=new Vector3(1,2,3),visual=new Vector3(4,5,6),scale=Vector3.one,hips=new Vector3(.1f,.2f,.3f),rotation=Quaternion.Euler(10,45,25),hp=95,maxHp=100,charge=3,maxCharge=5,punchSurface=2,step=7,hits=2,phase=3,attackSerial=21,live=true};
        for(int i=0;i<a.bones.Length;i++) a.bones[i]=Quaternion.Euler(i*3,i*2,i);return a;
    }
    static RedboolViewPacket ActorPacket(RedboolViewActor actor,byte kind=2)
    {
        return new RedboolViewPacket {kind=kind,session=Guid.NewGuid(),nonce=Guid.NewGuid(),seq=1,time=1,actor=actor};
    }
    static RedboolViewPacket WorldPacket(RedboolViewWorld world)
    {
        return new RedboolViewPacket {kind=1,session=Guid.NewGuid(),nonce=Guid.NewGuid(),seq=1,time=1,world=world};
    }
    static void CheckLobbyCodec()
    {
        Check(RedboolViewProtocol.Version==33,"feedback, lobby and READY/FIGHT gates use version33");
        Check(RedboolPvpRoster.Count==12,"version32 roster has exactly twelve character indices");
        for(byte slot=2;slot<=3;slot++) for(byte character=0;character<12;character++)
        {
            var actor=Actor();actor.character=character;actor.specialSerial=(byte)(255-character);
            actor.specialFlags=7;actor.feedback=(byte)RedboolCombatFeedbackCode.Interrupted;actor.practiceRun=Guid.NewGuid();
            var bytes=RedboolViewProtocol.Encode(ActorPacket(actor,slot));
            Check(RedboolViewProtocol.TryDecode(bytes,out var decoded),"both actors and every roster entry round trip");
            Check(decoded.actor.character==character &&decoded.actor.specialSerial==255-character &&decoded.actor.specialFlags==7,"roster and special state preserved exactly");
            Check(decoded.actor.feedback==actor.feedback && decoded.actor.practiceRun==actor.practiceRun &&decoded.actor.punchSurface==actor.punchSurface,"feedback, correction surface and tutorial identity survive alongside roster fields");
        }
        var valid=Actor();valid.character=11;
        foreach(byte flags in new byte[]{0,1,2,3,6,7}) {valid.specialFlags=flags;Check(RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out _),"valid special flags "+flags);}
        foreach(byte flags in new byte[]{4,5,8,255}) {valid.specialFlags=flags;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out _),"unknown/inconsistent special flags rejected: "+flags);}
        valid.specialFlags=0;valid.character=12;
        Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out _),"thirteenth roster index rejected");
        valid.character=255;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out _),"out-of-range roster byte rejected");
        valid.character=0;valid.feedback=255;
        Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out _),"unknown authoritative feedback still rejected in version32");
        valid.feedback=0;
        foreach(byte serial in new byte[]{255,0}) {valid.specialSerial=serial;Check(RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(ActorPacket(valid)),out var decoded)&&decoded.actor.specialSerial==serial,"special serial wrap value preserved: "+serial);}

        var world=new RedboolViewWorld {fov=60,rotation=Quaternion.identity,cursor0=0,cursor1=11,locked=3,selectSeconds=60};
        for(byte stage=0;stage<=2;stage++)
        {
            world.stage=stage;
            Check(RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out var decoded)
                &&decoded.world.stage==stage &&decoded.world.cursor0==0 &&decoded.world.cursor1==11 &&decoded.world.locked==3 &&decoded.world.selectSeconds==60,"lobby stage and boundary selections round trip: "+stage);
        }
        world.stage=3;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out _),"unknown lobby stage rejected");world.stage=2;
        world.cursor0=12;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out _),"invalid first lobby cursor rejected");world.cursor0=0;
        world.cursor1=12;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out _),"invalid second lobby cursor rejected");world.cursor1=11;
        world.locked=4;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out _),"unknown lobby lock bit rejected");world.locked=3;
        world.selectSeconds=61;Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(WorldPacket(world)),out _),"excessive selection timer rejected");
    }
    static RedboolViewFrame Frame(Guid session)
    {
        var f=new RedboolViewFrame {session=session,world=new RedboolViewWorld()};f.actors[0]=Actor();f.actors[1]=Actor();return f;
    }
    static void CheckInterpolationBoundaries()
    {
        var session=Guid.NewGuid();var a=Frame(session);var b=Frame(session);
        Check(RedboolViewLink.CanBlendSnapshots(a,b),"same session/round/avatar snapshots may interpolate");
        b.actors[0].character=1;Check(!RedboolViewLink.CanBlendSnapshots(a,b),"P1 avatar replacement never mixes old and new bone pose");b.actors[0].character=0;
        b.actors[1].character=11;Check(!RedboolViewLink.CanBlendSnapshots(a,b),"P2 avatar replacement never mixes old and new bone pose");b.actors[1].character=0;
        b.world.stage=1;Check(!RedboolViewLink.CanBlendSnapshots(a,b),"lobby-to-match transition does not interpolate across presentation modes");b.world.stage=2;
        b.world.round=1;Check(!RedboolViewLink.CanBlendSnapshots(a,b),"rematch never interpolates old body position");b.world.round=0;
        b.world.paused=true;Check(!RedboolViewLink.CanBlendSnapshots(a,b),"pause transition remains a hard snapshot boundary");b.world.paused=false;
        b.session=Guid.NewGuid();Check(!RedboolViewLink.CanBlendSnapshots(a,b),"reconnect cannot blend two host sessions");
        Check(!RedboolViewLink.CanBlendSnapshots(a,null),"incomplete view is never an interpolation input");
    }
    static void CheckReplicaRebind()
    {
        var root=new GameObject("View rebind fixture");
        try
        {
            var mirror=root.AddComponent<RedboolViewMirror>();var view=root.AddComponent<Fighter3DView>();
            var visual=new GameObject("Replacement visual");visual.transform.SetParent(root.transform);
            view.visualRoot=visual.transform;view.modelAnimator=visual.AddComponent<Animator>();
            var body=visual.AddComponent<Rigidbody2D>();var c2=visual.AddComponent<BoxCollider2D>();
            var hitGeometry=new GameObject("Replacement 3D hit geometry");hitGeometry.transform.SetParent(visual.transform);
            var c3=hitGeometry.AddComponent<BoxCollider>();
            typeof(RedboolViewMirror).GetField("views",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(mirror,new[]{view,view});
            mirror.Rebind(0);
            Check(!view.enabled&&!view.modelAnimator.enabled,"replacement guest visual cannot run its own animation/controller");
            Check(!body.simulated&&!c2.enabled&&!c3.enabled,"replacement guest visual stays non-simulated and cannot introduce new collision behavior");
            mirror.Rebind(-1);mirror.Rebind(2);Check(true,"out-of-range rebind is ignored safely");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }
    static void CheckReconnectAssembly()
    {
        var session=Guid.NewGuid();var other=Guid.NewGuid();var assembler=new RedboolViewAssembler();
        var world=new RedboolViewWorld {stage=1,cursor0=3,cursor1=11,locked=1,selectSeconds=15};
        var p1=Actor();p1.character=3;p1.feedback=(byte)RedboolCombatFeedbackCode.Ready;
        var p2=Actor();p2.character=11;p2.feedback=(byte)RedboolCombatFeedbackCode.Interrupted;p2.specialSerial=255;p2.specialFlags=7;
        var first=new RedboolViewPacket {kind=1,session=session,seq=1,time=5,world=world};
        var second=new RedboolViewPacket {kind=2,session=session,seq=1,time=5,actor=p1};
        var third=new RedboolViewPacket {kind=3,session=other,seq=1,time=5,actor=p2};
        assembler.Push(first,1);assembler.Push(second,1.01);
        Check(assembler.Push(third,1.02)==null&&assembler.Latest==null,"a foreign-session actor cannot complete an in-progress lobby frame");
        third.session=session;var complete=assembler.Push(third,1.03);
        Check(complete!=null&&complete.world.cursor1==11&&complete.actors[1].character==11&&complete.actors[1].specialSerial==255
            &&complete.actors[1].feedback==(byte)RedboolCombatFeedbackCode.Interrupted,"only a matching three-part snapshot publishes roster, special and own-player feedback");
        Check(assembler.Push(third,1.04)==null,"duplicate special snapshot cannot create a new completed frame");
        assembler.Reset();first.session=other;second.session=other;third.session=session;
        assembler.Push(first,2);assembler.Push(second,2.01);
        Check(assembler.Push(third,2.02)==null&&assembler.Latest==null,"old-session tail remains unusable after reconnect reset");
        third.session=other;p2.specialSerial=0;
        complete=assembler.Push(third,2.03);
        Check(complete!=null&&complete.session==other&&complete.actors[1].specialSerial==0,"fresh complete reconnect frame preserves wrapped special serial without old parts");
    }
    public static void Run()
    {
        checks=0;
        CheckLobbyCodec();CheckInterpolationBoundaries();CheckReplicaRebind();CheckReconnectAssembly();
        var session=Guid.NewGuid();var nonce=Guid.NewGuid();var actor=Actor();
        var world=new RedboolViewWorld {round=3,remaining=87.4f,fov=60,camera=new Vector3(0,3,-7),rotation=Quaternion.identity,paused=false,fighting=true};
        for(int i=0;i<8;i++) world.waves.Add(new RedboolViewWave {id=i,point=new Vector3(i,1,0),rotation=Quaternion.identity});
        for(int i=0;i<12;i++) world.effects.Add(new RedboolViewEffect {id=i,kind=i%4,time=19,point=Vector3.one});
        var parts=new RedboolViewPacket[3];int total=0,max=0;
        for(byte i=1;i<=3;i++)
        {
            var p=new RedboolViewPacket {kind=i,session=session,nonce=nonce,seq=10,time=20,echo=10,actor=actor,world=world};var bytes=RedboolViewProtocol.Encode(p);total+=bytes.Length;max=Math.Max(max,bytes.Length);
            Check(bytes.Length<=1200,"MTU budget");Check(RedboolViewProtocol.TryDecode(bytes,out parts[i-1]),"round trip");Check(parts[i-1].nonce==nonce && parts[i-1].session==session,"session and recipient identity");
            for(int cut=0;cut<bytes.Length;cut++) Check(!RedboolViewProtocol.TryDecode(new ArraySegment<byte>(bytes,0,cut).ToArray(),out _),"truncated packet");
            bytes[4]=(byte)(RedboolViewProtocol.Version-1);Check(!RedboolViewProtocol.TryDecode(bytes,out _),"old release rejected");
        }
        var skip=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=5,session=session,nonce=nonce,seq=11,time=20,practiceRun=Guid.NewGuid(),practiceStep=7});
        Check(RedboolViewProtocol.TryDecode(skip,out var skipDecoded) &&skipDecoded.practiceStep==7,"skip control round trip");
        for(int cut=0;cut<skip.Length;cut++)Check(!RedboolViewProtocol.TryDecode(new ArraySegment<byte>(skip,0,cut).ToArray(),out _),"truncated skip control");
        for(int i=0;i<actor.bones.Length;i++) Check(Quaternion.Angle(actor.bones[i],parts[1].actor.bones[i])<.08f,"bone precision");
        for(int i=1;i<3;i++) Check(parts[i].actor.charge==actor.charge && parts[i].actor.maxCharge==actor.maxCharge,"both actor meter values survive snapshot encoding exactly");
        for(int i=1;i<3;i++) Check(parts[i].actor.punchSurface==2,"both actors preserve the host straight correction surface");
        for(byte surface=0;surface<3;surface++) {
            actor.punchSurface=surface;
            var encoded=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=2,session=session,nonce=nonce,seq=1,time=1,actor=actor});
            Check(RedboolViewProtocol.TryDecode(encoded,out var decoded)&&decoded.actor.punchSurface==surface,"idle/jab/straight surface round trip");
        }
        actor.punchSurface=3;
        Check(!RedboolViewProtocol.TryDecode(RedboolViewProtocol.Encode(new RedboolViewPacket {kind=2,session=session,nonce=nonce,seq=1,time=1,actor=actor}),out _),"unknown correction surface rejected");
        actor.punchSurface=2;
        Check(parts[0].world.effects.Count==12 &&parts[0].world.waves.Count==8,"bounded effect lists");
        var assembler=new RedboolViewAssembler();Check(assembler.Push(parts[2],1)==null,"part 3 first");Check(assembler.Push(parts[0],1.02)==null,"incomplete frame stays hidden");
        Check(assembler.Push(parts[1],1.03)!=null,"complete out of order frame");Check(assembler.Push(parts[1],1.04)==null,"duplicate suppressed");
        foreach(var p in parts) p.seq=9;foreach(var p in parts) Check(assembler.Push(p,1.1)==null,"older state rejected");
        foreach(var p in parts) p.seq=11;assembler.Push(parts[0],2);assembler.Push(parts[1],2.01);Check(assembler.Push(parts[2],2.2)==null,"late partial frame discarded");
        foreach(var p in parts) p.seq=12;assembler.Push(parts[1],3);assembler.Push(parts[2],3.01);Check(assembler.Push(parts[0],3.02)!=null,"next full frame recovers after loss");
        foreach(var p in parts) {p.session=Guid.NewGuid();p.seq=13;}Check(assembler.Push(parts[0],4)==null,"foreign session blocked");
        assembler.Reset();Check(assembler.Latest==null,"explicit reconnect reset");
        var invalid=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=2,session=session,nonce=nonce,seq=1,time=1,actor=actor});
        Array.Copy(BitConverter.GetBytes(float.NaN),0,invalid,62,4);Check(!RedboolViewProtocol.TryDecode(invalid,out _),"NaN position rejected");
        var random=new System.Random(28);for(int i=0;i<2000;i++) {var data=new byte[random.Next(1300)];random.NextBytes(data);Check(!RedboolViewProtocol.TryDecode(data,out _),"random malformed datagram");}
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<10000;i++) {var bytes=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=2,session=session,nonce=nonce,seq=(ulong)i,time=i,actor=actor});RedboolViewProtocol.TryDecode(bytes,out _);}
        watch.Stop();
        string result=$"PASS {checks}\nMaximum datagram {max} bytes\nWorst-case full frame {total} bytes; 30Hz {total*30} bytes/s excluding UDP/IP\n10000 actor encode/decode pairs {watch.Elapsed.TotalMilliseconds:F2} ms\n";
        var path=Environment.GetEnvironmentVariable("REDBOOL_VIEW_CHECK_OUTPUT");if(!String.IsNullOrEmpty(path)) File.WriteAllText(path,result);Debug.Log(result);
    }
}
