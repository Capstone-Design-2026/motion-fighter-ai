using UnityEngine;
using UnityEngine.UI;
/// <summary>Small drifting dust motes and lantern sparks, in logical screen coordinates.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StoryAtmosphere : MaskableGraphic {
 public float PreviewTime=-1;public bool cave;
 protected override void Awake(){base.Awake();raycastTarget=false;}
 void Update(){SetVerticesDirty();}
 protected override void OnPopulateMesh(VertexHelper vh){
  vh.Clear();float t=PreviewTime>=0?PreviewTime:Time.unscaledTime;var r=rectTransform.rect;
  for(int i=0;i<30;i++){
   float life=Mathf.Repeat(t*(.038f+i%4*.008f)+i*.173f,1);
   float x=Mathf.Repeat(i*.618034f+Mathf.Sin(t*.23f+i)*.035f,1),y=life;
   float a=Mathf.Sin(life*Mathf.PI)*(.10f+i%3*.04f);float size=1.2f+i%4*.65f;
   var c=new Color(1,.84f,.57f,a);var p=new Vector2(r.xMin+x*r.width,r.yMin+y*r.height);
   int n=vh.currentVertCount;vh.AddVert(p+new Vector2(-size,0),c,Vector2.zero);vh.AddVert(p+new Vector2(0,size),c,Vector2.zero);vh.AddVert(p+new Vector2(size,0),c,Vector2.zero);vh.AddVert(p+new Vector2(0,-size),c,Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
  }
 }
}
