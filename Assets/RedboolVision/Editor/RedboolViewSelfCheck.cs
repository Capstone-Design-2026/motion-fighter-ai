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
        var a=new RedboolViewActor {root=new Vector3(1,2,3),visual=new Vector3(4,5,6),scale=Vector3.one,hips=new Vector3(.1f,.2f,.3f),rotation=Quaternion.Euler(10,45,25),hp=95,maxHp=100,charge=3,maxCharge=5,step=7,hits=2,phase=3,attackSerial=21,live=true};
        for(int i=0;i<a.bones.Length;i++) a.bones[i]=Quaternion.Euler(i*3,i*2,i);return a;
    }
    public static void Run()
    {
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
            bytes[4]=27;Check(!RedboolViewProtocol.TryDecode(bytes,out _),"old release rejected");
        }
        for(int i=0;i<actor.bones.Length;i++) Check(Quaternion.Angle(actor.bones[i],parts[1].actor.bones[i])<.08f,"bone precision");
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
