using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicitly invoked, unsaved play-mode verification. Never runs during normal play/builds.</summary>
[InitializeOnLoad]
public static class CombatHUDVerification
{
    const string Flag="CombatHUDVerification.Active",SaveKey="VisionStriker.Run";
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator flow;
    static int lastFrame=-1;
    static CombatHUD hud; static BattleSceneBridge battle;
    static Health player,enemy; static SpecialMove special;
    static Canvas canvas;static Camera camera;
    static WuxiaCombatBar hp,enemyHP,gauge,enemyGauge;
    static Text timer;
    static string Out=>UnifiedBoxingInstaller.Out;
    static CombatHUDVerification(){if(SessionState.GetBool(Flag,false)){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}}
    public static void Run()
    {
        Directory.CreateDirectory(Out);
        SessionState.SetBool(Flag+".HadSave",PlayerPrefs.HasKey(SaveKey));
        SessionState.SetString(Flag+".Save",PlayerPrefs.GetString(SaveKey,""));
        SessionState.SetFloat(Flag+".Started",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Flag,true);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        EditorApplication.delayCall+=()=>EditorApplication.EnterPlaymode();
    }
    static void Log(string m,string stack,LogType t)
    {
        if((t==LogType.Error||t==LogType.Exception)&&!m.Contains("MCP")&&!stack.Contains("IvanMurzak"))errors.Add(m+"\n"+stack);
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+".Started",0)>240){errors.Add("HUD verification timeout");Finish();return;}
        if(!Application.isPlaying||EditorApplication.isCompiling)return;
        Application.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.QueuePlayerLoopUpdate();
        if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(flow==null)flow=Tests();if(!flow.MoveNext())Finish();}
        catch(Exception e){errors.Add(e.ToString());Finish();}
    }
    static void Finish()
    {
        SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        bool hadSave=SessionState.GetBool(Flag+".HadSave",false);
        string save=SessionState.GetString(Flag+".Save","");
        if(hadSave)PlayerPrefs.SetString(SaveKey,save);else PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        Check(PlayerPrefs.HasKey(SaveKey)==hadSave&&(!hadSave||PlayerPrefs.GetString(SaveKey)==save),"Campaign save key presence and exact contents restored on fixture exit");
        File.WriteAllLines(Path.Combine(Out,"runtime_errors.txt"),errors);
        bool ok=errors.Count==0&&checks.All(c=>c.StartsWith("PASS"));
        File.WriteAllText(Path.Combine(Out,"verification_done.txt"),ok?"PASS":"FAIL");
        EditorApplication.Exit(ok?0:1);
    }
    static void Check(bool ok,string detail)
    {checks.Add((ok?"PASS ":"FAIL ")+detail);File.WriteAllLines(Path.Combine(Out,"checks.txt"),checks);}
    static void Invoke(object o,string m)=>o.GetType().GetMethod(m,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
    static void TimeLeft(float t)=>typeof(BattleSceneBridge).GetField("<RemainingSeconds>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(battle,t);
    static IEnumerator Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)yield return null;}
    // 2026-11-16: every round restart replays 승준's READY/FIGHT intro, which holds the timer and damage (~1.7 s).
    static IEnumerator UntilIntroDone()
    {
        RedboolBattlePreparation.SetKeyboardTest(true);
        var intro=UnityEngine.Object.FindAnyObjectByType<BattleIntroSequence>();float end=Time.realtimeSinceStartup+6;
        while(intro!=null&&intro.HoldRound&&Time.realtimeSinceStartup<end)yield return null;
        Check(intro==null||!intro.HoldRound,"READY/FIGHT intro releases the round");
    }
    static IEnumerator Tests()
    {
        yield return null;yield return null;
        hud=UnityEngine.Object.FindAnyObjectByType<CombatHUD>();
        battle=UnityEngine.Object.FindAnyObjectByType<BattleSceneBridge>();
        var pc=UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var ai=UnityEngine.Object.FindAnyObjectByType<EnemyAI>();
        player=pc.GetComponent<Health>();enemy=ai.GetComponent<Health>();special=pc.GetComponent<SpecialMove>();
        pc.enabled=false;ai.enabled=false;
        foreach(var f in UnityEngine.Object.FindObjectsByType<Fighter>()){f.CancelAttack();f.InputLocked=true;}
        battle.enabled=false;
        canvas=hud.GetComponentInChildren<Canvas>();camera=Camera.main;
        var rail=canvas.transform.Find("DuelHeader");
        hp=rail.Find("PlayerVitals/HealthBar").GetComponent<WuxiaCombatBar>();
        enemyHP=rail.Find("EnemyVitals/HealthBar").GetComponent<WuxiaCombatBar>();
        gauge=rail.Find("PlayerVitals/SpecialGauge").GetComponent<WuxiaCombatBar>();
        enemyGauge=rail.Find("EnemyVitals/SpecialGauge").GetComponent<WuxiaCombatBar>();
        timer=rail.Find("RoundTimer/Seconds").GetComponent<Text>();
        Check(hp!=null&&enemyHP!=null&&gauge!=null&&timer!=null,"Real GameScene constructs all HUD widgets");
        Check(!hp.mirror&&enemyHP.mirror,"Opposing HP fill directions");
        Check(enemy.GetComponent<SpecialMove>()==null&&!enemyGauge.Available&&enemyGauge.FillRatio==0,"Enemy without SpecialMove has no fabricated charge");
        Check(canvas.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"HUD does not intercept gameplay/UI pointer events");
        Check(rail.Find("PlayerVitals/FighterName").GetComponent<Text>().text=="서청운","PLAYER placeholder displays protagonist name");
        player.ResetHealth();enemy.ResetHealth();special.ResetCharge();TimeLeft(120);
        // Keep the real keyboard panel available in-game; hide it only for the clean screenshot.
        canvas.transform.Find("KeyGuide")?.gameObject.SetActive(false);
        for(var w=Wait(.8f);w.MoveNext();)yield return null;
        Check(hp.FillRatio==1&&enemyHP.FillRatio==1&&gauge.FillRatio==0&&timer.text=="120","Full HP / empty charge / 120 seconds at reset");
        for(var c=Capture("01_full",1920,1080);c.MoveNext();)yield return null;

        player.SetCurrentHealth(Mathf.RoundToInt(player.MaxHealth*.72f));
        enemy.SetCurrentHealth(Mathf.RoundToInt(enemy.MaxHealth*.36f));
        special.AddCharge(2);TimeLeft(87.1f);yield return null;
        Check(Mathf.Abs(hp.FillRatio-(float)player.CurrentHealth/player.MaxHealth)<.0001f,"Carried / changed player health is exact");
        Check(Mathf.Abs(enemyHP.FillRatio-(float)enemy.CurrentHealth/enemy.MaxHealth)<.0001f,"Boss HP uses its own maximum");
        Check(hp.TrailRatio>hp.FillRatio&&enemyHP.TrailRatio>enemyHP.FillRatio,"Damage leaves a visible delayed trail");
        Check(Mathf.Abs(gauge.FillRatio-2f/special.HitsToCharge)<.0001f&&rail.Find("PlayerVitals/GaugeLabel").GetComponent<Text>().text=="파운장 충전 중","Real SpecialMove charge updates the blue bar without a fraction label");
        Check(timer.text=="88","Remaining time rounds up");
        for(var c=Capture("02_damage",1920,1080);c.MoveNext();)yield return null;
        for(var w=Wait(1.7f);w.MoveNext();)yield return null;
        Check(Mathf.Approximately(hp.TrailRatio,hp.FillRatio)&&Mathf.Approximately(enemyHP.TrailRatio,enemyHP.FillRatio),"Damage trail catches up");
        player.Heal(10);yield return null;
        Check(Mathf.Approximately(hp.TrailRatio,hp.FillRatio),"Healing clears obsolete damage trail");
        special.AddCharge(99);yield return null;
        Check(gauge.FillRatio==1&&gauge.Ready&&rail.Find("PlayerVitals/GaugeLabel").GetComponent<Text>().text=="파운장 준비 · 가슴 앞 X자","Full charge uses the actual camera gesture instead of an inactive keyboard shortcut");
        for(var c=Capture("03_ready",1920,1080);c.MoveNext();)yield return null;
        special.ResetCharge();yield return null;
        Check(gauge.FillRatio==0&&!gauge.Ready,"Consumed/reset charge clears readiness");

        TimeLeft(9.2f);yield return null;
        Check(timer.text=="10"&&timer.color.g<.5f,"Last ten seconds have warning color");
        for(var c=Capture("04_warning",1920,1080);c.MoveNext();)yield return null;
        enemy.Configure("수문요새 · 호송대 감찰관",350);enemy.SetCurrentHealth(175);yield return null;
        Check(enemyHP.FillRatio==.5f&&rail.Find("EnemyVitals/FighterName").GetComponent<Text>().text.Contains("감찰관"),"Stage name and different maximum health update");
        for(var c=Capture("05_ultrawide",2560,1080);c.MoveNext();)yield return null;
        for(var c=Capture("06_classic",1440,1080);c.MoveNext();)yield return null;

        Invoke(hud,"RestartRound");yield return null;
        Check(hp.FillRatio==1&&enemyHP.FillRatio==1&&gauge.FillRatio==0&&timer.text=="120","Standalone round restart restores HUD values");
        battle.enabled=true;
        for(var w=UntilIntroDone();w.MoveNext();)yield return null;
        for(var w=Wait(.3f);w.MoveNext();)yield return null;
        Check(battle.RemainingSeconds<120&&battle.RemainingSeconds>118,"Real round countdown advances");
        Time.timeScale=0;float paused=battle.RemainingSeconds;
        for(var w=Wait(.2f);w.MoveNext();)yield return null;
        Check(Mathf.Approximately(paused,battle.RemainingSeconds),"Timer pauses with gameplay");Time.timeScale=1;
        player.SetCurrentHealth(player.MaxHealth/2);enemy.SetCurrentHealth(enemy.MaxHealth/2);
        // Equal exact ratios, including different maximum HP.
        player.SetCurrentHealth(player.MaxHealth);enemy.SetCurrentHealth(enemy.MaxHealth);
        TimeLeft(.001f);for(var w=Wait(.15f);w.MoveNext();)yield return null;
        Check(battle.IsResolved&&canvas.transform.Find("BattleResult/KOText").GetComponent<Text>().text.Contains("YOU LOSE"),"Equal HP ratio at timeout is a player loss");
        var headline=canvas.transform.Find("BattleResult/KOText").GetComponent<Text>();
        var caption=canvas.transform.Find("BattleResult/ResultCaption").GetComponent<Text>();
        var restart=canvas.transform.Find("BattleResult/RestartHint").GetComponent<Text>();
        Check(caption.text=="시간 종료 · 패배" &&restart.text.Contains("다시 시작")&&restart.gameObject.activeSelf,"Result keeps timeout cause and the Korean restart instruction separate from decorative lettering");
        Check(headline.font==Resources.Load<Font>("UI/Fonts/TradeWinds-Regular") &&headline.GetComponents<CombatAnnouncementStyle>().Length==1,"Result uses the bundled announcement font and one bounded mesh effect");
        Check(restart.font!=headline.font&&!restart.raycastTarget&&!headline.raycastTarget,"Korean restart text retains the regular HUD font and does not intercept input");
        for(var c=Capture("07_timeout",1920,1080);c.MoveNext();)yield return null;
        Invoke(hud,"RestartRound");
        Check(!canvas.transform.Find("BattleResult").gameObject.activeSelf,"Restart clears the entire result panel, including caption and restart hint");
        for(var w=UntilIntroDone();w.MoveNext();)yield return null;
        player.SetCurrentHealth(player.MaxHealth);enemy.SetCurrentHealth(1);TimeLeft(.001f);
        for(var w=Wait(.15f);w.MoveNext();)yield return null;
        Check(battle.IsResolved&&canvas.transform.Find("BattleResult/KOText").GetComponent<Text>().text.Contains("YOU WIN"),"Higher HP ratio at timeout wins");
        Invoke(hud,"RestartRound");
        for(var w=UntilIntroDone();w.MoveNext();)yield return null;
        battle.enabled=false;
        enemy.TakeDamage(10000,Vector2.zero);yield return null;
        Check(enemyHP.FillRatio==0&&canvas.transform.Find("BattleResult").gameObject.activeSelf,"KO empties health and preserves result overlay");
        for(var c=Capture("08_ko",1920,1080);c.MoveNext();)yield return null;
        // 2026-11-16: story battles also show the K.O./YOU LOSE lettering; the bridge keeps the one Korean result line under it.
        var runMode=typeof(BattleSceneBridge).GetField("runMode",BindingFlags.NonPublic|BindingFlags.Instance);
        bool previousRunMode=(bool)runMode.GetValue(battle);
        try
        {
            canvas.transform.Find("BattleResult").gameObject.SetActive(false);
            runMode.SetValue(battle,true);
            hud.ShowTimeOut(false);
            Check(canvas.transform.Find("BattleResult").gameObject.activeSelf&&!canvas.transform.Find("BattleResult/ResultCaption").gameObject.activeSelf,"Story timeout shows the lettering without repeating the bridge's Korean line");
            canvas.transform.Find("BattleResult").gameObject.SetActive(false);
            typeof(CombatHUD).GetMethod("OnDeath",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(hud,new object[]{enemy});
            Check(canvas.transform.Find("BattleResult").gameObject.activeSelf&&canvas.transform.Find("BattleResult/KOText").GetComponent<Text>().text=="K.O."&&!canvas.transform.Find("BattleResult/ResultCaption").gameObject.activeSelf,"Story KO shows K.O. lettering with the bridge owning the Korean line");
            canvas.transform.Find("BattleResult").gameObject.SetActive(false);
            Invoke(battle,"BuildBanner");
            typeof(BattleSceneBridge).GetMethod("ShowBanner",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(battle,new object[]{false,true});
            var banner=(Text)typeof(BattleSceneBridge).GetField("bannerText",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(battle);
            Check(banner.gameObject.activeSelf&&banner.text=="시간 종료 · 패배 · 이어하기로 다시 도전할 수 있습니다","Story result immediately gives the Korean cause and next route");
            banner.transform.parent.gameObject.SetActive(false);
        }
        finally {runMode.SetValue(battle,previousRunMode);}
        CheckExternalHud();
        UnityEngine.Object.Destroy(hud.gameObject);yield return null;
        player.Heal(1);special.AddCharge();yield return null;
        Check(errors.Count==0,"No dangling HUD event callbacks / runtime errors");
    }
    static void CheckExternalHud()
    {
        var active=typeof(RedboolPvp).GetField("<Active>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);
        bool previousActive=(bool)active.GetValue(null);
        GameObject go=null;
        try
        {
            // Only select the HUD's external-data path. No match, socket, camera, or scene is started.
            active.SetValue(null,true);
            go=new GameObject("ExternalCombatHUDCheck");
            var external=go.AddComponent<CombatHUD>();
            active.SetValue(null,previousActive);
            Check(!external.enabled&&!external.ExternalVisible,"Shared PvP HUD starts hidden before the lobby admits combat");
            external.SetExternalVisible(true);
            external.ShowExternalFighter(true,"1P 서청운",72,100);
            external.ShowExternalFighter(false,"2P 철문",90,150);
            external.ShowExternalGauge(true,"파운장",2,5,false);
            external.ShowExternalGauge(false,"파운장",5,5,false);
            external.ShowExternalClock(87.1f);
            var view=external.GetComponentInChildren<Canvas>();
            var top=view.transform.Find("DuelHeader");
            var left=top.Find("PlayerVitals/HealthBar").GetComponent<WuxiaCombatBar>();
            var right=top.Find("EnemyVitals/HealthBar").GetComponent<WuxiaCombatBar>();
            var qi=top.Find("PlayerVitals/SpecialGauge").GetComponent<WuxiaCombatBar>();
            Check(external.ExternalVisible&&external.ExternalClockText=="88","Shared PvP HUD shows the supplied authoritative clock");
            Check(Mathf.Approximately(left.FillRatio,.72f)&&Mathf.Approximately(right.FillRatio,.6f)&&!left.mirror&&right.mirror,"Shared HUD keeps exact distinct health maxima and opposing fill directions");
            Check(Mathf.Approximately(qi.FillRatio,.4f)&&!qi.Ready&&external.ExternalSummary(true).Contains("파운장  충전 중"),"Partial special charge uses the bar rather than a fraction label");
            Check(external.ExternalSummary(false).Contains("준비 · 가슴 앞 X자"),"Ready PvP special describes the actual X gesture");
            external.ShowExternalGauge(true,"파운장",0,5,true);
            Check(qi.FillRatio==1&&qi.Ready&&external.ExternalSummary(true).Contains("발동"),"An active special stays lit from authoritative firing state after spending charge");
            external.ShowExternalFighter(true,"1P 서청운",50,100);
            Check(left.TrailRatio>left.FillRatio,"Shared HUD damage creates the same delayed health trail as story");
            external.ShowExternalFighter(true,"1P 서청운",100,100);
            Check(left.FillRatio==1&&left.TrailRatio==1,"Shared HUD rematch/heal clears an obsolete damage trail");
            external.SetExternalVisible(false);
            Check(!external.enabled&&!external.ExternalVisible,"Lobby/result transitions can hide the shared HUD without owning gameplay");
            external.SetExternalVisible(true);
            Check(go.GetComponentsInChildren<Canvas>(true).Length==1&&view.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget),"Showing the shared HUD again neither duplicates its canvas nor intercepts input");
        }
        finally
        {
            active.SetValue(null,previousActive);
            if(go!=null)UnityEngine.Object.Destroy(go);
        }
    }
    static IEnumerator Capture(string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,24);rt.Create();
        camera.targetTexture=rt;camera.aspect=(float)width/height;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+.1f;
        yield return null;yield return null;yield return null;
        Canvas.ForceUpdateCanvases();Invoke(hud,"LateUpdate");Canvas.ForceUpdateCanvases();
        foreach(var graphic in new[]{hp,enemyHP,gauge,enemyGauge})
        {
            var mesh=graphic.canvasRenderer!=null?graphic.canvasRenderer.GetMesh():null;
            Check(mesh!=null&&mesh.vertexCount>0,name+" "+graphic.transform.parent.name+"/"+graphic.name+" has rendered geometry");
        }
        var a=ScreenRect(hp.rectTransform);var b=ScreenRect(enemyHP.rectTransform);var t=ScreenRect(timer.rectTransform);
        Check(a.xMin>=0&&b.xMax<=width+1&&a.xMax<t.xMin&&t.xMax<b.xMin,name+" HP/timer stay on screen without overlap at "+width+"x"+height);
        camera.Render();RenderTexture.active=rt;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
        File.WriteAllBytes(Path.Combine(Out,name+".png"),tex.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        UnityEngine.Object.Destroy(tex);rt.Release();UnityEngine.Object.Destroy(rt);
        yield return null;
    }
    static Rect ScreenRect(RectTransform r)
    {
        var corners=new Vector3[4];r.GetWorldCorners(corners);
        var p=corners.Select(v=>(Vector2)camera.WorldToScreenPoint(v)).ToArray();
        return Rect.MinMaxRect(p.Min(v=>v.x),p.Min(v=>v.y),p.Max(v=>v.x),p.Max(v=>v.y));
    }
}
