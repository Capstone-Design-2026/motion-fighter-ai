using UnityEngine;

[CreateAssetMenu(menuName="VisionStriker/Story Actor")]
public class StoryActorDefinition : ScriptableObject
{
    public string displayName;
    public Texture2D artwork;
    public Rect crop = new Rect(0,0,1,1);
    [Tooltip("Coordinates within the cropped portrait, bottom-left origin.")]
    public Vector2 mouth = new Vector2(.5f,.72f);
    public Vector2 hand = new Vector2(.74f,.25f);
    public bool masked;
}
