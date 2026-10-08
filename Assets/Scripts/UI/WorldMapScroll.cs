using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
/// <summary>Two-axis drag, bounded wheel zoom, and explicit focus without modifying saved exploration.</summary>
public class WorldMapScroll : ScrollRect {
 public float Zoom {get {return content!=null?content.localScale.x:1;}}
 public override void OnScroll(PointerEventData e){if(!isActiveAndEnabled)return;SetZoom(Zoom*Mathf.Exp(e.scrollDelta.y*.12f));}
 public void SetZoom(float value){
  if(content==null||viewport==null)return;
  float min=Mathf.Max(viewport.rect.width/content.rect.width,viewport.rect.height/content.rect.height);
  Vector2 local;RectTransformUtility.ScreenPointToLocalPointInRectangle(content,RectTransformUtility.WorldToScreenPoint(null,viewport.TransformPoint(viewport.rect.center)),null,out local);
  StopMovement();content.localScale=Vector3.one*Mathf.Clamp(value,min,1.35f);Canvas.ForceUpdateCanvases();Focus(local);
 }
 public void Focus(Vector2 point){
  if(content==null||viewport==null)return;StopMovement();
  float w=content.rect.width*Zoom,h=content.rect.height*Zoom,vw=viewport.rect.width,vh=viewport.rect.height;
  horizontalNormalizedPosition=w>vw?Mathf.Clamp01(((point.x-content.rect.xMin)*Zoom-vw*.5f)/(w-vw)):.5f;
  verticalNormalizedPosition=h>vh?Mathf.Clamp01(((point.y-content.rect.yMin)*Zoom-vh*.5f)/(h-vh)):.5f;
 }
}
