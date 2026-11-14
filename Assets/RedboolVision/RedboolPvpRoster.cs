using UnityEngine;

/// <summary>PvP에서 고를 수 있는 인물 한 명.</summary>
public sealed class RedboolPvpCharacter
{
    /// <summary>화면에 쓰는 이름</summary>
    public string name;
    /// <summary>초상 그림이 있는 등장인물 정의 (Resources/Story/Actors/&lt;actor&gt;)</summary>
    public string actor;
    /// <summary>겉모습을 가져올 스토리 칸 id (RunConfig.nodeEnemyVisuals). 주인공은 null — 씬에 있는 원래 겉모습을 쓴다.</summary>
    public string node;
    /// <summary>이 인물의 고유 필살기 이름</summary>
    public string signature;
    /// <summary>선택 화면에 보이는 한 줄 소개</summary>
    public string line;
}

/// <summary>
/// PvP 인물 명단: 주인공 1명과 스토리의 적 11명.
/// 순서가 곧 번호이고, 이 번호가 두 화면 사이(RedboolViewProtocol)로 오간다. 순서를 바꾸면 양쪽 빌드를 함께 바꿔야 한다.
/// </summary>
public static class RedboolPvpRoster
{
    public const int Hero = 0;
    /// <summary>인물 선택을 거치지 않을 때의 2P (기존 PvP와 같은 철문)</summary>
    public const int DefaultSecond = 5;

    public static readonly RedboolPvpCharacter[] All =
    {
        new RedboolPvpCharacter { name = "청운", actor = "Seojin", node = null, signature = "파운장", line = "고르게 강한 주인공" },
        new RedboolPvpCharacter { name = "산길 척후병", actor = "Enemy01", node = "scout", signature = "잔영연환각", line = "가볍고 재빠른 척후" },
        new RedboolPvpCharacter { name = "계곡 잠복병", actor = "Enemy02", node = "river", signature = "잠룡출수", line = "물가에 숨어 노리는 잠복병" },
        new RedboolPvpCharacter { name = "고용 산적", actor = "Enemy03", node = "ridge", signature = "천근추지렬", line = "덩치로 밀어붙이는 산적" },
        new RedboolPvpCharacter { name = "옛 나루 약탈자", actor = "Enemy04", node = "old_ferry", signature = "승표탈기", line = "밧줄 표창을 쓰는 약탈자" },
        new RedboolPvpCharacter { name = "철문", actor = "Enemy05", node = "gate", signature = "금종폐관", line = "관문을 지키는 철벽" },
        new RedboolPvpCharacter { name = "채석장 정예병", actor = "Enemy06", node = "quarry", signature = "격산비석", line = "돌을 다루는 정예병" },
        new RedboolPvpCharacter { name = "능선 정예병", actor = "Enemy07", node = "east", signature = "선풍승룡", line = "회오리처럼 도는 발차기" },
        new RedboolPvpCharacter { name = "추포대 추적자", actor = "Enemy08", node = "camp", signature = "삼점봉혈", line = "급소를 짚는 추적자" },
        new RedboolPvpCharacter { name = "옥졸장", actor = "Enemy09", node = "rescue", signature = "진옥후", line = "뇌옥을 울리는 옥졸의 우두머리" },
        new RedboolPvpCharacter { name = "흑견", actor = "Enemy10", node = "entrance", signature = "흑영교", line = "그림자처럼 무는 흑운의 개" },
        new RedboolPvpCharacter { name = "무진", actor = "Enemy11", node = "final", signature = "흑운압성", line = "흑운을 이끄는 우두머리" },
    };

    public static int Count => All.Length;
    public static int Clamp(int index) => Mathf.Clamp(index, 0, All.Length - 1);
    public static RedboolPvpCharacter Get(int index) => All[Clamp(index)];

    /// <summary>겉모습 프리팹. 주인공이거나 찾지 못하면 null.</summary>
    public static GameObject Visual(int index)
    {
        var character = Get(index);
        if (character.node == null) return null;
        var config = RunState.Instance != null ? RunState.Instance.Config : Resources.Load<RunConfig>("RunConfig");
        return config != null ? config.GetEnemyVisual(character.node) : null;
    }

    public static StoryActorDefinition Actor(int index) => Resources.Load<StoryActorDefinition>("Story/Actors/" + Get(index).actor);
}
