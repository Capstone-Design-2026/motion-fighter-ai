using System;
using System.IO;
using UnityEngine;
public static class RedboolReleaseSelfCheck
{
    public static void Run()
    {
        RedboolInputSelfCheck.Run();
        RedboolV36SceneSelfCheck.Run();
        RedboolViewSelfCheck.Run();
        RedboolPracticeSelfCheck.Run();
        RedboolPvpPracticeSelfCheck.Run();
        RedboolViewBindingSelfCheck.Run();
        RedboolSettingsSelfCheck.Run();
        RedboolRenderPressureSelfCheck.Run();
        RedboolCombatFeedbackSelfCheck.Run();
        RedboolCombatPresentationSelfCheck.Run();
        Debug.Log("[REDBOOL V36 CHECK] All release self-checks passed");
    }
}
