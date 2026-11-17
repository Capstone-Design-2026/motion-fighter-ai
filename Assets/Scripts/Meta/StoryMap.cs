using System.Collections.Generic;

/// <summary>Authored story topology: directed links describe forward travel; explored links allow return.</summary>
public static class StoryMap
{
    public static MapData Create()
    {
        var m = new MapData { storyVersion = 2, rows = 12, columns = 5 };
        Add(m,"cave",0,2,NodeType.Event,"마른 폭포 동굴","벽화 수련을 마치고 사부의 지도를 챙겼다. 첫 목적지는 산 아래 청계역이다.");
        Add(m,"scout",1,2,NodeType.Battle,"산길 · 척후병","척후병의 짐에서 청계역으로 향하는 호송 표식을 발견했다.","boss_01");
        Add(m,"inn",2,2,NodeType.Rest,"청계역","역참 주인: 포로를 실은 수레는 물소리를 따라갔네. 능선에는 말 탄 자들이, 서쪽 옛 다리에는 짐꾼들이 갔어. 서로 다른 행렬이니 흔적을 살펴보게.");
        Add(m,"river",3,2,NodeType.Battle,"계곡 · 마교 척후병","붉은 진흙의 수레 자국이 폐쇄된 관문으로 이어진다.","boss_01");
        Add(m,"ridge",3,4,NodeType.Battle,"산등성이 · 고용 산적","산적들이 지키던 길에는 수레바퀴 대신 말발굽 자국만 남아 있다. 이들은 누구를 호위한 것일까? 능선 위 전망대로 흔적이 이어진다.","boss_02");
        Add(m,"bridge",3,0,NodeType.Event,"서쪽 옛 다리","다리 옆 좁은 길은 건널 수 있다. 짐꾼들의 발자국이 옛 나루로 이어진다. 포로들의 흔적인지는 더 조사해야 한다.");
        Add(m,"watch",4,4,NodeType.Event,"버려진 전망대","전망대에서 마교의 전령 표식을 발견했다. 다음 쉼터에도 같은 표식이 보인다. 이 길은 관문으로 합류하지 않는다.");
        Add(m,"gate",4,2,NodeType.Elite,"폐쇄된 관문 · 철문","철문의 호송 기록: 사부는 살아 있다. 의원과 석공도 붙잡혀 있다. 모두 흑운협의 옛 요새로 옮겨졌다.","boss_03");
        Add(m,"herb",5,2,NodeType.Rest,"약초꾼의 쉼터","약초꾼: 사부와 닮은 포로가 돌가루 묻은 길로 끌려갔소. 동쪽으로는 다른 포로들이, 서쪽으로는 추포대가 갔지. 각 길은 별개요.");
        Add(m,"quarry",6,2,NodeType.Battle,"채석장 · 정예병","바위 뒤에서 요새로 이어지는 지하 운반 통로를 찾았다. 통로 입구에 사부가 남긴 매듭이 있다.","boss_02");
        Add(m,"east",6,3,NodeType.Battle,"동쪽 능선 · 정예병","정예병이 지키던 곳에서 여러 포로의 발자국을 찾았다. 망루 뒤로 이어지지만, 사부가 함께 있었는지는 알 수 없다.","boss_02");
        Add(m,"camp",6,0,NodeType.Battle,"추포대 야영지","추포대의 짐에서 도망친 포로들의 명부를 발견했다. 서쪽 군량고에 더 많은 기록이 남아 있다고 한다.","boss_01");
        Add(m,"tunnel",7,2,NodeType.Event,"옛 운반 통로","사부의 매듭을 찾았다. 포로들은 아직 요새 안에 있다. 수로 기록에는 비상시 계곡으로 물을 흘려보낸다고 적혀 있다.");
        Add(m,"pass",7,3,NodeType.Event,"망루 뒷길","망루 너머에서 도움을 청하는 목소리가 들린다. 길은 요새가 아니라 외곽 포로 뇌옥으로 이어진다.");
        Add(m,"rescue",8,3,NodeType.Battle,"포로 뇌옥","포로들을 풀어 주었다. 사부는 이곳에 없었다. 풀려난 의원이 약고를 알려 주며, 특별 호송대는 채석장으로 갔다고 증언했다.","boss_02");
        Add(m,"supply",9,3,NodeType.Rest,"숨겨진 약고","약품을 챙겼다. 뇌옥 뒤편은 절벽으로 막혀 있다. 온 길을 돌아 약초꾼의 쉼터에서 채석장 방향을 선택하자.","",true);
        Add(m,"entrance",8,2,NodeType.Elite,"흑운협 입구 · 흑견","흑견을 쓰러뜨리고 사부를 구했다. 사부는 포로들을 이끌고 탈출한다. 흑운협주 무진이 수문을 열기 전에 막아야 한다.","boss_04");
        Add(m,"shelter",9,2,NodeType.Rest,"수문 앞 대피처","사부: 돌아갈 사람들은 내가 맡으마. 너는 수문을 지켜 다오. 이 길을 모두가 살아 내려가야 한다.");
        Add(m,"final",10,2,NodeType.Boss,"수문 요새 · 무진","무진을 저지했다. 수문과 퇴로를 확보했고 마교 잔당의 재건은 끝났다.","boss_final");
        Add(m,"ridge_rest",5,4,NodeType.Rest,"능선의 빈 정자","전령이 쉬어 간 자리에서 봉한 서찰을 찾았다. 능선 끝 봉화대의 수신인을 확인하자.");
        Add(m,"ridge_end",6,4,NodeType.Event,"꺼진 봉화대","서찰은 포로 호송이 아닌 지원군을 부르는 연락이었다. 더 갈 길은 절벽이다. 포로 수레를 찾으려면 청계역까지 돌아가 계곡으로 가야 한다.","",true);
        Add(m,"old_ferry",4,0,NodeType.Battle,"옛 나루 · 약탈자","짐꾼들이 옮기던 것은 포로가 아니라 군량이었다. 나루 뒤 군량터에서 운송 기록을 찾아보자.","boss_01");
        Add(m,"ferry_lookout",5,0,NodeType.Event,"폐쇄된 군량터","운송 장부를 찾았다. 군량은 배로 떠났고 나루 건너 길은 유실되었다. 포로 호송 기록은 계곡 관문에서 관리한다. 청계역으로 돌아가자.","",true);
        Add(m,"camp_record",7,0,NodeType.Event,"추포대 군량고","명부 속 포로들은 이미 탈출했다. 사부의 이름은 없지만, 별도 호송대가 채석장으로 향했다는 기록이 있다. 앞쪽 움막에서 쉴 수 있다.");
        Add(m,"camp_shelter",8,0,NodeType.Rest,"버려진 추포대 움막","추포대가 떠난 길은 낙석으로 막혀 있다. 잠시 쉬고 온 길을 돌아가자. 사부의 호송대는 채석장으로 향했다.","",true);
        Link(m,"cave","scout"); Link(m,"scout","inn");
        Link(m,"inn","river","ridge","bridge");
        Link(m,"river","gate"); Link(m,"gate","herb");
        Link(m,"ridge","watch"); Link(m,"watch","ridge_rest"); Link(m,"ridge_rest","ridge_end");
        Link(m,"bridge","old_ferry"); Link(m,"old_ferry","ferry_lookout");
        Link(m,"herb","quarry","east","camp");
        Link(m,"quarry","tunnel"); Link(m,"tunnel","entrance");
        Link(m,"east","pass"); Link(m,"pass","rescue"); Link(m,"rescue","supply");
        Link(m,"camp","camp_record"); Link(m,"camp_record","camp_shelter");
        Link(m,"entrance","shelter"); Link(m,"shelter","final");
        return m;
    }
    static void Add(MapData m,string id,int row,int col,NodeType type,string title,string story,string stage="",bool dead=false)
    {
        m.nodes.Add(new MapNode { id=id,row=row,column=col,type=type,title=title,story=story,stageId=stage,deadEnd=dead });
    }
    static void Link(MapData m,string from,params string[] to) { m.Find(from).next.AddRange(to); }
    /// <summary>
    /// 전투 HUD 에 띄울 상대 이름(2026-11-16: 장소 없이 이름만). 칸 제목 "장소 · 이름"에서 이름만 쓰고,
    /// 장소만 적힌 칸은 그 칸의 상대 이름을 쓴다(PvP 인물 명단과 같은 이름). 지도 칸 제목은 그대로다.
    /// </summary>
    public static string EnemyName(MapNode node)
    {
        if(node==null) return "";
        switch(node.id) { case "camp": return "추포대 추적자"; case "rescue": return "옥졸장"; }
        string title=node.title??"";
        int cut=title.LastIndexOf(" · ",System.StringComparison.Ordinal);
        return cut>=0?title.Substring(cut+3).Trim():title;
    }
    public static IEnumerable<MapNode> Neighbors(MapData m,MapNode node)
    {
        foreach(var n in m.nodes) if(node.next.Contains(n.id)||n.next.Contains(node.id)) yield return n;
    }
}
