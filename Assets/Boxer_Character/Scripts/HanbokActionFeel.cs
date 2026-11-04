using UnityEngine;

// 한복 캐릭터 "절도감" 조절용 스크립트
// 2단계(완급): 동작 구간별 재생 속도 + 타격 순간 멈춤(히트스톱)
// 3단계(연출): 카메라 흔들림 + 주먹 궤적 + 타격 번쩍임
// Inspector 체크박스로 각각 켜고 끌 수 있음 (회의에서 전/후 비교용)
[RequireComponent(typeof(Animator))]
public class HanbokActionFeel : MonoBehaviour
{
    [Header("2단계: 완급 (속도 곡선 + 타격 멈춤)")]
    [Tooltip("끄면 원래 Mixamo 속도 그대로 재생")]
    public bool useTiming = true;

    [Tooltip("ATTACK 구간별 재생 속도. 가로=동작 진행(0~1), 세로=배속")]
    public AnimationCurve attackSpeed = new AnimationCurve(
        new Keyframe(0.00f, 2.2f), new Keyframe(0.24f, 2.2f),   // 준비 동작: 2.2배로 빠르게
        new Keyframe(0.27f, 1.6f), new Keyframe(0.44f, 1.6f),   // 주먹 뻗기: 1.6배
        new Keyframe(0.47f, 0.9f), new Keyframe(1.00f, 0.9f));  // 복귀: 0.9배로 천천히

    [Tooltip("주먹이 가장 멀리 뻗는 순간 (ATTACK 진행 0~1). 측정값 0.45 = 0.57초")]
    [Range(0f, 1f)] public float attackImpact = 0.45f;

    [Tooltip("타격 순간 캐릭터가 멈추는 시간(초). 0이면 멈춤 없음")]
    [Range(0f, 0.2f)] public float hitStop = 0.07f;

    [Tooltip("JUMP 구간별 재생 속도")]
    public AnimationCurve jumpSpeed = new AnimationCurve(
        new Keyframe(0.00f, 1.6f), new Keyframe(0.24f, 1.6f),   // 웅크리기: 빠르게
        new Keyframe(0.27f, 1.05f), new Keyframe(0.56f, 1.05f), // 공중
        new Keyframe(0.60f, 1.3f), new Keyframe(1.00f, 1.3f));  // 착지 후 일어나기: 빠르게

    [Tooltip("착지 순간 (JUMP 진행 0~1). 측정값 0.58 = 1.1초")]
    [Range(0f, 1f)] public float jumpLand = 0.58f;

    [Header("3단계: 연출")]
    public bool useCameraShake = true;
    [Range(0f, 0.2f)] public float shakeStrength = 0.05f;
    [Range(0f, 0.5f)] public float shakeTime = 0.15f;
    [Range(0f, 1f)] public float landShakeScale = 0.5f;

    public bool useFistTrail = true;
    public HumanBodyBones strikeHand = HumanBodyBones.LeftHand;
    [Tooltip("주먹 궤적이 그려지는 구간 (ATTACK 진행 0~1)")]
    public Vector2 trailWindow = new Vector2(0.25f, 0.50f);
    public Material trailMaterial;
    public Color trailColor = new Color(1f, 0.85f, 0.55f, 0.9f);

    public bool useImpactFlash = true;
    public Material flashMaterial;
    public Color flashColor = new Color(1f, 0.9f, 0.6f, 1f);

    [Header("새 동작: 앉아서 공격 / 점프 공격")]
    [Tooltip("CROUCHATTACK 타격 순간 (진행 0~1)")]
    [Range(0f, 1f)] public float crouchAttackImpact = 0.42f;
    [Tooltip("CROUCHATTACK 궤적 구간")]
    public Vector2 crouchAttackTrail = new Vector2(0.22f, 0.52f);
    [Tooltip("CROUCHATTACK 에서 궤적·번쩍임을 붙일 뼈 (펀치면 손, 킥이면 발)")]
    public HumanBodyBones crouchAttackBone = HumanBodyBones.LeftHand;
    public AnimationCurve crouchAttackSpeed = new AnimationCurve(
        new Keyframe(0.00f, 1.8f), new Keyframe(0.22f, 1.8f),
        new Keyframe(0.26f, 1.4f), new Keyframe(0.52f, 1.4f),
        new Keyframe(0.56f, 1.0f), new Keyframe(1.00f, 1.0f));

