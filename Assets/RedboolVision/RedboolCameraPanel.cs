using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>Own-camera preview and calibration controls; no video crosses the LAN.</summary>
[DefaultExecutionOrder(500)] // Apply the displayed lobby phase after the guest view updates.
public sealed class RedboolCameraPanel : MonoBehaviour
{
    RedboolCameraRuntime cameraRuntime;
    Canvas canvas;RectTransform panel;
    RawImage image;Text heading,hint,placeholder,combatHint,calibrationStatus;
    Image progress;RectTransform track;Button calibrate,reconnect,fold,resize;
    bool folded,tutorial,manualExpanded,wasCalibrated;float nextUi,nextTutorialCheck;
    CaveTutorial cave;
    readonly RedboolCombatPresentation presentation=new RedboolCombatPresentation();
    public bool SmallPreview { get; private set; }
    public bool DetailedGuidance { get; private set; }
    int cachedScene=-1;
    public RedboolCombatFeedbackCode DisplayedFeedback { get; private set; }
    public string DisplayedFeedbackText => combatHint!=null?combatHint.text:"";
    void Start()
    {
        cameraRuntime=GetComponent<RedboolCameraRuntime>();
        canvas=UIFactory.CreateCanvas("MyCameraPanel",transform,250);
        var background=UIFactory.CreateImage("CameraFrame",canvas.transform,new Color(.035f,.045f,.05f,.94f),true);
        panel=background.rectTransform;
        var border=background.gameObject.AddComponent<Outline>();border.effectColor=new Color(.65f,.51f,.28f,.8f);border.effectDistance=new Vector2(1,-1);
        heading=WuxiaTheme.Text("CameraHeading",panel,"내 동작",22,TextAnchor.MiddleLeft,WuxiaTheme.Gold);
        UIFactory.Place(heading.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(14,-8),new Vector2(210,30));
        calibrationStatus=WuxiaTheme.Text("CalibrationStatus",panel,"",15,TextAnchor.MiddleLeft,WuxiaTheme.Paper);
        calibrationStatus.horizontalOverflow=HorizontalWrapMode.Overflow;calibrationStatus.raycastTarget=false;
        fold=UIFactory.CreateButton("FoldCamera",panel,"접기",18,WuxiaTheme.Ink,()=> {folded=!folded;cameraRuntime.PreviewVisible=!folded;});
        UIFactory.Place(fold.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-10,-8),new Vector2(60,30));
        resize=UIFactory.CreateButton("ResizeCamera",panel,"크게",18,WuxiaTheme.Ink,()=>manualExpanded=!manualExpanded);
        var display=UIFactory.CreateRect("LocalCameraImage",panel);image=display.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
        UIFactory.Place(display,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-46),new Vector2(288,216));
        placeholder=WuxiaTheme.Text("CameraPlaceholder",display,"카메라 연결 중",20,TextAnchor.MiddleCenter,WuxiaTheme.Paper);UIFactory.Stretch(placeholder.rectTransform);
        hint=WuxiaTheme.Text("CameraHint",panel,"중앙에서 가드를 잡고 보정하세요",18,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        hint.horizontalOverflow=HorizontalWrapMode.Wrap;hint.verticalOverflow=VerticalWrapMode.Truncate;
        combatHint=WuxiaTheme.Text("CombatInputFeedback",panel,"",18,TextAnchor.MiddleCenter,WuxiaTheme.Gold);
        combatHint.horizontalOverflow=HorizontalWrapMode.Wrap;combatHint.verticalOverflow=VerticalWrapMode.Truncate;
        UIFactory.Place(combatHint.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,116),new Vector2(288,52));
        var trackImage=UIFactory.CreateImage("CalibrationTrack",panel,new Color(.15f,.18f,.18f,1));track=trackImage.rectTransform;
        UIFactory.Place(track,new Vector2(0,0),Vector2.zero,new Vector2(14,57),new Vector2(288,5));
        progress=UIFactory.CreateImage("CalibrationProgress",track,WuxiaTheme.Gold);progress.type=Image.Type.Simple;
        calibrate=UIFactory.CreateButton("CameraCalibrate",panel,"보정 · C",20,WuxiaTheme.Ink,()=>cameraRuntime.Calibrate());
        UIFactory.Place(calibrate.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(14,12),new Vector2(136,34));
        WuxiaTheme.StyleButton(calibrate);
        reconnect=UIFactory.CreateButton("CameraReconnect",panel,"다시 연결",18,WuxiaTheme.Ink,()=>cameraRuntime.Reconnect());
        UIFactory.Place(reconnect.GetComponent<RectTransform>(),new Vector2(1,0),new Vector2(1,0),new Vector2(-14,12),new Vector2(136,34));
        WuxiaTheme.StyleButton(reconnect);
    }
    void LateUpdate()
    {
        if(cameraRuntime==null || canvas==null)return;
        canvas.enabled=cameraRuntime.ShowPanel;
        if(!canvas.enabled)return;
        if(Time.unscaledTime<nextUi && panel.gameObject.activeSelf != SelectingCharacter())return;
        RefreshDisplay();
    }
    static bool SelectingCharacter()
    {
        var game = RedboolPvp.Instance;
        return RedboolPvp.Active && game != null && game.Lobby != null
            && game.Lobby.Current == RedboolPvpLobby.Stage.Select;
    }
    // Runs at 10Hz and immediately when selection opens or closes.
    void RefreshDisplay()
    {
        nextUi=Time.unscaledTime+.1f;
        int scene=SceneManager.GetActiveScene().handle;
        if(scene!=cachedScene)
        {cachedScene=scene;cave=null;nextTutorialCheck=0;manualExpanded=false;presentation.Reset();}
        if(cave==null&&Time.unscaledTime>=nextTutorialCheck)
        {cave=FindAnyObjectByType<CaveTutorial>();nextTutorialCheck=Time.unscaledTime+.5f;}
        bool pvpPractice=LocalPracticeActive();
        var pvp=RedboolPvp.Instance;
        bool newPvpCards=pvpPractice&&pvp!=null&&pvp.Lobby!=null;
        bool selecting = SelectingCharacter();
        if (panel.gameObject.activeSelf == selecting) panel.gameObject.SetActive(!selecting);
        if (selecting)
        {
            pvp.Lobby.ShowLocalSelectionPreview(!folded && cameraRuntime.PreviewFresh ? cameraRuntime.Preview : null);
            return;
        }
        tutorial=cave!=null&&cave.isActiveAndEnabled||pvpPractice;
        // New PvP cards already explain each motion in detail. Keep the own-camera
        // image compact between the two pose diagrams rather than covering a card.
        DetailedGuidance=tutorial&&!newPvpCards||RedboolDemoSettings.OperatorMode;
        var state=cameraRuntime.Status;
        bool calibrated=state!=null&&state.calibrated;
        if(wasCalibrated&&!calibrated)
        {manualExpanded=false;folded=false;cameraRuntime.PreviewVisible=true;presentation.Reset();}
        wasCalibrated=calibrated;
        SmallPreview=calibrated&&!DetailedGuidance&&!manualExpanded;
        bool attention=NeedsAttention(cameraRuntime);
        string guidance=!SmallPreview?Explain(cameraRuntime):attention?CompactGuidance(cameraRuntime):"";
        float width=SmallPreview?240:316,inner=width-28;
        float previewHeight=SmallPreview?159:216;
        float footer=SmallPreview?(string.IsNullOrEmpty(guidance)?52:104):174;
        float height=folded?footer+70:previewHeight+70+footer;
        bool top=cave!=null&&cave.isActiveAndEnabled;
        if(newPvpCards)
        {
            float centre=SmallPreview?Mathf.Max(555f,428f+height*.5f):920f-height*.5f;
            UIFactory.Place(panel,new Vector2(.5f,centre/1080f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(width,height));
        }
        else UIFactory.Place(panel,top?Vector2.one:new Vector2(1,0),top?Vector2.one:new Vector2(1,0),
            top?new Vector2(-24,-210):new Vector2(-24,24),new Vector2(width,height));
        UIFactory.Place(heading.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(14,-8),new Vector2(inner-110,30));
        heading.fontSize=SmallPreview?18:22;
        UIFactory.Place(calibrationStatus.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(14,-40),new Vector2(inner,24));
        calibrationStatus.text=CalibrationSummary(state,cameraRuntime.Running&&cameraRuntime.Fresh
            &&string.IsNullOrEmpty(cameraRuntime.Error)&&string.IsNullOrEmpty(cameraRuntime.InputConnectionError));
        UIFactory.Place(fold.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-10,-8),new Vector2(52,30));
        UIFactory.Place(resize.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-66,-8),new Vector2(52,30));
        resize.gameObject.SetActive(calibrated&&!DetailedGuidance);
        resize.GetComponentInChildren<Text>().text=SmallPreview?"크게":"작게";
        image.gameObject.SetActive(!folded);
        UIFactory.Place(image.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-70),new Vector2(inner,previewHeight));
        // The same persistent preview texture and existing adaptive cadence.
        image.texture=cameraRuntime.PreviewFresh?cameraRuntime.Preview:null;
        image.color=image.texture!=null?Color.white:new Color(.08f,.1f,.11f,1);
        placeholder.gameObject.SetActive(image.texture==null);
        placeholder.text=cameraRuntime.Running?"카메라 연결 중":"카메라 연결 대기";
        fold.GetComponentInChildren<Text>().text=folded?"펼치기":"접기";
        heading.text=RedboolPvp.Active?(RedboolPvp.IsClient?"2P · 내 동작":"1P · 내 동작"):"내 동작";
        UIFactory.Place(hint.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,SmallPreview?56:68),new Vector2(inner,44));
        hint.text=guidance;hint.gameObject.SetActive(!string.IsNullOrEmpty(guidance));
        DisplayedFeedback=LocalFeedback();
        combatHint.text=presentation.Present(DisplayedFeedback,DetailedGuidance,Time.unscaledTimeAsDouble);
        UIFactory.Place(combatHint.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,SmallPreview?28:116),new Vector2(inner,SmallPreview?24:52));
        combatHint.gameObject.SetActive(!string.IsNullOrEmpty(combatHint.text));
        track.gameObject.SetActive(!SmallPreview);
        float amount=state!=null?Mathf.Clamp01(state.calibration_progress):0;
        if(float.IsNaN(amount)||float.IsInfinity(amount))amount=0;
        progress.rectTransform.anchorMin=Vector2.zero;progress.rectTransform.anchorMax=new Vector2(amount,1);
        progress.rectTransform.offsetMin=progress.rectTransform.offsetMax=Vector2.zero;
        UIFactory.Place(calibrate.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(14,SmallPreview?4:12),new Vector2(SmallPreview?96:136,SmallPreview?24:34));
        calibrate.GetComponentInChildren<Text>().text=SmallPreview?"재보정 · C":"보정 · C";
        calibrate.GetComponentInChildren<Text>().fontSize=SmallPreview?16:20;
        calibrate.interactable=cameraRuntime.Running&&cameraRuntime.Fresh&&(cameraRuntime.Synthetic||(state!=null&&state.camera_connected));
        reconnect.gameObject.SetActive(!SmallPreview||attention);
        UIFactory.Place(reconnect.GetComponent<RectTransform>(),new Vector2(1,0),new Vector2(1,0),new Vector2(-14,SmallPreview?4:12),new Vector2(SmallPreview?100:136,SmallPreview?24:34));
    }
    bool LocalPracticeActive()
    {
        if(!RedboolPvp.Active)return false;
        var game=RedboolPvp.Instance;
        if(game==null)return false;
        if(game.Lobby!=null&&!game.Lobby.Complete)return false;
        if(!RedboolPvp.IsClient)return !game.Practices[0].Complete;
        var frame=game.ViewLink!=null?game.ViewLink.Latest:null;
        return frame==null||!frame.Complete||frame.actors[1].step<RedboolPractice.Actions.Length;
    }
    static string CompactGuidance(RedboolCameraRuntime runtime)
    {
        if(!string.IsNullOrEmpty(runtime.InputConnectionError))return "다른 게임을 닫아주세요\n자동으로 다시 연결합니다";
        if(!string.IsNullOrEmpty(runtime.Error)||runtime.Status!=null&&!string.IsNullOrEmpty(runtime.Status.error))
            return "카메라 연결 확인\n다시 연결을 눌러주세요";
        if(!runtime.Running)return "카메라 연결 대기\n다시 연결을 눌러주세요";
        if(!runtime.Fresh||runtime.Status==null)return "카메라 응답 대기\n자동으로 연결 확인 중";
        var s=runtime.Status;
        if(!s.camera_connected&&!runtime.Synthetic)return "카메라 연결 대기\n카메라 권한을 확인하세요";
        string physical=CameraInputGuidance(s,true);
        if(physical!=null)return physical;
        if(s.tracking=="STALE")return "영상 처리 지연\n잠시 기다려주세요";
        return "양손과 팔꿈치를\n카메라에 보여주세요";
    }
    // Framing is actionable even when capture is also slow. Do not hide the
    // clipped-elbow explanation behind a general performance warning.
    public static string CameraInputGuidance(RedboolCameraRuntime.CameraStatus state,bool compact)
    {
        if(state==null)return null;
        // Coordinates from stale processing are not a current pose instruction.
        if(state.tracking=="STALE"||!state.calibrated&&state.calibration_reason=="STALE_FRAME")return null;
        var framing=state.framing;
        if(framing!=null&&framing.code=="OUTSIDE")
        {
            var edges=framing.outside_edges;
            if(edges!=null&&System.Array.IndexOf(edges,"BOTTOM")>=0)
                return compact?"조금 뒤로 앉아주세요\n팔꿈치까지 보여주세요":"조금 뒤로 앉아주세요\n팔꿈치 아래까지 보이게 맞추세요";
            return compact?"양팔이 화면 안에\n들어오게 앉아주세요":"양팔이 화면 안에 들어오도록\n거리와 카메라 각도를 맞추세요";
        }
        if(framing!=null&&(framing.code=="UNCERTAIN"||framing.code=="NO_POSE"))
            return compact?"양손과 팔꿈치가\n가리지 않게 해주세요":"양손과 팔꿈치가 가리지 않게\n카메라에 보여주세요";
        if(state.camera_slow)
            return compact?"카메라 영상 지연\n밝기·다른 앱 확인":"카메라 영상이 느리게 들어와요\n밝기와 다른 앱을 확인해주세요";
        return null;
    }
    static bool NeedsAttention(RedboolCameraRuntime runtime)
    {
        if(!runtime.Running||!runtime.Fresh||!string.IsNullOrEmpty(runtime.InputConnectionError)||!string.IsNullOrEmpty(runtime.Error))return true;
        var s=runtime.Status;
        return s==null||!string.IsNullOrEmpty(s.error)||!s.calibrated||(!runtime.Synthetic&&
            (!s.camera_connected||s.camera_slow||s.tracking!="VALID"||s.framing!=null&&(s.framing.code=="OUTSIDE"||s.framing.code=="UNCERTAIN"||s.framing.code=="NO_POSE")));
    }
    RedboolCombatFeedbackCode LocalFeedback()
    {
        // Keep the existing connection/calibration explanation visible. Never
        // leave a previous success beside a stopped or newly calibrated camera.
        var state=cameraRuntime.Status;
        if(!cameraRuntime.Running || !cameraRuntime.Fresh || state==null || !state.calibrated
            || !string.IsNullOrEmpty(cameraRuntime.Error) || !string.IsNullOrEmpty(cameraRuntime.InputConnectionError)
            || !string.IsNullOrEmpty(state.error))return RedboolCombatFeedbackCode.None;
        var game=RedboolPvp.Instance;
        var receiver=RedboolDemoReceiver.ListeningOn(5065);
        return SelectFeedback(RedboolPvp.Active&&RedboolPvp.IsClient,
            game!=null&&game.ViewLink!=null?game.ViewLink.Latest:null,
            game!=null&&game.ViewLink!=null&&game.ViewLink.Fresh,
            receiver!=null?receiver.FeedbackCode:RedboolCombatFeedbackCode.None);
    }
    // Public deterministic seam for native fixtures. P2 never reads a local
    // replica's Fighter/Health or the host's P1 input outcome.
    public static RedboolCombatFeedbackCode SelectFeedback(bool client,RedboolViewFrame frame,bool fresh,RedboolCombatFeedbackCode local)
    {
        if(!client)return local;
        if(!fresh || frame==null || !frame.Complete)return RedboolCombatFeedbackCode.ViewLost;
        if(frame.world.resolved)return RedboolCombatFeedbackCode.RoundOver;
        byte code=frame.actors[1].feedback;
        return RedboolCombatFeedback.Valid(code)?(RedboolCombatFeedbackCode)code:RedboolCombatFeedbackCode.ViewLost;
    }
    public static string CalibrationSummary(RedboolCameraRuntime.CameraStatus state,bool fresh)
    {
        if(state==null)return "카메라 연결 확인 중";
        if(!fresh||!string.IsNullOrEmpty(state.error)||!state.camera_connected)
            return state.calibrated?"보정 완료 · 입력 연결 확인 중":"카메라 연결 확인 중";
        if(!state.calibrated)return state.phase=="calibrating"?"보정 중 · 가드 유지":"보정 대기 · C로 시작";
        if(state.tracking!="VALID")return "보정 완료 · 입력 복구 중";
        if(state.framing!=null&&(state.framing.code=="OUTSIDE"||state.framing.code=="UNCERTAIN"||state.framing.code=="NO_POSE"))return "보정 완료 · 양팔 확인 중";
        return state.ready?"보정 완료 · 동작 준비됨":"보정 완료 · 자세 확인 중";
    }
    public static string Explain(RedboolCameraRuntime runtime)
    {
        if(!string.IsNullOrEmpty(runtime.InputConnectionError))return runtime.InputConnectionError;
        if(runtime.Synthetic)return "연결 검사 화면 · 실제 카메라는 꺼져 있어요";
        if(!string.IsNullOrEmpty(runtime.Error))return runtime.Error;
        if(!runtime.Running)return "다시 연결을 눌러 카메라를 켜주세요";
        var s=runtime.Status;
        if(!runtime.Fresh||s==null)return "카메라 준비 중 · 잠시만 기다려주세요";
        if(!string.IsNullOrEmpty(s.error))return "카메라 연결을 확인하고 다시 연결해주세요";
        if(s.phase=="optimizing_camera")return "카메라 설정을 맞추고 있어요\n처음 연결할 때 잠시 걸릴 수 있어요";
        if(s.phase=="starting"||s.phase=="loading_models")return "동작 인식을 준비하고 있어요\n잠시만 기다려주세요";
        if(!s.camera_connected)return "카메라 연결 중 · 최초 실행이라면\n카메라 사용 권한을 허용해주세요";
        string physical=CameraInputGuidance(s,false);
        if(physical!=null)return physical;
        if((!s.calibrated&&s.calibration_reason=="STALE_FRAME")||s.tracking=="STALE")
            return "영상 처리 지연을 줄이고 있어요\nC 반복 없이 잠시 기다려주세요";
        if(!s.calibrated)
        {
            if(s.calibration_reason=="ARMS_MOVING"||s.calibration_reason=="GUARD_SHIFTED")return "팔을 잠시 멈추면 보정이 이어져요\nC를 다시 누를 필요는 없어요";
            if(s.calibration_reason=="FISTS_LOW")return "주먹을 턱 근처로 올리고\n편안한 가드를 유지하세요";
            if(s.calibration_reason=="FISTS_HIGH")return "주먹을 턱 근처로 조금 내려주세요";
            if(s.calibration_reason=="ARMS_EXTENDED")return "팔꿈치를 굽혀 가드를 잡아주세요";
            if(s.calibration_reason=="FISTS_FAR")return "주먹을 몸 가까이 가져와\n편안한 가드를 잡아주세요";
            if(s.calibration_reason=="JOINT_HISTORY")return "자세를 안정적으로 확인하고 있어요\n중앙 가드를 잠시 유지하세요";
            if(s.calibration_reason=="JOINTS_OUTSIDE")return "어깨와 양팔이 화면 안에\n들어오도록 앉아주세요";
            if(s.calibration_reason=="JOINTS_UNCERTAIN")return "양손과 팔꿈치가 가리지 않게\n카메라에 보여주세요";
            if(s.calibration_progress>0||s.phase=="calibrating")return "가드 유지 중 · C를 다시 누르지 않아도 돼요";
            return "중앙에서 가드를 잡고\n보정 버튼 또는 C를 누르세요";
        }
        if(s.tracking!="VALID")return runtime.InferencePressure ? "처리량을 줄여 인식을 복구 중이에요\n가드를 잠시 유지하세요" : "어깨와 양팔이 보이면 자동으로 이어져요";
        if(s.recording!=null)
        {
            if(s.recording.state=="budget_exhausted")return "플레이 가능 · 관절 기록 용량에 도달했어요\n현재까지 기록은 진단 ZIP에 남아요";
            if(s.recording.state=="error")return "플레이 가능 · 관절 기록 저장을 확인해주세요";
            if(s.recording.queue_dropped_rows>0)return "플레이 가능 · 일부 관절 기록이 빠졌어요\n누락 구간은 진단 ZIP에 표시돼요";
        }
        if(runtime.InferencePressure)return "속도 우선으로 인식 중이에요\n사람이 바뀌면 C로 재보정";
        return s.ready?"동작 준비됨 · 사람이 바뀌면 C로 재보정":"보정은 끝났어요 · 편안한 가드를 잠시 유지하세요";
    }
}
