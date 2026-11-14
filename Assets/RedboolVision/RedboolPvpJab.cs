using UnityEngine;

// The second player uses the reviewed upstream Cheolmun clips. The current jab
// already extends fully; the v33 elbow-only replacement is retained only as an Editor fixture.
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
