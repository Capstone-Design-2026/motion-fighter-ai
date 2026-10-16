# 확장 지도와 단서 기록 UI

- 단서 기록은 왼쪽 장소 목록과 오른쪽 상세 기록으로 구분한다. 현재 위치를 기본 선택하며, 각 기록은 장소 제목 / 단서 요약 / 대화 기록 / 첫 방문·전투 이후 / 발화자로 구분한다. 양쪽 영역은 각각 스크롤된다.
- `JourneyMapExtended.png`는 지도 콘텐츠와 같은 부모에서 스크롤되는 세로 지도다. 아래쪽 나루·마을에서 중간 산길·채석장, 위쪽 협곡·수문 요새로 이어진다. 화면에 맞게 비율을 유지해 채운다.
- `DriftingMapClouds`는 표식 아래에 흐르는 연한 장식 구름이며 `ExplorationClouds`는 기존 미탐험 구름이다. 둘 다 시간에 따라 변화한다. 발견 범위와 이동 가능 여부는 그대로 유지한다.
- 전체 지도 보기에서는 두 구름을 모두 숨기고, 다시 켜면 복원한다.
- 시작화면과 컷씬 임시 배경은 기존 그림을 유지한다.

## 이미지 제작
내장 image_gen 도구로 기존 JourneyMap.png를 참고하여 생성.
최종 파일: Assets/Resources/UI/Wuxia/JourneyMapExtended.png

프롬프트:
Use case: style-transfer. Asset: tall continuous scrolling background for a Korean wuxia exploration game. Extend/recompose the supplied landscape ink map into ONE coherent PORTRAIT painting, aspect 2:3, ideally 2048x3072. Retain warm aged parchment, fine grey ink mountains, muted jade streams, sparse dark pine and vermilion roofs. Bottom quarter: foothill villages, small inn, river ferry and winding low valley. Middle half: progressively rugged hills, abandoned watchtower, quarry and mountain paths. Top quarter: narrow dark ravine and ancient fortified water gate with reservoir, a distant restrained ominous citadel. No hard horizontal panels or repeating tiles. Detail mostly along outer left/right thirds; broad pale low-detail readable spaces across central 65% throughout height for clickable game nodes, but not an empty blank sheet. Aerial oblique illustrated cartography with consistent scale and continuous waterways. No text, labels, calligraphy, UI, symbols, marked routes, icons or characters. No opaque clouds painted across the center; cloud animation will be supplied in-engine. Finished seamless continuous scroll artwork, not a screenshot.
