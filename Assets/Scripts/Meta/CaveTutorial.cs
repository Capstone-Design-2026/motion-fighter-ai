using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Persisted seven-step training, fed by the main-thread UDP stream or explicitly selected debug keys.</summary>
public class CaveTutorial : MonoBehaviour {
 public static readonly string[] Actions={"JAB","STRAIGHT","HIGH_KICK","LOW_KICK","BLOCK","MOVE_LEFT","MOVE_RIGHT"};
 static readonly string[] Names={"잽","스트레이트","하이킥","로우킥","막기","왼쪽 이동","오른쪽 이동"};
 static readonly string[] Gestures={"왼손을 앞으로 지르세요","오른손을 앞으로 뻗으세요","왼손을 위로 들어 올리세요","왼손을 아래로 내리세요","두 손을 올려 가드 자세를 취하세요","어깨 중심을 왼쪽으로 옮기세요","어깨 중심을 오른쪽으로 옮기세요"};
 RunState run;Text title,instruction,progress,status,modeLabel,actionLabel,inputHint,mirrorHint;TrainingPoseGraphic inputPose,resultPose;
 bool keyboard,armed,exit,advancing;int hits;float lastPacket=-100,nextStepAt;string lastSignal="";
 public static IEnumerator Play(RunState state){
  if(state.Data.tutorialCompleted)yield break;
  if(state.Data.tutorialStep>=Actions.Length){state.CompleteTutorial();yield break;}
  var canvas=UIFactory.CreateCanvas("CaveTutorialCanvas",null,210);var tutorial=canvas.gameObject.AddComponent<CaveTutorial>();
  tutorial.run=state;tutorial.Build();
  // Subscribe before adding our receiver, avoiding an unbound packet during scene creation.
  UdpActionReceiver.TutorialAction+=tutorial.OnVision;
  if(Object.FindAnyObjectByType<UdpActionReceiver>()==null){var receiver=new GameObject("TutorialVisionReceiver");receiver.transform.SetParent(canvas.transform);receiver.AddComponent<UdpActionReceiver>();}
  try{while(!tutorial.exit)yield return null;}finally{UdpActionReceiver.TutorialAction-=tutorial.OnVision;if(canvas!=null)Destroy(canvas.gameObject);}
 }
 void OnDestroy(){UdpActionReceiver.TutorialAction-=OnVision;}
 void Build(){
  var backdrop=UIFactory.CreateImage("BlockMapInput",transform,new Color(.06f,.05f,.04f),true);UIFactory.Stretch(backdrop.rectTransform);
  var rect=UIFactory.CreateRect("CaveBackground",transform);UIFactory.Stretch(rect);var bg=rect.gameObject.AddComponent<RawImage>();bg.texture=Resources.Load<Texture2D>("UI/Wuxia/TrainingCaveMurals");bg.raycastTarget=false;
  var shade=UIFactory.CreateImage("CaveShade",transform,new Color(.035f,.03f,.02f,.22f));UIFactory.Stretch(shade.rectTransform);
  Label("Location","마른 폭포 동굴 · 벽화 수련",28,new Vector2(0,460),new Vector2(1050,55));
  title=Label("TrainingStep","",40,new Vector2(0,373),new Vector2(1200,65));
  instruction=Label("Instruction","",30,new Vector2(0,302),new Vector2(1350,65));
  inputPose=Panel("InputPose","나의 동작",-395,false);resultPose=Panel("ResultPose","게임 속 동작",395,true);
  actionLabel=Label("ActionName","",32,new Vector2(395,-356),new Vector2(550,55));
  mirrorHint=Label("MirrorHint","정면 거울 보기 · 내 왼손은 화면 오른쪽",22,new Vector2(-395,-356),new Vector2(640,55));
  Label("MappingArrow","→",64,new Vector2(0,-245),new Vector2(90,100));
  status=Label("TrainingStatus","",25,new Vector2(0,-406),new Vector2(1450,70));
  progress=Label("TrainingProgress","",26,new Vector2(0,-454),new Vector2(1300,50));
  inputHint=Label("TrainingInputHint","",22,new Vector2(0,-504),new Vector2(1400,50));
  var mode=UIFactory.CreateButton("TutorialInputMode",transform,"키보드 테스트",23,WuxiaTheme.Ink,ToggleInputMode);
  UIFactory.Place(mode.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-30,-28),new Vector2(220,56));modeLabel=mode.GetComponentInChildren<Text>();
  var leave=UIFactory.CreateButton("LeaveTraining",transform,"수련 중단",23,WuxiaTheme.Ink,()=>{run.Save();exit=true;});
  UIFactory.Place(leave.GetComponent<RectTransform>(),new Vector2(0,1),new Vector2(0,1),new Vector2(30,-28),new Vector2(170,56));RefreshStep();
 }
 Text Label(string id,string text,int size,Vector2 pos,Vector2 dimensions){var label=WuxiaTheme.Text(id,transform,text,size,TextAnchor.MiddleCenter,WuxiaTheme.Paper);UIFactory.Place(label.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),pos,dimensions);return label;}
 TrainingPoseGraphic Panel(string id,string heading,float x,bool result){
  var panel=UIFactory.CreateImage(id+"Panel",transform,new Color(.09f,.075f,.045f,.77f));UIFactory.Place(panel.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(x,-245),new Vector2(690,180));
  Label(id+"Heading",heading,28,new Vector2(x,-175),new Vector2(500,48));
  var rect=UIFactory.CreateRect(id,panel.transform);UIFactory.Stretch(rect,255,15,255,45);var figure=rect.gameObject.AddComponent<TrainingPoseGraphic>();figure.raycastTarget=false;figure.gameAction=result;return figure;
 }
 public void ToggleInputMode(){keyboard=!keyboard;hits=0;armed=false;advancing=false;modeLabel.text=keyboard?"비전 인식으로 전환":"키보드 테스트";RefreshStep();}
 void RefreshStep(){int step=Mathf.Clamp(run.Data.tutorialStep,0,Actions.Length-1);title.text=(step+1)+" / "+Actions.Length+"   "+Names[step];instruction.text=Gestures[step];actionLabel.text=Names[step];inputPose.step=resultPose.step=step;
  mirrorHint.text=step>=5?"어깨 중심을 옮긴 뒤 중앙으로 돌아오세요":"정면 거울 보기 · 내 왼손은 화면 오른쪽";
  inputHint.text=keyboard?"테스트: Space 기본 자세 · 1 잽 · 2 스트레이트 · 3 하이킥 · 4 로우킥 · 5 막기 · 6 왼쪽 · 7 오른쪽":"카메라에 상체와 양손이 보이게 앉으세요. 동작 후에는 손과 어깨를 중앙의 기본 자세로 되돌리세요.";
  progress.text="성공  "+hits+" / 2";status.text=keyboard?"Space로 기본 자세를 확인한 뒤 안내된 숫자 키를 누르세요.":"기본 자세로 카메라 앞에 준비하세요. 비전 신호를 기다립니다.";
 }
 void Update(){if(run==null||exit)return;
  if(advancing){if(Time.unscaledTime<nextStepAt)return;advancing=false;run.Data.tutorialStep++;run.Save();hits=0;armed=false;
   if(run.Data.tutorialStep>=Actions.Length){run.CompleteTutorial();exit=true;return;}RefreshStep();}
  if(keyboard){if(Input.GetKeyDown(KeyCode.Space))Accept("IDLE",1);for(int i=0;i<Actions.Length;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))Accept(Actions[i],1);}
  else if(Time.unscaledTime-lastPacket>3){armed=false;status.text="비전 신호 대기 중 · 백엔드와 카메라 연결을 확인하거나 키보드 테스트를 선택하세요.";}
 }
 void OnVision(string action,float confidence,string movement,float strength){
  if(keyboard)return;lastPacket=Time.unscaledTime;
  if(run.Data.tutorialStep<5){Accept(action,confidence);return;}
  // Shoulder movement has its own strength, independent of attack classifier confidence.
  // Missing movement is not evidence of a return to the center.
  if(movement=="NEUTRAL")Accept("IDLE",1);
  else if(movement=="LEFT"||movement=="RIGHT"){
   if(float.IsNaN(strength)||float.IsInfinity(strength)||strength<.25f){status.text="어깨를 조금 더 옆으로 옮겨보세요.";return;}
   Accept(movement=="LEFT"?"MOVE_LEFT":"MOVE_RIGHT",1);
  }
 }
 public void SubmitKeyboardAction(string action){if(keyboard)Accept(action,1);}
 public static string Normalize(string action){switch(action){case "LEFT_PUNCH":return "JAB";case "RIGHT_PUNCH":return "STRAIGHT";case "LEFT_HAND_UP":return "HIGH_KICK";case "LEFT_HAND_DOWN":return "LOW_KICK";default:return action;}}
 void Accept(string raw,float confidence){
  if(run==null||exit||advancing)return;string action=Normalize(raw);lastSignal=action;
  if(action=="IDLE"||action=="NONE"){armed=true;status.text="준비 완료 · "+Gestures[run.Data.tutorialStep];return;}
  if(confidence<.7f){status.text="자세가 불분명해요. 양손이 보이도록 천천히 다시 해보세요.";return;}
  if(action=="ATTACK"){status.text="기존 ATTACK 신호 수신 중 · 동작별 인식 코드 연결 전에는 키보드 테스트를 사용할 수 있어요.";return;}
  if(!armed){status.text="손과 어깨를 중앙의 기본 자세로 되돌린 뒤 다시 시도하세요.";return;}
  if(action!=Actions[run.Data.tutorialStep]){status.text="다른 동작이 들어왔어요. "+Gestures[run.Data.tutorialStep];return;}
  armed=false;hits++;progress.text="성공  "+hits+" / 2";status.text=Names[run.Data.tutorialStep]+" 성공! 손과 어깨를 중앙의 기본 자세로 되돌리세요.";
  if(hits==2){advancing=true;nextStepAt=Time.unscaledTime+1;status.text="수련 성공 · 다음 벽화로 이동합니다.";}
 }
}
