using System;
using UnityEditor;
using UnityEngine;

// The upstream v10 retarget already supplies a fully extended jab. Keep this old
// entry point safe: never reapply the v33 elbow bake to a newer authored clip.
public static class RedboolPvpJabBake
{
    public const string Original="Assets/Characters/StoryOpponents/05_Cheolmun/Animation/05_Cheolmun_JAB.anim";
    public const string Selected=Original;
    public static void Run()
    {
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Original);
        if(source==null)throw new Exception("Missing reviewed Cheolmun jab");
        Debug.Log("[PVP JAB] Using upstream "+source.name+" unchanged; no elbow rebake is required.");
    }
}