    [Tooltip("JUMPATTACK 타격 순간 (진행 0~1). 측정값 0.233 = 0.35초, 발이 가장 멀리 뻗는 순간")]
    [Range(0f, 1f)] public float jumpAttackImpact = 0.233f;
    [Tooltip("JUMPATTACK 궤적 구간 (발이 휘두르는 구간)")]
    public Vector2 jumpAttackTrail = new Vector2(0.13f, 0.33f);
    [Tooltip("JUMPATTACK 에서 궤적·번쩍임을 붙일 뼈 — 킥이라 왼발")]
    public HumanBodyBones jumpAttackBone = HumanBodyBones.LeftFoot;
    [Tooltip("JUMPATTACK 착지 순간 (진행 0~1). 측정값 0.55 = 0.83초")]
    [Range(0f, 1f)] public float jumpAttackLand = 0.55f;
    public AnimationCurve jumpAttackSpeed = new AnimationCurve(
        new Keyframe(0.00f, 1.0f), new Keyframe(0.20f, 1.0f),
        new Keyframe(0.24f, 1.25f), new Keyframe(0.34f, 1.25f),
        new Keyframe(0.38f, 0.95f), new Keyframe(1.00f, 1.05f));

    [Header("복싱 동작 (JAB / CROSS / HIGHKICK / LOWKICK): 완급 + 타격 멈춤 + 연출")]
    [Tooltip("동작마다 타격 순간, 때리는 부위, 속도 곡선, 멈춤 시간, 세기를 따로 조절")]
    public Strike[] strikes = DefaultStrikes();

    [System.Serializable]
    public class Strike
    {
        [Tooltip("애니메이터 상태 이름")] public string stateName = "JAB";
        [Tooltip("때리는 부위 (펀치면 손, 킥이면 발)")] public HumanBodyBones bone = HumanBodyBones.LeftHand;
        [Tooltip("타격 순간 (동작 진행 0~1). 주먹·발이 가장 멀리 뻗는 시점")]
        [Range(0f, 1f)] public float impact = 0.27f;
        [Tooltip("타격 순간 캐릭터가 멈추는 시간(초)")]
        [Range(0f, 0.2f)] public float hitStop = 0.05f;
        [Tooltip("타격 세기. 카메라 흔들림과 번쩍임 크기에 곱해짐")]
        [Range(0f, 2f)] public float power = 1f;
        [Tooltip("궤적이 그려지는 구간 (동작 진행 0~1)")] public Vector2 trailWindow = new Vector2(0.17f, 0.32f);
        [Tooltip("재생 속도 곡선. 가로=동작 진행(0~1), 세로=배속")] public AnimationCurve speed;
        [HideInInspector] public int hash;
    }

    // 준비 동작은 빠르게 → 타격 구간은 더 빠르게 → 복귀는 천천히 (절도 있게)
    static AnimationCurve MakeCurve(float impact, float windup, float strike, float recover)
    {
        return new AnimationCurve(
            new Keyframe(0f, windup), new Keyframe(Mathf.Max(0.02f, impact - 0.10f), windup),
            new Keyframe(Mathf.Max(0.04f, impact - 0.05f), strike), new Keyframe(impact + 0.03f, strike),
            new Keyframe(impact + 0.08f, recover), new Keyframe(1f, recover));
    }

