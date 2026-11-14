using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>Own-camera preview and calibration controls; no video crosses the LAN.</summary>
public sealed class RedboolCameraPanel : MonoBehaviour
{
    RedboolCameraRuntime cameraRuntime;
    Canvas canvas;RectTransform panel;
    RawImage image;Text heading,hint,placeholder,combatHint;
    Image progress;Button calibrate,reconnect,fold;
    bool compact,tutorial;float nextUi;
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
        fold=UIFactory.CreateButton("FoldCamera",panel,"접기",18,WuxiaTheme.Ink,()=> {compact=!compact;cameraRuntime.PreviewVisible=!compact;});
        UIFactory.Place(fold.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-10,-8),new Vector2(60,30));
        var display=UIFactory.CreateRect("LocalCameraImage",panel);image=display.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
        UIFactory.Place(display,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-46),new Vector2(288,216));
        placeholder=WuxiaTheme.Text("CameraPlaceholder",display,"카메라 연결 중",20,TextAnchor.MiddleCenter,WuxiaTheme.Paper);UIFactory.Stretch(placeholder.rectTransform);
        hint=WuxiaTheme.Text("CameraHint",panel,"중앙에서 가드를 잡고 보정하세요",18,TextAnchor.MiddleCenter,WuxiaTheme.Paper);
        hint.horizontalOverflow=HorizontalWrapMode.Wrap;hint.verticalOverflow=VerticalWrapMode.Truncate;
        combatHint=WuxiaTheme.Text("CombatInputFeedback",panel,"",18,TextAnchor.MiddleCenter,WuxiaTheme.Gold);
        combatHint.horizontalOverflow=HorizontalWrapMode.Wrap;combatHint.verticalOverflow=VerticalWrapMode.Truncate;
        UIFactory.Place(combatHint.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,116),new Vector2(288,36));
        var track=UIFactory.CreateImage("CalibrationTrack",panel,new Color(.15f,.18f,.18f,1));
        UIFactory.Place(track.rectTransform,new Vector2(0,0),Vector2.zero,new Vector2(14,57),new Vector2(288,5));
        progress=UIFactory.CreateImage("CalibrationProgress",track.transform,WuxiaTheme.Gold);progress.type=Image.Type.Simple;
        calibrate=UIFactory.CreateButton("CameraCalibrate",panel,"보정 · C",20,WuxiaTheme.Ink,()=>cameraRuntime.Calibrate());
        UIFactory.Place(calibrate.GetComponent<RectTransform>(),Vector2.zero,Vector2.zero,new Vector2(14,12),new Vector2(136,34));WuxiaTheme.StyleButton(calibrate);
        reconnect=UIFactory.CreateButton("CameraReconnect",panel,"다시 연결",18,WuxiaTheme.Ink,()=>cameraRuntime.Reconnect());
        UIFactory.Place(reconnect.GetComponent<RectTransform>(),new Vector2(1,0),new Vector2(1,0),new Vector2(-14,12),new Vector2(136,34));WuxiaTheme.StyleButton(reconnect);
    }
    void Update()
    {
        if(cameraRuntime==null || canvas==null)return;
        canvas.enabled=cameraRuntime.ShowPanel;
        if(!canvas.enabled || Time.unscaledTime<nextUi)return;
        nextUi=Time.unscaledTime+.1f;
        int scene=SceneManager.GetActiveScene().handle;
        if(scene!=cachedScene) {cachedScene=scene;tutorial=FindAnyObjectByType<CaveTutorial>()!=null;}
        UIFactory.Place(panel,tutorial?Vector2.one:new Vector2(1,0),tutorial?Vector2.one:new Vector2(1,0),
            tutorial?new Vector2(-24,-210):new Vector2(-24,24),new Vector2(316,compact?199:420));
        image.gameObject.SetActive(!compact);
        // Preview is one persistent texture: LoadImage refreshes its pixels at
        // the preview cadence even though this status/layout UI runs at10Hz.
        image.texture=cameraRuntime.PreviewFresh?cameraRuntime.Preview:null;
        image.color=image.texture!=null?Color.white:new Color(.08f,.1f,.11f,1);
        placeholder.gameObject.SetActive(image.texture==null);
        placeholder.text=cameraRuntime.Running?"카메라 연결 중":"카메라 연결 대기";
        fold.GetComponentInChildren<Text>().text=compact?"펼치기":"접기";
        heading.text=cameraRuntime.Synthetic?"카메라 연결 검사":RedboolPvp.Active?(RedboolPvp.IsClient?"2P · 내 동작":"1P · 내 동작"):"내 동작";
        UIFactory.Place(hint.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,68),new Vector2(288,44));
        hint.text=Explain(cameraRuntime);
        var state=cameraRuntime.Status;
        DisplayedFeedback=LocalFeedback();
        combatHint.text=RedboolCombatFeedback.Text(DisplayedFeedback);
        float amount=state!=null?Mathf.Clamp01(state.calibration_progress):0;
        if(float.IsNaN(amount)||float.IsInfinity(amount))amount=0;
        progress.rectTransform.anchorMin=Vector2.zero;progress.rectTransform.anchorMax=new Vector2(amount,1);
        progress.rectTransform.offsetMin=progress.rectTransform.offsetMax=Vector2.zero;
        calibrate.interactable=cameraRuntime.Running&&cameraRuntime.Fresh&&(cameraRuntime.Synthetic||(state!=null&&state.camera_connected));
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
        if(s.camera_slow)return "카메라 영상이 느리게 들어와요\n밝은 조명에서 다시 연결해보세요";
        if((!s.calibrated&&s.calibration_reason=="STALE_FRAME")||s.tracking=="STALE")
            return "영상 처리 지연을 줄이고 있어요\nC 반복 없이 잠시 기다려주세요";
        if(s.framing!=null&&s.framing.code=="OUTSIDE")
        {
            var edges=s.framing.outside_edges;
            if(edges!=null&&System.Array.IndexOf(edges,"BOTTOM")>=0)return "손목이 화면 아래에 있어요\n손을 들거나 카메라 각도를 조절하세요";
            return "양쪽 팔꿈치와 손목이 화면에\n들어오도록 거리와 각도를 맞추세요";
        }
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
        return "준비됐어요 · 사람이 바뀌면 C로 재보정";
    }
}
