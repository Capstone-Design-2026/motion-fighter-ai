using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Only exists in the unsaved verification scene. Uses the real rigidbody/colliders.
[DefaultExecutionOrder(-100)]
public sealed class BoxingStepPhysicsDriver : MonoBehaviour
{
    public float speed;
    void FixedUpdate() { var b=GetComponent<Rigidbody2D>();b.linearVelocity=new Vector2(speed,b.linearVelocity.y); }
}
[InitializeOnLoad]
public static class BoxingStepVerification
{
    const string Flag="BoxingStepVerification.Running";
    static IEnumerator flow;
    static int lastFrame=-1,captured;
    static readonly List<string> checks=new List<string>(), errors=new List<string>();
    static Fighter3DView[] views;static Fighter[] fighters;static Health[] health;
    static Rigidbody2D[] bodies;static BoxingStepPhysicsDriver[] drivers;
    static Camera camera;static RenderTexture rt;static Texture2D image;
    static StreamWriter frames,metrics;
    static bool record;static string label="";
    static string Out=>UnifiedBoxingInstaller.Out;
    static BoxingStepVerification(){if(SessionState.GetBool(Flag,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
    public static void Run()
    {
        Directory.CreateDirectory(Out);EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Flag,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        EditorApplication.isPaused=false;EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Log(string t,string s,LogType kind)
    {if((kind==LogType.Error||kind==LogType.Exception)&&!s.Contains("IvanMurzak")&&!t.Contains("MCP"))errors.Add(t);}
    static void Check(bool ok,string text)
    {checks.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Path.Combine(Out,"checks.txt"),checks);}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||!Application.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.isPaused=false;Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();
        if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
        try
        {
            if(flow==null)flow=Tests();bool more=flow.MoveNext();
            if(record&&Time.frameCount%2==0)Capture();
            if(!more)Finish();
        }
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Finish()
    {
        record=false;frames?.Dispose();metrics?.Dispose();
        File.WriteAllLines(Path.Combine(Out,"runtime_errors.txt"),errors);
        File.WriteAllText(Path.Combine(Out,"verification_done.txt"),errors.Count==0&&!checks.Any(x=>x.StartsWith("FAIL"))?"PASS":"FAIL");
        SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        EditorApplication.Exit(errors.Count==0?0:1);
    }
    static IEnumerator Wait(float t){float end=Time.time+t;while(Time.time<end)yield return null;}
    static void SetSpeed(float speed){foreach(var d in drivers)d.speed=speed;}
    static void Reset()
    {
        SetSpeed(0);
        for(int p=0;p<2;p++)
        {
            fighters[p].InputLocked=false;fighters[p].IsBlocking=fighters[p].IsCrouching=false;fighters[p].CancelAttack();
            health[p].DamageLocked=false;health[p].ResetHealth();fighters[p].Facing=1;
            bodies[p].linearVelocity=Vector2.zero;bodies[p].position=new Vector2(p*50,50);
            views[p].transform.position=new Vector3(p*50,50,0);views[p].ResetPresentation();
        }
        Physics2D.SyncTransforms();
    }
    static IEnumerator Travel(string title,float speed,float seconds)
    {
        label=title;SetSpeed(speed);
        float begin=Time.time,maxContact=0,maxReach=0,maxDrift=0,minGap=100;int count=0;
        int[] lastSupport={-1,-1};Vector3[] previous=new Vector3[2];
        while(Time.time<begin+seconds)
        {
            yield return null;
            for(int p=0;p<2;p++)
            {
                var w=views[p].Walk;
                metrics.WriteLine(Time.time.ToString("F4")+","+title+","+p+","+w.Speed.ToString("F4")+","+w.PlantedFoot+","+w.ContactError.ToString("F6")+","+w.ReachError.ToString("F6"));
                if(Time.time-begin>.35f&&w.Weight>.999f)
                {
                    maxContact=Mathf.Max(maxContact,w.ContactError);maxReach=Mathf.Max(maxReach,w.ReachError);
                    if(w.PlantedFoot>=0&&lastSupport[p]==w.PlantedFoot)
                        maxDrift=Mathf.Max(maxDrift,Vector3.Distance(previous[p],w.ContactPosition));
                    var left=views[p].modelAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var right=views[p].modelAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
                    minGap=Mathf.Min(minGap,Vector3.Dot(left.position-right.position,views[p].visualRoot.forward));
                    count++;
                }
                lastSupport[p]=w.PlantedFoot;previous[p]=w.ContactPosition;
            }
        }
        Check(count>20,title+" sampled moving contacts");
        Check(minGap>.10f,title+" left lead stays ahead of rear foot; min gap="+minGap.ToString("F4")+"m");
        Check(maxContact<.012f,title+" contact target error="+maxContact.ToString("F5")+"m");
        Check(maxReach<.012f,title+" leg reach error="+maxReach.ToString("F5")+"m");
        Check(maxDrift<.012f,title+" planted-foot frame drift="+maxDrift.ToString("F5")+"m");
        Check(views.All(v=>v.CurrentState==((speed* fighters[0].Facing<0)?"WALK_BACK":"WALK_FORWARD")),title+" correct locomotion state");
    }
    static IEnumerator Tests()
    {
        yield return null;
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerController>();var enemy=UnityEngine.Object.FindAnyObjectByType<EnemyAI>();
        views=new[]{player.GetComponent<Fighter3DView>(),enemy.GetComponent<Fighter3DView>()};
        fighters=views.Select(v=>v.GetComponent<Fighter>()).ToArray();health=views.Select(v=>v.GetComponent<Health>()).ToArray();bodies=views.Select(v=>v.GetComponent<Rigidbody2D>()).ToArray();
        player.enabled=enemy.enabled=false;
        foreach(var x in UnityEngine.Object.FindObjectsByType<BattleSceneBridge>())UnityEngine.Object.Destroy(x);
        foreach(var x in UnityEngine.Object.FindObjectsByType<CombatHUD>())UnityEngine.Object.Destroy(x);
        foreach(var x in UnityEngine.Object.FindObjectsByType<UdpActionReceiver>())x.enabled=false;
        foreach(var x in UnityEngine.Object.FindObjectsByType<Pushbox>())x.enabled=false;
        foreach(var x in UnityEngine.Object.FindObjectsByType<Camera>())x.enabled=false;
        foreach(var h in health)h.Configure("Test",10000);
        foreach(var f in fighters){var so=new SerializedObject(f);so.FindProperty("useHitImpact").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();}
        Check(views[1].ApplyVisual(AssetDatabase.LoadAssetAtPath<GameObject>(UnifiedBoxingInstaller.Scout)),"Stage Scout visual binds new walking system");
        yield return null;
        drivers=views.Select(v=>v.gameObject.AddComponent<BoxingStepPhysicsDriver>()).ToArray();
        for(int p=0;p<2;p++)
        {
            foreach(var t in views[p].visualRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=10+p;
            bodies[p].bodyType=RigidbodyType2D.Dynamic;bodies[p].simulated=true;bodies[p].gravityScale=3;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Walking review floor";floor.layer=10+p;
            floor.transform.position=new Vector3(p*50,49.95f,0);floor.transform.localScale=new Vector3(45,.10f,8);
            var physicsFloor=new GameObject("Walking physics floor");physicsFloor.transform.position=floor.transform.position;physicsFloor.AddComponent<BoxCollider2D>().size=new Vector2(45,.10f);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.12f,.15f,.19f);floor.GetComponent<Renderer>().sharedMaterial=material;
            for(int i=-22;i<=22;i++)
            {
                var line=GameObject.CreatePrimitive(PrimitiveType.Cube);line.layer=10+p;line.transform.position=new Vector3(p*50+i,50.002f,0);
                line.transform.localScale=new Vector3(.025f,.003f,7);line.GetComponent<Renderer>().sharedMaterial=new Material(material){color=new Color(.26f,.32f,.38f)};
            }
        }
        camera=new GameObject("Walking side review").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.27f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.07f);
        rt=new RenderTexture(640,640,24);camera.targetTexture=rt;image=new Texture2D(1280,640,TextureFormat.RGB24,false);
        var light=new GameObject("Review light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,-25,0);
        Time.timeScale=1;Time.captureDeltaTime=1f/60;
        Directory.CreateDirectory(Path.Combine(Out,"frames"));frames=new StreamWriter(Path.Combine(Out,"frames.csv"));frames.WriteLine("frame,label,boxerState,scoutState");
        metrics=new StreamWriter(Path.Combine(Out,"metrics.csv"));metrics.WriteLine("time,case,actor,speed,support,contactError,reachError");
        Reset();label="IDLE";for(var w=Wait(.6f);w.MoveNext();)yield return null;record=true;
        var hipMin=new[]{10000f,10000f};var hipMax=new[]{-10000f,-10000f};
        float idleEnd=Time.time+6f;
        var soleMin=new[]{10000f,10000f};var soleMax=new[]{-10000f,-10000f};
        while(Time.time<idleEnd)
        {
            yield return null;
            for(int p=0;p<2;p++)
            {
                float y=views[p].modelAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y-views[p].transform.position.y;
                hipMin[p]=Mathf.Min(hipMin[p],y);hipMax[p]=Mathf.Max(hipMax[p],y);
                if(Time.frameCount%3==0)
                {
                    float sole=UnifiedBoxingInstaller.SkinFloor(views[p].visualRoot.gameObject)-views[p].Walk.GroundY;
                    soleMin[p]=Mathf.Min(soleMin[p],sole);soleMax[p]=Mathf.Max(soleMax[p],sole);
                }
            }
        }
        for(int p=0;p<2;p++)Check(hipMax[p]-hipMin[p]>.012f&&hipMax[p]-hipMin[p]<.05f,"Actor "+p+" small idle hop pelvis excursion="+(hipMax[p]-hipMin[p]).ToString("F4")+"m");
        for(int p=0;p<2;p++)
        {
            Check(soleMin[p]>-.005f&&soleMin[p]<.008f,"Actor "+p+" idle sole minimum="+soleMin[p].ToString("F4")+"m");
            Check(soleMax[p]>.010f&&soleMax[p]<.028f,"Actor "+p+" small airborne sole height="+soleMax[p].ToString("F4")+"m");
        }
        if(Environment.GetCommandLineArgs().Contains("-walkingStress"))
        {
            record=false;
            for(var w=Stress(player,enemy);w.MoveNext();)yield return null;
            yield break;
        }
        foreach(var test in new[]{("FORWARD",1.08f),("BACKWARD",-.864f),("SLOW_VISION",.6f),("FAST_ENEMY",1.44f)})
        {
            for(var w=Travel(test.Item1,test.Item2,3f);w.MoveNext();)yield return null;
            SetSpeed(0);label="STOP";for(var w=Wait(.9f);w.MoveNext();)yield return null;
            Check(views.All(v=>v.CurrentState=="IDLE"&&v.Walk.Weight<.01f),test.Item1+" settles to idle");
            Reset();for(var w=Wait(.3f);w.MoveNext();)yield return null;
        }
        foreach(var f in fighters)f.Facing=-1;
        for(var w=Travel("LEFT_FACING",-1.08f,2.5f);w.MoveNext();)yield return null;
        for(var w=Travel("REVERSE",.864f,2.5f);w.MoveNext();)yield return null;
        SetSpeed(0);for(var w=Wait(.8f);w.MoveNext();)yield return null;Reset();
        // Real wall collision: intent stays nonzero while actual travel stops.
        var walls=new List<GameObject>();
        for(int p=0;p<2;p++)
        {var wall=new GameObject("Test wall");wall.transform.position=new Vector3(p*50+2,52,0);var col=wall.AddComponent<BoxCollider2D>();col.size=new Vector2(.25f,5);walls.Add(wall);}
        label="WALL";SetSpeed(1.08f);for(var w=Wait(2f);w.MoveNext();)yield return null;
        Check(views.All(v=>v.CurrentState=="IDLE"&&!v.Walk.IsMoving),"Wall prevents walking in place despite nonzero movement input");
        foreach(var w in walls)UnityEngine.Object.Destroy(w);Reset();for(var w=Wait(.3f);w.MoveNext();)yield return null;
        foreach(CombatMove move in Enum.GetValues(typeof(CombatMove)))
        {
            label="WALK_TO_"+move;SetSpeed(1.08f);for(var w=Wait(.6f);w.MoveNext();)yield return null;
            SetSpeed(0);foreach(var f in fighters)f.TryAttack(move);
            for(var w=Wait(.14f);w.MoveNext();)yield return null;
            Check(views.All(v=>v.Walk.Weight<.001f&&Mathf.Abs(v.modelAnimator.speed-1)<.001f),move+" fully releases walk IK and restores attack timing");
            for(var w=Wait(Fighter3DView.Duration(move)+.25f);w.MoveNext();)yield return null;
            Check(views.All(v=>v.CurrentState=="IDLE"),move+" returns to idle");
        }
        foreach(bool low in new[]{false,true})
        {
            label=low?"WALK_TO_LOW_GUARD":"WALK_TO_GUARD";SetSpeed(-.864f);for(var w=Wait(.6f);w.MoveNext();)yield return null;
            SetSpeed(0);foreach(var f in fighters){f.IsBlocking=true;f.IsCrouching=low;}
            for(var w=Wait(.45f);w.MoveNext();)yield return null;
            Check(views.All(v=>v.Walk.Weight<.001f&&v.CurrentState==(low?"GUARD_LOWKICK_HOLD":"GUARD_HOLD")),label+" no planted-foot interference");
            foreach(var f in fighters)f.IsBlocking=f.IsCrouching=false;
            for(var w=Wait(.6f);w.MoveNext();)yield return null;
        }
        label="WALK_TO_HURT";SetSpeed(1.08f);for(var w=Wait(.5f);w.MoveNext();)yield return null;
        SetSpeed(0);foreach(var h in health)h.TakeDamage(1,Vector2.zero);for(var w=Wait(.3f);w.MoveNext();)yield return null;
        Check(views.All(v=>v.Walk.Weight<.001f&&v.CurrentState=="HURT"),"Hit interrupts walking");
        for(var w=Wait(.6f);w.MoveNext();)yield return null;
        label="WALK_TO_KO";SetSpeed(-.864f);for(var w=Wait(.5f);w.MoveNext();)yield return null;
        SetSpeed(0);foreach(var h in health)h.TakeDamage(10000,Vector2.zero);for(var w=Wait(1.9f);w.MoveNext();)yield return null;
        Check(views.All(v=>v.Walk.Weight<.001f&&v.CurrentState=="KO"),"KO releases both feet");
        record=false;Reset();for(var w=Wait(.25f);w.MoveNext();)yield return null;
        Check(views.All(v=>v.CurrentState=="IDLE"&&!v.Walk.IsMoving),"Round reset clears old anchors");
        Check(errors.Count==0,"No runtime errors");
        if(Environment.GetCommandLineArgs().Contains("-allBoxingChecks"))
            for(var w=Stress(player,enemy);w.MoveNext();)yield return null;
    }
    static IEnumerator Stress(PlayerController player,EnemyAI enemy)
    {
        foreach(int fps in new[]{30,120})
        {
            Time.captureDeltaTime=1f/fps;Reset();for(var w=Wait(.4f);w.MoveNext();)yield return null;
            foreach(float speed in new[]{.6f,1.08f,-.864f,1.44f})
                for(var w=Travel(fps+"FPS_"+speed,speed,1.8f);w.MoveNext();)yield return null;
            SetSpeed(0);for(var w=Wait(.8f);w.MoveNext();)yield return null;
            Check(views.All(v=>v.CurrentState=="IDLE"&&v.Walk.Weight<.01f),fps+"fps stop settles");
        }
        Time.captureDeltaTime=1f/60;Reset();
        drivers[0].enabled=drivers[1].enabled=false;player.useKeyboard=false;player.enabled=true;enemy.enabled=true;
        player.ExternalAction("IDLE");player.ExternalMovement(.5f);
        for(var w=Wait(1.4f);w.MoveNext();)yield return null;
        Check(views.All(v=>v.CurrentState=="WALK_FORWARD"),"Actual vision movement entry and EnemyAI both drive walking");
        Check(views.All(v=>v.Walk.ContactError<.012f),"Actual player/AI supporting feet remain planted");
        player.ExternalMovement(-1f);for(var w=Wait(.9f);w.MoveNext();)yield return null;
        Check(views[0].CurrentState=="WALK_BACK","Actual player backward multiplier routes into back walk");
        player.ExternalMovement(0);for(var w=Wait(.8f);w.MoveNext();)yield return null;
        Check(views[0].CurrentState=="IDLE"&&views[0].Walk.Weight<.01f,"Vision neutral input settles both feet");
        player.enabled=enemy.enabled=false;drivers[0].enabled=drivers[1].enabled=true;
        Reset();bodies[1].position=new Vector2(2,50);views[1].transform.position=new Vector3(2,50,0);fighters[1].Facing=-1;Physics2D.SyncTransforms();
        var push=views.Select(v=>v.GetComponent<Pushbox>()).ToArray();
        for(int p=0;p<2;p++){push[p].SetOpponent(push[1-p]);push[p].enabled=true;}
        drivers[0].speed=1.08f;drivers[1].speed=-1.04f;
        for(var w=Wait(2f);w.MoveNext();)yield return null;
        Check(views[1].transform.position.x>views[0].transform.position.x,"Real pushboxes preserve sides");
        Check(views.All(v=>v.CurrentState=="IDLE"&&!v.Walk.IsMoving),"Pushing opponent does not march in place");
        foreach(var p in push)p.enabled=false;
        Reset();for(var w=Wait(.4f);w.MoveNext();)yield return null;
        // Mesh-level sole check, independent of the ankle IK target metric.
        float worstPenetration=0,worstGap=0;
        SetSpeed(1.08f);
        for(int sample=0;sample<24;sample++)
        {
            for(var w=Wait(.05f);w.MoveNext();)yield return null;
            foreach(var view in views)
            {
                int support=view.Walk.PlantedFoot;if(support<0||view.Walk.Weight<.999f)continue;
                var foot=view.modelAnimator.GetBoneTransform(support==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                float bottom=float.PositiveInfinity;var mesh=new Mesh();
                foreach(var skin in view.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    int bone=Array.IndexOf(skin.bones,foot);if(bone<0)continue;
                    skin.BakeMesh(mesh);var matrix=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);
                    var weights=skin.sharedMesh.boneWeights;var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var b=weights[i];float influence=(b.boneIndex0==bone?b.weight0:0)+(b.boneIndex1==bone?b.weight1:0)+(b.boneIndex2==bone?b.weight2:0)+(b.boneIndex3==bone?b.weight3:0);
                        if(influence>.5f)bottom=Mathf.Min(bottom,matrix.MultiplyPoint3x4(vertices[i]).y);
                    }
                }
                UnityEngine.Object.Destroy(mesh);
                if(!float.IsInfinity(bottom))
                {float gap=bottom-view.Walk.GroundY;worstPenetration=Mathf.Min(worstPenetration,gap);worstGap=Mathf.Max(worstGap,gap);}
            }
        }
        Check(worstPenetration>-.02f,"Skinned support-foot sole penetration="+worstPenetration.ToString("F4")+"m");
        Check(worstGap<.03f,"Skinned support-foot sole gap="+worstGap.ToString("F4")+"m");
        SetSpeed(0);Check(errors.Count==0,"No runtime errors during frame-rate/input/pushbox checks");
    }
    static void Capture()
    {
        for(int p=0;p<2;p++)
        {
            Vector3 center=views[p].transform.position+new Vector3(.05f,1f,0);camera.transform.position=center+Vector3.back*6;camera.transform.LookAt(center);
            camera.cullingMask=1<<(10+p);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,640),p*640,0);
        }
        image.Apply();File.WriteAllBytes(Path.Combine(Out,"frames",captured.ToString("D5")+".jpg"),image.EncodeToJPG(87));
        frames.WriteLine(captured+","+label+","+views[0].CurrentState+","+views[1].CurrentState);captured++;RenderTexture.active=null;
    }
}


