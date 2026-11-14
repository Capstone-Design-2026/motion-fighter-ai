using UnityEngine;

/// <summary>PvP에서 고를 수 있는 인물 한 명.</summary>
public sealed class RedboolPvpCharacter
{
    /// <summary>화면에 쓰는 이름</summary>
    public string name;
    /// <summary>이름 아래에 붙는 한마디 (어떤 사람인지)</summary>
    public string title;
    /// <summary>싸우는 방식을 한 낱말로</summary>
    public string style;
    /// <summary>선택 화면의 소개 글</summary>
    public string line;
    /// <summary>초상 그림이 있는 등장인물 정의 (Resources/Story/Actors/&lt;actor&gt;)</summary>
    public string actor;
    /// <summary>겉모습과 필살기를 가져올 스토리 칸 id (RunConfig.nodeEnemyVisuals, EnemySpecial). 주인공은 null — 원래 겉모습과 파운장을 쓴다.</summary>
    public string node;
    /// <summary>이 인물의 필살기 이름</summary>
    public string signature;
    /// <summary>필살기가 무엇을 하는지</summary>
    public string specialLine;
}

/// <summary>
/// PvP 인물 명단: 주인공 1명과 스토리의 적 11명.
/// 순서가 곧 번호이고, 이 번호가 두 화면 사이(RedboolViewProtocol)로 오간다. 순서를 바꾸면 양쪽 빌드를 함께 바꿔야 한다.
/// 필살기 설명은 EnemySpecial.Moves.cs 의 기술 설명을 줄인 것이다. 기술을 고치면 여기도 맞춘다.
/// </summary>
public static class RedboolPvpRoster
{
    public const int Hero = 0;
    /// <summary>인물 선택을 거치지 않을 때의 2P (기존 PvP와 같은 철문)</summary>
    public const int DefaultSecond = 5;

    public static readonly RedboolPvpCharacter[] All =
    {
        new RedboolPvpCharacter { name = "청운", title = "길을 나선 수련생", style = "균형형", actor = "Seojin", node = null,
            line = "손과 발이 고르게 빠르다. 어느 거리에서도 제 몫을 한다.",
            signature = "파운장", specialLine = "기를 모아 손바닥으로 장풍을 쏜다. 멀리까지 곧게 날아간다." },
        new RedboolPvpCharacter { name = "산길 척후병", title = "산길을 지키는 척후", style = "기동형", actor = "Enemy01", node = "scout",
            line = "몸이 가볍고 발이 빠르다. 잔상을 남기며 치고 빠진다.",
            signature = "잔영연환각", specialLine = "잔상 둘이 먼저 차고, 본체가 위에서 떨어져 찍는다." },
        new RedboolPvpCharacter { name = "계곡 잠복병", title = "물가의 매복조", style = "기습형", actor = "Enemy02", node = "river",
            line = "물안개에 숨어 있다가 발밑에서 솟구친다.",
            signature = "잠룡출수", specialLine = "물결로 다가와 물기둥과 함께 솟구친다. 물결이 향한 자리를 벗어나면 맞지 않는다." },
        new RedboolPvpCharacter { name = "고용 산적", title = "돈에 팔린 장사", style = "괴력형", actor = "Enemy03", node = "ridge",
            line = "큰 덩치로 밀어붙인다. 한 방이 무겁다.",
            signature = "천근추지렬", specialLine = "높이 뛰어 온몸으로 내리찍는다. 땅이 갈라지며 넓게 덮친다." },
        new RedboolPvpCharacter { name = "옛 나루 약탈자", title = "나루터의 약탈자", style = "견제형", actor = "Enemy04", node = "old_ferry",
            line = "밧줄 표창으로 멀리 있는 상대를 낚아챈다.",
            signature = "승표탈기", specialLine = "밧줄 표창으로 감아 끌어당기고, 상대가 모아 둔 필살기 게이지를 비운다." },
        new RedboolPvpCharacter { name = "철문", title = "관문의 문지기", style = "철벽형", actor = "Enemy05", node = "gate",
            line = "쇠문 같은 몸으로 버틴다. 맞아도 물러서지 않는다.",
            signature = "금종폐관", specialLine = "모으는 동안 맞아도 끊기지 않는다. 쇠문 두 짝이 솟아 닫히며 그 사이를 친다." },
        new RedboolPvpCharacter { name = "채석장 정예병", title = "채석장의 돌주먹", style = "원거리형", actor = "Enemy06", node = "quarry",
            line = "바위를 다룬다. 떨어져 있어도 안심할 수 없다.",
            signature = "격산비석", specialLine = "땅에서 띄운 바위를 주먹으로 쳐 날리고, 깨진 파편이 덮친다." },
        new RedboolPvpCharacter { name = "능선 정예병", title = "능선의 회오리", style = "공중형", actor = "Enemy07", node = "east",
            line = "돌며 차는 발차기가 특기다. 상대를 띄워 놓고 찍는다.",
            signature = "선풍승룡", specialLine = "회오리를 보내 띄우고, 뛰어올라 찍는다. 회오리를 막으면 휘말리지 않는다." },
        new RedboolPvpCharacter { name = "추포대 추적자", title = "추포대의 사냥꾼", style = "봉쇄형", actor = "Enemy08", node = "camp",
            line = "급소를 짚어 상대의 손발을 묶는다.",
            signature = "삼점봉혈", specialLine = "혈 자리 셋을 찌르고 한 박자 뒤 터뜨린다. 맞으면 2초 동안 공격하지 못한다." },
        new RedboolPvpCharacter { name = "옥졸장", title = "뇌옥의 옥졸장", style = "제압형", actor = "Enemy09", node = "rescue",
            line = "고함 한 번에 뇌옥이 울린다. 가둬 놓고 때린다.",
            signature = "진옥후", specialLine = "고함의 음파로 밀어내고, 떨어지는 쇠창살로 가둔다." },
        new RedboolPvpCharacter { name = "흑견", title = "흑운의 사냥개", style = "흡수형", actor = "Enemy10", node = "entrance",
            line = "그림자처럼 따라붙어 문다. 싸울수록 기운을 되찾는다.",
            signature = "흑영교", specialLine = "그림자가 달려들어 물고 기운을 빨아들인다. 맞히면 체력을 되찾는다." },
        new RedboolPvpCharacter { name = "무진", title = "흑운의 우두머리", style = "절정형", actor = "Enemy11", node = "final",
            line = "먹구름을 부른다. 느리지만 가장 크게 친다.",
            signature = "흑운압성", specialLine = "먹구름이 끌어당긴 뒤 구름 기둥과 벼락이 떨어진다. 모으는 시간이 가장 길다." },
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
