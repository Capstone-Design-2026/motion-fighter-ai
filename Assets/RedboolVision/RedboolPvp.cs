using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One authoritative LAN arena, two isolated input leases, no campaign writes.</summary>
[DefaultExecutionOrder(200)]
public sealed class RedboolPvp : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static RedboolPvp Instance { get; private set; }
    public readonly PlayerController[] Players = new PlayerController[2];
    public readonly RedboolDemoReceiver[] Receivers = new RedboolDemoReceiver[2];
    public readonly RedboolPractice[] Practices = { new RedboolPractice(), new RedboolPractice() };
    readonly Vector3[] positions = new Vector3[2];
    readonly int[] generations = { -1,-1 };
    readonly string[] streams = new string[2];
    readonly Text[] playerLabels = new Text[2];
    readonly Text[] practiceLabels = new Text[2];
    readonly TrainingPoseGraphic[] diagrams=new TrainingPoseGraphic[2];
    Text banner, clockLabel;
    Canvas canvas;
    RedboolRenderWarmup warmup;
    public bool Fighting { get; private set; }
    public bool Resolved { get; private set; }
    public float Remaining { get; private set; } = 120f;
    public string Result { get; private set; } = "";
    bool configured, paused=true;
    double readySince=-1;
    public bool BothLive => configured && Receivers[0]!=null && Receivers[1]!=null && Receivers[0].LiveReady && Receivers[1].LiveReady;
    public bool CanFight => Fighting && !Resolved && !paused && BothLive && warmup==null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { Active=false; Instance=null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CommandLineEntry()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--redbool-pvp")>=0) Begin();
    }
    public static void Begin()
    {
        RedboolDuelTest.End();
        if(Instance!=null) return;
        Active=true;
        var go=new GameObject("REDBOOL Two Player LAN");
        Instance=go.AddComponent<RedboolPvp>(); DontDestroyOnLoad(go);
        SceneManager.sceneLoaded+=Instance.SceneLoaded;
        Time.timeScale=1;Time.fixedDeltaTime=.02f;HitImpact.ReleaseTimeControl();
        SceneManager.LoadScene(RedboolDuelTest.ArenaScene);
    }
    void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!=RedboolDuelTest.ArenaScene) { Leave(false); return; }
        var first=FindAnyObjectByType<PlayerController>();
        var enemy=FindAnyObjectByType<EnemyAI>();
        if(first==null || enemy==null) throw new InvalidOperationException("PvP arena needs two spawn positions");
        positions[0]=first.transform.position;positions[1]=enemy.transform.position;
        enemy.gameObject.SetActive(false);
        Players[0]=first;
        Players[1]=Instantiate(first.gameObject,positions[1],first.transform.rotation).GetComponent<PlayerController>();
        for(int i=0;i<2;i++)
        {
            int slot=i;var p=Players[i];p.name="REDBOOL Player "+(i+1);p.useKeyboard=false;
            p.SetOpponent(Players[1-i].transform);
            var health=p.GetComponent<Health>();health.Configure((i+1)+"P",100);
            health.SetAppearance(i==0?new Color(.9f,.75f,.55f):new Color(.55f,.75f,1));
            p.GetComponent<Fighter>().SetVisionPreparationLock(true);
            var host=new GameObject("REDBOOL Player "+(i+1)+" Input");host.SetActive(false);
            var receiver=host.AddComponent<RedboolDemoReceiver>();
            receiver.port=5065+i;receiver.expectedActorId="P"+(i+1);receiver.player=p;
            receiver.PvpOwned=true;receiver.showStatus=false;receiver.Opponent=Players[1-i].GetComponent<Fighter>();
            receiver.ExternalGate=()=>CanFight;
            receiver.PracticeContext=()=>new RedboolPracticeContext {run=Practices[slot].Run,action=Practices[slot].Action,phase=Practices[slot].Phase,hits=Practices[slot].Hits};
            receiver.PracticeAction+=Practices[i].Observe;
            receiver.CalibrationObserved+=(calibrated,generation)=>OnCalibration(slot,calibrated,generation);
            Receivers[i]=receiver;host.SetActive(true);
        }
        // Explicit targets avoid any FindFirst ambiguity between two humans.
        foreach(var camera in FindObjectsByType<Battle3DCamera>(FindObjectsSortMode.None))
        { camera.player=Players[0].transform;camera.enemy=Players[1].transform; }
        foreach(var legacy in FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) legacy.enabled=false;
        configured=true;
        if(RedboolEffectPreparation.Enabled) warmup=Receivers[0].gameObject.AddComponent<RedboolRenderWarmup>();
        BuildUi();SetPaused(true);
        Debug.Log("[REDBOOL PVP] ScoutPass, P1 :5065 / P2 :5066, campaign untouched");
    }
    void OnCalibration(int slot,bool calibrated,int generation)
    {
        if(generations[slot]!=generation)
        {
            if(generations[slot]>=0) ResetPractice(slot);
            generations[slot]=generation;
        }
        if(!calibrated) { readySince=-1;SetPaused(true); }
    }
    void ResetPractice(int slot)
    {
        Receivers[slot].PracticeAction-=Practices[slot].Observe;
        Practices[slot].Reset();Receivers[slot].PracticeAction+=Practices[slot].Observe;
        readySince=-1;SetPaused(true);
    }
    void SetPaused(bool value)
    {
        bool entering=value&&!paused;
        paused=value;
        if(!configured) return;
        for(int i=0;i<2;i++)
        {
            var f=Players[i].GetComponent<Fighter>();
            f.SetVisionPreparationLock(value || Resolved);
            f.Health.DamageLocked=value || Resolved;
            if(value) Players[i].ClearExternalInput();
            if(entering) Players[i].GetComponent<SpecialMove>()?.CancelForPause();
        }
        if(entering)
        {
            foreach(var wave in FindObjectsByType<QiWave>(FindObjectsSortMode.None)) Destroy(wave.gameObject);
            HitImpact.ReleaseTimeControl();
        }
        if(value) Time.timeScale=Resolved?1:0;
        else if(Time.timeScale==0) {Time.timeScale=1;Time.fixedDeltaTime=.02f;}
    }
    void Update()
    {
        if(!configured) return;
        if(Input.GetKeyDown(KeyCode.Escape)) { Leave(true);return; }
        if(Resolved && Input.GetKeyDown(KeyCode.R)) Rematch();
        for(int i=0;i<2;i++)
        {
            string stream=Receivers[i].StreamId;
            if(!String.IsNullOrEmpty(stream) && streams[i]!=stream)
            { if(streams[i]!=null) ResetPractice(i);streams[i]=stream; }
            if(Practices[i].Complete) Receivers[i].PracticeAction-=Practices[i].Observe;
        }
        bool ready=BothLive && Practices[0].Complete && Practices[1].Complete && warmup==null && !Resolved;
        if(!ready) { readySince=-1;SetPaused(true); }
        else if(paused)
        {
            if(!Receivers[0].NeutralReady || !Receivers[1].NeutralReady) readySince=-1;
            else if(readySince<0) readySince=Time.realtimeSinceStartupAsDouble;
            else if(Time.realtimeSinceStartupAsDouble-readySince>=1.0) { Fighting=true;SetPaused(false); }
        }
        if(CanFight) Remaining=Mathf.Max(0,Remaining-Time.deltaTime);
        RefreshUi();
    }
    // Resolve after the contact world: simultaneous KO must not become a 1P win.
    void LateUpdate()
    {
        if(!configured) return;
        if(!Resolved && Fighting)
        {
            var a=Players[0].GetComponent<Health>();var b=Players[1].GetComponent<Health>();
            if(a.IsDead || b.IsDead || Remaining<=0)
            {
                Result=a.CurrentHealth==b.CurrentHealth?"무승부":a.CurrentHealth>b.CurrentHealth?"1P 승리":"2P 승리";
                Resolved=true;SetPaused(true);
            }
        }
        if(paused && !Resolved) Time.timeScale=0;
    }
    public void Rematch()
    {
        if(!configured || !Resolved) return;
        Resolved=false;Fighting=false;Remaining=120;Result="";readySince=-1;
        for(int i=0;i<2;i++)
        {
            var p=Players[i];p.GetComponent<SpecialMove>()?.CancelForPause();p.GetComponent<SpecialMove>()?.ResetCharge();
            p.GetComponent<Health>().ResetHealth();p.GetComponent<Fighter>().InputLocked=false;
            p.transform.position=positions[i];var body=p.GetComponent<Rigidbody2D>();if(body) {body.position=positions[i];body.linearVelocity=Vector2.zero;}
            p.GetComponent<Fighter>().Contacts.ResetHistory();
        }
        SetPaused(true);
    }
    void BuildUi()
    {
        UIFactory.EnsureEventSystem();canvas=UIFactory.CreateCanvas("REDBOOL PvP",transform,210);
        for(int i=0;i<2;i++)
        {
            float anchor=i==0?0:1;float x=i==0?35:-35;
            var panel=UIFactory.CreateImage("PlayerPanel"+i,canvas.transform,new Color(.02f,.04f,.06f,.85f));
            UIFactory.Place(panel.rectTransform,new Vector2(anchor,1),new Vector2(anchor,1),new Vector2(x,-30),new Vector2(540,175));
            playerLabels[i]=WuxiaTheme.Text("Health"+i,panel.transform,"",28,TextAnchor.MiddleCenter,i==0?WuxiaTheme.Gold:new Color(.65f,.8f,1));
            UIFactory.Place(playerLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-8),new Vector2(520,56));
            var guide=UIFactory.CreateRect("MotionGuide"+i,canvas.transform);
            UIFactory.Place(guide,new Vector2(i==0?.25f:.75f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(220,240));
            diagrams[i]=guide.gameObject.AddComponent<TrainingPoseGraphic>();
            practiceLabels[i]=WuxiaTheme.Text("Practice"+i,panel.transform,"",21,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
            UIFactory.Place(practiceLabels[i].rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,10),new Vector2(520,100));
        }
        clockLabel=WuxiaTheme.Text("RoundClock",canvas.transform,"120",40,TextAnchor.UpperCenter,WuxiaTheme.Gold);
        UIFactory.Place(clockLabel.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-35),new Vector2(200,60));
        banner=WuxiaTheme.Text("Status",canvas.transform,"",27,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Place(banner.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,25),new Vector2(1500,100));
    }
    void RefreshUi()
    {
        if(banner==null) return;
        for(int i=0;i<2;i++)
        {
            diagrams[i].gameObject.SetActive(!Practices[i].Complete);diagrams[i].step=Practices[i].Step;
            var hp=Players[i].GetComponent<Health>();var special=Players[i].GetComponent<SpecialMove>();
            playerLabels[i].text=(i+1)+"P   체력 "+hp.CurrentHealth+" / "+hp.MaxHealth+"   기 "+(special?special.Charge:0)+" / "+(special?special.HitsToCharge:5);
            practiceLabels[i].text=(!Receivers[i].LiveReady?"카메라 연결 · C로 중앙 가드 보정\n":"")+Practices[i].Instruction+"\n"+(Receivers[i].PersonalStatus.Contains("PERSONAL RANGE")?"개인 동작 범위 확인됨":Receivers[i].PersonalStatus.Contains("CYCLES")?"동작과 복귀를 확인 중":"기본 인식 기준 사용");
        }
        clockLabel.text=Mathf.CeilToInt(Remaining).ToString();
        banner.text=Resolved?Result+" · R: 재대전 · Esc: 메뉴":!BothLive?"두 카메라를 기다립니다 · P1 :5065 / P2 :5066\n연결이 끊기거나 보정 중이면 양쪽 전투가 멈춥니다":!Practices[0].Complete||!Practices[1].Complete?"각자 안내 동작을 두 번씩 한 뒤 팔과 몸을 중앙으로 돌려주세요":paused?"두 사람 모두 중앙 가드 · 1초 뒤 대전 시작":"2인 대전 · 블락은 자세를 유지하는 동안 · X자: 파운장 · Esc: 메뉴";
    }
    public void Leave(bool menu)
    {
        Active=false;configured=false;SceneManager.sceneLoaded-=SceneLoaded;
        HitImpact.ReleaseTimeControl();Time.timeScale=1;Time.fixedDeltaTime=.02f;
        Instance=null;Destroy(gameObject);
        if(menu) SceneRoutes.LoadStart();
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded-=SceneLoaded;
        if(Instance==this) {Instance=null;Active=false;Time.timeScale=1;Time.fixedDeltaTime=.02f;}
    }
}
