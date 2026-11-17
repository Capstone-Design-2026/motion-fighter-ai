using System.Collections.Generic;
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
        // Underside of the shoe/bare foot, sampled once from the mesh, in the space of the
        // bone it moves with (toes in front of the toe joint, otherwise the foot).
        public Vector3[] footSole = new Vector3[0], toeSole = new Vector3[0];
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
    float pelvisHeight, legLength, stopTime, swingLift, pelvisDrop, stepTime = 1;
    readonly Vector3[] goals = new Vector3[2];
    readonly Quaternion[] rotations = new Quaternion[2];
    float stepDirection;
    float groundY;
    int swing = -1, nextFoot;
    bool calibrated, active, stopping, firstStep;

    public float Speed => speed;
    public float Direction => direction;
    public float GroundY => groundY;
    public bool IsMoving { get; private set; }
    public float Weight => weight;
    public int PlantedFoot => active && swing >= 0 ? 1 - swing : -1;
    public float ContactError { get; private set; }
    /// <summary>발바닥 표본을 잡았다(LowestSoleY 가 실제 메시 기준이다).</summary>
    public bool SoleReady => calibrated && legs[0].footSole.Length + legs[0].toeSole.Length > 0 && legs[1].footSole.Length + legs[1].toeSole.Length > 0;
    public bool Calibrated => calibrated;
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
        previousRoot = root.position; speed = movingUntil = weight = pelvisDrop = 0;
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
            // Only the heading turns (about the vertical axis): pitch and roll stay as in the
            // approved idle, where every character's sole is flat on the floor. The fixed slope
            // used before fitted the hero rig only; on the other rigs it tipped the foot and the
            // planted sole hovered 1-4cm above the floor.
            Quaternion original = l.originalRotation = l.foot.rotation;
            Vector3 toe = l.toes ? l.toes.position - l.foot.position : root.forward * .15f - Vector3.up * .055f;
            Vector3 heading = root.rotation * new Vector3(i == 0 ? -.018f : .055f, 0, .17f);
            Vector3 flat = Vector3.ProjectOnPlane(toe, Vector3.up);
            float turn = flat.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(flat, heading, Vector3.up) : 0;
            l.foot.rotation = Quaternion.AngleAxis(turn, Vector3.up) * original;
            l.soleRotation = Quaternion.Inverse(root.rotation) * l.foot.rotation;
            legs[i] = l;
        }
        // One-off mesh calibration includes boots/bare feet and actual prefab scale.
        // Nothing is baked or allocated during walking.
        float[] bottom = { float.PositiveInfinity, float.PositiveInfinity };
        var mesh = new Mesh();
        var candidates = new[] { new List<Vector3>(), new List<Vector3>() };
        Vector3[] ankle = new Vector3[2], tip = new Vector3[2], kneePoint = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; ankle[i] = l.foot.position; kneePoint[i] = l.knee.position;
            Vector3 toe = l.toes ? l.toes.position - ankle[i] : root.rotation * new Vector3(0, -.055f, .15f);
            tip[i] = ankle[i] + toe * 1.7f;
        }
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || skin.sharedMesh == null) continue;
            skin.BakeMesh(mesh);
            var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            foreach (var vertex in mesh.vertices)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertex);
                for (int i = 0; i < 2; i++)
                {
                    if ((world - ankle[i]).sqrMagnitude >= .075f) continue;
                    bottom[i] = Mathf.Min(bottom[i], world.y);
                    // The shoe, not a trouser hem or the other foot: below the ankle and nearer
                    // this foot's ankle-toe axis than to its shin or to the other foot.
                    float own = SegmentDistance(world, ankle[i], tip[i]);
                    if (world.y < ankle[i].y && own <= SegmentDistance(world, kneePoint[i], ankle[i])
                        && own <= SegmentDistance(world, ankle[1 - i], tip[1 - i]))
                        candidates[i].Add(world);
                }
            }
        }
        Object.Destroy(mesh);
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            float sole = float.PositiveInfinity;
            foreach (var p in candidates[i]) sole = Mathf.Min(sole, p.y);
            if (!float.IsInfinity(sole))
            {
                // Keep the lower part of the shoe: whichever point is lowest after the ankle
                // pitches or the toes bend is among these.
                float band = sole + Mathf.Max(.015f, .45f * (ankle[i].y - sole));
                var footSole = new List<Vector3>(); var toeSole = new List<Vector3>();
                Vector3 toeAxis = l.toes ? l.toes.position - ankle[i] : Vector3.zero;
                foreach (var p in candidates[i])
                {
                    if (p.y > band) continue;
                    if (l.toes && Vector3.Dot(p - l.toes.position, toeAxis) > 0) toeSole.Add(l.toes.InverseTransformPoint(p));
                    else footSole.Add(l.foot.InverseTransformPoint(p));
                }
                l.footSole = footSole.ToArray(); l.toeSole = toeSole.ToArray();
                bottom[i] = sole;
            }
            l.soleHeight = float.IsInfinity(bottom[i]) ? .09f : l.foot.position.y - bottom[i] + .004f;
            l.rest.y = l.soleHeight;
            l.foot.rotation = l.originalRotation;
        }
        pelvisHeight = pelvis.position.y - root.position.y;
        legLength = (legs[0].upper + legs[0].lower + legs[1].upper + legs[1].lower) * .5f;
        calibrated = true; return true;
    }
    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }
    /// <summary>
    /// 지금 자세에서 그 발바닥의 가장 낮은 점 높이(월드 y). 발목이 기울거나 발가락이 꺾여도 메시에서 뽑아 둔 표본으로 잰다.
    /// 표본이 없으면 발목 높이에서 기본자세 때의 발바닥 두께를 뺀다. 재기 전이면 NaN.
    /// </summary>
    public float LowestSoleY(int i)
    {
        var l = legs[i];
        if (!calibrated || l == null) return float.NaN;
        float y = float.PositiveInfinity;
        if (l.footSole.Length > 0) y = Mathf.Min(y, LowestY(l.foot.localToWorldMatrix, l.footSole));
        if (l.toes && l.toeSole.Length > 0) y = Mathf.Min(y, LowestY(l.toes.localToWorldMatrix, l.toeSole));
        return float.IsInfinity(y) ? l.foot.position.y - l.soleHeight + .004f : y;
    }
    static float LowestY(Matrix4x4 m, Vector3[] points)
    {
        float y = float.PositiveInfinity;
        foreach (var p in points) y = Mathf.Min(y, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13);
        return y;
    }
    /// <summary>기본자세에서 한 번 발 치수를 잰다. Fighter3DView 가 IDLE 이 자리 잡은 프레임에 부른다.</summary>
    public bool TryCalibrate() => calibrated || Calibrate();
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
        stepDirection = direction; firstStep = true;

    }
    void StartStep(int i, bool settle)
    {
        swing = i; progress = 0;
        var l = legs[i]; l.from = l.shown; l.startRotation = l.shownRotation;
        // The first step out of the stance is a short push: the planted foot behind it then trails
        // only part of a stance, so the pelvis does not sink at every walk start.
        stepTime = firstStep && !settle ? .6f : 1f; firstStep = false;
        duration = settle ? .20f : stepTime * Mathf.Clamp(StepLength / (2 * Mathf.Max(speed, .15f)), .09f, .40f);
        l.target = settle ? Rest(i) : Landing(i, duration);
        // Boxing steps skim the floor: the sole clears about 2.5cm at mid-swing
        // (it was 4-6cm, which read as the feet floating off the ground).
        swingLift = (settle ? .015f : .02f * strideScale) * legLength / .8f;
    }
    Vector3 Landing(int i, float remaining)
    {
        Vector3 p = Rest(i);
        // Predict the root only while this foot is airborne. Keep the anatomical
        // stance offset, so the rear foot follows instead of overtaking the lead.
        // Land half a stance ahead of the rest offset, so the body passes over the planted foot
        // while the other one swings and it trails at most half a stance behind. Trailing a whole
        // stance made the pelvis drop up to 10cm at every landing to keep the leg reachable.
        p.x += direction * speed * (remaining + .5f * duration);
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
                    duration = stepTime * Mathf.Clamp(StepLength / (2 * Mathf.Max(speed, .15f)), .09f, .40f);
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

        // Sole-aware ankle goals: with the rotation the foot gets this frame (and the toe bend
        // from the clip), place the ankle so the lowest point of the actual sole sits where the
        // anchor puts it. The thickness measured once in the idle cannot see the toe bend.
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i];
            rotations[i] = Quaternion.Slerp(l.foot.rotation, l.shownRotation, weight);
            l.foot.rotation = rotations[i];
            float sole = l.foot.position.y - LowestSoleY(i) + .004f;
            goals[i] = l.shown + Vector3.up * (sole - l.soleHeight);
        }
        float animatedY = pelvis.position.y;
        float nominalY = root.position.y + pelvisHeight - (swing >= 0 ? .012f * Mathf.Sin(progress * Mathf.PI) : 0);
        float limitY = nominalY;
        // Small vertical pelvis compensation keeps both IK chains reachable.
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; Vector3 d = l.hip.position - goals[i]; d.y = 0;
            float reach = (l.upper + l.lower) * .985f;
            float maxHipY = goals[i].y + Mathf.Sqrt(Mathf.Max(.02f, reach * reach - d.sqrMagnitude));
            limitY = Mathf.Min(limitY, animatedY + maxHipY - l.hip.position.y);
        }
        // Drop at once when a leg needs it, rise back slowly: the body settles a little lower
        // while walking instead of bobbing at every landing.
        float drop = nominalY - limitY;
        pelvisDrop = drop >= pelvisDrop ? drop : Mathf.MoveTowards(pelvisDrop, drop, dt * .2f);
        float desiredY = locomotionAllowed ? nominalY - pelvisDrop : animatedY;
        pelvis.position += Vector3.up * (Mathf.Lerp(animatedY, desiredY, weight) - animatedY);
        for (int i = 0; i < 2; i++)
        {
            var l = legs[i]; Vector3 goal = Vector3.Lerp(l.foot.position, goals[i], weight);
            Solve(l, goal, rotations[i]);
            ReachError = Mathf.Max(ReachError, Vector3.Distance(l.foot.position, goal));
            if (i != swing && weight >= .999f && locomotionAllowed)
                ContactError = Mathf.Max(ContactError, Vector3.Distance(l.foot.position, l.anchor + (goals[i] - l.shown)));
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
