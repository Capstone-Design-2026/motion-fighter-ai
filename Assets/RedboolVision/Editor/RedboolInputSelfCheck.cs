using System;
using UnityEditor;
using UnityEngine;

public static class RedboolInputSelfCheck
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    [MenuItem("Tools/REDBOOL/Run Input Adapter Self Check")]
    public static void Run()
    {
        int preparationChecks = RedboolPreparationChecks.Run();
        var guard=new RedboolGuardLatch();
        Check(!guard.WantsStart(true),"Idle must not start guard");
        guard.Observe(true);
        Check(!guard.WantsStart(false),"Busy Fighter must not receive guard");
        Check(guard.WantsStart(true),"Held input may start when game becomes ready");
        guard.MarkStarted();
        for(int i=0;i<100;i++) { guard.Observe(true);Check(!guard.WantsStart(true),"Held input repeated timed guard"); }
        guard.Observe(false);Check(!guard.WantsStart(true),"Release generated guard");
        guard.Observe(true);Check(guard.WantsStart(true),"New pose must re-arm guard");
        guard.Observe(false);Check(!guard.WantsStart(true),"Busy wait survived release");
        guard.Observe(true);guard.Reset();Check(!guard.WantsStart(true),"Disconnect retained guard input");
        Debug.Log("REDBOOL input adapter self check passed, including " + preparationChecks + " preparation checks. Verify actual Fighter, movement and network expiry in Play Mode separately.");
    }
}
