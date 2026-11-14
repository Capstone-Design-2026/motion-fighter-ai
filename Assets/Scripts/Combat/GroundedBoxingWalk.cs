using UnityEngine;

/// <summary>
/// Presentation-only stepping on the flat 2D battle floor. World-space contacts
/// follow actual displacement, including walls/pushboxes, never input intent.
/// Shared by every Fighter3DView; no changes to damage, hitboxes or travel speed.
/// </summary>
public sealed class GroundedBoxingWalk
{
    sealed class Leg
    {
        public Transform hip, knee, foot, toes;
        public float upper, lower, soleHeight;
        public Vector3 rest, anchor, from, target, shown;
        public Quaternion soleRotation, startRotation, shownRotation, originalRotation;
    }
    readonly Transform root;
    readonly Animator animator;
    readonly Rigidbody2D body;
    readonly float strideScale;
    readonly RaycastHit2D[] groundHits = new RaycastHit2D[12];
    readonly Leg[] legs = new Leg[2];
    Transform pelvis;
    Vector3 previousRoot;
    float speed, movingUntil, weight, progress, duration, direction, lastFacing;
    float pelvisHeight, legLength, stopTime, swingLift;
    float stepDirection;
    float groundY;
    int swing = -1, nextFoot;
    bool calibrated, active, stopping;

