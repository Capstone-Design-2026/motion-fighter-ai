using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>Presentation-only fixture: actual PvP result UI, no round, sockets or camera backend.</summary>
public static class RedboolPvpResultSelfCheck
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly List<string> checks=new List<string>();
    static string Output=>Environment.GetEnvironmentVariable("REDBOOL_PVP_RESULT_CHECK_OUTPUT");
    static void Check(bool ok,string message) {checks.Add((ok?"PASS ":"FAIL ")+message);if(!ok)throw new Exception(message);}
    static T Get<T>(object value,string name)=>(T)value.GetType().GetField(name,Private).GetValue(value);
    static void Call(object value,string method,params object[] args)=>value.GetType().GetMethod(method,Private).Invoke(value,args);
    static void Show(RedboolPvp game,bool ended,string winner,float remaining,int first,int second,bool remote)
        =>Call(game,"ShowResult",ended,winner,remaining,first,second,remote);
    static CombatAnnouncementStyle.Palette Palette(Text text)
        =>Get<CombatAnnouncementStyle.Palette>(text.GetComponent<CombatAnnouncementStyle>(),"palette");
    public static void Run()
    {
        if(!Application.isBatchMode||string.IsNullOrEmpty(Output))throw new Exception("Isolated graphical batch and REDBOOL_PVP_RESULT_CHECK_OUTPUT required");
        Directory.CreateDirectory(Output);checks.Clear();Scene scene=default(Scene);string failure=null;
        float oldScale=Time.timeScale,oldFixed=Time.fixedDeltaTime;
        try
        {
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var go=new GameObject("PvP result fixture");var game=go.AddComponent<RedboolPvp>();
            Call(game,"BuildUi");
            var canvas=Get<Canvas>(game,"canvas");var panel=Get<GameObject>(game,"resultPanel");
            var headline=Get<Text>(game,"roundHeadline");var caption=Get<Text>(game,"roundResult");
            var rematch=Get<Button>(game,"rematchButton");var reselect=Get<Button>(game,"reselectButton");
            var viewCamera=new GameObject("Result screenshot camera").AddComponent<Camera>();
            viewCamera.clearFlags=CameraClearFlags.SolidColor;viewCamera.backgroundColor=new Color(.09f,.12f,.14f);
            int round=game.Round;float remaining=game.Remaining;string result=game.Result;
            bool resolved=game.Resolved,fighting=game.Fighting,paused=game.Paused;
            string run0=game.Practices[0].Run,run1=game.Practices[1].Run;
            Show(game,true,"1P 승리",45,80,0,false);
            Check(panel.activeSelf&&headline.text=="K.O."&&caption.text.Contains("1P 승리"),"KO preserves the authoritative winner in the Korean caption");
            Check(headline.GetComponent<CombatAnnouncementStyle>()!=null&&Palette(headline)==CombatAnnouncementStyle.Palette.Victory,"Host victory uses the shared upstream battle lettering");
            Check(headline.font==Resources.Load<Font>("UI/Fonts/TradeWinds-Regular")&&caption.font!=headline.font,"Only Latin headline uses TradeWinds; Korean caption retains HUD font");
            Check(!headline.raycastTarget&&caption.GetComponent<CombatAnnouncementStyle>()==null,"Decorative headline does not capture button input or alter Korean glyphs");
            Check(rematch.gameObject.activeSelf&&!reselect.gameObject.activeSelf,"Host keeps the same rematch control; no lobby means no reselect control");
            Shot(canvas,viewCamera,"ko_host_720",1280,720);
            Shot(canvas,viewCamera,"ko_host_1080",1920,1080);
            var style=headline.GetComponent<CombatAnnouncementStyle>();
            for(int i=0;i<120;i++)Show(game,true,"1P 승리",45,80,0,false);
            Check(headline.GetComponents<CombatAnnouncementStyle>().Length==1&&headline.GetComponent<CombatAnnouncementStyle>()==style,"Repeated snapshots reuse one result decoration");
            Show(game,true,"1P 승리",45,80,0,true);
            Check(Palette(headline)==CombatAnnouncementStyle.Palette.Defeat&&caption.text.Contains("1P 화면에서 다시 대전"),"Guest defeat keeps the same winner and host-rematch guidance");
            Check(!rematch.gameObject.activeSelf&&!reselect.gameObject.activeSelf,"Guest does not gain host rematch or reselect authority");
            Shot(canvas,viewCamera,"ko_guest_defeat_720",1280,720);
            Show(game,true,"2P 승리",0,25,60,true);
            Check(headline.text=="TIME UP"&&caption.text.Contains("시간 종료")&&caption.text.Contains("2P 승리")&&Palette(headline)==CombatAnnouncementStyle.Palette.Victory,"Timeout guest victory uses clear headline and original winner");
            Shot(canvas,viewCamera,"timeout_guest_720",1280,720);
            Show(game,true,"2P 승리",0,25,60,false);
            Check(Palette(headline)==CombatAnnouncementStyle.Palette.Defeat,"The same timeout is defeat from the host viewpoint");
            Show(game,true,"무승부",0,25,25,false);
            Check(headline.text=="TIME UP"&&caption.text.Contains("무승부")&&Palette(headline)==CombatAnnouncementStyle.Palette.Ready,"Equal-health timeout shows a neutral draw");
            Shot(canvas,viewCamera,"timeout_draw_720",1280,720);
            Show(game,true,"무승부",17,0,0,false);
            Check(headline.text=="K.O."&&caption.text.Contains("무승부"),"Simultaneous KO keeps a draw and KO headline");
            var lobby=go.AddComponent<RedboolPvpLobby>();
            typeof(RedboolPvp).GetField("<Lobby>k__BackingField",Private).SetValue(game,lobby);
            Show(game,true,"1P 승리",17,1,0,false);
            Check(reselect.gameObject.activeSelf&&rematch.gameObject.activeSelf,"Host lobby retains both rematch and reselect buttons");
            Check(rematch.GetComponent<RectTransform>().anchoredPosition.x==-190&&reselect.GetComponent<RectTransform>().anchoredPosition.x==190,"Existing two-button positions remain separate");
            Shot(canvas,viewCamera,"ko_host_lobby_720",1280,720);
            Show(game,false,"",120,100,100,false);
            Check(!panel.activeSelf,"Ongoing/new round hides the old result panel");
            Check(game.Round==round&&game.Remaining==remaining&&game.Result==result&&game.Resolved==resolved&&game.Fighting==fighting&&game.Paused==paused,"Result presentation cannot change round outcome, time, or gameplay gates");
            Check(game.Practices[0].Run==run0&&game.Practices[1].Run==run1,"Result presentation cannot reset either participant's practice");
            Check(Mathf.Abs(Time.timeScale-oldScale)<.0001f&&Mathf.Abs(Time.fixedDeltaTime-oldFixed)<.000001f,"Result presentation does not take time ownership");
            Check(!game.Configured&&game.ViewLink==null&&game.Receivers[0]==null&&game.Receivers[1]==null&&RedboolCameraRuntime.Instance==null,"Fixture started no game receiver, view link, or physical camera");
        }
        catch(Exception e) {failure=e.ToString();}
        finally
        {
            if(scene.IsValid())foreach(var obj in scene.GetRootGameObjects())Object.DestroyImmediate(obj);
            if(Time.timeScale!=oldScale)Time.timeScale=oldScale;
            if(Time.fixedDeltaTime!=oldFixed)Time.fixedDeltaTime=oldFixed;
            File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);
            File.WriteAllText(Path.Combine(Output,"result.txt"),failure==null?"PASS "+checks.Count:failure);
        }
        if(failure!=null)throw new Exception(failure);
        Debug.Log("REDBOOL PVP RESULT PASS "+checks.Count+"; presentation only, no camera/UDP");
    }
    static void Shot(Canvas canvas,Camera camera,string name,int width,int height)
    {
        var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        canvas.GetComponent<CanvasScaler>().enabled=false;canvas.scaleFactor=width/1920f;
        var previous=RenderTexture.active;Texture2D image=null;
        try
        {
            Canvas.ForceUpdateCanvases();
            if(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline!=null)
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target});
            else camera.Render();
            RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;camera.targetTexture=null;if(image)Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);}
    }
}
