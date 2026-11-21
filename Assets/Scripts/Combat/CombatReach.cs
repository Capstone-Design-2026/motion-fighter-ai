using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Capsule = CombatContactGeometry.Capsule;

/// <summary>
/// 3D 전투의 실제 공격 사거리.
/// 캐릭터마다 키·팔 길이·잽 모션이 달라서, 모두 같은 거리(1.0m)에서 공격하면 키 작은 상대는 헛친다.
/// 잽 클립을 판정 구간(CombatContactWorld.Window) 동안 직접 샘플링해 판정 주먹(잽은 왼손)의 위치를 기록하고,
/// 상대의 실제 몸통 판정 캡슐(머리·가슴·배)과 3D 로 겹치는 가장 먼 몸 중심 거리를 구한다.
/// 옆으로 선 자세라 주먹과 상대 몸이 같은 선 위에 있지 않으므로 앞뒤 깊이(z)까지 함께 본다.
/// 주먹 판정은 CombatBodyPose 와 같은 식(손목→손 방향으로 4.5cm 앞, 반지름 9.5cm, 몸 크기 비례)을 쓴다.
/// 애니메이션이 공격 시작보다 한 프레임 늦게 출발하는 만큼 덜 뻗은 자세로 잰다(낮은 프레임에서도 맞도록).
/// </summary>
public static class CombatReach
{
    /// <summary>판정 구간 동안의 주먹 위치. 공격자 몸 중심 기준, x 는 앞쪽 거리, z 는 바라보는 방향에 맞춰 뒤집어 저장한다.</summary>
    public sealed class Profile
    {
        public Vector3[] fist;
        public float radius;
        /// <summary>주먹 앞면이 몸 중심에서 가장 멀리 나간 거리(m). 기록용.</summary>
        public float forwardReach;
        /// <summary>잴 때 반영한 애니메이션 지연(초).</summary>
        public float lag;
    }

    /// <summary>
    /// 잽 판정 구간의 왼주먹 위치를 잰다. 잴 수 없으면 null.
    /// 모델 뼈를 한 번 덮어쓰므로 Update 단계에서 부른다(같은 프레임의 애니메이션 갱신이 원래 자세로 되돌린다).
    /// </summary>
    public static Profile MeasureJab(Fighter3DView view, Transform root)
    {
        var animator = view != null ? view.modelAnimator : null;
        if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return null;
        var clip = FindClip(animator.runtimeAnimatorController, "_JAB");
        if (clip == null) return null;

        // CombatContactWorld 는 기술 번호로 주먹을 고른다(strikes[(int)move]). 잽(0) = 왼손.
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head), hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand), elbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        if (!head || !hips || !hand || !elbow) return null;

        float dir = animator.transform.forward.x >= 0f ? 1f : -1f;   // 모델이 보는 쪽이 공격 방향
        Vector2 window = CombatContactWorld.Window(CombatMove.Jab);
        // CombatContactWorld 가 실제로 보는 주먹을 그대로 재현한다.
        //  - 판정 시각은 공격을 시작한 프레임부터 프레임 단위(τ = k·dt)로 센다.
        //  - 애니메이션은 다음 프레임부터 재생되므로 프레임 k 의 클립 시각은 (k-1)·dt 다.
        //  - 구간 끝은 마지막 프레임과 다음 프레임 사이를 직선으로 이어 구간이 끝나는 지점까지만 본다.
        // 프레임이 낮을수록 덜 뻗으므로 최소 30fps 기준(고정 프레임이면 그 프레임)으로 잰다.
        float dt = Mathf.Max(1f / 30f, Time.captureFramerate > 0 ? 1f / Time.captureFramerate : 0f);
        var profile = new Profile { forwardReach = -1f, lag = dt };
        var points = new System.Collections.Generic.List<Vector3>();
        var graph = PlayableGraph.Create("CombatReachProbe");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "probe", animator);
            var play = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(play);
            Vector3 Fist(float clipTime)
            {
                play.SetTime(Mathf.Max(0f, clipTime));
                graph.Evaluate();
                float scale = Mathf.Clamp(Vector3.Distance(head.position, hips.position) / .65f, .85f, 1.15f);
                profile.radius = .095f * scale;
                Vector3 rel = hand.position + (hand.position - elbow.position).normalized * .045f * scale - root.position;
                return new Vector3(rel.x * dir, rel.y, rel.z * dir);
            }
            int k = Mathf.CeilToInt(window.x / dt - 1e-4f);
            Vector3 previous = Fist((k - 2) * dt);
            for (; k * dt <= window.y + 1e-5f; k++) { previous = Fist((k - 1) * dt); points.Add(previous); }
            float tail = (window.y - (k - 1) * dt) / dt;   // 마지막 프레임 → 다음 프레임 중 구간 안에 드는 비율
            if (tail > 0f) points.Add(Vector3.Lerp(previous, Fist(k * dt - dt), tail));
        }
        finally { graph.Destroy(); }
        profile.fist = points.ToArray();
        foreach (var p in profile.fist) profile.forwardReach = Mathf.Max(profile.forwardReach, p.x + profile.radius);
        return profile;
    }

    /// <summary>
    /// 잽이 상대 몸통(머리·가슴·배)에 실제로 닿는 가장 먼 몸 중심 사이 거리(m). 상대의 지금 자세를 쓴다.
    /// 계산할 수 없으면 -1.
    /// </summary>
    public static float MaxHitDistance(Profile profile, Fighter attacker, Fighter target, float far = 1.3f, float near = .3f)
    {
        var pose = target != null ? target.Contacts : null;
        if (profile == null || attacker == null || pose == null || !pose.Ready) return -1f;
        Vector3 aRoot = attacker.transform.position, tRoot = target.transform.position;
        float dir = tRoot.x >= aRoot.x ? 1f : -1f;
        var body = new Capsule[3];
        for (int i = 0; i < 3; i++)
        {
            var c = pose.hurt[i];
            if (c.radius <= 0f) return -1f;   // 아직 한 번도 캡처하지 않았다
            // 상대 몸 중심 기준으로 옮겨 둔다(x 는 거리마다 다시 더한다).
            body[i] = new Capsule(c.a - tRoot, c.b - tRoot, c.radius);
        }
        // 몸 중심끼리 높이·앞뒤(z) 위치가 다를 수 있다(2.5D 전투는 x 만 움직인다).
        float dy = tRoot.y - aRoot.y, dz = tRoot.z - aRoot.z;
        for (float d = far; d >= near; d -= .005f)
        {
            var shift = new Vector3(dir * d, dy, dz);
            foreach (var f in profile.fist)
            {
                var fist = new Vector3(f.x * dir, f.y, f.z * dir);
                var strike = new Capsule(fist, fist, profile.radius);
                for (int i = 0; i < 3; i++)
                {
                    var part = new Capsule(body[i].a + shift, body[i].b + shift, body[i].radius);
                    if (CombatContactGeometry.Distance(strike, part, out _, out _) <= strike.radius + part.radius) return d;
                }
            }
        }
        return -1f;
    }

    static AnimationClip FindClip(RuntimeAnimatorController controller, string suffix)
    {
        if (controller == null) return null;
        if (controller is AnimatorOverrideController overrides)
        {
            var pairs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            foreach (var p in pairs)
                if (p.Key != null && p.Key.name.EndsWith(suffix)) return p.Value != null ? p.Value : p.Key;
        }
        foreach (var c in controller.animationClips)
            if (c != null && c.name.EndsWith(suffix)) return c;
        return null;
    }
}
