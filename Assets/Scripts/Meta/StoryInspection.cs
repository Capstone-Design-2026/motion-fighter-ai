using UnityEngine;
using UnityEngine.UI;
public enum StoryEvidence {None,Mural,Letter,Tracks,Knot,WaterPlan}
/// <summary>Per-line investigation shot: tangible evidence fills the frame instead of a portrait.</summary>
public class StoryInspection : MonoBehaviour {
 RawImage picture;RectTransform imageRect;CanvasGroup group;Text caption;float age;
 StoryEvidence kind;public float PreviewTime=-1;
 public static StoryInspection Create(Transform parent){
  var root=UIFactory.CreateRect("EvidenceShot",parent);UIFactory.Stretch(root);var view=root.gameObject.AddComponent<StoryInspection>();view.Build();return view;
 }
 void Build(){
  group=gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;
  var bg=UIFactory.CreateImage("EvidenceBlack",transform,Color.black);UIFactory.Stretch(bg.rectTransform);
  imageRect=UIFactory.CreateRect("EvidenceImage",transform);UIFactory.Stretch(imageRect);
  picture=imageRect.gameObject.AddComponent<RawImage>();picture.raycastTarget=false;
  var fit=imageRect.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
  var particles=UIFactory.CreateRect("EvidenceDust",transform);UIFactory.Stretch(particles);particles.gameObject.AddComponent<StoryAtmosphere>();
  var strip=UIFactory.CreateImage("EvidenceCaptionBack",transform,new Color(.03f,.035f,.025f,.72f));
  UIFactory.Place(strip.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(80,-112),new Vector2(650,60));
  caption=WuxiaTheme.Text("EvidenceCaption",strip.transform,"",27,TextAnchor.MiddleLeft,WuxiaTheme.Gold);UIFactory.Stretch(caption.rectTransform,22,4,20,4);
  gameObject.SetActive(false);
 }
 public void Show(StoryDialogueLine line){
  kind=line.evidence;gameObject.SetActive(kind!=StoryEvidence.None);if(kind==StoryEvidence.None)return;
  picture.texture=Resources.Load<Texture2D>(kind==StoryEvidence.Mural?"UI/Wuxia/TrainingCaveMurals":"Story/Evidence/"+kind);
  if(picture.texture==null){Debug.LogWarning("Missing story evidence: "+kind);gameObject.SetActive(false);return;}
  imageRect.GetComponent<AspectRatioFitter>().aspectRatio=(float)picture.texture.width/picture.texture.height;
  caption.text="살펴보기  ·  "+line.evidenceTitle;age=0;PreviewTime=-1;Animate();
 }
 void Update(){age+=Time.unscaledDeltaTime;Animate();}
 void Animate(){
  float t=PreviewTime>=0?PreviewTime:age;float enter=1-Mathf.Exp(-t*4);
  group.alpha=Mathf.Clamp01(t/.35f);
  // A deliberate approach followed by a slow lateral examination. The mural remains fully visible.
  float zoom=kind==StoryEvidence.Mural?1f:1.015f+enter*.045f+Mathf.Sin(t*.35f)*.008f;
  imageRect.localScale=Vector3.one*zoom;
  imageRect.anchoredPosition=kind==StoryEvidence.Mural?Vector2.zero:new Vector2(Mathf.Sin(t*.30f)*14,(1-enter)*-22);
 }
}
