/// <summary>Shared input mapping. Bilateral SPECIAL is explicit, never two single-arm attacks.</summary>
public static class RedboolGestureMap
{
    public static string Resolve(string kind, string gesture, string side)
    {
        if (kind == "ATTACK") return side == "LEFT" ? "JAB" : side == "RIGHT" ? "STRAIGHT" : null;
        if (kind != "GESTURE") return null;
        if (gesture == "SPECIAL") return side == "BOTH" ? "SPECIAL" : null;
        if (side != "LEFT" && side != "RIGHT") return null;
        if (string.IsNullOrEmpty(gesture) || !gesture.StartsWith(side + "_", System.StringComparison.Ordinal)) return null;
        switch (gesture)
        {
            case "LEFT_PUNCH": return "JAB";
            case "RIGHT_PUNCH": return "STRAIGHT";
            case "LEFT_RAISE": return "HIGH_KICK";
            case "RIGHT_RAISE": return "LOW_KICK";
            default: return null;
        }
    }
}
