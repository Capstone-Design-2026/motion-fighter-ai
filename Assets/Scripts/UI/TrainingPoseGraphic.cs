// REDBOOL raised-kicks-v3
using UnityEngine;
using UnityEngine.UI;
/// <summary>Animated wall diagram: front-facing user input on left, in-game side-view action on right.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TrainingPoseGraphic : MaskableGraphic {
 public int step;public bool gameAction;
 void Update(){SetVerticesDirty();}
 void Bone(VertexHelper vh,Vector2 a,Vector2 b,Color tint,float width=.016f){
  Rect r=rectTransform.rect;a=new Vector2(r.xMin+a.x*r.width,r.yMin+a.y*r.height);b=new Vector2(r.xMin+b.x*r.width,r.yMin+b.y*r.height);
  var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*r.width;int i=vh.currentVertCount;
  vh.AddVert(a-n,tint,Vector2.zero);vh.AddVert(a+n,tint,Vector2.zero);vh.AddVert(b+n,tint,Vector2.zero);vh.AddVert(b-n,tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
 }
 void Ring(VertexHelper vh,Vector2 center,float size,Color tint){for(int i=0;i<28;i++){float a=i*Mathf.PI*2/28,b=(i+1)*Mathf.PI*2/28;Bone(vh,center+new Vector2(Mathf.Cos(a)*size,Mathf.Sin(a)*size),center+new Vector2(Mathf.Cos(b)*size,Mathf.Sin(b)*size),tint,.006f);}}
 protected override void OnPopulateMesh(VertexHelper vh){
  vh.Clear();Color ink=new Color(.79f,.75f,.63f),active=new Color(.98f,.73f,.33f);
  float t=Mathf.SmoothStep(0,1,(Mathf.Sin(Time.unscaledTime*2.4f)+1)*.5f);
  if(step==5||step==6){
   float direction=step==5?-1:1;
   float shift=direction*.15f*t;
   Vector2 torso=new Vector2(.5f+shift,.68f),basePoint=new Vector2(.5f+(gameAction?shift:0),.36f);
   Ring(vh,torso+new Vector2(0,.16f),.07f,ink);Bone(vh,basePoint,torso,ink,.025f);
   Bone(vh,torso+new Vector2(-.15f,0),torso+new Vector2(.15f,0),active,.025f);
   Bone(vh,torso+new Vector2(-.15f,0),torso+new Vector2(-.17f,-.20f),ink);
   Bone(vh,torso+new Vector2(.15f,0),torso+new Vector2(.17f,-.20f),ink);
   Bone(vh,basePoint,basePoint+new Vector2(-.12f,-.25f),ink);
   Bone(vh,basePoint,basePoint+new Vector2(.12f,-.25f),ink);
   Vector2 end=new Vector2(.5f+direction*.36f,.48f);
   Bone(vh,new Vector2(.5f+direction*.21f,.48f),end,active);
   Bone(vh,end,end+new Vector2(-direction*.07f,.06f),active);
   Bone(vh,end,end+new Vector2(-direction*.07f,-.06f),active);return;
  }
  Vector2 hip=new Vector2(.5f,.40f),neck=new Vector2(.5f,.72f);
  Ring(vh,new Vector2(.5f,.83f),.075f,ink);Bone(vh,hip,neck,ink,.026f);
  Vector2 lshoulder=new Vector2(.61f,.70f),rshoulder=new Vector2(.39f,.70f);
  Bone(vh,rshoulder,lshoulder,ink);
  Vector2 le=new Vector2(.71f,.58f),lh=new Vector2(.64f,.75f),re=new Vector2(.29f,.58f),rh=new Vector2(.36f,.75f);
  Vector2 lknee=new Vector2(.59f,.25f),lfoot=new Vector2(.65f,.09f),rknee=new Vector2(.41f,.25f),rfoot=new Vector2(.35f,.09f);
  if(!gameAction){
   if(step==0){le=Vector2.Lerp(le,new Vector2(.70f,.68f),t);lh=Vector2.Lerp(lh,new Vector2(.70f,.79f),t);Ring(vh,lh,.025f+t*.04f,active);}
   if(step==1){re=Vector2.Lerp(re,new Vector2(.30f,.68f),t);rh=Vector2.Lerp(rh,new Vector2(.30f,.79f),t);Ring(vh,rh,.025f+t*.04f,active);}
   if(step==2){le=Vector2.Lerp(le,new Vector2(.72f,.84f),t);lh=Vector2.Lerp(lh,new Vector2(.69f,.98f),t);}
   if(step==3){re=Vector2.Lerp(re,new Vector2(.28f,.84f),t);rh=Vector2.Lerp(rh,new Vector2(.31f,.98f),t);}
  }else{
   if(step<2){le=Vector2.Lerp(le,new Vector2(.76f,.69f),t);lh=Vector2.Lerp(lh,new Vector2(.95f,.72f),t);}
   if(step==2){lknee=Vector2.Lerp(lknee,new Vector2(.73f,.57f),t);lfoot=Vector2.Lerp(lfoot,new Vector2(.96f,.78f),t);}
   if(step==3){lknee=Vector2.Lerp(lknee,new Vector2(.73f,.34f),t);lfoot=Vector2.Lerp(lfoot,new Vector2(.96f,.28f),t);}
  }
  if(step==4){lh=new Vector2(.57f,.86f);rh=new Vector2(.43f,.86f);le=new Vector2(.68f,.70f);re=new Vector2(.32f,.70f);}
  if(step==7){
   le=new Vector2(.72f,.48f);re=new Vector2(.28f,.48f);
   if(!gameAction){lh=Vector2.Lerp(new Vector2(.70f,.70f),new Vector2(.34f,.72f),t);rh=Vector2.Lerp(new Vector2(.30f,.70f),new Vector2(.66f,.72f),t);}
   else {le=Vector2.Lerp(le,new Vector2(.68f,.62f),t);re=Vector2.Lerp(re,new Vector2(.68f,.56f),t);lh=new Vector2(.82f,.72f);rh=new Vector2(.82f,.59f);Ring(vh,new Vector2(.88f+.08f*t,.66f),.05f+.08f*t,active);}
  }
  bool leftActive=gameAction?(step<2||step==4||step==7):(step!=1&&step!=3);
  bool rightActive=gameAction?(step==4||step==7):(step==1||step==3||step==4||step==7);
  Bone(vh,lshoulder,le,leftActive?active:ink);Bone(vh,le,lh,leftActive?active:ink);
  Bone(vh,rshoulder,re,rightActive?active:ink);Bone(vh,re,rh,rightActive?active:ink);
  Bone(vh,hip,lknee,gameAction&&(step==2||step==3)?active:ink);Bone(vh,lknee,lfoot,gameAction&&(step==2||step==3)?active:ink);
  Bone(vh,hip,rknee,ink);Bone(vh,rknee,rfoot,ink);
 }
}
