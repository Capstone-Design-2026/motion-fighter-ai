using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RedboolDemoSetup
{
    [MenuItem("Tools/REDBOOL/Add Vision Demo Receiver")]
    private static void Install() {
        if(EditorApplication.isPlaying) { Debug.LogWarning("Stop Play Mode before installing.");return; }
        var player=Object.FindAnyObjectByType<PlayerController>();
        if(player==null) { Debug.LogError("Open Assets/Scenes/GameScene.unity first (PlayerController required).");return; }
        var receiver=Object.FindAnyObjectByType<RedboolDemoReceiver>();
        if(receiver==null) {
            var go=new GameObject("REDBOOL Vision Demo");Undo.RegisterCreatedObjectUndo(go,"Add REDBOOL receiver");
            receiver=Undo.AddComponent<RedboolDemoReceiver>(go);
        }
        Undo.RecordObject(receiver,"Configure REDBOOL receiver");receiver.player=player;
        receiver.port=5065;receiver.bindAddress="0.0.0.0";receiver.expectedActorId="DEV01";
        EditorUtility.SetDirty(receiver);EditorSceneManager.MarkSceneDirty(receiver.gameObject.scene);
        Selection.activeGameObject=receiver.gameObject;
        Debug.Log("REDBOOL ready. Save scene, enter Play Mode, then run Python with this PC's LAN IP. Old 5055 receiver is disabled during Play by the new component.");
    }
}
