using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Frame both fighters; the complete arena also supports restrained movement-driven orbit.</summary>
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(Camera))]
public sealed class Battle3DCamera : MonoBehaviour
{
    public Transform player;
    public Transform enemy;
    public float minimumDistance = 4.6f, maximumDistance = 16f;
    [Header("전투 화면 여백")]
    [Tooltip("캐릭터 발밑 여백을 줄이도록 두 캐릭터 사이에서 바라보는 높이.")]
    public float focusHeight = 1.2f;
    [Range(.5f, .95f)] public float horizontalSafeArea = .90f;
    [Range(.5f, .9f)] public float verticalSafeArea = .80f;
    [Header("360도 무대")]
    public bool orbitArena;
    public bool allowMouseOrbit = true;
    [Range(7, 30)] public float combatPitch = 7;
    public float orbitSensitivity = 2.4f;
    public Vector3 arenaCenter = Vector3.zero;
    public float cameraBoundaryRadius = 17f;

    [Header("전투 자동 구도 (360도 무대)")]
    public bool automaticCombatFraming = true;
    [Tooltip("전투 중심이 이동할 때 측면에서 벗어나는 최대 각도. 전후가 뒤집히지 않게 제한한다.")]
    [Range(0, 25)] public float maximumMovementYaw = 14f;
    [Range(0, 6)] public float movementYawPerMetre = 3.2f;
    [Min(0)] public float movementDeadZone = .2f;
    [Min(.05f)] public float orbitSmoothTime = .38f;
    [Min(1)] public float maximumOrbitSpeed = 40f;
    [Tooltip("마우스 조작을 멈춘 뒤 자동 전투 구도로 돌아오기까지의 시간.")]
    [Min(0)] public float manualReturnDelay = 2.5f;

    Camera cam;
    Fighter fighter;
    SpecialMove special;
    Vector3 stableSide = Vector3.back;
    float baseFov, yaw, pitch, zoom = 1, horizontalSign = 1;
    float yawVelocity, pitchVelocity, zoomVelocity, manualUntil;
    bool dragging, manualView;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly ActorSilhouette playerShape = new ActorSilhouette(), enemyShape = new ActorSilhouette();
    public float OrbitYaw => Mathf.DeltaAngle(0, yaw);
    public bool IsManualView => manualView;

