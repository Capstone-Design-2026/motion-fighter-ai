using System;
using System.IO;
using UnityEngine;
public static class RedboolReleaseSelfCheck
{
    public static void Run()
    {
        RedboolInputSelfCheck.Run();
        RedboolViewSelfCheck.Run();
        RedboolPracticeSelfCheck.Run();
        RedboolViewBindingSelfCheck.Run();
        RedboolSettingsSelfCheck.Run();
        RedboolCombatFeedbackSelfCheck.Run();
        Debug.Log("[REDBOOL V35 CHECK] All release self-checks passed");
    }
}
