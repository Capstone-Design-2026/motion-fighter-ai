using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Editor-only deterministic room UI rendering; no Play, camera backend or network sockets.</summary>
public static class RedboolPvpRoomSelfCheck
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static string Output => Environment.GetEnvironmentVariable("REDBOOL_ROOM_ADDRESS_CHECK_OUTPUT");
    static void Check(bool value, string reason) { checks.Add((value ? "PASS " : "FAIL ") + reason); if (!value) throw new Exception(reason); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Role(bool client, bool dual)
    {
        typeof(RedboolPvp).GetField("<IsClient>k__BackingField", Static).SetValue(null, client);
        typeof(RedboolPvp).GetField("<RequireView>k__BackingField", Static).SetValue(null, dual);
    }
    public static void RunAll()
    {
        Run(); RedboolInputSelfCheck.Run(); RedboolViewSelfCheck.Run(); RedboolPracticeSelfCheck.Run(); RedboolPvpPracticeSelfCheck.Run();
    }
    public static void Run()
    {
        if (!Application.isBatchMode || string.IsNullOrEmpty(Output)) throw new Exception("Isolated graphical batch output required");
        Directory.CreateDirectory(Output); checks.Clear();
        bool oldClient = RedboolPvp.IsClient, oldDual = RedboolPvp.RequireView;
        string clipboard = GUIUtility.systemCopyBuffer;
        Scene oldScene = SceneManager.GetActiveScene(); Scene scene = default(Scene);
        string failure = null;
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); SceneManager.SetActiveScene(scene);
            var root = new GameObject("Room UI selfcheck"); var game = root.AddComponent<RedboolPvp>(); var room = root.AddComponent<RedboolPvpLobby>();
            Role(false, true); Set(room, "game", game); Call(room, "BuildUi");
            var canvas = (Canvas)Get(room, "canvas");
            var camera = new GameObject("Room UI render camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f,.035f,.04f);
            var rows = (GameObject[])Get(room, "roomAddressRows"); var values = (Text[])Get(room, "roomAddressValues"); var buttons = (Button[])Get(room, "roomAddressCopies");
            var sample = new RedboolLanAddress.Snapshot(new[] { "192.168.0.12", "10.0.0.7", "192.168.137.1", "172.16.4.9" }, new[] { "Wi-Fi", "유선 연결", "핫스팟", "다른 연결" });
            Call(room, "ApplyRoomAddresses", sample, false); Call(room, "Refresh", new object[] { null });
            Check(((GameObject)Get(room,"roomShareRoot")).activeSelf, "Host dual room shows join addresses");
            Check(rows.Length == 3 && Array.TrueForAll(rows, r => r.activeSelf) && values[0].text == "192.168.0.12" && values[2].text == "192.168.137.1", "Multiple addresses preserve helper order and show bounded three rows");
            Check(((Text)Get(room,"roomAddressNotice")).text.Contains("1–3 / 4"), "Additional candidates are disclosed");
            Check(!((Text)Get(room,"roomShareStatus")).text.Contains("연결됐"), "An address without a bound listener never claims a connected peer");
            buttons[1].onClick.Invoke(); Check(GUIUtility.systemCopyBuffer == "10.0.0.7", "Copy button copies only chosen IPv4");
            Check(((Text)Get(room,"roomAddressNotice")).text.Contains("복사됨"), "Copy feedback is visible without changing lobby stage");
            Set(room,"addressCopiedUntil",-1d); Call(room,"RefreshRoomAddresses");
            Check(!((Text)Get(room,"roomAddressNotice")).text.Contains("복사됨"), "Copy feedback expires");
            Shot(canvas,camera,"host_multiple");
            var next = (Button)Get(room,"roomAddressNext"); next.onClick.Invoke();
            Check(values[0].text == "172.16.4.9" && rows[0].activeSelf && !rows[1].activeSelf && !rows[2].activeSelf, "Fourth candidate remains reachable on the next page");
            buttons[0].onClick.Invoke(); Check(GUIUtility.systemCopyBuffer == "172.16.4.9", "Next-page copy uses its actual candidate");
            buttons[1].onClick.Invoke(); Check(GUIUtility.systemCopyBuffer == "172.16.4.9", "Hidden prior-page copy cannot reuse stale candidate");
            Set(room,"addressCopiedUntil",-1d); Call(room,"ApplyRoomAddresses",sample,false); Call(room,"RefreshRoomAddresses");
            Check(values[0].text == "172.16.4.9" && ((Text)Get(room,"roomAddressNotice")).text.Contains("4 / 4"), "Identical periodic result preserves current page");
            Shot(canvas,camera,"host_next_page"); next.onClick.Invoke(); Check(values[0].text == "192.168.0.12", "Other-address button wraps to the first page");
            var roomRect = ((GameObject)Get(room,"roomRoot")).GetComponent<RectTransform>();
            var share = ((GameObject)Get(room,"roomShareRoot")).GetComponent<RectTransform>();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(roomRect,share);
            Check(bounds.min.x >= roomRect.rect.xMin && bounds.max.x <= roomRect.rect.xMax && bounds.min.y >= roomRect.rect.yMin && bounds.max.y <= roomRect.rect.yMax, "Address controls fit existing 1040 by 470 plaque");
            var status = (Text)Get(room,"roomStatus"); Check(status.rectTransform.anchoredPosition.x == 255 && status.rectTransform.sizeDelta.x == 440, "Host status keeps its separate right column");
            next.onClick.Invoke();
            Call(room,"ApplyRoomAddresses",new RedboolLanAddress.Snapshot(new[] { "192.168.0.12" },new[] { "Wi-Fi" }),false); Call(room,"Refresh",new object[] { null }); Shot(canvas,camera,"host_one");
            Check(rows[0].activeSelf && !rows[1].activeSelf && !rows[2].activeSelf && values[0].text == "192.168.0.12" && !next.gameObject.activeSelf, "Changed candidate list resets page and hides stale addresses/buttons");
            Call(room,"ApplyRoomAddresses",new RedboolLanAddress.Snapshot(Array.Empty<string>(),Array.Empty<string>(),true),false); Call(room,"Refresh",new object[] { null }); Shot(canvas,camera,"host_no_ipv4");
            Check(Array.TrueForAll(rows,r=>!r.activeSelf) && ((Text)Get(room,"roomAddressEmpty")).text.Contains("IPv4 주소를 찾지 못"), "Translation-only connection gives honest missing IPv4 guidance");
            Call(room,"ApplyRoomAddresses",new RedboolLanAddress.Snapshot(Array.Empty<string>(),Array.Empty<string>(),false,"capture_failed"),false);
            Check(((Text)Get(room,"roomAddressEmpty")).text.Contains("확인하지 못") && !((Text)Get(room,"roomAddressEmpty")).text.Contains("capture_failed"), "Enumeration error has concise UI without raw error codes");
            Call(room,"ApplyRoomAddresses",sample,false);
            Role(true,true); Call(room,"Refresh",new object[] { null }); Shot(canvas,camera,"guest");
            Check(!((GameObject)Get(room,"roomShareRoot")).activeSelf && status.rectTransform.anchoredPosition.x == 0, "Guest never advertises its own addresses as the host");
            string copied = GUIUtility.systemCopyBuffer; buttons[0].onClick.Invoke(); Call(room,"RequestRoomAddresses");
            Check(GUIUtility.systemCopyBuffer == copied && Get(room,"addressTask") == null, "Guest callbacks cannot copy host candidates or launch enumeration");
            Role(false,false); Call(room,"Refresh",new object[] { null });
            Check(!((GameObject)Get(room,"roomShareRoot")).activeSelf, "Single-screen local PvP keeps existing room UI");
            Role(false,true); Set(room,"<Current>k__BackingField",RedboolPvpLobby.Stage.Select); Call(room,"RefreshRoomAddresses"); Call(room,"RequestRoomAddresses");
            Check(!((GameObject)Get(room,"roomShareRoot")).activeSelf && Get(room,"addressTask") == null, "Selection/combat path does not enumerate interfaces");
            Set(room,"<Current>k__BackingField",RedboolPvpLobby.Stage.Room);
            // Publish a completed background result through the actual main-thread poll path.
            Type resultType = typeof(RedboolPvpLobby).GetNestedType("AddressResult",BindingFlags.NonPublic);
            object result = Activator.CreateInstance(resultType); resultType.GetField("snapshot").SetValue(result,sample);
            object task = typeof(Task).GetMethod("FromResult").MakeGenericMethod(resultType).Invoke(null,new[]{result});
            Set(room,"addressTask",task); Set(room,"nextAddressRefresh",100d); Call(room,"TickRoomAddresses",1d);
            Check(Get(room,"addressTask") == null && values[0].text == "192.168.0.12" && buttons[0].interactable, "Completed task applies snapshot on poll and re-enables copy");
            Check(!game.Configured && game.ViewLink == null && game.Receivers[0] == null && game.Receivers[1] == null, "Check started no game receiver or camera backend");
        }
        catch(Exception e) { failure=e.ToString(); }
        finally
        {
            GUIUtility.systemCopyBuffer=clipboard; Role(oldClient,oldDual);
            // This isolated batch owns the new empty scene. Unity cannot close
            // its only scene, so remove only the objects created inside it.
            if(scene.IsValid()) foreach(var owned in scene.GetRootGameObjects()) Object.DestroyImmediate(owned);
            if(oldScene.IsValid() && oldScene != scene) SceneManager.SetActiveScene(oldScene);
            File.WriteAllLines(Path.Combine(Output,"checks.txt"),checks);
            File.WriteAllText(Path.Combine(Output,"result.txt"),failure==null?"PASS\n"+checks.Count+" checks":"FAIL\n"+failure);
        }
        if(failure!=null) throw new Exception(failure);
        Debug.Log("REDBOOL ROOM UI PASS "+checks.Count+" checks; no camera/backend/socket/PlayerPrefs use");
    }
    static void Shot(Canvas canvas,Camera camera,string name)
    {
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        canvas.GetComponent<CanvasScaler>().enabled=false;canvas.scaleFactor=2f/3f;
        var previous=RenderTexture.active;Texture2D image=null;
        try
        {
            Canvas.ForceUpdateCanvases();
            if(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline!=null)
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination=target });
            else camera.Render();
            RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        }
        finally { RenderTexture.active=previous;camera.targetTexture=null;if(image)Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target); }
    }
}
