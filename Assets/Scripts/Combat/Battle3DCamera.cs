using UnityEngine;

/// <summary>Frames both fighters; leaves finishing/special zooms to HitImpact.</summary>
[RequireComponent(typeof(Camera))]
public sealed class Battle3DCamera : MonoBehaviour
{
    public Transform player;
    public Transform enemy;
    public float minimumDistance = 6.3f;
    public float maximumDistance = 12f;
    private Camera cam;
    private Fighter fighter;
    private SpecialMove special;
    void Start()
    {
        cam=GetComponent<Camera>();
        var p=FindAnyObjectByType<PlayerController>();var e=FindAnyObjectByType<EnemyAI>();
        if(p!=null){player=p.transform;fighter=p.GetComponent<Fighter>();special=p.GetComponent<SpecialMove>();}
        if(e!=null)enemy=e.transform;
    }
    void LateUpdate()
    {
        if(player==null||enemy==null||(fighter!=null&&fighter.InputLocked)||(special!=null&&special.IsPlaying))return;
        float width=Mathf.Abs(player.position.x-enemy.position.x)+3f;
        float distance=Mathf.Clamp(width/(2f*Mathf.Tan(cam.fieldOfView*.5f*Mathf.Deg2Rad)*cam.aspect),minimumDistance,maximumDistance);
        float center=(player.position.x+enemy.position.x)*.5f;
        float height=1.05f+Mathf.Max(player.position.y,enemy.position.y)*.3f;
        Vector3 target=new Vector3(center,height+distance*.17f,-distance);
        float blend=1f-Mathf.Exp(-5f*Time.deltaTime);
        transform.position=Vector3.Lerp(transform.position,target,blend);
        transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(new Vector3(center,height,0)-transform.position),blend);
    }
}