    static Strike[] DefaultStrikes()
    {
        Strike Make(string n, HumanBodyBones b, float imp, float stop, float pw, float w, float st, float rc)
        {
            return new Strike { stateName = n, bone = b, impact = imp, hitStop = stop, power = pw,
                trailWindow = new Vector2(imp - 0.10f, imp + 0.05f), speed = MakeCurve(imp, w, st, rc) };
        }
        return new[]
        {
            Make("JAB",      HumanBodyBones.LeftHand,  0.27f, 0.05f, 0.7f, 2.2f, 2.0f, 1.3f),
            Make("CROSS",    HumanBodyBones.RightHand, 0.32f, 0.07f, 1.0f, 2.2f, 2.0f, 1.3f),
            Make("HIGHKICK", HumanBodyBones.RightFoot, 0.31f, 0.09f, 1.3f, 1.8f, 1.8f, 1.2f),
            Make("LOWKICK",  HumanBodyBones.RightFoot, 0.33f, 0.08f, 1.1f, 1.8f, 1.8f, 1.2f),
        };
    }

    [Header("막혔을 때 튕겨 나오기")]
    [Tooltip("되감는 속도 (배속)")] public float reboundSpeed = 1.2f;
    [Tooltip("이 진행도까지 되감은 뒤 대기 자세로")] [Range(0f, 1f)] public float reboundBack = 0.15f;
    [Tooltip("대기 자세로 돌아가는 시간 (초)")] public float reboundFade = 0.3f;
    public string reboundState = "IDLE";
    bool rebounding; int reboundHash; float reboundTo, reboundFadeUntil;

    /// <summary>공격이 막혔을 때 호출: 맞은 순간 멈춘 뒤, 차던 다리(주먹)를 거꾸로 되감아 거둬들인다 (상대 몸을 뚫고 지나가지 않게)</summary>
    public void Rebound()
    {
        var info = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
        if (FindStrike(info.shortNameHash) == null) return;
        rebounding = true; reboundHash = info.shortNameHash;
        reboundTo = Mathf.Max(0f, info.normalizedTime - reboundBack);
    }

    Strike FindStrike(int shortHash)
    {
        if (strikes == null) return null;
        foreach (var k in strikes) { if (k.hash == 0) k.hash = Animator.StringToHash(k.stateName); if (k.hash == shortHash) return k; }
        return null;
    }

    static readonly int SpeedId = Animator.StringToHash("MotionSpeed");
    static readonly int AttackHash = Animator.StringToHash("Base Layer.ATTACK");
    static readonly int JumpHash = Animator.StringToHash("Base Layer.JUMP");
    static readonly int CrouchAttackHash = Animator.StringToHash("Base Layer.CROUCHATTACK");
    static readonly int JumpAttackHash = Animator.StringToHash("Base Layer.JUMPATTACK");

    Animator anim;
    Transform hand;
    TrailRenderer trail;
    readonly System.Collections.Generic.Dictionary<HumanBodyBones, TrailRenderer> trails
        = new System.Collections.Generic.Dictionary<HumanBodyBones, TrailRenderer>();
    ParticleSystem flash;
    Camera cam;
    Vector3 camBase;
    float shakeUntil, shakeAmp, stopUntil;
    int lastHash;
    float lastTime;
    bool impactDone, landDone;

    void Awake()
    {
        anim = GetComponent<Animator>();
        hand = anim.GetBoneTransform(strikeHand);
        cam = Camera.main;
        if (cam) camBase = cam.transform.localPosition;
        BuildTrail();
        BuildFlash();
    }

    void BuildTrail() { trail = BuildTrailFor(strikeHand); }

    TrailRenderer BuildTrailFor(HumanBodyBones bone)
    {
        TrailRenderer found;
        if (trails.TryGetValue(bone, out found)) return found;
        var b = anim.GetBoneTransform(bone);
        if (b == null) { trails[bone] = null; return null; }
        var tr = MakeTrail(b, bone.ToString() + "Trail");
        trails[bone] = tr;
        return tr;
    }

