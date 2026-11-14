// REDBOOL raised-kicks-v3
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Seven responsive practice steps. Vision and explicitly selected keyboard tests stay separate.</summary>
public class CaveTutorial : MonoBehaviour {
 public static readonly string[] Actions={"JAB","STRAIGHT","HIGH_KICK","LOW_KICK","BLOCK","MOVE_LEFT","MOVE_RIGHT","SPECIAL"};
 static readonly string[] Names={"잽","스트레이트","하이킥","로우킥","막기","왼쪽 이동","오른쪽 이동","파운장"};
 static readonly string[] Gestures={"왼손을 앞으로 지르세요","오른손을 앞으로 뻗으세요","왼손을 위로 들어 올리세요","오른손을 위로 들어 올리세요","두 손을 올려 가드 자세를 취하세요","어깨 중심을 왼쪽으로 옮기세요","어깨 중심을 오른쪽으로 옮기세요","가슴 앞에서 팔을 X자로 1초 유지한 뒤 풀어주세요"};
 static readonly string[] Tips={"왼손 주먹이 카메라에 가려지지 않게, 한 번 뻗고 되돌리세요.","왼손은 가드에 두고 오른손만 앞으로 뻗어보세요.","실제로 다리를 차지 않아도 됩니다. 왼손을 어깨보다 높이 들어보세요.","실제로 다리를 차지 않아도 됩니다. 오른손을 어깨보다 높이 들어보세요.","두 손을 얼굴 앞에 올린 뒤 기본 자세로 내려보세요.","몸을 비틀기보다 두 어깨의 중심을 왼쪽으로 옮겨보세요.","두 어깨의 중심을 오른쪽으로 옮긴 뒤 중앙으로 돌아오세요.","양쪽 손목과 팔꿈치가 보이도록 가슴 앞에서 교차하세요. 수련에서는 충전이 필요 없습니다."};
 static CaveTutorial active;
 readonly string teachingRun=System.Guid.NewGuid().ToString("N");
 public static RedboolPracticeContext TeachingContext => active==null || active.keyboard || active.exit ? null : new RedboolPracticeContext {
  run=active.teachingRun,action=Actions[active.CurrentStep],phase=active.IsDemonstrating?"DEMO":active.advancing?"RETURN":"PRACTICE",hits=active.hits};
 RunState run;Transform frame;Text title,instruction,progress,status,modeLabel,inputHint,signal,phaseLabel;TrainingPoseGraphic inputPose,resultPose;
 RectTransform focus;Image flash;bool keyboard,armed,exit,advancing;int hits,mistakes;float lastPacket=-100,nextStepAt,demoUntil,stepStarted,feedbackUntil,successPulse;string lastSignal="";
 public int CurrentStep=>run==null?0:Mathf.Clamp(run.Data.tutorialStep,0,Actions.Length-1);
 public int SuccessfulAttempts=>hits;
 public bool IsDemonstrating=>Time.unscaledTime<demoUntil;
 public bool IsKeyboardMode=>keyboard;
 bool DiscreteVisionStep=>!keyboard&&(CurrentStep<4||CurrentStep==7);
 public static IEnumerator Play(RunState state){
  if(state.Data.tutorialCompleted)yield break;
  if(state.Data.tutorialStep>=Actions.Length){state.CompleteTutorial();yield break;}
  UIFactory.EnsureEventSystem();var canvas=UIFactory.CreateCanvas("CaveTutorialCanvas",null,210);var tutorial=canvas.gameObject.AddComponent<CaveTutorial>();tutorial.run=state;active=tutorial;tutorial.Build();
  RedboolDemoReceiver.TutorialAction+=tutorial.OnVision;
  if(Object.FindAnyObjectByType<RedboolDemoReceiver>()==null){var receiver=new GameObject("TutorialVisionReceiver");receiver.transform.SetParent(canvas.transform);receiver.AddComponent<RedboolDemoReceiver>();}
  try{while(!tutorial.exit)yield return null;}finally{RedboolDemoReceiver.TutorialAction-=tutorial.OnVision;if(canvas!=null)Destroy(canvas.gameObject);}
 }
 void OnDestroy(){if(active==this)active=null;RedboolDemoReceiver.TutorialAction-=OnVision;}
 void Build(){
  var backdrop=UIFactory.CreateImage("BlockMapInput",transform,Color.black,true);UIFactory.Stretch(backdrop.rectTransform);
  var logical=UIFactory.CreateRect("TrainingFrame",transform);logical.gameObject.AddComponent<CinematicFrame>().Fit();frame=logical;
  var rect=UIFactory.CreateRect("CaveBackground",frame);UIFactory.Stretch(rect);var bg=rect.gameObject.AddComponent<RawImage>();bg.texture=Resources.Load<Texture2D>("UI/Wuxia/TrainingCaveMurals");bg.raycastTarget=false;
  var fit=rect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=(float)bg.texture.width/bg.texture.height;
  var dust=UIFactory.CreateRect("CaveDust",frame);UIFactory.Stretch(dust);dust.gameObject.AddComponent<StoryAtmosphere>().cave=true;
  var top=UIFactory.CreateImage("TrainingHeader",frame,new Color(.025f,.03f,.025f,.83f));Box(top.rectTransform,0,920,1920,160);
  title=Label("TrainingStep","",35,390,1000,1140,55);instruction=Label("Instruction","",28,280,939,1360,55);
  var focusRect=UIFactory.CreateRect("MuralFocus",frame);focus=focusRect;var outline=focusRect.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.9f,.71f,.37f,.8f);outline.effectDistance=new Vector2(2,-2);
  var focusImage=focusRect.gameObject.AddComponent<Image>();focusImage.color=new Color(1,.82f,.4f,.045f);focusImage.raycastTarget=false;
  var bottom=UIFactory.CreateImage("TrainingDesk",frame,new Color(.025f,.03f,.025f,.94f));Box(bottom.rectTransform,0,0,1920,320);
  inputPose=Panel("InputPose","나의 동작",100,false);resultPose=Panel("ResultPose","게임 속 동작",1060,true);
  Label("MappingArrow","→",55,900,140,120,85);
  phaseLabel=Label("TrainingPhase","",25,680,350,560,52);
  status=Label("TrainingStatus","",26,180,65,1560,55);progress=Label("TrainingProgress","",22,740,8,440,45);
  inputHint=Label("TrainingInputHint","",20,160,865,1600,45);signal=Label("LiveSignal","",21,450,810,1020,45);
  var mode=Button("TutorialInputMode","키보드 테스트",ToggleInputMode,1640,1010,245);modeLabel=mode.GetComponentInChildren<Text>();
  Button("LeaveTraining","수련 중단",()=>{run.Save();exit=true;},28,1010,180);
  Button("ReplayDemonstration","시범 다시 보기",ReplayDemonstration,1640,340,245);
  var pulse=UIFactory.CreateImage("SuccessPulse",frame,new Color(.84f,1,.66f,0));Box(pulse.rectTransform,0,316,1920,4);flash=pulse;
  RefreshStep();
 }
 static void Box(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);}
 Text Label(string id,string value,int size,float x,float y,float w,float h){var text=WuxiaTheme.Text(id,frame,value,size,TextAnchor.MiddleCenter,WuxiaTheme.Paper);Box(text.rectTransform,x,y,w,h);return text;}
 Button Button(string id,string label,UnityEngine.Events.UnityAction action,float x,float y,float width){var b=UIFactory.CreateButton(id,frame,label,22,WuxiaTheme.Ink,action);Box(b.GetComponent<RectTransform>(),x,y,width,50);return b;}
 TrainingPoseGraphic Panel(string id,string heading,float x,bool result){
  Label(id+"Heading",heading,24,x,260,760,42);var rect=UIFactory.CreateRect(id,frame);Box(rect,x+95,110,160,145);var figure=rect.gameObject.AddComponent<TrainingPoseGraphic>();figure.gameAction=result;
  Label(id+"Explanation",result?"":"",24,x+285,155,470,90);return figure;
 }
 public void ReplayDemonstration(){armed=false;demoUntil=Time.unscaledTime+2.6f;feedbackUntil=0;status.text="먼저 벽화와 아래 시범을 보세요. 곧 따라 할 차례예요.";}
 public void ToggleInputMode(){keyboard=!keyboard;hits=0;armed=false;advancing=false;mistakes=0;modeLabel.text=keyboard?"비전 인식으로 전환":"키보드 테스트";RefreshStep();}
 void RefreshStep(){int step=CurrentStep;title.text="마른 폭포 동굴   ·   "+(step+1)+" / "+Actions.Length+"   "+Names[step];instruction.text=Gestures[step];inputPose.step=resultPose.step=step;stepStarted=Time.unscaledTime;mistakes=0;lastSignal="";
  float[] xs={505,790,1410,1100};if(step<4){focus.gameObject.SetActive(true);Box(focus,xs[step],390,275,380);}else focus.gameObject.SetActive(false);
  frame.Find("InputPoseExplanation").GetComponent<Text>().text=Gestures[step]+(step<5?"\n내 왼손·오른손 기준으로 움직이세요":"\n이동 후 중앙으로 돌아오기");
  frame.Find("ResultPoseExplanation").GetComponent<Text>().text=Names[step]+(step==2?"\n상대의 머리 높이를 공격":step==3?"\n상대의 허벅지 높이를 공격":"\n인식 성공 후 기본 자세로");
  inputHint.text=keyboard?"키보드 테스트 · Space 준비 / 1 잽 / 2 스트레이트 / 3 하이킥 / 4 로우킥 / 5 막기 / 6 왼쪽 / 7 오른쪽 / 8 X자":"비전 인식 · 카메라에 양손과 어깨가 보이게 준비하세요. 손동작으로 공격하고 어깨로 이동합니다.";
  progress.text="성공  "+hits+" / 2";signal.text=keyboard?"테스트 입력 대기":"카메라 신호 대기";ReplayDemonstration();
 }
 void Feedback(string message){status.text=message;feedbackUntil=Time.unscaledTime+2.2f;}
 void Update(){if(run==null||exit)return;
  successPulse=Mathf.MoveTowards(successPulse,0,Time.unscaledDeltaTime);if(flash!=null)flash.color=new Color(.84f,1,.66f,successPulse);
  if(advancing){if(Time.unscaledTime<nextStepAt)return;advancing=false;run.Data.tutorialStep++;run.Save();hits=0;armed=false;
   if(run.Data.tutorialStep>=Actions.Length){run.CompleteTutorial();exit=true;return;}RefreshStep();}
  phaseLabel.text=advancing?"확인 완료":IsDemonstrating?"① 시범 보기":armed||DiscreteVisionStep?"② 따라 해보세요":"③ 팔을 내리거나 중앙으로 돌아오기";
  if(IsDemonstrating)return;
  if(keyboard){if(!RedboolPauseMenu.BlocksStoryInput){if(Input.GetKeyDown(KeyCode.Space))Accept("IDLE",1);for(int i=0;i<Actions.Length;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))Accept(Actions[i],1);}}
  else if(Time.unscaledTime-lastPacket>3){armed=false;signal.text="카메라 신호 없음";if(Time.unscaledTime>feedbackUntil)status.text="비전 신호를 기다려요. 백엔드를 켜거나 우측 위에서 키보드 테스트를 선택하세요.";return;}
  if(Time.unscaledTime>feedbackUntil&&!advancing)status.text=Time.unscaledTime-stepStarted>12||mistakes>=3?Tips[CurrentStep]:armed||DiscreteVisionStep?Gestures[CurrentStep]:"팔을 내리거나 몸을 중앙으로 돌리면 다음 동작을 받을게요.";
 }
 void OnVision(string action,float confidence,string movement,float strength){
  if(keyboard||exit)return;lastPacket=Time.unscaledTime;
  if(action=="UNAVAILABLE"){armed=false;signal.text="보정 또는 관절 확인 중";return;}
  action=Normalize(action);
  // Punches/raises arrive as receiver-deduplicated one-shot events. Python
  // already checks their own-arm release; do not impose another guard gate.
  bool oneShot=action=="JAB"||action=="STRAIGHT"||action=="HIGH_KICK"||action=="LOW_KICK"||action=="SPECIAL";
  if(oneShot){Accept(action,confidence,true);return;}
  if(CurrentStep<4){
   if(action=="BUSY"){signal.text="카메라 연결됨 · 동작 인식 중";return;}
   Accept(action,confidence);return;
  }
  // Continuous block and movement still need a real release/center state.
  if(CurrentStep==4){Accept(action=="BUSY"?"IDLE":action,confidence);return;}
  if(movement=="NEUTRAL")Accept("IDLE",1);
  else if(movement=="LEFT"||movement=="RIGHT"){
   if(float.IsNaN(strength)||float.IsInfinity(strength)||strength<.25f){Feedback("어깨를 조금 더 옆으로 옮겨보세요.");return;}
   Accept(movement=="LEFT"?"MOVE_LEFT":"MOVE_RIGHT",1);
  }
 }
 public void SubmitKeyboardAction(string action){if(keyboard)Accept(action,1);}
 public static string Normalize(string action){switch((action??"").Trim().ToUpperInvariant()){case "LEFT_PUNCH":return "JAB";case "RIGHT_PUNCH":case "CROSS":return "STRAIGHT";case "LEFT_RAISE":case "LEFT_HAND_UP":case "HIGHKICK":return "HIGH_KICK";case "RIGHT_RAISE":case "RIGHT_HAND_UP":case "LOWKICK":return "LOW_KICK";case "GUARD":return "BLOCK";default:return (action??"").Trim().ToUpperInvariant();}}
 void Accept(string raw,float confidence,bool oneShot=false){
  if(run==null||exit||advancing||IsDemonstrating)return;string action=Normalize(raw);
  if(float.IsNaN(confidence)||float.IsInfinity(confidence)||confidence<.7f){Feedback("자세가 불분명해요. 양손이 카메라에 보이도록 다시 준비해보세요.");return;}
  if(action=="IDLE"||action=="NONE"){armed=true;if(Time.unscaledTime>feedbackUntil)Feedback("준비됐어요. "+Gestures[CurrentStep]);signal.text="기본 자세 확인";lastSignal=action;return;}
  int detected=System.Array.IndexOf(Actions,action);signal.text="받은 동작: "+(detected>=0?Names[detected]:action);
  if(action=="ATTACK"){Feedback("공격은 들어왔지만 잽/스트레이트 구분 신호가 없어요. 동작별 백엔드 또는 키보드 테스트를 사용하세요.");return;}
  if(action==lastSignal&&!armed&&!oneShot)return;lastSignal=action;
  if(!armed&&!oneShot){Feedback("좋아요. 다음 시도 전에 손과 어깨를 기본 자세로 되돌려주세요.");return;}
  if(action!=Actions[CurrentStep]){mistakes++;armed=false;Feedback("지금은 "+Names[CurrentStep]+" 차례예요. "+Tips[CurrentStep]);return;}
  armed=false;hits++;successPulse=1;progress.text="성공  "+hits+" / 2";Feedback(Names[CurrentStep]+" 확인! "+(hits==1?"같은 동작을 한 번 더 해볼까요?":"다음 동작으로 넘어갑니다."));
  Debug.Log("[Tutorial] "+(keyboard?"Keyboard":"Vision")+" "+Actions[CurrentStep]+" "+hits+"/2");
  if(hits==2){advancing=true;nextStepAt=Time.unscaledTime+1.15f;}
 }
}
