using System;
using System.IO;
using UnityEngine;

/// <summary>No camera, process, network, preferences or filesystem mutation.</summary>
public static class RedboolCameraCheckModeSelfCheck
{
    public static void Run()
    {
        int checks=0;
        Action<bool,string> check=(ok,name)=>{checks++;if(!ok)throw new Exception(name);};
        string output=Path.Combine(Path.GetTempPath(),"redbool-offline-mode-fixture");
        var both=new[]{RedboolCameraCheckMode.CheckFlag,RedboolCameraCheckMode.SyntheticFlag};
        check(!RedboolCameraCheckMode.Requested(null),"Null command line stays ordinary");
        check(!RedboolCameraCheckMode.Requested(new string[0]),"Ordinary launch has no fixture mode");
        check(!RedboolCameraCheckMode.Authorize(new string[0],output,false,false,out _),"Output alone cannot enable synthetic mode");
        foreach(string flag in both)
        {
            var partial=new[]{flag};
            check(RedboolCameraCheckMode.Requested(partial),"Each partial opt-in is intercepted: "+flag);
            check(!RedboolCameraCheckMode.Authorize(partial,output,false,false,out var error)&&!String.IsNullOrEmpty(error),"Partial opt-in fails closed: "+flag);
        }
        foreach(string invalid in new[]{null,""," ","relative-output",Path.GetPathRoot(output)})
            check(!RedboolCameraCheckMode.Authorize(both,invalid,false,false,out _),"Missing/relative/root output rejected");
        // Windows considers these rooted, but they still depend on a current
        // drive or its working directory. They must never authorize a fixture.
        foreach(string invalid in new[]{@"C:relative",@"C:",@"\relative"})
            check(!RedboolCameraCheckMode.Authorize(both,invalid,false,false,out _),"Drive-relative output rejected: "+invalid);
        check(!RedboolCameraCheckMode.Authorize(both,output,true,false,out _),"Editor does not run standalone fixture");
        check(!RedboolCameraCheckMode.Authorize(both,output,false,true,out _),"Batch rendering cannot masquerade as visible fixture");
        foreach(string conflict in new[]{"--redbool-external-camera","--redbool-online-host","--redbool-online-join"})
            check(!RedboolCameraCheckMode.Authorize(new[]{both[0],both[1],conflict},output,false,false,out _),"Offline check rejects conflicting automation: "+conflict);
        check(RedboolCameraCheckMode.Authorize(both,output,false,false,out var allowed)&&allowed==null,"Two explicit flags and dedicated output authorize exact-release synthetic check");
        check(RedboolCameraCheckMode.Authorize(new[]{"game path with spaces",both[0],both[1]},Path.Combine(output,"space folder"),false,false,out _),"Spaces in executable/output remain valid");
        Debug.Log("REDBOOL CAMERA CHECK MODE PASS "+checks+" checks; no camera, processes, network or preferences");
    }
}
