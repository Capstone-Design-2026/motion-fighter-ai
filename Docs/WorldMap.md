# 지형 탐험 지도 적용

## 적용 범위
기존 25개 장소와 24개 연결, 총 11개 전투(본선 6개, 곁길 5개)를 그대로 사용한다.
장소 ID/전투 설정/단서/컷씬/휴식/저장 형식은 바꾸지 않고 표현 좌표만 분리했다.
기존 저장도 새 지형 배치로 표시되며 새 게임을 강요하지 않는다.

- 지도 공간: 4200 × 2800 UI 단위, StoryMapLayout.Size.
- 배경: Assets/Resources/UI/Wuxia/JourneyWorldMap.png.
- 노드 위치: StoryMapLayout의 장소 ID별 정규화 좌표.
- 길: 같은 함수를 써서 점선과 여행 경로를 그린다. 역방향 이동도 정확히 같은 곡선이다.
- 청계역 진입로는 서쪽 다리를 거치지 않도록 별도 곡선으로 우회한다.
- 금빛 길: 현재 위치에서 새로 선택 가능한 연결. 갈색 길: 지나왔거나 그 밖의 연결.
- 직접 연결된 곳만 이동 가능. 전체 지도 보기 역시 이동 제한을 해제하지 않는다.
- 미발견 장소 표식은 생성하지 않으며, 동적 구름과 발견 기록은 분리한다.
- 수문 요새는 가장 큰 건축물로 강조한 배경을 사용한다.

## 조작
- 마우스 드래그: 지도 상하좌우 이동.
- 휠 또는 왼쪽 아래 − / +: 확대·축소.
- 왼쪽 아래 ◎: 현재 위치로 복귀.
- 장소 선택: 곡선 길을 따라 캐릭터와 화면 이동 후 첫 방문 컷씬/전투.
- 컷씬, 기록창, 이동 중에는 지도 조작을 막아 충돌하지 않게 한다.

## 본선
마른 폭포 동굴 → 산길 척후병 → 청계역 → 계곡 척후병 → 폐쇄된 관문 철문 → 약초꾼의 쉼터 → 채석장 정예병 → 옛 운반 통로 → 흑운협 입구 흑견 → 수문 앞 대피처 → 수문 요새 무진.

## 곁길
- 청계역 → 산등성이 → 전망대 → 빈 정자 → 봉화대.
- 청계역 → 서쪽 옛 다리 → 옛 나루 → 폐쇄된 보급 터.
- 약초꾼의 쉼터 → 동쪽 능선 → 초소 뒷길 → 포로 수용소 → 약품 보관소.
- 약초꾼의 쉼터 → 수색대 야영지 → 보급 창고 → 수색 움막.
모두 독립된 경로이며 다른 길로 합류하지 않는다. 완료 장소는 전투/회복/컷씬 반복 없이 되돌아간다.

## 이미지 생성
내장 image_gen 사용. 아래 프롬프트로 시안에서 글씨·표식을 제거한 뒤 수문 요새를 강화했다. 최종 파일은 위 배경 경로에 저장했다.

1차:
Edit this exact illustrated world map into a CLEAN GAME BACKGROUND asset, landscape 3:2, highest available resolution preferably 3072x2048. Keep its geographical layout and painterly ink/parchment style, its villages, towers, rivers, bridge, quarry, distant dam fortress. Remove ALL lettering including title and red seal, ALL labels, ALL circles and map markers, ALL dotted red or brown route markings. Inpaint each removed marking with continuous terrain. No UI, no words, no symbols. Preserve: southwest cave and foothill path, west-central inn village and bridge, northwest tall ridge with watchtower and beacon, southwest river ferry area, central gate crossing, east-central herb cottage, northeast quarry rising to enormous water-gate fortress, southeast prisoner camp plateau, south-central abandoned military camp. Give the center generous readable terrain patches for 25 small runtime location markers. Keep clouds only as faint distant atmospheric valley mist; no large opaque white cloud blanket. Geography should remain recognizable to reference, do not rearrange landmarks. Game engine will draw all actual paths, fog, text and buttons independently.

최종 수정:
Edit ONLY the far upper-right finale fortress region of this wuxia game map, preserving the rest of the map, its aspect ratio 3:2, all other geography and landmarks in the EXACT same positions. Make the final watergate citadel much more monumental and clearly readable: a towering ancient multi-tier fortress with broad imposing fortified walls, three gigantic sluice gates, heavy stone towers with traditional East Asian roofs, a vast visible jade reservoir behind and cascading water below into the gorge. It should occupy the upper-right quadrant prominently, roughly x=78%-98%, y=3%-25%, with its principal gate around x94%, y10%. Dramatic ink contrast and strong silhouette, largest single architecture on map, no modern dam or European castle. Keep existing nearby quarry at x72%,y25% and all villages, paths, rivers elsewhere intact. Same aged parchment and ink wash painting, delicate muted colors, high-resolution final asset. No labels, no words, no circles, no UI, no marked dotted routes. Do not add clouds that conceal the fortress: dynamic game fog will do that. Increase grandeur without altering any other place.

## 미탐험 구름 보강
미탐험 배경은 94~99.5% 불투명한 움직이는 구름으로 가린다. 발견 장소 주변 반경 190, 실제 연결 곡선 주변 반경 95의 통로를 공개한다. 경계는 부드럽게 혼합하며 먼 배경은 거의 노출하지 않는다. 공개 거리 계산은 지도 구성 시 캐시하고 구름 애니메이션에는 재사용한다. 전체 지도 보기에서만 가림을 해제한다.
