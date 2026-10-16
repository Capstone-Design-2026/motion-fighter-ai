using UnityEngine;

/// <summary>A fixed logical canvas, uniformly fitted inside its parent. No stretch or crop.</summary>
public class CinematicFrame : MonoBehaviour
{
    public Vector2 referenceSize = new Vector2(1920,1080);
    RectTransform rect;
    void OnEnable(){rect=GetComponent<RectTransform>();Fit();}
    void LateUpdate(){Fit();}
    public void Fit(){
        if(rect==null)rect=GetComponent<RectTransform>();
        var parent=rect.parent as RectTransform;if(parent==null)return;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.sizeDelta=referenceSize;rect.anchoredPosition=Vector2.zero;
        float scale=Mathf.Min(parent.rect.width/referenceSize.x,parent.rect.height/referenceSize.y);
        rect.localScale=Vector3.one*Mathf.Max(.001f,scale);
    }
}
