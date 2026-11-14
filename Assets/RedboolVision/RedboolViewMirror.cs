using System.Collections.Generic;
using UnityEngine;

/// <summary>Visual replica only: no controller, collision simulation, damage, or story saves.</summary>
public sealed class RedboolViewMirror : MonoBehaviour
{
    RedboolPvp game;Fighter3DView[] views;Transform[][] bones;QiPalmAssets assets;
    readonly Dictionary<int,GameObject> waves=new Dictionary<int,GameObject>();
    readonly QiChargeFx[] chargeFx=new QiChargeFx[2];
    readonly GameObject[,] readyFx=new GameObject[2,2];
    RedboolRenderWarmup warmup;
    MaterialPropertyBlock tint;
    public bool Ready => warmup==null;
    readonly int[] serial={-1,-1};
    readonly Renderer[][] renderers=new Renderer[2][];
    readonly HeroPunchSurfaceVisibility[][] punchSurfaces=new HeroPunchSurfaceVisibility[2][];
    int lastEffect;
    public void Initialize(RedboolPvp owner,Fighter3DView[] presentation,Transform[][] rig)
    {
        game=owner;tint=new MaterialPropertyBlock();if(RedboolEffectPreparation.Enabled) warmup=gameObject.AddComponent<RedboolRenderWarmup>();views=presentation;bones=rig;assets=QiPalmAssets.Load();
        for(int i=0;i<2;i++)
        {
            var p=game.Players[i];p.ClearExternalInput();p.enabled=false;
            p.GetComponent<Health>().DamageLocked=true;
            p.GetComponent<Fighter>().enabled=false;p.GetComponent<SpecialMove>().enabled=false;
            foreach(var b in p.GetComponentsInChildren<Rigidbody2D>(true)) b.simulated=false;
            foreach(var c in p.GetComponentsInChildren<Collider2D>(true)) c.enabled=false;
            foreach(var c in p.GetComponentsInChildren<Collider>(true)) c.enabled=false;
            views[i].enabled=false;views[i].modelAnimator.enabled=false;
            punchSurfaces[i]=views[i].visualRoot.GetComponentsInChildren<HeroPunchSurfaceVisibility>(true);
            foreach(var surface in punchSurfaces[i]) surface.enabled=false;
            renderers[i]=views[i].visualRoot.GetComponentsInChildren<Renderer>();
            var props=new MaterialPropertyBlock();props.SetColor("_BaseColor",views[i].fighterTint);props.SetColor("_Color",views[i].fighterTint);
            foreach(var r in renderers[i]) r.SetPropertyBlock(props);
        }
        foreach(var c in FindObjectsByType<Battle3DCamera>(FindObjectsSortMode.None)) c.enabled=false;
    }
    public void ClearTransient()
    {
        foreach(var wave in waves.Values) if(wave) Destroy(wave);waves.Clear();
        for(int i=0;i<2;i++) {if(chargeFx[i]) Destroy(chargeFx[i].gameObject);for(int j=0;j<2;j++) if(readyFx[i,j]) Destroy(readyFx[i,j]);serial[i]=-1;}
        lastEffect=0;
    }
    public void Apply(RedboolViewFrame a,RedboolViewFrame b,float t)
    {
        for(int i=0;i<2;i++)
        {
            var x=a.actors[i];var y=b.actors[i];var v=views[i];
            game.Players[i].transform.position=Vector3.Lerp(x.root,y.root,t);
            v.visualRoot.position=Vector3.Lerp(x.visual,y.visual,t);v.visualRoot.rotation=Quaternion.Slerp(x.rotation,y.rotation,t);v.visualRoot.localScale=Vector3.Lerp(x.scale,y.scale,t);
            for(int j=0;j<bones[i].Length;j++) if(bones[i][j]) bones[i][j].localRotation=Quaternion.Slerp(x.bones[j],y.bones[j],t);
            if(bones[i][0]) bones[i][0].localPosition=Vector3.Lerp(x.hips,y.hips,t);
            foreach(var surface in punchSurfaces[i]) surface.ApplyRemoteSurface(y.punchSurface);
            if(serial[i]>=0 && y.attackSerial!=serial[i]) CombatSfx.PlayAttack();serial[i]=y.attackSerial;
            var hands=(bones[i][(int)HumanBodyBones.LeftHand].position+bones[i][(int)HumanBodyBones.RightHand].position)*.5f;
            bool charging=y.phase==1||y.phase==2||y.phase==3;
            if(charging && !chargeFx[i] &&assets &&assets.charge) chargeFx[i]=Instantiate(assets.charge).GetComponent<QiChargeFx>();
            if(!charging && chargeFx[i]) {chargeFx[i].End();chargeFx[i]=null;}
            if(chargeFx[i]) {chargeFx[i].Follow(hands,y.visual);chargeFx[i].SetLevel(y.chargeLevel);}
            bool ready=y.charge>=y.maxCharge &&y.phase==0 &&y.hp>0;
            for(int j=0;j<2;j++)
            {
                if(ready && !readyFx[i,j] &&assets &&assets.ready) readyFx[i,j]=Instantiate(assets.ready,bones[i][j==0?(int)HumanBodyBones.LeftHand:(int)HumanBodyBones.RightHand],false);
                if(!ready &&readyFx[i,j]) {Destroy(readyFx[i,j]);readyFx[i,j]=null;}
            }
            tint.SetColor("_BaseColor",y.tint);tint.SetColor("_Color",y.tint);
            foreach(var renderer in renderers[i]) renderer.SetPropertyBlock(tint);
        }
        var cam=Camera.main;if(cam) {cam.transform.position=Vector3.Lerp(a.world.camera,b.world.camera,t);cam.transform.rotation=Quaternion.Slerp(a.world.rotation,b.world.rotation,t);cam.fieldOfView=Mathf.Lerp(a.world.fov,b.world.fov,t);}
        var ids=new HashSet<int>();
        foreach(var wave in b.world.waves)
        {
            ids.Add(wave.id);
            if(!waves.TryGetValue(wave.id,out var go) || !go)
            {
                if(assets==null ||assets.wave==null) continue;
                var holder=new GameObject("Replica wave holder");holder.SetActive(false);
                go=Instantiate(assets.wave,holder.transform);foreach(var script in go.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=false;
                foreach(var c in go.GetComponentsInChildren<Collider>(true)) c.enabled=false;
                foreach(var c in go.GetComponentsInChildren<Collider2D>(true)) c.enabled=false;
                go.transform.SetParent(null);Destroy(holder);go.SetActive(true);waves[wave.id]=go;
                if(assets.muzzle) QiFxLife.Spawn(assets.muzzle,wave.point,wave.rotation);
                if(assets.release) AudioSource.PlayClipAtPoint(assets.release,wave.point);
            }
            Vector3 point=wave.point;foreach(var old in a.world.waves) if(old.id==wave.id) point=Vector3.Lerp(old.point,wave.point,t);
            go.transform.position=point;go.transform.rotation=wave.rotation;
        }
        var remove=new List<int>();foreach(var pair in waves) if(!ids.Contains(pair.Key)) {if(pair.Value) Destroy(pair.Value);remove.Add(pair.Key);}foreach(var id in remove) waves.Remove(id);
        foreach(var fx in b.world.effects)
        {
            if(fx.id<=lastEffect) continue;lastEffect=fx.id;
            if(b.time-fx.time>.3) continue;
            if(fx.kind<2) {HitVfx.Play(fx.point,fx.kind==1);CombatSfx.PlayHit(fx.kind==1);}
            else if(assets) {QiFxLife.Spawn(assets.impact,fx.point,Quaternion.identity,fx.kind==3?.65f:1.1f);if(assets.impactClip) AudioSource.PlayClipAtPoint(assets.impactClip,fx.point);}
        }
    }
    void OnDestroy() {ClearTransient();}
}