    TrailRenderer MakeTrail(Transform parent, string goName)
    {
        var hand = parent;
        var go = new GameObject(goName);
        go.transform.SetParent(hand, false);
        go.transform.localPosition = Vector3.zero;
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.12f;
        tr.minVertexDistance = 0.01f;
        tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.09f), new Keyframe(1f, 0f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
                  new[] { new GradientAlphaKey(trailColor.a, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        if (trailMaterial) tr.sharedMaterial = trailMaterial;
        tr.emitting = false;
        return tr;
    }

    void BuildFlash()
    {
        var go = new GameObject("ImpactFlash");
        go.transform.SetParent(transform, false);
        flash = go.AddComponent<ParticleSystem>();
        flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flash.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.startLifetime = 0.15f;
        main.startSpeed = 0f;
        main.startSize = 0.3f;
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = flash.emission; em.enabled = false;
        var shape = flash.shape; shape.enabled = false;
        var size = flash.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.3f)));
        var col = flash.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = flash.GetComponent<ParticleSystemRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (flashMaterial) r.sharedMaterial = flashMaterial;
    }

    void Update()
    {
        // 지금 재생 중인(또는 넘어가는 중인) 동작
        var info = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
        int hash = info.fullPathHash;
        float t = info.normalizedTime;
        bool isAttack = hash == AttackHash, isJump = hash == JumpHash;
        bool isCrouchAttack = hash == CrouchAttackHash, isJumpAttack = hash == JumpAttackHash;
        Strike strike = FindStrike(info.shortNameHash);

        // 새로 시작했는지 확인
        if (hash != lastHash || t < lastTime) { impactDone = false; landDone = false; }
        lastHash = hash; lastTime = t;

        // --- 2단계: 속도 ---
        float speed = 1f;
        if (useTiming)
        {
            if (isAttack) speed = attackSpeed.Evaluate(Mathf.Clamp01(t));
            else if (isJump) speed = jumpSpeed.Evaluate(Mathf.Clamp01(t));
            else if (isCrouchAttack) speed = crouchAttackSpeed.Evaluate(Mathf.Clamp01(t));
            else if (isJumpAttack) speed = jumpAttackSpeed.Evaluate(Mathf.Clamp01(t));
            else if (strike != null && strike.speed != null) speed = strike.speed.Evaluate(Mathf.Clamp01(t));
        }

        // 타격 순간
        if (isAttack && !impactDone && t >= attackImpact)
        {
            impactDone = true;
            if (useTiming && hitStop > 0f) stopUntil = Time.time + hitStop;
            OnImpact(1f, true);
        }
        // 착지 순간
        if (isJump && !landDone && t >= jumpLand)
        {
            landDone = true;
            OnImpact(landShakeScale, false);
        }
        // 앉아서 공격 타격
        if (isCrouchAttack && !impactDone && t >= crouchAttackImpact)
        {
            impactDone = true;
            if (useTiming && hitStop > 0f) stopUntil = Time.time + hitStop;
            OnImpactAt(crouchAttackBone, 1f, true);
        }
        // 점프 공격 타격 / 착지
        if (isJumpAttack && !impactDone && t >= jumpAttackImpact)
        {
            impactDone = true;
            if (useTiming && hitStop > 0f) stopUntil = Time.time + hitStop;
            OnImpactAt(jumpAttackBone, 1.2f, true);
        }
        if (isJumpAttack && !landDone && t >= jumpAttackLand)
        {
            landDone = true;
            OnImpactAt(jumpAttackBone, landShakeScale, false);
        }
        // 복싱 동작 타격
        if (strike != null && !impactDone && t >= strike.impact)
        {
            impactDone = true;
            if (useTiming && strike.hitStop > 0f) stopUntil = Time.time + strike.hitStop;
            OnImpactAt(strike.bone, strike.power, true);
        }
        // 막혀서 튕겨 나옴: 잠깐 멈췄다가 동작을 거꾸로 되감고 대기 자세로
        if (rebounding)
        {
            if (strike == null || info.shortNameHash != reboundHash) rebounding = false;
            else if (t > reboundTo) speed = -reboundSpeed;
            else { rebounding = false; anim.CrossFadeInFixedTime(reboundState, reboundFade, 0); reboundFadeUntil = Time.time + reboundFade; }
        }
        // 대기 자세로 섞이는 동안에도 차던 동작이 다시 앞으로 나가지 않게 천천히 계속 거둬들임
        else if (Time.time < reboundFadeUntil && anim.IsInTransition(0) && anim.GetCurrentAnimatorStateInfo(0).shortNameHash == reboundHash) speed = -reboundSpeed * 0.5f;
        if (Time.time < stopUntil) speed = 0f;
        anim.SetFloat(SpeedId, speed);

        // --- 3단계: 주먹 궤적 ---
        SetTrail(strikeHand,       useFistTrail && isAttack       && t >= trailWindow.x        && t <= trailWindow.y);
        SetTrail(crouchAttackBone, useFistTrail && isCrouchAttack && t >= crouchAttackTrail.x && t <= crouchAttackTrail.y);
        SetTrail(jumpAttackBone,   useFistTrail && isJumpAttack   && t >= jumpAttackTrail.x   && t <= jumpAttackTrail.y);
        if (strikes != null)
        {
            bool inWin = strike != null && t >= strike.trailWindow.x && t <= strike.trailWindow.y;
            foreach (var k in strikes) SetTrail(k.bone, useFistTrail && inWin && strike != null && k.bone == strike.bone);
        }
    }

    void SetTrail(HumanBodyBones bone, bool on)
    {
        var tr = BuildTrailFor(bone);
        if (tr == null) return;
        if (on && !tr.emitting) tr.Clear();
        tr.emitting = on;
    }

    void LateUpdate()
    {
        if (!cam) return;
        if (useCameraShake && Time.time < shakeUntil)
        {
            float k = (shakeUntil - Time.time) / Mathf.Max(0.0001f, shakeTime); // 점점 약해짐
            float n = Time.time * 60f;
            var off = new Vector3(Mathf.PerlinNoise(n, 0f) - 0.5f, Mathf.PerlinNoise(0f, n) - 0.5f, 0f) * 2f * shakeAmp * k;
            cam.transform.localPosition = camBase + cam.transform.localRotation * off;
        }
        else cam.transform.localPosition = camBase;
    }

    void OnImpact(float scale, bool withFlash) { OnImpactAt(strikeHand, scale, withFlash); }

    // 타격 부위의 "뿌리" 뼈 — 방향 계산용 (손이면 위팔, 발이면 허벅지)
    static HumanBodyBones RootOf(HumanBodyBones b)
    {
        switch (b)
        {
            case HumanBodyBones.LeftHand:  return HumanBodyBones.LeftUpperArm;
            case HumanBodyBones.RightHand: return HumanBodyBones.RightUpperArm;
            case HumanBodyBones.LeftFoot:  case HumanBodyBones.LeftToes:  return HumanBodyBones.LeftUpperLeg;
            case HumanBodyBones.RightFoot: case HumanBodyBones.RightToes: return HumanBodyBones.RightUpperLeg;
            case HumanBodyBones.LeftLowerArm:  return HumanBodyBones.LeftUpperArm;
            case HumanBodyBones.RightLowerArm: return HumanBodyBones.RightUpperArm;
            default: return HumanBodyBones.Hips;
        }
    }

    void OnImpactAt(HumanBodyBones bone, float scale, bool withFlash)
    {
        if (useCameraShake) { shakeUntil = Time.time + shakeTime; shakeAmp = shakeStrength * scale; }
        var tip = anim.GetBoneTransform(bone);
        if (withFlash && useImpactFlash && flash && tip)
        {
            var p = new ParticleSystem.EmitParams();
            var rootT = anim.GetBoneTransform(RootOf(bone));
            Vector3 dir = rootT ? tip.position - rootT.position : transform.forward;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
            p.position = tip.position + dir * 0.08f;
            p.startColor = flashColor;
            p.startSize = 0.35f;
            p.startLifetime = 0.12f;
            p.applyShapeToPosition = false;
            flash.Emit(p, 1);
            // 작은 불꽃 조각
            for (int i = 0; i < 8; i++)
            {
                var s = new ParticleSystem.EmitParams();
                s.position = p.position;
                s.velocity = (dir * 1.8f + Random.insideUnitSphere * 2.0f);
                s.startSize = 0.04f;
                s.startLifetime = 0.18f;
                s.startColor = flashColor;
                flash.Emit(s, 1);
            }
        }
    }
}
