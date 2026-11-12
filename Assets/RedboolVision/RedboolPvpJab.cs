using UnityEngine;

// Only the second human receives the extended elbow curve. Story Cheolmun keeps its authored controller.
public static class RedboolPvpJab
{
    public static bool Apply(Fighter3DView view)
    {
        if(view==null || view.modelAnimator==null || view.visualRoot==null || !view.visualRoot.name.Contains("Cheolmun"))return false;
        var controller=Resources.Load<RuntimeAnimatorController>("RedboolPvpCheolmun");
        if(controller==null){Debug.LogError("[REDBOOL PVP] Missing second-player jab controller");return false;}
        view.modelAnimator.runtimeAnimatorController=controller;
        view.ResetPresentation();
        return true;
    }
}
