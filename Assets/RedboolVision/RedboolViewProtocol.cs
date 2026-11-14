using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

// Three self-contained datagrams per frame, each below 1200 bytes. No simulation on the guest.
[Serializable]
public sealed class RedboolViewActor
{
    public Vector3 root,visual,scale,hips;
    public Quaternion rotation;
    public Quaternion[] bones=new Quaternion[(int)HumanBodyBones.LastBone];
    public int hp,maxHp,charge,maxCharge,step,hits,phase,attackSerial;
    public bool live;
    public Color tint=Color.white;
    public float chargeLevel;
    public byte personal, punchSurface;
    public Guid practiceRun;
}
[Serializable]
public sealed class RedboolViewWorld
{
    public int round,winner;
    public byte gate;public float countdown;
    public bool paused,resolved,fighting;
    public float remaining,fov;
    public Vector3 camera;
    public Quaternion rotation;
    public List<RedboolViewEffect> effects=new List<RedboolViewEffect>();
    public List<RedboolViewWave> waves=new List<RedboolViewWave>();
}
[Serializable]
public struct RedboolViewWave { public int id;public Vector3 point;public Quaternion rotation; }
[Serializable]
public struct RedboolViewEffect { public int id,kind;public double time;public Vector3 point; }
public sealed class RedboolViewFrame
{
    public ulong seq; public double time,arrival;public Guid session;
    public RedboolViewWorld world;
    public readonly RedboolViewActor[] actors=new RedboolViewActor[2];
    public bool Complete => world!=null && actors[0]!=null && actors[1]!=null;
}
public sealed class RedboolViewPacket
{
    public byte kind;public Guid session,nonce;public ulong seq;public double time,echo;
    public RedboolViewActor actor;public RedboolViewWorld world;
    public Guid practiceRun;public byte practiceStep;
}
public static class RedboolViewProtocol
{
    public const int Port=5067,MaxBytes=1200,Version=30;
    const uint Magic=0x52425657;
    static float Finite(float v) { if(float.IsNaN(v)||float.IsInfinity(v)||Mathf.Abs(v)>100000) throw new InvalidDataException();return v; }
    static double Finite(double v) {if(double.IsNaN(v)||double.IsInfinity(v)||v<0) throw new InvalidDataException();return v;}
    static int Range(int n,int max) {if(n<0||n>max) throw new InvalidDataException();return n;}
    static void V(BinaryWriter w,Vector3 v) {w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    static Vector3 V(BinaryReader r) => new Vector3(Finite(r.ReadSingle()),Finite(r.ReadSingle()),Finite(r.ReadSingle()));
    static void Q(BinaryWriter w,Quaternion q)
    {
        q=q.normalized;w.Write((short)Mathf.RoundToInt(q.x*32767));w.Write((short)Mathf.RoundToInt(q.y*32767));w.Write((short)Mathf.RoundToInt(q.z*32767));w.Write((short)Mathf.RoundToInt(q.w*32767));
    }
    static Quaternion Q(BinaryReader r)
    {
        var q=new Quaternion(r.ReadInt16()/32767f,r.ReadInt16()/32767f,r.ReadInt16()/32767f,r.ReadInt16()/32767f);
        float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
        if(norm<.9f||norm>1.1f) throw new InvalidDataException();return q.normalized;
    }
    public static byte[] Encode(RedboolViewPacket p)
    {
        using(var ms=new MemoryStream()) using(var w=new BinaryWriter(ms))
        {
            w.Write(Magic);w.Write((byte)Version);w.Write(p.kind);w.Write(p.session.ToByteArray());w.Write(p.nonce.ToByteArray());w.Write(p.seq);w.Write(p.time);w.Write(p.echo);
            if(p.kind==1)
            {
                var x=p.world;w.Write(x.round);w.Write((byte)x.winner);w.Write(x.paused);w.Write(x.resolved);w.Write(x.fighting);w.Write(x.remaining);w.Write(x.fov);V(w,x.camera);Q(w,x.rotation);w.Write(x.gate);w.Write(x.countdown);
                w.Write((byte)x.waves.Count);foreach(var wave in x.waves) {w.Write(wave.id);V(w,wave.point);Q(w,wave.rotation);}
                w.Write((byte)x.effects.Count);foreach(var fx in x.effects) {w.Write(fx.id);w.Write((byte)fx.kind);w.Write(fx.time);V(w,fx.point);}
            }
            else if(p.kind==2 || p.kind==3)
            {
                var a=p.actor;V(w,a.root);V(w,a.visual);V(w,a.scale);V(w,a.hips);Q(w,a.rotation);
                w.Write((ushort)a.hp);w.Write((ushort)a.maxHp);w.Write((byte)a.charge);w.Write((byte)a.maxCharge);w.Write((byte)a.step);w.Write((byte)a.hits);w.Write((byte)a.phase);w.Write(a.attackSerial);w.Write(a.live);var color=(Color32)a.tint;w.Write(color.r);w.Write(color.g);w.Write(color.b);w.Write(color.a);w.Write((byte)Mathf.RoundToInt(a.chargeLevel*255));w.Write(a.personal);w.Write(a.punchSurface);w.Write(a.practiceRun.ToByteArray());
                foreach(var q in a.bones) Q(w,q);
            }
            else if(p.kind==5) {w.Write(p.practiceRun.ToByteArray());w.Write(p.practiceStep);}
            var bytes=ms.ToArray();if(bytes.Length>MaxBytes) throw new InvalidDataException("View datagram exceeds MTU budget");return bytes;
        }
    }
    public static bool TryDecode(byte[] bytes,out RedboolViewPacket p)
    {
        p=null;if(bytes==null || bytes.Length>MaxBytes || bytes.Length<62) return false;
        try
        {
            using(var ms=new MemoryStream(bytes)) using(var r=new BinaryReader(ms))
            {
                if(r.ReadUInt32()!=Magic || r.ReadByte()!=Version) return false;
                var n=new RedboolViewPacket {kind=r.ReadByte(),session=new Guid(r.ReadBytes(16)),nonce=new Guid(r.ReadBytes(16)),seq=r.ReadUInt64(),time=Finite(r.ReadDouble()),echo=Finite(r.ReadDouble())};
                if(n.kind>5) return false;
                if(n.kind==1)
                {
                    var x=new RedboolViewWorld {round=Range(r.ReadInt32(),1000000),winner=Range(r.ReadByte(),2),paused=r.ReadBoolean(),resolved=r.ReadBoolean(),fighting=r.ReadBoolean(),remaining=Finite(r.ReadSingle()),fov=Finite(r.ReadSingle()),camera=V(r),rotation=Q(r),gate=(byte)Range(r.ReadByte(),7),countdown=Finite(r.ReadSingle())};
                    if(x.countdown<0 ||x.countdown>1.01f) return false;
                    if(x.remaining<0||x.remaining>120.1f ||x.fov<1||x.fov>179) return false;
                    int count=Range(r.ReadByte(),8);for(int i=0;i<count;i++) x.waves.Add(new RedboolViewWave {id=r.ReadInt32(),point=V(r),rotation=Q(r)});
                    count=Range(r.ReadByte(),12);for(int i=0;i<count;i++) x.effects.Add(new RedboolViewEffect {id=r.ReadInt32(),kind=Range(r.ReadByte(),3),time=Finite(r.ReadDouble()),point=V(r)});
                    n.world=x;
                }
                else if(n.kind==2 || n.kind==3)
                {
                    var a=new RedboolViewActor {root=V(r),visual=V(r),scale=V(r),hips=V(r),rotation=Q(r),hp=Range(r.ReadUInt16(),10000),maxHp=Range(r.ReadUInt16(),10000),charge=Range(r.ReadByte(),100),maxCharge=Range(r.ReadByte(),100),step=Range(r.ReadByte(),8),hits=Range(r.ReadByte(),2),phase=Range(r.ReadByte(),4),attackSerial=r.ReadInt32(),live=r.ReadBoolean()};
                    a.tint=new Color32(r.ReadByte(),r.ReadByte(),r.ReadByte(),r.ReadByte());a.chargeLevel=r.ReadByte()/255f;a.personal=(byte)Range(r.ReadByte(),2);a.punchSurface=(byte)Range(r.ReadByte(),2);a.practiceRun=new Guid(r.ReadBytes(16));
                    if(a.hp>a.maxHp || a.scale.x<=0 || a.scale.y<=0 ||a.scale.z<=0) return false;
                    for(int i=0;i<a.bones.Length;i++) a.bones[i]=Q(r);n.actor=a;
                }
                else if(n.kind==5) {n.practiceRun=new Guid(r.ReadBytes(16));n.practiceStep=(byte)Range(r.ReadByte(),7);}
                if(ms.Position!=ms.Length) return false;p=n;return true;
            }
        }
        catch(Exception e) when(e is IOException || e is InvalidDataException || e is ArgumentException || e is OverflowException) {return false;}
    }
}
// Latest complete frames only: out-of-order parts cannot mix two different game moments.
public sealed class RedboolViewAssembler
{
    readonly SortedDictionary<ulong,RedboolViewFrame> pending=new SortedDictionary<ulong,RedboolViewFrame>();
    public RedboolViewFrame Latest {get;private set;}
    public void Reset() {pending.Clear();Latest=null;}
    public RedboolViewFrame Push(RedboolViewPacket p,double now)
    {
        if(p.kind<1||p.kind>3 || Latest!=null && (p.session!=Latest.session || p.seq<=Latest.seq)) return null;
        if(!pending.TryGetValue(p.seq,out var f))
        {
            if(pending.Count>=4) {var it=pending.GetEnumerator();it.MoveNext();pending.Remove(it.Current.Key);}
            f=new RedboolViewFrame {seq=p.seq,time=p.time,arrival=now,session=p.session};pending[p.seq]=f;
        }
        if(f.time!=p.time || f.session!=p.session) return null;
        if(p.kind==1) f.world=p.world;else f.actors[p.kind-2]=p.actor;
        if(!f.Complete || now-f.arrival>.12) return null;
        f.arrival=now;Latest=f;var obsolete=new List<ulong>();foreach(var entry in pending) if(entry.Key<=p.seq) obsolete.Add(entry.Key);foreach(var key in obsolete) pending.Remove(key);return f;
    }
}
