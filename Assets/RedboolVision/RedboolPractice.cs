using System;
using UnityEngine;

/// <summary>One participant's practice state. No campaign or global player state.</summary>
public sealed class RedboolPractice
{
    public static readonly string[] Actions = { "JAB", "STRAIGHT", "HIGH_KICK", "LOW_KICK", "BLOCK", "MOVE_LEFT", "MOVE_RIGHT", "SPECIAL" };
    public static readonly string[] Instructions = { "왼손 잽", "오른손 스트레이트", "왼손 레이즈 · 하이킥", "오른손 레이즈 · 로우킥", "양손 블락 후 내리기", "몸을 왼쪽으로 기울인 뒤 중앙", "몸을 오른쪽으로 기울인 뒤 중앙", "가슴 앞 X자 1초 유지 후 팔 풀기" };
    public string Run { get; private set; }
    public int Step { get; private set; }
    public int Hits { get; private set; }
    public bool Complete => Step >= Actions.Length;
    public string Action => Complete ? "" : Actions[Step];
    public string Phase => Complete ? "COMPLETE" : Hits >= 2 ? "RETURN" : "PRACTICE";
    public string Instruction => Complete ? "수련 완료 · 중앙에서 준비" : Instructions[Step] + "  " + Hits + "/2";
    bool armed;
    double returnSince = -1;
    public RedboolPractice() { Reset(); }
    public void Reset() { Run=Guid.NewGuid().ToString("N"); Step=Hits=0; armed=false; returnSince=-1; }
    public void Observe(string action, float confidence, string movement, float strength)
    {
        if (Complete) return;
        if (action=="UNAVAILABLE") { armed=false; returnSince=-1; return; }
        bool rest=action=="IDLE" && movement=="NEUTRAL";
        if (rest)
        {
            armed=true;
            if (Hits>=2)
            {
                if (returnSince<0) returnSince=Time.realtimeSinceStartupAsDouble;
                if (Time.realtimeSinceStartupAsDouble-returnSince>=.35) { Step++; Hits=0; returnSince=-1; }
            }
            return;
        }
        returnSince=-1;
        if (Hits>=2 || float.IsNaN(confidence) || confidence<.7f) return;
        string received=action;
        if (Step==5 || Step==6) received=(movement=="LEFT" || movement=="RIGHT") && strength>=.25f ? "MOVE_"+movement : "";
        bool discrete=Step<4 || Step==7;
        if (received==Action && (discrete || armed)) { Hits++; armed=false; }
    }
}

public sealed class RedboolPracticeContext
{
    public string run="", action="", phase="";
    public int hits;
}
