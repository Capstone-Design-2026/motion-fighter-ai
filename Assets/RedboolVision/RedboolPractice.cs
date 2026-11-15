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
    public int Skipped { get; private set; }
    public string Action => Complete ? "" : Actions[Step];
    public string Phase => Complete ? "COMPLETE" : Hits >= 2 ? "RETURN" : "PRACTICE";
    public string Instruction => Describe(Step, Hits);
    public static string Describe(int step,int hits) => step>=Actions.Length ? "수련 완료 · 상대 준비 대기"
        : hits>=2 ? (step==4 ? "블락 성공! 팔을 편하게 내려주세요" : step==5 || step==6 ? "이동 성공! 몸을 중앙으로 돌려주세요" : "인식 성공! 다음 동작으로")
        : Instructions[step]+"  "+hits+"/2";
    bool armed;
    /// <summary>인물 선택이 끝나기 전에는 수련을 세지 않는다 (RedboolPvpLobby).</summary>
    public bool Suspended;
    double returnSince = -1;
    readonly Func<double> clock;
    readonly Func<long> sourceClock;
    readonly RedboolPracticeContinuity continuity=new RedboolPracticeContinuity();
    bool sourceTiming;
    public RedboolPractice(Func<double> clock=null,Func<long> sourceClock=null) { this.clock=clock??(()=>Time.realtimeSinceStartupAsDouble);this.sourceClock=sourceClock??RedboolPracticeContinuity.ClockNs;Reset(); }
    public void Reset() { Run=Guid.NewGuid().ToString("N"); Step=Hits=Skipped=0; armed=false; returnSince=-1;continuity.Reset();sourceTiming=false; }
    void Advance() { Step++;Hits=0;armed=true;returnSince=-1;continuity.Reset();sourceTiming=false; }
    public void Skip() { if(!Complete) {Skipped++;Advance();} }
    // Discrete events already passed v26's onset/return/re-arm checks. Requiring
    // both arms READY again here strands recognized gestures in a second FSM.
    public void Tick() {
        if(Suspended){continuity.Reset();sourceTiming=false;armed=false;if(Hits<2||Step>=4&&Step<=6)returnSince=-1;return;}
        long now=sourceClock();
        if(sourceTiming&&continuity.Tick(now)){armed=false;if(Step>=4&&Step<=6)returnSince=-1;}
        if(!Complete && Hits>=2 && (Step<4 || Step==7) && returnSince>=0 && clock()-returnSince>=.25) Advance();
        else if(!Complete&&Hits>=2&&Step>=4&&Step<=6&&sourceTiming&&continuity.Fresh(now)&&continuity.RestNs>=150000000L)Advance();
    }
    public void ObserveInput(RedboolPracticeInput input)
    {
        if(Complete||Suspended){continuity.Reset();sourceTiming=false;return;}
        bool rest=input!=null&&(Step==4 ? input.Action!="BLOCK" : Step==5||Step==6 ? input.Movement=="NEUTRAL" : input.Action=="IDLE");
        var result=continuity.Observe(input,rest);
        if(result==RedboolPracticeContinuity.Result.Legacy){sourceTiming=false;Observe(input.Action,input.Confidence,input.Movement,input.Strength);return;}
        sourceTiming=true;
        if(result==RedboolPracticeContinuity.Result.Reset){armed=false;if(Hits<2||Step>=4&&Step<=6)returnSince=-1;return;}
        if(result==RedboolPracticeContinuity.Result.Paused)return;
        if(result==RedboolPracticeContinuity.Result.FreshReset){armed=false;if(Step>=4&&Step<=6)returnSince=-1;}
        ObserveCore(input.Action,input.Confidence,input.Movement,input.Strength,true);
    }
    public void Observe(string action, float confidence, string movement, float strength)
    { continuity.Reset();sourceTiming=false;ObserveCore(action,confidence,movement,strength,false); }
    void ObserveCore(string action, float confidence, string movement, float strength,bool bounded)
    {
        if (Complete || Suspended) return;
        if (action=="UNAVAILABLE") { armed=false; if(Hits<2 || Step>=4 && Step<=6) returnSince=-1; return; }
        bool rest=(Step==4 ? action!="BLOCK" : Step==5 || Step==6 ? movement=="NEUTRAL" : action=="IDLE");
        if (rest)
        {
            armed=true;
            if (Hits>=2)
            {
                if (returnSince<0) returnSince=clock();
                if ((Step==4 || Step==5 || Step==6) && (bounded
                    ? continuity.Fresh(sourceClock())&&continuity.RestNs>=150000000L
                    : clock()-returnSince>=.15)) Advance();
            }
            return;
        }
        if (Hits>=2) { if(Step>=4 && Step<=6) returnSince=-1;return; }
        returnSince=-1;
        if (float.IsNaN(confidence) || confidence<.7f) return;
        string received=action;
        if (Step==5 || Step==6) received=(movement=="LEFT" || movement=="RIGHT") && strength>=.25f ? "MOVE_"+movement : "";
        bool discrete=Step<4 || Step==7;
        if (received==Action && (discrete || armed)) { Hits++; armed=false;if(Hits>=2 &&discrete) returnSince=clock(); }
    }
}

public sealed class RedboolPracticeContext
{
    public string run="", action="", phase="";
    public int hits;
}
