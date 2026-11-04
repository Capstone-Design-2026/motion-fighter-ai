using UnityEngine;

/// <summary>Small scenery motions; never moves gameplay bodies or changes shared materials.</summary>
public sealed class StoryStageMotion : MonoBehaviour
{
    public float bob = .045f, roll = .6f, speed = .65f, phase;
    public bool flicker;
    Vector3 rest; Quaternion rotation; Light lamp; float intensity;
    void Awake() { rest=transform.localPosition; rotation=transform.localRotation; lamp=GetComponent<Light>(); if(lamp)intensity=lamp.intensity; }
    void Update()
    {
        float t=Time.time*speed+phase;
        if(flicker&&lamp)lamp.intensity=intensity*(.92f+.08f*Mathf.Sin(t*9)+.04f*Mathf.Sin(t*17));
        else { transform.localPosition=rest+Vector3.up*(Mathf.Sin(t)*bob); transform.localRotation=rotation*Quaternion.Euler(0,0,Mathf.Sin(t*.87f)*roll); }
    }
}
