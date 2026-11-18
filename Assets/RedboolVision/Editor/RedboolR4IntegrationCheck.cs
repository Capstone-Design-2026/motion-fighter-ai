using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Explicit integration checks; never starts a camera or changes a saved campaign.
public static class RedboolR4IntegrationCheck
{
    public static void Run()
    {
        string root=Environment.GetEnvironmentVariable("REDBOOL_ONLINE_CHECK_ROOT");
        if(string.IsNullOrEmpty(root)) throw new Exception("Explicit verification output required");
        Environment.SetEnvironmentVariable("REDBOOL_SELECTION_CHECK_OUTPUT",Path.Combine(root,"selection"));
        RedboolReleaseSelfCheck.RunWithoutSocketBinding();
        RedboolPvpSelectionSelfCheck.Run();
        RedboolOnlineProtocolCheck.Run();
        RedboolOnlineAnticipationCheck.Run();
        RedboolCameraCheckModeSelfCheck.Run();
        RedboolMacCameraPrivacyCheck.Run();
        RedboolHeroAssetsCheck.Run();
        var config=Resources.Load<RunConfig>("RunConfig");
        if(config==null||config.carryPlayerHealth||config.playerMaxHealth!=100)
            throw new Exception("Integrated story must start each battle with 100 HP");
        if(EditorBuildSettings.scenes[0].path!="Assets/Scenes/BootScene.unity")
            throw new Exception("Authored opening scene must be first");
        foreach(byte gate in new[]{(byte)8,(byte)9})
        {
            var world=new RedboolViewWorld {gate=gate,remaining=120,fov=60,rotation=Quaternion.identity};
            var bytes=RedboolViewProtocol.Encode(new RedboolViewPacket {kind=1,session=Guid.NewGuid(),world=world});
            if(!RedboolViewProtocol.TryDecode(bytes,out var packet)||packet.world.gate!=gate)
                throw new Exception("READY/FIGHT must reach both displays");
            bytes[4]=33;
            if(RedboolViewProtocol.TryDecode(bytes,out _)) throw new Exception("An R3 client must not decode new-layout state");
        }
        Debug.Log("REDBOOL R4 INTEGRATION PASS; preserved camera, selection, transport, new intro and full-health rules");
    }
}
