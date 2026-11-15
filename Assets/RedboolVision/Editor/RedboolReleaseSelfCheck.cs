using System;
using System.IO;
using UnityEngine;
public static class RedboolReleaseSelfCheck
{
    public static void Run()
    {
        RunWithoutSocketBinding();
        RedboolViewBindingSelfCheck.Run();
        Debug.Log("[REDBOOL V36.2 CHECK] All release self-checks passed");
    }
    // Safe to run while another player owns its normal LAN ports.
    public static void RunWithoutSocketBinding()
    {
        RedboolInputSelfCheck.Run();
        RedboolV36SceneSelfCheck.Run();
        RedboolViewSelfCheck.Run();
        RedboolPracticeSelfCheck.Run();
        RedboolPvpPracticeSelfCheck.Run();
        RedboolPracticeContinuitySelfCheck.Run();
        RedboolPracticeDispatchSelfCheck.Run();
        RedboolLanAddressSelfCheck.Run();
        RedboolSettingsSelfCheck.Run();
        RedboolRenderPressureSelfCheck.Run();
        RedboolCombatFeedbackSelfCheck.Run();
        RedboolCombatPresentationSelfCheck.Run();
        Debug.Log("[REDBOOL V36.2 CHECK] Release self-checks without socket binding passed");
    }
}
