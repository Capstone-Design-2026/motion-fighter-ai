using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Pointer clicks only: Lobby is the single keyboard handler, so
/// EventSystem Submit cannot double-confirm or undo the same Enter press.</summary>
public sealed class RedboolPvpSelectionButton : Button
{
    protected override void Awake() { base.Awake();navigation=new Navigation {mode=Navigation.Mode.None}; }
    public override void OnSubmit(BaseEventData eventData) { }
    public override void OnMove(AxisEventData eventData) { }
}
