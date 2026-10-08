using System;
using UnityEngine;
[Serializable]
public class StoryDialogueLine {
    [Tooltip("Optional entry video, relative to StreamingAssets.")]
    public string videoFileName;
    [Tooltip("Silent loop shown after the entry video until Space is pressed.")]
    public string holdVideoFileName;
    public string speaker;
    [TextArea(2,6)] public string text;
    public Sprite leftPortrait;
    public Sprite rightPortrait;
    public bool speakerOnRight;
}
[CreateAssetMenu(menuName="VisionStriker/Story Cutscene")]
public class StoryCutsceneDefinition : ScriptableObject {
    public string location;
    [Tooltip("Optional video path relative to StreamingAssets. Dialogue remains available for the journal and playback fallback.")]
    public string videoFileName;
    public Sprite background;
    public Texture2D backgroundTexture;
    public StoryDialogueLine[] lines;
}
