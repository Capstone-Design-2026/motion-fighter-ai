using System;
using System.IO;
using UnityEngine;

// One reproducible Edit-mode run. Never creates a Photon room or opens a camera.
public static class RedboolOnlineCandidateCheck
{
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("REDBOOL_ONLINE_CHECK_ROOT");
        if(!Application.isBatchMode || string.IsNullOrEmpty(output))
            throw new InvalidOperationException("Use batch mode and REDBOOL_ONLINE_CHECK_ROOT.");
        Directory.CreateDirectory(output);
        RedboolMacCameraPrivacyCheck.Run();
        RedboolCameraCheckModeSelfCheck.Run();
        RedboolReleaseSelfCheck.RunWithoutSocketBinding();
        Environment.SetEnvironmentVariable("REDBOOL_ONLINE_CHECK_OUTPUT",Path.Combine(output,"online-protocol.json"));
        Environment.SetEnvironmentVariable("REDBOOL_ANTICIPATION_CHECK_OUTPUT",Path.Combine(output,"anticipation.json"));
        Environment.SetEnvironmentVariable("REDBOOL_SELECTION_CHECK_OUTPUT",Path.Combine(output,"selection"));
        Environment.SetEnvironmentVariable("REDBOOL_VIEW_CHECK_OUTPUT",Path.Combine(output,"view-check.txt"));
        Environment.SetEnvironmentVariable("REDBOOL_MENU_CHECK_OUTPUT",Path.Combine(output,"menu-check.txt"));
        RedboolOnlineProtocolCheck.Run();
        RedboolOnlineAnticipationCheck.Run();
        RedboolPvpSelectionSelfCheck.RunAll();
        RedboolMenuSelfCheck.Run();
        Debug.Log("REDBOOL ONLINE CANDIDATE OFFLINE CHECKS PASS; Cloud, real gameplay FPS and physical latency remain unmeasured.");
    }
}
