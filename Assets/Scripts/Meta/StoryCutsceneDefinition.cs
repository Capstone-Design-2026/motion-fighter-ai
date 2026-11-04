using System;
using UnityEngine;
[Serializable]
public class StoryDialogueLine {
    [Tooltip("Optional entry video, relative to StreamingAssets.")]
    public string videoFileName;
    [Tooltip("Silent loop shown after the entry video until Space is pressed.")]
    public string holdVideoFileName;
    public AudioClip voice;
    [HideInInspector] public float[] voiceEnvelope;
    [Tooltip("Tracked mouth positions for entry/hold cinematics; omitted for off-screen speech.")]
    public TextAsset videoSpeechTrack;
    public string speaker;
    public StoryEvidence evidence;
    public string evidenceTitle;
    [TextArea(2,6)] public string text;
    public Sprite leftPortrait;
    public Sprite rightPortrait;
    public bool speakerOnRight;
    public StoryActorDefinition leftActor;
    public StoryActorDefinition rightActor;
}
[CreateAssetMenu(menuName="VisionStriker/Story Cutscene")]
public class StoryCutsceneDefinition : ScriptableObject {
    public string location;
    [Tooltip("Optional video path relative to StreamingAssets. Dialogue remains available for the journal and playback fallback.")]
    public string videoFileName;
    public Sprite background;
    public Texture2D backgroundTexture;
    public Rect backgroundUv = new Rect(0,0,1,1);
    public StoryDialogueLine[] lines;
}
