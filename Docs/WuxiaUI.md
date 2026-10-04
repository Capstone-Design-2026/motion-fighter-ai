# 시작 화면 · 지도 디자인

2026-09-30 적용. GameScene 전투 UI와 지도 생성/저장 규칙은 변경하지 않음.

## 시작 화면
- 새로 제작한 노을 산속 수련장 배경, 왼쪽 제목과 메뉴.
- 원본 비율을 유지하며 화면을 채우고, 느린 배경 이동/안개/낙엽을 Unity에서 합성. 영상 파일 불필요.
- 새 게임, 이어하기, 조작 안내, 종료 기능 유지.
- StartUI의 Animate Background / Background Motion으로 움직임 끄기·속도 조절 가능.
- Background Sprite / Logo Sprite / Button Sprite를 지정하면 기본 디자인 교체 가능.

## 지도
- 한지·산수화 배경, 먹색 점선 길, 붉은 지나온 길.
- 교차한 검=전투, 정자=휴식, 성문=최종 보스. 기존 스테이지 초상화가 있으면 우선 사용.
- 선택 가능 노드에 붉은 이중 테두리, 완료 도장, 현재 위치 표기.
- 이름과 적 HP/공격력 또는 회복량 표시. 선택 불가능한 미래 경로는 읽을 수 있게 남기고, 버린 경로는 흐리게 표시.
- 기존 분기/보스 다섯 번/휴식/저장/전투 진입 로직 유지.
- 마우스 휠과 드래그 스크롤 유지.

## 구성
- Assets/Scripts/Meta/StartSceneUI.cs
- Assets/Scripts/Meta/MapSceneUI.cs
- Assets/Scripts/UI/WuxiaBackdrop.cs: 비율 유지와 배경 연출
- Assets/Scripts/UI/WuxiaGraphic.cs: 안개·낙엽·지도 표식 벡터 도형
- Assets/Scripts/UI/WuxiaTheme.cs: 전용 팔레트와 글꼴
- Assets/Resources/UI/Wuxia/StartCourtyard.png
- Assets/Resources/UI/Wuxia/JourneyMap.png

UI는 플레이 시 생성됨. 씬 편집 상태에서 배경이 보이지 않는 것은 정상. 16:9 데스크톱 화면 기준. 글꼴은 Windows 맑은 고딕 등 OS 한글 글꼴을 사용하며 다른 플랫폼 배포 시 한글 폰트 포함을 검토할 것.

## 이미지 제작 기록
내장 이미지 생성 도구(built-in image generation) 사용. 웹에서 가져온 배경이나 영상 없음. 다음 지시문으로 생성된 PNG를 프로젝트에 복사함.

### StartCourtyard
Create a finished game background artwork, landscape 16:9, 1920x1080 or higher. Korean wuxia martial arts mountain training courtyard at sunset, painterly premium game environment, quiet atmospheric layered blue-grey mountains, warm ivory dusk sky, an old Korean tiled wooden pavilion and stone courtyard predominantly on the RIGHT half, dark pine branches framing top right, subtle antique bronze and vermilion accents. LEFT 40 percent is dark quiet misty negative space for a title and menu that will be added separately. No people, no text, no lettering, no logos, no buttons, no UI, no borders. Grounded elegant cinematic illustration, restrained saturation, detailed architecture, depth and distant soft mist. Avoid Japanese torii, avoid neon, avoid modern objects. This is the base layer for a subtly animated Unity main menu; leave room for separate drifting fog and leaves.

### JourneyMap
Use case stylized-concept. Create landscape 16:9 game world MAP BACKGROUND, aged warm ivory hanji paper with subtle grain, elegant traditional Korean/Chinese ink wash mountain cartography. Muted charcoal brush mountains and pine trees clustered along LEFT and RIGHT edges, small ancient tiled temple bottom right, distant walled mountain fortress near top edge, faint winding river at left. CENTRAL 65 percent must be quiet pale paper, very sparse low contrast faint contours only, reserved for interactive branching path nodes added later by game engine. Flat top-down illustrative map, not a perspective tabletop photograph. Beautiful hand-painted restrained antique map. NO text, NO lettering, NO labels, NO paths or dotted routes, NO UI, NO icons, NO markers, NO ornate border, NO people. Palette warm parchment ivory, faded charcoal, grey-green ink, tiny touches muted vermilion. High quality finished production game background, matching a Korean wuxia mountain courtyard at dusk in tone.

## 검증 결과
Unity 6000.4.5f1 별도 검증 프로젝트에서 실제 Play 모드 실행: 배경 로드, 조작 안내, 새 게임→지도, 선택 가능 노드 수, 세로 스크롤, 메뉴→이어하기, 전투 진입, 승리 후 지도 복귀, 현재 위치/완료 도장 확인 통과. 시작/지도 하단/최종 보스/진행 후 화면 캡처 확인. 기존 저장 데이터는 검증 후 복원. 기존 MCP 연결 및 에디터 검색 인덱스 오류는 UI 검사에서 제외함.


