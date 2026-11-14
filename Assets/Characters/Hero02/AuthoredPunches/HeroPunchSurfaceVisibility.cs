using UnityEngine;

// Only the two authored attacks use their matching skin correction.
// Input, combat logic, the Animator and all other motions remain untouched.
[DefaultExecutionOrder(125)]
public sealed class HeroPunchSurfaceVisibility : MonoBehaviour
{
    public Animator animator;
    public SkinnedMeshRenderer[] original;
    public SkinnedMeshRenderer jabSurface, straightSurface;
    static readonly int Jab = Animator.StringToHash("JAB"), Cross = Animator.StringToHash("CROSS");
    public byte CurrentSurface { get; private set; }
    // The remote view has no running Animator; it receives this visual choice from the host.
    public void ApplyRemoteSurface(byte surface) { Show(surface == 1 ? 1 : surface == 2 ? 2 : 0); }
    void LateUpdate()
    {
        if (!animator || !animator.runtimeAnimatorController) { Show(0); return; }
        int state = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (animator.IsInTransition(0))
        {
            int next = animator.GetNextAnimatorStateInfo(0).shortNameHash;
            if (next == Jab || next == Cross) state = next;
        }
        Show(state == Jab ? 1 : state == Cross ? 2 : 0);
    }
    void Show(int attack)
    {
        CurrentSurface = (byte)attack;
        if (original != null) foreach (var r in original) if (r) r.enabled = attack == 0;
        if (jabSurface) jabSurface.enabled = attack == 1;
        if (straightSurface) straightSurface.enabled = attack == 2;
    }
    void OnDisable() { Show(0); }
}
