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
        Debug.Log("REDBOOL input adapter self check passed, including " + preparationChecks + " preparation checks. Verify actual Fighter, movement and network expiry in Play Mode separately.");
    }
}
