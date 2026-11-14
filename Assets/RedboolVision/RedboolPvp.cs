using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One authoritative LAN arena, two isolated input leases, no campaign writes.</summary>
[DefaultExecutionOrder(200)]
public sealed class RedboolPvp : MonoBehaviour
{
    public const string ArenaScene = "UndergroundPit";
    public static bool Active { get; private set; }
    public static bool IsClient { get; private set; }
    public static string HostAddress { get; private set; } = "127.0.0.1";
    public static bool RequireView { get; private set; }
    public RedboolViewLink ViewLink { get; private set; }
    public int Round { get; private set; }
    public bool Paused => paused;
    public bool Configured => configured;
    public static RedboolPvp Instance { get; private set; }
    public readonly PlayerController[] Players = new PlayerController[2];
    public readonly RedboolDemoReceiver[] Receivers = new RedboolDemoReceiver[2];
    public readonly RedboolPractice[] Practices = { new RedboolPractice(), new RedboolPractice() };
    /// <summary>대기실·인물 선택. 꺼져 있으면 null 이고 예전처럼 바로 수련으로 간다.</summary>
    public RedboolPvpLobby Lobby { get; private set; }
    /// <summary>두 사람이 고른 인물 번호 (RedboolPvpRoster). 선택을 거치지 않으면 주인공과 철문.</summary>
    public readonly int[] Characters = { RedboolPvpRoster.Hero, RedboolPvpRoster.DefaultSecond };
    public bool ViewReady => !RequireView || ViewLink!=null && ViewLink.PeerReady;
    GameObject heroTemplate;
    /// <summary>실제로 쓰는 PvP 무대(ArenaScene). 빌드에 없으면 예전처럼 결투 연습 무대를 쓴다.</summary>
    public static string Arena => Application.CanStreamedLevelBeLoaded(ArenaScene)?ArenaScene:RedboolDuelTest.ArenaScene;
    // 스토리와 같은 체력 막대·게이지·시계. 대기실을 쓰는 흐름에서만 켠다(예전 흐름은 예전 패널 그대로).
    CombatHUD hud;
    bool StoryHud => Lobby!=null && hud!=null;
    // 인물 고유 필살기를 쓰는가(false 면 파운장), 손님 화면에서 마지막으로 본 발동 횟수
    readonly bool[] ownSpecial=new bool[2];
    readonly int[] seenSpecial={-1,-1};
    readonly bool[] specialActing=new bool[2];
    Guid remoteSession;
    int remoteRound=-1;
    bool remoteFresh;
    readonly GameObject[] poseBacks=new GameObject[2];
    public string SpecialName(int slot) => ownSpecial[slot]?RedboolPvpRoster.Get(Characters[slot]).signature:"파운장";
    readonly Vector3[] positions = new Vector3[2];
    readonly int[] generations = { -1,-1 };
    readonly string[] streams = new string[2];
    readonly WuxiaCombatBar[] healthBars=new WuxiaCombatBar[2];
    readonly float[] healthTarget=new float[2],healthTrail=new float[2],healthTrailUntil=new float[2];
    readonly WuxiaCombatBar[] chargeBars=new WuxiaCombatBar[2];
    readonly Text[] chargeLabels=new Text[2];
    readonly Text[] playerLabels = new Text[2];
    readonly Text[] practiceLabels = new Text[2];
    readonly TrainingPoseGraphic[] diagrams=new TrainingPoseGraphic[2];
    Text banner, clockLabel, roundResult;
    GameObject resultPanel;
    Button rematchButton, reselectButton;
    double fightHintUntil=-1;   // 대전이 시작된 뒤 안내 줄을 잠깐만 보여 준다
    readonly Button[] skipButtons=new Button[2];
    readonly RectTransform[] playerPanels=new RectTransform[2];
    string lastDiagnostic;double lastDiagnosticTime;
    public byte Gate {get;private set;}
    public float Countdown => readySince<0?resumeSeconds:Mathf.Max(0,resumeSeconds-(float)(Time.realtimeSinceStartupAsDouble-readySince));
    Canvas canvas;
    RedboolRenderWarmup warmup;
    public bool Fighting { get; private set; }
    public bool Resolved { get; private set; }
    public float Remaining { get; private set; } = 120f;
    public string Result { get; private set; } = "";
    bool configured, paused=true;
    double readySince=-1, interruptionSince=-1;
    bool fullPreparation=true;
    float resumeSeconds=1;
    public bool BothLive => configured && Receivers[0]!=null && Receivers[1]!=null && Receivers[0].LiveReady && Receivers[1].LiveReady;
    public bool BothPresent => configured && Receivers[0]!=null && Receivers[1]!=null && Receivers[0].PresenceReady && Receivers[1].PresenceReady;
    public bool CanFight => Fighting && !Resolved && !paused && BothPresent && warmup==null && (!RequireView || ViewLink != null && ViewLink.PeerReady);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { Active=false; Instance=null; IsClient=false; RequireView=false; HostAddress="127.0.0.1"; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CommandLineEntry()
    {
        var args=Environment.GetCommandLineArgs();
        int join=Array.IndexOf(args,"--redbool-pvp-client");
        if(join>=0 && join+1<args.Length) Join(args[join+1]);
        else if(Array.IndexOf(args,"--redbool-pvp")>=0) { RequireView=Array.IndexOf(args,"--redbool-require-view")>=0; Begin(); }
    }
    public static void HostDual() { RequireView=true;Begin(); }
    public static void Join(string address)
    {
        if(!System.Net.IPAddress.TryParse(address,out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("Host IPv4 required");
        IsClient=true;HostAddress=address;Begin();
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
        SceneManager.LoadScene(Arena);
    }
    void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!=Arena) { Leave(false); return; }
        var first=FindAnyObjectByType<PlayerController>();
        var enemy=FindAnyObjectByType<EnemyAI>();
        if(first==null || enemy==null) throw new InvalidOperationException("PvP arena needs two spawn positions");
        positions[0]=first.transform.position;positions[1]=enemy.transform.position;
        enemy.gameObject.SetActive(false);
        Players[0]=first;
        // 인물 선택에서 주인공을 다시 고를 수 있게, 원래 겉모습을 꺼 둔 채로 하나 간직한다.
        var heroView=first.GetComponent<Fighter3DView>();
        if(RedboolPvpLobby.Enabled && heroView!=null && heroView.visualRoot!=null)
        {
            var holder=new GameObject("PvP hero visual template");holder.transform.SetParent(transform,false);holder.SetActive(false);
            heroTemplate=Instantiate(heroView.visualRoot.gameObject,holder.transform,false);heroTemplate.name=heroView.visualRoot.name;
        }
        Players[1]=Instantiate(first.gameObject,positions[1],first.transform.rotation).GetComponent<PlayerController>();
        // Reuse the authored Cheolmun opponent visual; all gameplay stats remain on the cloned player controller.
        var secondVisual=RunState.Instance!=null?RunState.Instance.Config.GetEnemyVisual("gate"):null;
        if(secondVisual!=null && Players[1].GetComponent<Fighter3DView>().ApplyVisual(secondVisual))
            Debug.Log("[REDBOOL PVP] 2P visual: "+secondVisual.name);
        else Debug.LogWarning("[REDBOOL PVP] 2P visual unavailable; original character with UI identification");
        RedboolPvpJab.Apply(Players[1].GetComponent<Fighter3DView>());
        for(int i=0;i<2;i++)
        {
            int slot=i;var p=Players[i];p.name="REDBOOL Player "+(i+1);p.useKeyboard=false;
            p.SetOpponent(Players[1-i].transform);
            var health=p.GetComponent<Health>();health.Configure((i+1)+"P",100);
            health.SetAppearance(Color.white);
            p.GetComponent<Fighter>().SetVisionPreparationLock(true);
            if(IsClient) continue;
            var host=new GameObject("REDBOOL Player "+(i+1)+" Input");host.SetActive(false);
            var receiver=host.AddComponent<RedboolDemoReceiver>();
            receiver.port=5065+i;receiver.expectedActorId="P"+(i+1);receiver.player=p;
            receiver.PvpOwned=true;receiver.Opponent=Players[1-i].GetComponent<Fighter>();
            Practices[i].Suspended=RedboolPvpLobby.Enabled;
            receiver.ExternalGate=()=>CanFight;
            receiver.PracticeContext=()=>Lobby!=null && !Lobby.Complete
                ? new RedboolPracticeContext {run=Practices[slot].Run,action="",phase="SELECT",hits=0}   // 인물을 고르는 중: 수련 동작이 아니다
                : new RedboolPracticeContext {run=Practices[slot].Run,action=Practices[slot].Action,phase=Practices[slot].Phase,hits=Practices[slot].Hits};
            receiver.PracticeAction+=Practices[i].Observe;
            receiver.CalibrationObserved+=(calibrated,generation)=>OnCalibration(slot,calibrated,generation);
            Receivers[i]=receiver;host.SetActive(true);
        }
        // Explicit targets avoid any FindFirst ambiguity between two humans.
        foreach(var camera in FindObjectsByType<Battle3DCamera>(FindObjectsSortMode.None))
        { camera.player=Players[0].transform;camera.enemy=Players[1].transform; }
        foreach(var legacy in FindObjectsByType<UdpActionReceiver>(FindObjectsSortMode.None)) legacy.enabled=false;
        configured=true;
        if(!IsClient && RedboolEffectPreparation.Enabled) warmup=Receivers[0].gameObject.AddComponent<RedboolRenderWarmup>();
        BuildUi();SetPaused(true);
        ViewLink=gameObject.AddComponent<RedboolViewLink>();ViewLink.Initialize(this);
        if(RedboolPvpLobby.Enabled) {Lobby=gameObject.AddComponent<RedboolPvpLobby>();Lobby.Initialize(this);hud=FindAnyObjectByType<CombatHUD>(FindObjectsInactive.Include);LobbyLayout();}
        Debug.Log("[REDBOOL PVP] "+scene.name+", P1 :5065 / P2 :5066, campaign untouched");
    }
    void OnCalibration(int slot,bool calibrated,int generation)
    {
        if(generations[slot]!=generation)
        {
            if(generations[slot]>=0) ResetPractice(slot);
            generations[slot]=generation;
            Lobby?.ParticipantChanged(slot);
        }
        if(!calibrated) { fullPreparation=true;readySince=-1;SetPaused(true); }
    }
    void ResetPractice(int slot)
    {
        Receivers[slot].PracticeAction-=Practices[slot].Observe;
        Practices[slot].Reset();Practices[slot].Suspended=Lobby!=null && !Lobby.Complete;
        Receivers[slot].PracticeAction+=Practices[slot].Observe;
        Lobby?.ParticipantChanged(slot);
        fullPreparation=true;readySince=-1;SetPaused(true);
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
            if(entering) {Players[i].GetComponent<SpecialMove>()?.CancelForPause();Players[i].GetComponent<EnemySpecial>()?.CancelForPause();}
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
        if(IsClient) return;
        if(Resolved && !RedboolPauseMenu.BlocksStoryInput && Input.GetKeyDown(KeyCode.R)) Rematch();
        if(Resolved && !RedboolPauseMenu.BlocksStoryInput && Input.GetKeyDown(KeyCode.F8)) Reselect();
        if(RedboolDemoSettings.OperatorMode && !RedboolPauseMenu.BlocksStoryInput) {
            if(Input.GetKeyDown(KeyCode.F6)) SkipPractice(0);
            if(Input.GetKeyDown(KeyCode.F7)) SkipPractice(1);
        }
        for(int i=0;i<2;i++)
        {
            string stream=Receivers[i].StreamId;
            if(!String.IsNullOrEmpty(stream) && streams[i]!=stream)
            { if(streams[i]!=null) ResetPractice(i);streams[i]=stream; }
            Practices[i].Suspended=Lobby!=null && !Lobby.Complete;
            Practices[i].Tick();
            if(Practices[i].Complete) Receivers[i].PracticeAction-=Practices[i].Observe;
        }
        bool ready=BothPresent && (Lobby==null || Lobby.Complete) && Practices[0].Complete && Practices[1].Complete && warmup==null && !Resolved && (!RequireView || ViewLink!=null && ViewLink.PeerReady);
        double now=Time.realtimeSinceStartupAsDouble;
        if(!ready) {
            if(interruptionSince<0) interruptionSince=now;
            if(now-interruptionSince>=1) fullPreparation=true;
            readySince=-1;SetPaused(true);
        }
        else if(paused)
        {
            // Completion is already an explicit practice interaction. A second simultaneous
            // two-arm READY gate deadlocks high guards and otherwise valid camera users.
            // Bounded transport-only presence tolerates one delayed camera frame.
            // Starting/resuming still requires both input leases to be fresh; waiting
            // for that edge does not restart the countdown on every brief lease gap.
            resumeSeconds=fullPreparation?1f:.10f;
            if(readySince<0) readySince=now;
            else if(now-readySince>=resumeSeconds && BothLive) {
                Debug.Log(FormattableString.Invariant($"[REDBOOL PVP RESUME] round={Round} delay={resumeSeconds:F2} interruption={(interruptionSince<0?0:now-interruptionSince):F3} utc={DateTime.UtcNow:o}"));
                Fighting=true;fullPreparation=false;interruptionSince=-1;SetPaused(false);
            }
        }
        if(CanFight) Remaining=Mathf.Max(0,Remaining-Time.deltaTime);
        Gate=Resolved?(byte)7:warmup!=null?(byte)1:RequireView&&(ViewLink==null||!ViewLink.PeerReady)?(byte)2:!Receivers[0].PresenceReady?(byte)3:!Receivers[1].PresenceReady?(byte)4:!Practices[0].Complete||!Practices[1].Complete?(byte)5:paused?(byte)6:(byte)0;
        Diagnostic();RefreshUi();
    }
    public void SkipPractice(int slot,string expectedRun=null,int expectedStep=-1)
    {
        if(IsClient || !configured || slot<0 || slot>1 || Practices[slot].Complete || Lobby!=null && !Lobby.Complete) return;
        var p=Practices[slot];
        if(expectedRun!=null && (p.Run!=expectedRun || p.Step!=expectedStep)) return;
        Debug.Log("[REDBOOL PRACTICE SKIP] player="+(slot+1)+" action="+p.Action+" baseline_retained=True");
        p.Skip();
    }
    void SkipFromUi(int slot)
    {
        if(IsClient) {if(slot==1) ViewLink?.RequestPracticeSkip();}
        else SkipPractice(slot);
    }
    void Diagnostic()
    {
        string row="gate="+Gate+" fight="+Fighting+" paused="+paused+" view="+(!RequireView||ViewLink!=null&&ViewLink.PeerReady);
        for(int i=0;i<2;i++) row+=" p"+(i+1)+"="+Practices[i].Step+":"+Practices[i].Hits+":"+Practices[i].Phase+":"+Receivers[i].LiveReady+":"+Receivers[i].InputPhase+":gen"+generations[i];
        row+=" present1="+Receivers[0].PresenceReady+" present2="+Receivers[1].PresenceReady;
        for(int i=0;i<2;i++) row+=" freshness"+(i+1)+"="+Receivers[i].InputFreshnessReason;
        double now=Time.realtimeSinceStartupAsDouble;
        if(row!=lastDiagnostic &&now-lastDiagnosticTime>.25 || now-lastDiagnosticTime>=2)
        { Debug.Log("[REDBOOL PVP GATE] "+row+FormattableString.Invariant($" round={Round} remaining={Remaining:F2} hp1={Players[0].GetComponent<Health>().CurrentHealth} hp2={Players[1].GetComponent<Health>().CurrentHealth} age1_ms={Receivers[0].InputAgeMs:F1} age2_ms={Receivers[1].InputAgeMs:F1} source1_ms={Receivers[0].SourceAgeMs:F1} source2_ms={Receivers[1].SourceAgeMs:F1} lease1_ms={Receivers[0].LeaseRemainingMs:F1} lease2_ms={Receivers[1].LeaseRemainingMs:F1} uncertainty1_ms={Receivers[0].ClockUncertaintyMs:F1} uncertainty2_ms={Receivers[1].ClockUncertaintyMs:F1}")+" utc="+DateTime.UtcNow.ToString("o"));lastDiagnostic=row;lastDiagnosticTime=now; }
    }
    // 대전 중(gate 0) 안내 줄은 라운드가 시작된 뒤 3초만 보여 준다. 그 밖의 단계(준비·대기·수련)는 그대로 계속 보여 준다.
    string Banner(byte gate,float countdown)
    {
        if(gate!=0) {fightHintUntil=-1;return GateText(gate,countdown);}
        double now=Time.realtimeSinceStartupAsDouble;
        if(fightHintUntil<0) fightHintUntil=now+3;
        return now<fightHintUntil?GateText(gate,countdown):"";
    }
    static string GateText(byte gate,float countdown) => gate==1?"전투 효과 준비 중":gate==2?"2P 게임 화면 연결 대기 · 연결 복구 후 자동 재개":gate==3?"1P 카메라 신호 대기 · 어깨와 팔이 보이면 자동 재개 · 첫 보정/새 사람은 C":gate==4?"2P 카메라 신호 대기 · 어깨와 팔이 보이면 자동 재개 · 첫 보정/새 사람은 C":gate==5?"각자 안내 동작 두 번 · 어려운 동작은 건너뛰어도 기본 인식으로 플레이할 수 있어요":gate==6?"신호 확인 · 잠시 후 자동 시작":"대전 시작 · 양손을 올려 막기 · 가슴 앞 X자로 필살기";
    // Resolve after the contact world: simultaneous KO must not become a 1P win.
    void LateUpdate()
    {
        if(!configured || IsClient) return;
        if(!Resolved && Fighting)
        {
            var a=Players[0].GetComponent<Health>();var b=Players[1].GetComponent<Health>();
            if(a.IsDead || b.IsDead || Remaining<=0)
            {
                Result=a.CurrentHealth==b.CurrentHealth?"무승부":a.CurrentHealth>b.CurrentHealth?"1P 승리":"2P 승리";
                Resolved=true;SetPaused(true);
                Debug.Log(FormattableString.Invariant($"[REDBOOL PVP ROUND END] round={Round} reason={(a.IsDead||b.IsDead?"KO":"TIMEOUT")} remaining={Remaining:F2} hp1={a.CurrentHealth} hp2={b.CurrentHealth} utc={DateTime.UtcNow:o}"));
            }
        }
        if(paused && !Resolved) Time.timeScale=0;
    }
    public void Rematch()
    {
        if(!configured || !Resolved || IsClient) return;
        Round++;
        fullPreparation=true;interruptionSince=-1;
        Resolved=false;Fighting=false;Remaining=120;Result="";readySince=-1;
        for(int i=0;i<2;i++)
        {
            var p=Players[i];p.GetComponent<SpecialMove>()?.CancelForPause();p.GetComponent<SpecialMove>()?.ResetCharge();p.GetComponent<EnemySpecial>()?.ResetForRound();
            p.GetComponent<Health>().ResetHealth();p.GetComponent<Fighter>().InputLocked=false;
            p.transform.position=positions[i];var body=p.GetComponent<Rigidbody2D>();if(body) {body.position=positions[i];body.linearVelocity=Vector2.zero;}
            p.GetComponent<Fighter>().Contacts.ResetHistory();
        }
        SetPaused(true);
        Debug.Log($"[REDBOOL PVP REMATCH] round={Round} utc={DateTime.UtcNow:o}");
    }
    public void Reselect()
    {
        if(!configured || !Resolved || IsClient || Lobby==null) return;
        Rematch();Lobby.Reopen();
    }
    void BuildUi()
    {
        UIFactory.EnsureEventSystem();canvas=UIFactory.CreateCanvas("REDBOOL PvP",transform,210);
        for(int i=0;i<2;i++)
        {
            float anchor=i==0?0:1;float x=i==0?35:-35;
            int slot=i;
            var skip=UIFactory.CreateButton("SkipPractice"+i,canvas.transform,"이 동작 건너뛰기 · 기본 인식 유지",19,new Color(.12f,.17f,.20f,.95f),()=>SkipFromUi(slot));
            UIFactory.Place(skip.GetComponent<RectTransform>(),new Vector2(i==0?.25f:.75f,1),new Vector2(.5f,1),new Vector2(0,-300),new Vector2(480,45));skipButtons[i]=skip;
            var panel=UIFactory.CreateImage("PlayerPanel"+i,canvas.transform,new Color(.02f,.04f,.06f,.85f));
            playerPanels[i]=panel.rectTransform;
            UIFactory.Place(panel.rectTransform,new Vector2(anchor,1),new Vector2(anchor,1),new Vector2(x,-30),new Vector2(540,260));
            playerLabels[i]=WuxiaTheme.Text("Health"+i,panel.transform,"",28,TextAnchor.MiddleCenter,i==0?WuxiaTheme.Gold:new Color(.65f,.8f,1));
            UIFactory.Place(playerLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-5),new Vector2(520,36));
            var health=UIFactory.CreateRect("PvpHealthBar"+i,panel.transform);
            UIFactory.Place(health,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-45),new Vector2(500,38));
            healthBars[i]=health.gameObject.AddComponent<WuxiaCombatBar>();healthBars[i].mirror=i==1;healthBars[i].raycastTarget=false;
            var gauge=UIFactory.CreateRect("PvpSpecialGauge"+i,panel.transform);
            UIFactory.Place(gauge,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-88),new Vector2(475,24));
            chargeBars[i]=gauge.gameObject.AddComponent<WuxiaCombatBar>();chargeBars[i].resource=true;chargeBars[i].mirror=i==1;chargeBars[i].raycastTarget=false;
            chargeLabels[i]=WuxiaTheme.Text("PvpSpecialLabel"+i,panel.transform,"파운장 충전 중",21,TextAnchor.MiddleCenter,new Color(.66f,.93f,1f));
            UIFactory.Place(chargeLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-118),new Vector2(500,32));
            var guide=UIFactory.CreateRect("MotionGuide"+i,canvas.transform);
            UIFactory.Place(guide,new Vector2(i==0?.25f:.75f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(220,240));
            diagrams[i]=guide.gameObject.AddComponent<TrainingPoseGraphic>();
            practiceLabels[i]=WuxiaTheme.Text("Practice"+i,panel.transform,"",21,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
            UIFactory.Place(practiceLabels[i].rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,10),new Vector2(520,75));
        }
        clockLabel=WuxiaTheme.Text("RoundClock",canvas.transform,"120",40,TextAnchor.UpperCenter,WuxiaTheme.Gold);
        UIFactory.Place(clockLabel.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-35),new Vector2(200,60));
        banner=WuxiaTheme.Text("Status",canvas.transform,"",27,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        UIFactory.Place(banner.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,25),new Vector2(1500,100));
        var result=UIFactory.CreateImage("PvpRoundResult",canvas.transform,new Color(.02f,.04f,.06f,.94f));
        resultPanel=result.gameObject;
        UIFactory.Place(result.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(780,270));
        roundResult=WuxiaTheme.Text("RoundResultText",result.transform,"",36,TextAnchor.MiddleCenter,WuxiaTheme.Gold);
        UIFactory.Place(roundResult.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-20),new Vector2(750,155));
        rematchButton=UIFactory.CreateButton("PvpRematch",result.transform,"다시 대전 · R",25,new Color(.14f,.20f,.23f,1),Rematch);
        UIFactory.Place(rematchButton.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,25),new Vector2(360,55));
        // 인물 다시 고르기(F8)도 결과 판의 단추로 둔다. 아래 안내 줄에 같은 내용을 또 쓰지 않기 위해서다.
        reselectButton=UIFactory.CreateButton("PvpReselect",result.transform,"인물 다시 고르기 · F8",25,new Color(.14f,.20f,.23f,1),Reselect);
        UIFactory.Place(reselectButton.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(190,25),new Vector2(360,55));
        reselectButton.gameObject.SetActive(false);
        resultPanel.SetActive(false);
    }
    void RefreshUi()
    {
        if(banner==null) return;
        for(int i=0;i<2;i++)
        {
            diagrams[i].gameObject.SetActive(!Practices[i].Complete);diagrams[i].step=Practices[i].Step;
            skipButtons[i].gameObject.SetActive(!Practices[i].Complete);
            var hp=Players[i].GetComponent<Health>();var special=Players[i].GetComponent<SpecialMove>();
            ShowHealth(i,hp.CurrentHealth,hp.MaxHealth);ShowPracticeVisibility(i,!(Fighting&&!paused || Resolved));
            var own=Players[i].GetComponent<EnemySpecial>();bool mine=ownSpecial[i] && own!=null;
            ShowCharge(i,mine?(own.IsPlaying?own.HitsToCharge:own.Charge):special?special.Charge:0,mine?own.HitsToCharge:special?special.HitsToCharge:5,mine?own.IsPlaying:special!=null && special.IsPlaying);
            practiceLabels[i].text=(!Receivers[i].LiveReady?"카메라 연결 · C로 중앙 가드 보정\n":"")+Practices[i].Instruction+"\n"+(Receivers[i].PersonalStatus.Contains("PERSONAL RANGE")?"개인 동작 범위 적용":Receivers[i].PersonalStatus.Contains("CYCLES")?"동작과 복귀를 확인 중":"기본 동작 인식 사용");
        }
        clockLabel.text=Mathf.CeilToInt(Remaining).ToString();
        banner.text=Resolved?"":Banner(Gate,Countdown);   // 결과는 가운데 판에만
        LobbyUi();
        if(!string.IsNullOrEmpty(ViewLink?.ConnectionIssue)) banner.text=ViewLink.ConnectionIssue;
        ShowResult(Resolved,Result,Remaining,Players[0].GetComponent<Health>().CurrentHealth,Players[1].GetComponent<Health>().CurrentHealth,false);
    }
    void ShowResult(bool ended,string winner,float time,int hp1,int hp2,bool remote)
    {
        resultPanel.SetActive(ended);if(!ended)return;
        roundResult.text=(hp1<=0||hp2<=0?"K.O. · 라운드 종료":"시간 종료 · 라운드 종료")+"\n"+winner+(remote?"\n1P 화면에서 다시 대전을 선택하세요":"");
        rematchButton.gameObject.SetActive(!remote);
        bool reselect=!remote && Lobby!=null;
        reselectButton.gameObject.SetActive(reselect);
        rematchButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(reselect?-190:0,25);
    }
    void ShowPracticeVisibility(int slot,bool show) {practiceLabels[slot].gameObject.SetActive(show);playerPanels[slot].SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,show?260:155);}
    void ShowHealth(int slot,int current,int maximum)
    {
        float value=maximum>0?Mathf.Clamp01((float)current/maximum):0;
        if(value<healthTarget[slot]) healthTrailUntil[slot]=Time.unscaledTime+.35f;
        else if(value>healthTarget[slot]) healthTrail[slot]=value;
        healthTarget[slot]=value;
        if(Time.unscaledTime>=healthTrailUntil[slot]) healthTrail[slot]=Mathf.MoveTowards(healthTrail[slot],value,1.5f*Time.unscaledDeltaTime);
        healthBars[slot].SetValues(value,healthTrail[slot]);
        playerLabels[slot].text=Lobby==null?(slot+1)+"P":(slot+1)+"P   "+RedboolPvpRoster.Get(Characters[slot]).name;
        if(StoryHud) hud.ShowExternalFighter(slot==0,(slot+1)+"P  "+RedboolPvpRoster.Get(Characters[slot]).name,current,maximum);
    }
    void ShowCharge(int slot,int value,int maximum,bool firing=false)
    {
        bool ready=maximum>0 &&value>=maximum;
        chargeBars[slot].fillColor=ready?new Color(.32f,.81f,1f):new Color(.17f,.59f,.77f);
        chargeBars[slot].SetValues(maximum>0?(float)value/maximum:0,0,ready);
        string move=SpecialName(slot);
        chargeLabels[slot].text=firing?move+" 발동":ready?move+" 준비 · 가슴 앞 X자":move+" 충전 중";
        if(StoryHud) hud.ShowExternalGauge(slot==0,move,value,maximum,firing);
    }
    public void ShowRemote(RedboolViewFrame frame,bool fresh)
    {
        if(banner==null) return;
        bool baseline=!fresh || !remoteFresh || frame==null || frame.session!=remoteSession || frame.world.round!=remoteRound;
        if(baseline) for(int i=0;i<2;i++) {
            Players[i].GetComponent<EnemySpecial>()?.StopRemote();seenSpecial[i]=-1;specialActing[i]=false;
        }
        remoteFresh=fresh;
        if(frame!=null) {remoteSession=frame.session;remoteRound=frame.world.round;}
        if(!string.IsNullOrEmpty(ViewLink?.ConnectionIssue)) {banner.text=ViewLink.ConnectionIssue;resultPanel.SetActive(false);return;}
        if(frame==null) { banner.text="호스트 연결 대기 · "+HostAddress+" · 방이 열려 있는지 확인하세요 · Esc: 메뉴";resultPanel.SetActive(false);Lobby?.ShowRemote(null);return; }
        paused=frame.world.paused;
        Remaining=frame.world.remaining;Resolved=frame.world.resolved;Fighting=frame.world.fighting;Round=frame.world.round;
        for(int i=0;i<2;i++)
        {
            var a=frame.actors[i];diagrams[i].gameObject.SetActive(a.step<8);diagrams[i].step=a.step;skipButtons[i].gameObject.SetActive(i==1 &&a.step<8);
            ShowHealth(i,a.hp,a.maxHp);ShowPracticeVisibility(i,!(frame.world.fighting&&!frame.world.paused || frame.world.resolved));
            ShowCharge(i,a.charge,a.maxCharge,(a.specialFlags&1)!=0);
            practiceLabels[i].text=(!a.live?"카메라 연결 · C로 중앙 가드 보정\n":"")+RedboolPractice.Describe(a.step,a.hits)+"\n"+(a.personal==2?"개인 동작 범위 적용":a.personal==1?"동작과 복귀를 확인 중":"기본 동작 인식 사용");
        }
        clockLabel.text=Mathf.CeilToInt(Remaining).ToString();
        banner.text=!fresh?"화면 연결이 끊겼습니다 · 전투 일시 정지 · 재연결 중":Resolved?"":Banner(frame.world.gate,frame.world.countdown);
        ShowResult(fresh&&Resolved,frame.world.winner==0?"무승부":frame.world.winner+"P 승리",Remaining,frame.actors[0].hp,frame.actors[1].hp,true);
        // 호스트에서 정해진 인물을 이 화면의 두 사람에게도 입힌다.
        for(int i=0;i<2;i++) if(frame.actors[i].character!=Characters[i]) ApplyCharacter(i,frame.actors[i].character);
        // Clear BOTH old presentations before either new one captures lighting.
        // Otherwise slot 1's idle fade could overwrite a new slot 0 cut-in.
        for(int i=0;i<2;i++)
            if(!fresh || (frame.actors[i].specialFlags&1)==0 || frame.world.paused || frame.world.resolved)
                Players[i].GetComponent<EnemySpecial>()?.StopRemote();
        // 호스트에서 인물 고유 필살기가 시작됐으면 이 화면에서도 같은 연출을 시작한다.
        for(int i=0;i<2;i++)
        {
            var a=frame.actors[i];var own=Players[i].GetComponent<EnemySpecial>();
            if(!ownSpecial[i] || own==null) {seenSpecial[i]=a.specialSerial;continue;}
            bool playing=(a.specialFlags&1)!=0;
            if(seenSpecial[i]<0) seenSpecial[i]=a.specialSerial;
            else if(seenSpecial[i]!=a.specialSerial) {seenSpecial[i]=a.specialSerial;if(fresh && playing && !frame.world.paused && !frame.world.resolved) own.PlayRemote();}
            if((a.specialFlags&2)!=0) own.RemoteGuard((a.specialFlags&4)!=0);
            // 진단용(캡처 폴더를 준 실행에서만): 기술이 나가는 순간의 손님 화면을 남긴다.
            bool acting=own.CurrentPhase==EnemySpecial.Phase.Acting;
            if(acting && !specialActing[i]) Lobby?.CaptureAfter("special_"+Characters[i].ToString("D2")+"_"+(i+1)+"p",.45f);
            specialActing[i]=acting;
        }
        if(Lobby!=null) {Lobby.ShowRemote(frame);LobbyUi();}
        if(!fresh) banner.text="화면 연결이 끊겼습니다 · 전투 일시 정지 · 재연결 중";
    }
    // 대기실을 쓰는 흐름의 수련 화면 배치: "나의 동작" 그림을 설명 카드의 "게임 속 동작"과 한 쌍으로 가운데에 두고,
    // 뒤에 어두운 판을 깔아 뒤의 인물과 겹쳐도 읽히게 한다. 건너뛰기 단추는 그 판 바로 위에 붙인다.
    void LobbyLayout()
    {
        for(int i=0;i<2;i++)
        {
            var half=new Vector2(i==0?.25f:.75f,.5f);
            var back=WuxiaTheme.Ornament("MotionBack"+i,canvas.transform,WuxiaGraphic.Shape.Plaque,new Color(.03f,.045f,.055f,.80f));back.raycastTarget=false;
            UIFactory.Place(back.rectTransform,half,new Vector2(.5f,.5f),new Vector2(0,18),new Vector2(620,286));
            back.transform.SetSiblingIndex(0);poseBacks[i]=back.gameObject;back.gameObject.SetActive(false);
            diagrams[i].rectTransform.anchoredPosition=new Vector2(-131,0);
            var skip=skipButtons[i].GetComponent<RectTransform>();
            skip.anchorMin=skip.anchorMax=half;skip.pivot=new Vector2(.5f,0);skip.anchoredPosition=new Vector2(0,166);skip.sizeDelta=new Vector2(620,40);
        }
    }
    // 대기실·인물 선택 중에는 수련 그림과 건너뛰기를 감추고, 아래 안내 줄을 그 단계의 글로 바꾼다.
    void LobbyUi()
    {
        bool choosing=Lobby!=null && !Lobby.Complete;
        // 고르는 화면 뒤로 체력 막대와 시계가 비치지 않게 한다. 대기실을 쓰는 흐름에서는 예전 패널 대신 스토리 HUD 를 쓴다.
        for(int i=0;i<2;i++) playerPanels[i].gameObject.SetActive(!choosing && !StoryHud);
        clockLabel.gameObject.SetActive(!choosing && !StoryHud);
        if(StoryHud) {hud.SetExternalVisible(!choosing);hud.ShowExternalClock(Remaining);}
        if(choosing)
        {
            for(int i=0;i<2;i++) {diagrams[i].gameObject.SetActive(false);skipButtons[i].gameObject.SetActive(false);}
            banner.text=Lobby.BannerText??"";
        }
        for(int i=0;i<2;i++) if(poseBacks[i]!=null)
        {
            // 수련 그림은 싸우는 동안에도 켜진 채 남을 수 있어(수련을 다 끝내지 않고 넘어간 경우는 없지만) 판은 그림이 보일 때만 켠다.
            bool show=diagrams[i].gameObject.activeSelf && !(Fighting&&!paused || Resolved);
            if(poseBacks[i].activeSelf!=show) poseBacks[i].SetActive(show);
        }
    }
    /// <summary>
    /// 고른 인물의 겉모습과 필살기를 입힌다. 조작·체력·일반 공격은 그대로이고 겉모습, 그 인물의 동작, 필살기가 바뀐다.
    /// 호스트는 인물 선택이 끝날 때, 손님은 호스트가 알려 준 번호가 바뀔 때 부른다.
    /// </summary>
    public void ApplyCharacter(int slot,int index)
    {
        if(!configured || slot<0 || slot>1) return;
        index=RedboolPvpRoster.Clamp(index);
        var view=Players[slot].GetComponent<Fighter3DView>();
        var prefab=RedboolPvpRoster.Visual(index);
        if(prefab==null) prefab=heroTemplate;   // 주인공
        // 주인공이 이미 주인공 모습이면 그대로 둔다. 그 밖에는 새로 입혀서 그 인물 본래의 동작 세트를 쓰게 한다.
        bool keep=prefab==null || view==null || index==RedboolPvpRoster.Hero && view.visualRoot!=null && view.visualRoot.name==prefab.name;
        if(!keep)
        {
            if(!view.ApplyVisual(prefab)) {Debug.LogWarning("[REDBOOL PVP] character visual unavailable: "+RedboolPvpRoster.Get(index).name);return;}
            Players[slot].GetComponent<Fighter>().Contacts.ResetHistory();
            ViewLink?.Rebind(slot);
        }
        Characters[slot]=index;
        ConfigureSpecial(slot);
        Debug.Log("[REDBOOL PVP] "+(slot+1)+"P character: "+RedboolPvpRoster.Get(index).name+" special: "+SpecialName(slot));
    }
    // 주인공은 파운장, 적 인물은 그 인물의 기술. 기술 재료를 찾지 못하면 파운장을 그대로 쓴다.
    void ConfigureSpecial(int slot)
    {
        var p=Players[slot];var palm=p.GetComponent<SpecialMove>();var own=p.GetComponent<EnemySpecial>();
        string id=RedboolPvpRoster.Get(Characters[slot]).node;
        bool use=false;
        if(id!=null)
        {
            if(own==null) own=p.gameObject.AddComponent<EnemySpecial>();
            own.enabled=true;
            use=own.ConfigureForPlayer(id,Players[1-slot].GetComponent<Fighter>(),IsClient);
        }
        if(own!=null && !use) {own.CancelForPause();own.enabled=false;}
        if(palm!=null)
        {
            if(use) {palm.CancelForPause();palm.ResetCharge();}
            if(!IsClient) palm.enabled=!use;   // 손님 화면의 파운장은 원래 꺼져 있다(연출은 미러가 그린다)
        }
        ownSpecial[slot]=use;seenSpecial[slot]=-1;
    }
    public void Leave(bool menu)
    {
        Active=false;IsClient=false;RequireView=false;configured=false;SceneManager.sceneLoaded-=SceneLoaded;
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
