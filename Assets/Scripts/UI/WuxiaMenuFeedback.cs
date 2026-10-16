using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Menu-only bronze highlight, with equally visible keyboard focus.</summary>
public class WuxiaMenuFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    Button button; WuxiaGraphic body,frame;Text label;bool hover,selected,primary;
    public void Initialize(Button b,WuxiaGraphic fill,WuxiaGraphic border,Text text,bool main)
    {button=b;body=fill;frame=border;label=text;primary=main;}
    public void OnPointerEnter(PointerEventData e){hover=true;}
    public void OnPointerExit(PointerEventData e){hover=false;}
    public void OnSelect(BaseEventData e){selected=true;}
    public void OnDeselect(BaseEventData e){selected=false;}
    void Update()
    {
        if(button==null)return;
        bool enabled=button.IsInteractable(),active=enabled&&(hover||selected);
        float k=1-Mathf.Exp(-Time.unscaledDeltaTime*14);
        Color fill=active?new Color(.27f,.20f,.12f,.97f):new Color(.075f,.085f,.07f,.92f);
        body.color=Color.Lerp(body.color,fill,k);
        Color border=active?new Color(1,.83f,.5f,1):WuxiaTheme.Gold;
        border.a=enabled?(primary?1:.7f):.25f;
        frame.color=Color.Lerp(frame.color,border,k);
        label.color=Color.Lerp(label.color,enabled?WuxiaTheme.Paper:new Color(.54f,.52f,.45f),k);
    }
}