    public float ScreenHorizontalToWorld(float input)
    {
        if (!orbitArena) return input;
        // Preserve the previous sign at an end-on angle, where screen-horizontal is undefined.
        if (Mathf.Abs(transform.right.x) > .15f) horizontalSign = Mathf.Sign(transform.right.x);
        return input * horizontalSign;
    }
    void Start()
    {
        cam = GetComponent<Camera>(); baseFov = cam.fieldOfView; pitch = combatPitch;
        var p = FindAnyObjectByType<PlayerController>(); var e = FindAnyObjectByType<EnemyAI>();
        if (player == null && p != null) player = p.transform;
        if (enemy == null && e != null) enemy = e.transform;
        if (player != null) { fighter = player.GetComponent<Fighter>(); special = player.GetComponent<SpecialMove>(); }
    }
    void OnApplicationFocus(bool focused) { if (!focused) dragging = false; }
    void OnDisable() { dragging = false; }
    void Update()
    {
        if (!orbitArena || !allowMouseOrbit || Locked()) { dragging = false; return; }
        if (Input.GetKeyDown(KeyCode.Home)) { ResetOrbit(); return; }
        if (Input.GetMouseButtonDown(1))
            dragging = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonUp(1)) dragging = false;
        if (!dragging) return;
        HoldManualView();
        yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * orbitSensitivity, 360);
        pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * orbitSensitivity * .6f, 7, 30);
        zoom = Mathf.Clamp(zoom - Input.mouseScrollDelta.y * .06f, 1, 1.35f);
    }
    bool Locked() => Time.timeScale <= 0 || (fighter != null && fighter.InputLocked)
        || (special != null && special.IsPlaying) || HitImpact.CameraEffectActive;

    void HoldManualView()
    {
        manualView = true; manualUntil = Time.unscaledTime + manualReturnDelay;
        yawVelocity = pitchVelocity = zoomVelocity = 0;
    }
    /// <summary>Blend back to the movement-driven view, without cutting the camera.</summary>
    public void ResetOrbit()
    {
        manualView = dragging = false;
        yawVelocity = pitchVelocity = zoomVelocity = 0;
        if (!automaticCombatFraming) { yaw = 0; pitch = combatPitch; zoom = 1; }
    }
    // Used by scene tools and previews. Automatic framing resumes after the same manual delay.
    public void SetOrbit(float degrees, float elevation = 12, bool snap = false)
    {
        HoldManualView(); yaw = Mathf.Repeat(degrees, 360); pitch = Mathf.Clamp(elevation, 7, 30);
        if (snap && player != null && enemy != null) FrameArena(true);
    }
    void LateUpdate()
    {
        if (player == null || enemy == null) return;
        // Cinematics own their focus/FOV, but a KO or raised foot must still fit.
        // Frame the arena during READY; mouse controls remain locked in Update.
        bool introFraming = fighter != null && fighter.OnlyRoundIntroLocked
            && Time.timeScale > 0f && (special == null || !special.IsPlaying) && !HitImpact.CameraEffectActive;
        if (!Locked() || introFraming) { if (orbitArena) FrameArena(false); else FrameLegacy(); }
        KeepFightersVisible();
    }
    void FrameLegacy()
    {
        Vector3 center = (player.position + enemy.position) * .5f;
        Vector3 focus = new Vector3(center.x, Mathf.Max(player.position.y, enemy.position.y) + focusHeight, center.z);
        Vector3 back = Vector3.back * Mathf.Cos(combatPitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(combatPitch * Mathf.Deg2Rad);
        Vector3 right = Vector3.right, up = Vector3.Cross(-back, right);
        float tan = Mathf.Tan(baseFov * .5f * Mathf.Deg2Rad), distance = minimumDistance;
        FitActor(player, focus, back, right, up, tan * cam.aspect * horizontalSafeArea, tan * verticalSafeArea, ref distance);
        FitActor(enemy, focus, back, right, up, tan * cam.aspect * horizontalSafeArea, tan * verticalSafeArea, ref distance);
        Follow(focus + back * distance, focus, false);
    }
    void UpdateCombatAngle(Vector3 center)
    {
        if (!automaticCombatFraming) return;
        if (manualView && !dragging && Time.unscaledTime >= manualUntil) manualView = false;
        if (manualView) return;

        // Positional, never a time-based spin. Root positions avoid breathing/footwork camera shake.
        Vector3 lateral = Vector3.Cross(stableSide, Vector3.up).normalized;
        float offset = Vector3.Dot(center - arenaCenter, lateral);
        offset = Mathf.Sign(offset) * Mathf.Max(0, Mathf.Abs(offset) - movementDeadZone);
        float targetYaw = Mathf.Clamp(offset * movementYawPerMetre, -maximumMovementYaw, maximumMovementYaw);
        // Scaled time follows the actors during hit-stop; cap long frames to avoid camera snaps.
        float dt = Mathf.Min(Time.deltaTime, .05f);
        if (dt <= 0) return;
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, orbitSmoothTime, maximumOrbitSpeed, dt);
        pitch = Mathf.SmoothDamp(pitch, combatPitch, ref pitchVelocity, orbitSmoothTime, 25, dt);
        zoom = Mathf.SmoothDamp(zoom, 1, ref zoomVelocity, .5f, .7f, dt);
    }
    void FrameArena(bool snap)
    {
        if (cam == null) { cam = GetComponent<Camera>(); baseFov = cam.fieldOfView; }
        Vector3 axis = enemy.position - player.position; axis.y = 0;
        if (axis.sqrMagnitude > .04f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, axis.normalized);
            // Retain the viewing side even if fighter order changes or their roots briefly coincide.
            if (Vector3.Dot(side, stableSide) < 0) side = -side;
            stableSide = snap ? side : Vector3.RotateTowards(stableSide, side,
                maximumOrbitSpeed * Mathf.Deg2Rad * Mathf.Min(Time.deltaTime, .05f), 0);
        }
        Vector3 center = (player.position + enemy.position) * .5f;
        if (!snap) UpdateCombatAngle(center);
        Vector3 focus = new Vector3(center.x, Mathf.Max(player.position.y, enemy.position.y) + focusHeight, center.z);
        Vector3 horizontal = Quaternion.AngleAxis(yaw, Vector3.up) * stableSide;
        Vector3 back = horizontal * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
        Vector3 right = Vector3.Cross(Vector3.up, -back).normalized, up = Vector3.Cross(-back, right);
        float tangent = Mathf.Tan(baseFov * .5f * Mathf.Deg2Rad);
        float tanV = tangent * verticalSafeArea, tanH = tangent * cam.aspect * horizontalSafeArea;
        float distance = minimumDistance;
        FitActor(player, focus, back, right, up, tanH, tanV, ref distance);
        FitActor(enemy, focus, back, right, up, tanH, tanV, ref distance);
        distance = Mathf.Max(distance, Mathf.Min(distance * zoom, maximumDistance));
        Vector3 target = focus + back * distance;
        Vector3 planar = target - arenaCenter; planar.y = 0;
        if (planar.magnitude > cameraBoundaryRadius) target -= planar.normalized * (planar.magnitude - cameraBoundaryRadius);
        Vector3 ray = target - focus; float safeDistance = ray.magnitude;
        int count = Physics.SphereCastNonAlloc(focus, .24f, ray.normalized, hits, safeDistance, 1 << 2, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
            if (hits[i].collider.GetComponent<BattleCameraObstacle>() != null)
                safeDistance = Mathf.Min(safeDistance, Mathf.Max(1.8f, hits[i].distance - .15f));
        target = focus + ray.normalized * safeDistance;
        Follow(target, focus, snap);
    }
    static void FitActor(Transform actor, Vector3 focus, Vector3 back, Vector3 right, Vector3 up, float tanH, float tanV, ref float distance)
    {
        for (int x = -1; x <= 1; x += 2) for (int y = 0; y <= 1; y++) for (int z = -1; z <= 1; z += 2)
        {
            Vector3 p = actor.position + new Vector3(x * .65f, y == 0 ? -.08f : 2.15f, z * .45f) - focus;
            distance = Mathf.Max(distance, Mathf.Abs(Vector3.Dot(p, right)) / tanH + Vector3.Dot(p, back),
                Mathf.Abs(Vector3.Dot(p, up)) / tanV + Vector3.Dot(p, back));
        }
    }
    void Follow(Vector3 position, Vector3 focus, bool snap)
    {
        float dt = orbitArena && automaticCombatFraming && !manualView ? Mathf.Min(Time.deltaTime, .05f) : Time.unscaledDeltaTime;
        float blend = snap ? 1 : 1 - Mathf.Exp(-7 * dt);
        transform.position = Vector3.Lerp(transform.position, position, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(focus - transform.position), blend);
    }

    // Cache the eight silhouette landmarks; never bake a skinned mesh each frame.
    sealed class ActorSilhouette
    {
        static readonly HumanBodyBones[] Bones = { HumanBodyBones.Head, HumanBodyBones.Hips,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
        Transform owner; Fighter3DView view; Animator animator;
        readonly Transform[] points = new Transform[Bones.Length];
        public void Fit(Transform actor, Camera camera, ref float retreat)
        {
            if (owner != actor) { owner = actor; view = actor.GetComponent<Fighter3DView>(); animator = null; }
            if (view == null || view.modelAnimator == null || !view.modelAnimator.isHuman) return;
            if (animator != view.modelAnimator)
            {
                animator = view.modelAnimator;
                for (int i = 0; i < Bones.Length; i++) points[i] = animator.GetBoneTransform(Bones[i]);
            }
            for (int i = 0; i < points.Length; i++)
                if (points[i]) FitLandmark(camera, points[i].position, i == 0 ? .23f : .13f, ref retreat);
        }
    }
    static void FitLandmark(Camera camera, Vector3 point, float radius, ref float retreat)
    {
        Vector3 local = camera.transform.InverseTransformPoint(point);
        float tan = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        // 3% side margins; protect feet and keep the head below the HUD.
        retreat = Mathf.Max(retreat,
            (Mathf.Abs(local.x) + radius) / (tan * camera.aspect * .94f) - local.z + radius,
            (local.y + radius) / (tan * .72f) - local.z + radius,
            (-local.y + radius) / (tan * .86f) - local.z + radius);
    }
    void KeepFightersVisible()
    {
        if (!cam || cam.orthographic) return;
        float retreat = 0;
        playerShape.Fit(player, cam, ref retreat); enemyShape.Fit(enemy, cam, ref retreat);
        if (retreat > .001f) transform.position -= transform.forward * retreat;
    }
}