    public float Speed => speed;
    public float Direction => direction;
    public float GroundY => groundY;
    public bool IsMoving { get; private set; }
    public float Weight => weight;
    public int PlantedFoot => active && swing >= 0 ? 1 - swing : -1;
    public float ContactError { get; private set; }
    public float ReachError { get; private set; }
    // Distance for one step-and-follow pair. The lead/rear stance never swaps.
    public float StepLength => Mathf.Lerp(.24f, .38f, Mathf.InverseLerp(.25f, 1.4f, speed)) * legLength / .80f * strideScale;
    public float PlaybackSpeed => Mathf.Clamp(.66f * speed / Mathf.Max(.1f, StepLength), .35f, 3f);
    /// <summary>
    /// 주인공의 다리 길이(허벅지+정강이, 모델 배율 1 기준). 주인공 스텝을 적에게 옮길 때 이 비율로 보폭을 맞췄다.
    /// EnemyHeroMotionInstaller 가 로그에 적는 값("배율 1일 때 다리 길이")과 같아야 한다.
    /// </summary>
    public const float HeroLegLength = .4318f;
    /// <summary>
    /// 주인공 스텝을 옮긴 걷기 클립(적의 WALK_FORWARD / WALK_BACK)을 실제 이동 빠르기에 맞춰 재생할 빠르기.
    /// 그 클립은 한 바퀴에 "주인공 보폭 × (이 캐릭터 다리 길이 ÷ 주인공 다리 길이)"만큼 딛는다. 발이 미끄러지지 않게 그 거리와 실제 이동을 맞춘다.
    /// </summary>
    public float AuthoredPlaybackSpeed
    {
        get
        {
            float stride = HeroMovementSequence.StepDistance * LegLength() / HeroLegLength;
            return Mathf.Clamp(speed * HeroMovementSequence.StepSeconds / Mathf.Max(.01f, stride), .35f, 3.2f);
        }
    }
    float LegLength()
    {
        if (legLength > 0) return legLength;
        if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return .8f;
        float sum = 0;
        for (int i = 0; i < 2; i++)
        {
            var hip = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var knee = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (!hip || !knee || !foot) return .8f;
            sum += Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, foot.position);
        }
        return legLength = sum * .5f;
    }
    public Vector3 ContactPosition => PlantedFoot < 0 ? root.position : legs[PlantedFoot].foot.position;

    public GroundedBoxingWalk(Transform visual, Animator model)
    {
        root = visual; animator = model; previousRoot = root.position;
        strideScale = visual.GetComponentInParent<PlayerController>() != null ? 1.35f : 1f;
        body = visual.GetComponentInParent<Rigidbody2D>(); groundY = root.position.y;
    }
    public void Reset()
    {
        previousRoot = root.position; speed = movingUntil = weight = 0;
        IsMoving = active = stopping = false; swing = -1;
    }
    public void Measure(float dt, int facing)
    {
        Vector3 delta = root.position - previousRoot; previousRoot = root.position;
        if (dt <= 0) return;
        // Teleports, round resets and facing swaps must not drag old foot anchors.
        if (delta.sqrMagnitude > .36f || (lastFacing != 0 && lastFacing != facing))
        { Reset(); lastFacing = facing; return; }
        lastFacing = facing;
        groundY = root.position.y;
        var filter = ContactFilter2D.noFilter; filter.useTriggers = false;
        int hits = Physics2D.Raycast((Vector2)root.position + Vector2.up * .3f,
            Vector2.down, filter, groundHits, .85f);
        for (int i = 0; i < hits; i++)
        {
            var hit = groundHits[i];
            if (hit.rigidbody == body || hit.normal.y < .65f) continue;
            groundY = hit.point.y; break;
        }
        float raw = Mathf.Abs(delta.x) / dt;
        speed = Mathf.Lerp(speed, raw, 1 - Mathf.Exp(-dt * 28));
        if (raw > .045f) { movingUntil = Time.time + .045f; direction = Mathf.Sign(delta.x); }
        IsMoving = Time.time < movingUntil && speed > .06f;
    }
    bool Calibrate()
    {
        if (!animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return false;
        pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
        for (int i = 0; i < 2; i++)
        {
            var l = new Leg {
                hip = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                knee = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                foot = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot),
                toes = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes)
            };
            if (!l.hip || !l.knee || !l.foot) return false;
            l.upper = Vector3.Distance(l.hip.position, l.knee.position);
            l.lower = Vector3.Distance(l.knee.position, l.foot.position);
            l.rest = Quaternion.Inverse(root.rotation) * (l.foot.position - root.position);
            // Turn the toe axis toward travel, preserving the anatomical ankle/toe slope.
            // Slight toe-out keeps both feet in separate lanes instead of crossing them.
            Quaternion original = l.originalRotation = l.foot.rotation;
            Vector3 toe = l.toes ? l.toes.position - l.foot.position : root.forward * .15f - Vector3.up * .055f;
            Vector3 desired = root.rotation * new Vector3(i == 0 ? -.018f : .055f, -.035f, .17f);
            l.foot.rotation = Quaternion.FromToRotation(toe, desired) * original;
            l.soleRotation = Quaternion.Inverse(root.rotation) * l.foot.rotation;
            legs[i] = l;
        }
        // One-off mesh calibration includes boots/bare feet and actual prefab scale.
        // Nothing is baked or allocated during walking.
        float[] bottom = { float.PositiveInfinity, float.PositiveInfinity };
        var mesh = new Mesh();
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh);
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            foreach (var vertex in mesh.vertices)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertex);
                for (int i = 0; i < 2; i++)
                    if ((world - legs[i].foot.position).sqrMagnitude < .075f)
                        bottom[i] = Mathf.Min(bottom[i], world.y);
            }
        }
        Object.Destroy(mesh);
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            l.soleHeight = float.IsInfinity(bottom[i]) ? .09f : l.foot.position.y - bottom[i] + .004f;
            l.rest.y = l.soleHeight;
            l.foot.rotation = l.originalRotation;
        }
        pelvisHeight = pelvis.position.y - root.position.y;
        legLength = (legs[0].upper + legs[0].lower + legs[1].upper + legs[1].lower) * .5f;
        calibrated = true; return true;
    }
    Vector3 Rest(int i)
    {
        Vector3 p = root.position + root.rotation * legs[i].rest;
        p.y = groundY + legs[i].soleHeight; return p;
    }
    void StartWalking()
    {
        active = true; stopping = false; swing = -1;
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; l.anchor = l.shown = l.foot.position;
            l.anchor.y = groundY + l.soleHeight;
            l.shownRotation = l.foot.rotation;
        }
        // Forward: left/front first. Backward: right/rear first, then follow.
        nextFoot = direction * lastFacing > 0 ? 0 : 1;
        stepDirection = direction;

    }
    void StartStep(int i, bool settle)
    {
        swing = i; progress = 0;
        var l = legs[i]; l.from = l.shown; l.startRotation = l.shownRotation;
        duration = settle ? .20f : Mathf.Clamp(StepLength / (2 * Mathf.Max(speed, .15f)), .09f, .40f);
        l.target = settle ? Rest(i) : Landing(i, duration);
        swingLift = (settle ? .022f : .033f * strideScale) * legLength / .8f;
    }
    Vector3 Landing(int i, float remaining)
    {
        Vector3 p = Rest(i);
        // Predict the root only while this foot is airborne. Keep the anatomical
        // stance offset, so the rear foot follows instead of overtaking the lead.
        p.x += direction * speed * remaining;
        return p;
    }
    static float Smooth(float v) { v = Mathf.Clamp01(v); return v * v * (3 - 2 * v); }
    public void Apply(float dt, bool locomotionAllowed)
    {
        ContactError = ReachError = 0;
        if (dt <= 0) return;
        if (!calibrated)
        {
            // Always bind against the approved idle, before the first locomotion.
            if (!locomotionAllowed || !Calibrate()) return;
        }
        bool move = locomotionAllowed && IsMoving;
        if (move && active && direction != stepDirection)
        {
            // Finish a reversal from the current displayed contacts, not old anchors.
            StartWalking();
        }
        if (move && !active) StartWalking();
        if (!active) return;

        if (!locomotionAllowed)
        {
            // Release rapidly into the authored attack/guard/hurt/KO. Never pin a kick.
            weight = Mathf.MoveTowards(weight, 0, dt / .09f);
            if (weight <= 0) { active = false; swing = -1; return; }
        }
        else
        {
            weight = Mathf.MoveTowards(weight, 1, dt / .07f);
            if (!move && !stopping)
            {
                stopping = true; stopTime = Time.time;
                if (swing >= 0) StartStep(swing, true);
            }
            else if (move && stopping) { stopping = false; if (swing >= 0) StartStep(swing, false); }

            if (swing < 0)
            {
                if (move) StartStep(nextFoot, false);
                else
                {
                    float a = Vector3.Distance(legs[0].shown, Rest(0));
                    float b = Vector3.Distance(legs[1].shown, Rest(1));
                    if (Mathf.Max(a, b) > .025f) StartStep(a > b ? 0 : 1, true);
                    else if (Time.time - stopTime > .18f)
                    {
                        // Fade the settled stance back to the authored idle rhythm.
                        weight = Mathf.Max(0, 1 - (Time.time - stopTime - .18f) / .18f);
                        if (weight <= 0) { active = false; return; }
                    }
                }
            }
            if (swing >= 0)
            {
                if (move && !stopping)
                    duration = Mathf.Clamp(StepLength / (2 * Mathf.Max(speed, .15f)), .09f, .40f);
                var l = legs[swing]; progress = Mathf.Min(1, progress + dt / duration);
                // Retarget while airborne when input speed/direction changes, without
                // moving the supporting contact. No accumulated phase drift at walls.
                Vector3 destination = stopping ? Rest(swing) : Landing(swing, (1 - progress) * duration);
                l.target = Vector3.Lerp(l.target, destination, Mathf.Min(1, dt * 24));
                l.shown = Vector3.Lerp(l.from, l.target, Smooth(progress));
                l.shown.y += Mathf.Pow(Mathf.Sin(progress * Mathf.PI), 2) * swingLift;
                l.shownRotation = Quaternion.Slerp(l.startRotation, root.rotation * l.soleRotation, Smooth(progress));
                if (progress >= 1)
                { l.anchor = l.shown = l.target; nextFoot = 1 - swing; swing = -1; }
            }
            for (int i = 0; i < 2; i++) if (i != swing)
            { legs[i].shown = legs[i].anchor; legs[i].shownRotation = root.rotation * legs[i].soleRotation; }
        }

        float animatedY = pelvis.position.y;
        float desiredY = root.position.y + pelvisHeight - (swing >= 0 ? .012f * Mathf.Sin(progress * Mathf.PI) : 0);
        if (!locomotionAllowed) desiredY = animatedY;
        // Small vertical pelvis compensation keeps both IK chains reachable.
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; Vector3 d = l.hip.position - l.shown; d.y = 0;
            float reach = (l.upper + l.lower) * .985f;
            float maxHipY = l.shown.y + Mathf.Sqrt(Mathf.Max(.02f, reach * reach - d.sqrMagnitude));
            desiredY = Mathf.Min(desiredY, animatedY + maxHipY - l.hip.position.y);
        }
        pelvis.position += Vector3.up * (Mathf.Lerp(animatedY, desiredY, weight) - animatedY);
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; Vector3 goal = Vector3.Lerp(l.foot.position, l.shown, weight);
            Quaternion rotation = Quaternion.Slerp(l.foot.rotation, l.shownRotation, weight);
            Solve(l, goal, rotation);
            ReachError = Mathf.Max(ReachError, Vector3.Distance(l.foot.position, goal));
            if (i != swing && weight >= .999f && locomotionAllowed)
                ContactError = Mathf.Max(ContactError, Vector3.Distance(l.foot.position, l.anchor));
        }
    }
    void Solve(Leg l, Vector3 goal, Quaternion rotation)
    {
        Vector3 start = l.hip.position, delta = goal - start;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(l.upper - l.lower) + .001f, l.upper + l.lower - .001f);
        Vector3 axis = delta.normalized;
        // Knees bend toward the fighter's facing direction, not the travel direction.
        Vector3 bend = Vector3.ProjectOnPlane(root.forward, axis).normalized;
        if (bend.sqrMagnitude < .1f) bend = root.up;
        float along = (l.upper * l.upper - l.lower * l.lower + distance * distance) / (2 * distance);
        Vector3 knee = start + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, l.upper * l.upper - along * along));
        l.hip.rotation = Quaternion.FromToRotation(l.knee.position - start, knee - start) * l.hip.rotation;
        l.knee.rotation = Quaternion.FromToRotation(l.foot.position - l.knee.position, goal - l.knee.position) * l.knee.rotation;
        l.foot.rotation = rotation;
    }
}
