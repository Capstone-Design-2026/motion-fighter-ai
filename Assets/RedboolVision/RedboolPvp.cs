using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One authoritative LAN arena, two isolated input leases, no campaign writes.</summary>
[DefaultExecutionOrder(200)]
public sealed class RedboolPvp : MonoBehaviour
{
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
    readonly Vector3[] positions = new Vector3[2];
    readonly int[] generations = { -1,-1 };
    readonly string[] streams = new string[2];
    readonly WuxiaCombatBar[] chargeBars=new WuxiaCombatBar[2];
    readonly Text[] chargeLabels=new Text[2];
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
    public bool CanFight => Fighting && !Resolved && !paused && BothLive && warmup==null && (!RequireView || ViewLink != null && ViewLink.PeerReady);

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
        // Reuse the authored Cheolmun opponent visual; all gameplay stats remain on the cloned player controller.
        var secondVisual=RunState.Instance!=null?RunState.Instance.Config.GetEnemyVisual("gate"):null;
        if(secondVisual!=null && Players[1].GetComponent<Fighter3DView>().ApplyVisual(secondVisual))
            Debug.Log("[REDBOOL PVP] 2P visual: "+secondVisual.name);
        else Debug.LogWarning("[REDBOOL PVP] 2P visual unavailable; original character with UI identification");
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
        if(!IsClient && RedboolEffectPreparation.Enabled) warmup=Receivers[0].gameObject.AddComponent<RedboolRenderWarmup>();
        BuildUi();SetPaused(true);
        ViewLink=gameObject.AddComponent<RedboolViewLink>();ViewLink.Initialize(this);
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
        if(IsClient) { if(Input.GetKeyDown(KeyCode.Escape)) Leave(true);return; }
        if(Input.GetKeyDown(KeyCode.Escape)) { Leave(true);return; }
        if(Resolved && Input.GetKeyDown(KeyCode.R)) Rematch();
        for(int i=0;i<2;i++)
        {
            string stream=Receivers[i].StreamId;
            if(!String.IsNullOrEmpty(stream) && streams[i]!=stream)
            { if(streams[i]!=null) ResetPractice(i);streams[i]=stream; }
            if(Practices[i].Complete) Receivers[i].PracticeAction-=Practices[i].Observe;
        }
        bool ready=BothLive && Practices[0].Complete && Practices[1].Complete && warmup==null && !Resolved && (!RequireView || ViewLink!=null && ViewLink.PeerReady);
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
        if(!configured || IsClient) return;
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
        if(!configured || !Resolved || IsClient) return;
        Round++;
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
            UIFactory.Place(panel.rectTransform,new Vector2(anchor,1),new Vector2(anchor,1),new Vector2(x,-30),new Vector2(540,220));
            playerLabels[i]=WuxiaTheme.Text("Health"+i,panel.transform,"",28,TextAnchor.MiddleCenter,i==0?WuxiaTheme.Gold:new Color(.65f,.8f,1));
            UIFactory.Place(playerLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-8),new Vector2(520,56));
            var gauge=UIFactory.CreateRect("PvpSpecialGauge"+i,panel.transform);
            UIFactory.Place(gauge,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-64),new Vector2(475,24));
            chargeBars[i]=gauge.gameObject.AddComponent<WuxiaCombatBar>();chargeBars[i].resource=true;chargeBars[i].mirror=i==1;chargeBars[i].raycastTarget=false;
            chargeLabels[i]=WuxiaTheme.Text("PvpSpecialLabel"+i,panel.transform,"파운장 충전 중",21,TextAnchor.MiddleCenter,new Color(.66f,.93f,1f));
            UIFactory.Place(chargeLabels[i].rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-93),new Vector2(500,32));
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
    }
    void RefreshUi()
    {
        if(banner==null) return;
        for(int i=0;i<2;i++)
        {
            diagrams[i].gameObject.SetActive(!Practices[i].Complete);diagrams[i].step=Practices[i].Step;
            var hp=Players[i].GetComponent<Health>();var special=Players[i].GetComponent<SpecialMove>();
            playerLabels[i].text=(i+1)+"P   체력 "+hp.CurrentHealth+" / "+hp.MaxHealth;
            ShowCharge(i,special?special.Charge:0,special?special.HitsToCharge:5);
            practiceLabels[i].text=(!Receivers[i].LiveReady?"카메라 연결 · C로 중앙 가드 보정\n":"")+Practices[i].Instruction+"\n"+(Receivers[i].PersonalStatus.Contains("PERSONAL RANGE")?"v26 + 개인 동작 범위 적용":Receivers[i].PersonalStatus.Contains("CYCLES")?"동작과 복귀를 확인 중":"v26 기본 인식 기준 사용");
        }
        clockLabel.text=Mathf.CeilToInt(Remaining).ToString();
        banner.text=RequireView && (ViewLink==null || !ViewLink.PeerReady)?"2P 게임 화면 연결 대기 · 연결이 끊기면 양쪽 전투가 멈춥니다":Resolved?Result+" · R: 재대전 · Esc: 메뉴":!BothLive?"두 카메라를 기다립니다 · P1 :5065 / P2 :5066\n연결이 끊기거나 보정 중이면 양쪽 전투가 멈춥니다":!Practices[0].Complete||!Practices[1].Complete?"각자 안내 동작을 두 번씩 한 뒤 팔과 몸을 중앙으로 돌려주세요":paused?"두 사람 모두 중앙 가드 · 1초 뒤 대전 시작":"2인 대전 · 블락은 자세를 유지하는 동안 · X자: 파운장 · Esc: 메뉴";
    }
    void ShowCharge(int slot,int value,int maximum)
    {
        bool ready=maximum>0 &&value>=maximum;
        chargeBars[slot].fillColor=ready?new Color(.32f,.81f,1f):new Color(.17f,.59f,.77f);
        chargeBars[slot].SetValues(maximum>0?(float)value/maximum:0,0,ready);
        chargeLabels[slot].text=ready?"파운장 준비 · 가슴 앞 X자":"파운장 충전 중";
    }
    public void ShowRemote(RedboolViewFrame frame,bool fresh)
    {
        if(banner==null) return;
        if(frame==null) { banner.text="호스트 연결 대기 · "+HostAddress+":5067 · Esc: 메뉴";return; }
        Remaining=frame.world.remaining;Resolved=frame.world.resolved;Fighting=frame.world.fighting;Round=frame.world.round;
        for(int i=0;i<2;i++)
        {
            var a=frame.actors[i];diagrams[i].gameObject.SetActive(a.step<8);diagrams[i].step=a.step;
            playerLabels[i].text=(i+1)+"P   체력 "+a.hp+" / "+a.maxHp;
            ShowCharge(i,a.charge,a.maxCharge);
            practiceLabels[i].text=(!a.live?"카메라 연결 · C로 중앙 가드 보정\n":"")+(a.step>=8?"수련 완료 · 중앙에서 준비":RedboolPractice.Instructions[a.step]+"  "+a.hits+"/2")+"\n"+(a.personal==2?"v26 + 개인 동작 범위 적용":a.personal==1?"동작과 복귀를 확인 중":"v26 기본 인식 기준 사용");
        }
        clockLabel.text=Mathf.CeilToInt(Remaining).ToString();
        banner.text=!fresh?"화면 연결이 끊겼습니다 · 전투 일시 정지 · 재연결 중":Resolved?(frame.world.winner==0?"무승부":frame.world.winner+"P 승리")+" · 호스트에서 R: 재대전 · Esc: 메뉴":frame.world.paused?"수련 · 카메라 보정 및 두 화면 준비 중 · 완료 후 중앙 가드 1초":"2인 대전 · 2P 화면 · 블락 유지 / X자 파운장 · Esc: 메뉴";
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
