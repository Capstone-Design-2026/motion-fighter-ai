// A held camera pose starts at most one timed guard in the current combat rules.
// Wait only while the input is still held; release/disconnect cancels that wait.
public sealed class RedboolGuardLatch
{
    private bool held, started;
    public void Observe(bool active) { held=active; if(!active) started=false; }
    public bool WantsStart(bool canAct) { return held && !started && canAct; }
    public void MarkStarted() { started=true; }
    public void Reset() { held=started=false; }
}