## 구름·선명도·메뉴 후속 개선
- 시작 화면 상단에 네 겹의 구름 도형을 추가. 서로 다른 속도로 반복 이동하며 화면 밖에서 순환. 기존 배경 이미지에 포함된 구름과 별도로 합성됨.
- StartUI의 Show Clouds / Cloud Speed로 구름 표시와 속도 설정. Animate Background를 끄면 움직임도 정지.
- 메뉴: 모서리를 깎은 먹색 판, 이중 고금색 테두리, 명조 계열 글꼴, 붉은 시작 도장. 마우스 및 키보드 선택 시 금빛 강조, 이어하기 비활성 시 흐린 글자.
- 지도: 안개 불투명도 0, 배경 덮개 감소, 노드와 글자 바탕 불투명 처리, 더 진한 아이콘/길, 라벨 22px 굵게. 접근 불가능한 경로만 기존처럼 흐리게 유지.


## 메뉴별 아이콘 · 하늘 구름 개선
- 새 게임: 교차한 검 / 이어하기: 순환 화살표 / 조작 안내: 펼친 책 / 종료: 출구 화살표. 문자 표식을 전용 벡터 아이콘으로 교체.
- 하늘 구름 이동 속도 3배, 밝은 층 알파 0.36 / 어두운 층 0.38로 상향. 바위 사이 안개와 낙엽 움직임 유지.


## 하늘 실제 분리 · 단풍잎 이미지 교체
이전의 도형 구름을 사용하지 않으며, 다음 세 PNG를 내장 이미지 생성 도구로 제작했다. 원본 StartCourtyard.png는 보존했다.
- Assets/Resources/UI/Wuxia/StartForeground.png: 하늘만 투명한 전경. 산·건물·나무·해는 고정.
- Assets/Resources/UI/Wuxia/StartSky.png: 구름이 포함된 하늘 전용 이미지. 전경 뒤에서 천천히 이동.
- Assets/Resources/UI/Wuxia/MapleLeaf.png: 실제 잎 모양과 잎맥을 가진 투명 단풍잎 스프라이트.

하늘은 확대 여백을 두어 이동 중 가장자리가 드러나지 않는다. Sky 레이어 이동만 전경의 투명한 하늘 영역을 통해 보인다. 낙엽에는 서로 다른 크기·낙하 속도·좌우 흔들림·회전·폭 변화를 적용했다. Show Clouds는 분리된 하늘의 이동을 켜고 끄며, Cloud Speed로 속도를 조절한다. Background Sprite를 별도로 지정하면 사용자 지정 이미지를 우선 사용하므로 기본 전경/하늘 분리가 적용되지 않는다.

### 사용한 이미지 생성 지시문 (built-in)
Foreground / background-extraction: Edit this exact game background into its FOREGROUND layer. Preserve the complete original framing and aspect ratio, mountain ridge silhouettes, ALL mountains, mist between the mountains, courtyard, building, sun disk, trees and fine pine branches in their exact positions. Remove ONLY the open SKY above the highest distant mountain skyline, including all sky clouds, replacing that sky with genuine transparent alpha, including gaps between overhead pine needles. Keep everything below the skyline opaque including the valley mist. Do not crop, rescale, recompose or invent geometry. Output one full-size landscape PNG foreground plate, genuinely transparent sky, no checkerboard painted into pixels, no text. The intended use is to put a moving sky layer BEHIND this fixed foreground so sky cannot cover the mountains or roof.

Sky / precise-object-edit: Create a full-frame SKY-ONLY background plate matching the exact sunset sky of this image. Remove ALL mountains, trees, branches, architecture, landscape and foreground, and extend the sky across the entire canvas. Full 16:9 landscape. Preserve top sky colors: charcoal blue and muted mauve cloud masses on the left, delicate warm apricot golden evening clouds on the right. Detailed natural cloud edges and layered wispy cloud masses, not blurry fog. No sun disk (the foreground will retain it), no objects, no ground, no text. Sky only filling all pixels. This plate will slowly slide behind an extracted foreground mountain skyline; upper third should resemble the reference sky closely, lower area can be muted warm sunset atmospheric gradient.

Leaf: Game sprite asset: one single realistic Korean autumn maple leaf, seven pointed deeply lobed fingers, finely serrated edges, clearly visible natural branching veins, thin curved stem. Rich vermilion red center blending to burnt orange gold tips, warm late afternoon lighting, subtle curled organic surface, painterly realism matching a cinematic Korean mountain temple game. Leaf fills 80 percent of square canvas, isolated on genuine transparent alpha, no shadow outside the leaf, no ground, no background, no frame, no text, no multiple leaves. Recognizable maple silhouette even when rendered 25-45 pixels wide.

## 2026-10-05 무협 버전 복원
- 킥복싱 체급 캠페인, 성장 노드, 스토리 컷씬은 현재 플레이 흐름에서 제외.
- 무협 시작 메뉴와 산수화 지도, 경로별 5회 전투/2회 휴식, 기존 크레딧 사용.
- 120초 전투 제한시간 유지. 시간 종료 시 HP 비율 비교, 동률은 플레이어 패배.
- GameScene에 보이지 않는 좌우 벽 추가. 안쪽 경계 X=-8/+8, 높이 30.
- 킥복싱 작업은 프로젝트 밖 별도 ZIP에 보관하여 추후 복원 가능.
