using System;
using UnityEngine;

/// <summary>Camera-free deterministic checks for the lobby/practice boundary.</summary>
public static class RedboolPvpPracticeSelfCheck
{
    static int checks;
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    public static void Run()
    {
        checks = 0;
        double now = 0;
        var practice = new RedboolPractice(() => now) { Suspended = true };
        string firstRun = practice.Run;
        for (int i = 0; i < 2; i++) practice.Observe("JAB", 1, "NEUTRAL", 0);
        now = 20; practice.Tick();
        Check(practice.Step == 0 && practice.Hits == 0, "Lobby confirmation punches cannot teach or advance practice");
        practice.Suspended = false;
        practice.Observe("JAB", 1, "NEUTRAL", 0);
        practice.Observe("JAB", 1, "NEUTRAL", 0);
        Check(practice.Hits == 2, "Two validated one-shot events do not need another two-arm guard");
        practice.Suspended = true; now += 5; practice.Tick();
        Check(practice.Step == 0 && practice.Hits == 2, "Selection suspends a pending practice transition");
        practice.Suspended = false; practice.Tick();
        Check(practice.Step == 1, "Unsuspending preserves the validated transition");
        practice.Reset();
        Check(practice.Run != firstRun && practice.Step == 0 && practice.Hits == 0, "A new person receives a new practice run");
        for (int i = 0; i < 4; i++) practice.Skip();
        practice.Observe("IDLE", 1, "NEUTRAL", 0);
        for (int i = 0; i < 20; i++) practice.Observe("BLOCK", 1, "NEUTRAL", 0);
        Check(practice.Hits == 1, "One held block still counts once");
        practice.Observe("UNAVAILABLE", 1, "NEUTRAL", 0);
        practice.Observe("BLOCK", 1, "NEUTRAL", 0);
        Check(practice.Hits == 1, "Camera loss cannot fabricate a release cycle");
        practice.Observe("IDLE", 1, "NEUTRAL", 0); practice.Observe("BLOCK", 1, "NEUTRAL", 0);
        now += 1; practice.Tick();
        Check(practice.Step == 4, "The second block must actually release before advancing");
        practice.Observe("BUSY", 1, "NEUTRAL", 0); now += .16; practice.Observe("BUSY", 1, "NEUTRAL", 0);
        Check(practice.Step == 5, "Relaxing block works without forcing a READY pose");
        foreach(byte gate in new byte[]{1,2,3,4,6})
        {
            string message=RedboolPvp.PracticeMessage(8,0,true,gate,1,false);
            Check(message.Contains("수련 완료")&&!message.Contains("상대가 끝나면"),"Both completed players see the actual wait reason: "+gate);
        }
        Check(RedboolPvp.PracticeMessage(8,0,true,2,0,true).Contains("게임 화면 연결"),"Completed tutorial distinguishes view link waiting");
        Check(RedboolPvp.PracticeMessage(8,0,true,4,0,true).Contains("2P 카메라"),"Completed tutorial identifies the missing player input");
        Check(RedboolPvp.PracticeMessage(8,0,true,6,1,true).Contains("자동 시작"),"Completed tutorial shows the resume countdown state");
        Check(RedboolPvp.PracticeMessage(8,0,false,5,0,true).Contains("상대 준비 대기"),"Only an unfinished opponent produces practice waiting");
        Check(RedboolPvp.PracticeMessage(8,0,false,4,0,false).StartsWith("카메라 신호 복구 대기"),"Already calibrated player is not told to recalibrate during a brief gap");
        Check(RedboolPvp.PracticeMessage(0,0,false,3,0,false).Contains("C로 중앙 가드 보정"),"New player still receives calibration guidance");
        var cardCanvas=new GameObject("PracticeMessageCheck",typeof(RectTransform),typeof(Canvas));
        try {
            var card=RedboolPvpTutorialCard.Create(cardCanvas.transform,0);
            var text=cardCanvas.transform.Find("TutorialCard1/Waiting").GetComponent<UnityEngine.UI.Text>();
            foreach(byte gate in new byte[]{2,3,4,6}) {
                string message=RedboolPvp.PracticeMessage(8,0,true,gate,1,false);
                card.Show(8,0,false,RedboolPvpRoster.Get(0),"필살기",0,message);
                Check(text.gameObject.activeInHierarchy&&text.text==message,"Visible tutorial card uses the same wait reason as host and guest: "+gate);
            }
            card.Hide();Check(!text.gameObject.activeInHierarchy,"Battle entry hides completed tutorial guidance");
        } finally { UnityEngine.Object.DestroyImmediate(cardCanvas); }
        Check(RedboolPvpRoster.Count == 12 && RedboolPvpRoster.Get(0).node == null, "Roster keeps hero plus eleven distinct authored opponents");
        var ids = new System.Collections.Generic.HashSet<string>();
        for (int i = 1; i < RedboolPvpRoster.Count; i++) Check(ids.Add(RedboolPvpRoster.Get(i).node), "Each opponent has one distinct special identity: " + i);
        Debug.Log("REDBOOL PVP PRACTICE PASS " + checks + " checks; no physical camera or sockets");
    }
}
