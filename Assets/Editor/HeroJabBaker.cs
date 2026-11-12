using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주인공(Hero02) 전용 잽 클립을 굽는다. 동작은 승준의 Hero02 잽 v8(ArtSource/Hero02/Jab/Hero_Jab_v8.blend,
/// 동작 이름 Hero_Jab_Left_v8_Extended)이고, Hero_JAB.motion.json은 그 28프레임(60fps, 0.45초)을 그대로 내보낸 것이다.
/// 굽는 방법은 방어 동작과 같다(HeroGuardBaker.BakeSet): 자세를 주인공 뼈대에 얹어 Humanoid 클립으로 바꾸고
/// Hero_ApprovedMovement.controller의 JAB 상태에만 연결한다. 적은 계속 공용 잽(Unified_Common_JAB)을 쓴다.
/// 클립 이름은 _JAB로 끝나야 한다(CombatReach가 이 이름으로 잽 클립을 찾는다).
/// 실행: Unity -batchmode -executeMethod HeroJabBaker.Bake [-heroMotionReport &lt;폴더&gt;]
/// </summary>
public static class HeroJabBaker
{
    public const string Folder = "Assets/Characters/Hero02/ApprovedJab";
    public const string Source = Folder + "/Hero_JAB.motion.json";
    public const string JabClip = "Hero_JAB";
    /// <summary>클립 시작부터 주먹이 다 뻗고 앞발이 닿는 순간까지(초). Blender 9번째 프레임.</summary>
    public const float ContactTime = 8f / 60f;
    public const float Length = 27f / 60f;

    public static void Bake() => HeroGuardBaker.BakeSet(new[] { Source }, Folder,
        new Dictionary<string, string[]> { { JabClip, new[] { "JAB" } } },
        (anim, baked) =>
        {
            var shots = new List<(AnimationClip clip, float t, string tag)>();
            foreach (float t in new[] { 0f, 4f / 60f, 6f / 60f, 7f / 60f, ContactTime, 10f / 60f, 13f / 60f, 18f / 60f, Length })
                shots.Add((baked[JabClip], t, "jab_" + Mathf.RoundToInt(t * 1000).ToString("000")));
            HeroGuardBaker.RenderShots(anim, shots);
        });
}
