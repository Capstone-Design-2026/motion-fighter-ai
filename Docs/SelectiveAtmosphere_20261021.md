# 11개 전투맵 선택 보정 적용 — 2026-10-21

## 적용
- ScoutPass 및 StoryStages의 10개 씬에 카메라별 설정을 저장했습니다.
- 단순 석조·지붕·목재만 분류합니다. 상세 소품, 캐릭터, 지면, UI는 제외합니다.
- 계곡과 수색대 야영지는 해당하는 단순 구조물이 없어 설정만 저장하고 효과 패스를 생략합니다.
- 성벽 줄눈 65%, 표면 채도·반사 약화. 선택 표면 내부에 5×5 가우시안 필터를 사용합니다.
- 1600px 기준 샘플 반경 3.5px; 석조/지붕/목재 효과 가중치 1/.8/.7, 흐림 .90, 탈채도 .08, 국소 안개색 .07.
- 전역 안개·조명·전역 피사계심도는 변경하지 않았습니다.
- 흑운협 입구: 72m 연속 성벽, 완화한 모서리, 원래 중앙 문 유지, 바위를 성벽 뒤로 이동.
- 새 성벽에는 카메라 전용 트리거만 추가했습니다. 전투용 Collider2D는 추가하지 않았습니다.

## 구현
- SelectiveSceneryAtmosphere: 카메라별 표면 목록과 처리 강도. 기존 렌더링 레이어는 보존하고 실행 중 24~26번 비트를 추가하며 비활성화 시 복구합니다.
- SelectiveSceneryFeature: URP RenderGraph에서 기존 깊이 버퍼를 읽어 선택 구조물 마스크를 그리고 한 번 합성합니다. 투명 소품과 UI 렌더링 이전에 처리합니다.
- PC_Renderer와 Mobile_Renderer에 기능을 등록했습니다. 해당 카메라 컴포넌트가 없으면 실행하지 않습니다.
- 전투맵 재생성 시 보정이 유지되도록 StoryStagesBuilder와 ScoutStageDressing에 적용 함수를 연결했습니다.
- Tools > VisionStriker > Apply Selective Atmosphere to Eleven Stages로 재적용할 수 있습니다. 원본 재질 참조를 유지해 반복 적용해도 보정이 누적되지 않습니다.

## 검증
- Windows Unity 6000.4.5f1의 실제 씬 로딩과 렌더링으로 11개 맵, 정면·측면·뒤쪽 총 33장을 촬영했습니다.
- 검사 119개 PASS, 수집된 런타임/렌더링 오류 0개. 전투를 자동 수행한 검사는 아닙니다.
- 효과 대상이 있는 9개 맵 × 3방향의 픽셀 검사 27개 PASS: 대상 외부에서 3/255를 넘는 픽셀 변화 0개(경계 2px 제외).
- 플레이어 저장 데이터는 검사 전후 동일합니다.
- PC 품질에서 화면 검증을 수행했습니다. 모바일 기기 성능과 별도 빌드는 측정하지 않았습니다.

## 변경 파일
- Assets/Editor/ScoutStageDressing.cs
- Assets/Editor/StoryStagesBuilder.cs
- Assets/Scenes/ScoutPass.unity
- Assets/Scenes/StoryStages/BlackCloudEntrance.unity
- Assets/Scenes/StoryStages/EasternRidge.unity
- Assets/Scenes/StoryStages/HiddenQuarry.unity
- Assets/Scenes/StoryStages/OldFerry.unity
- Assets/Scenes/StoryStages/PrisonStockade.unity
- Assets/Scenes/StoryStages/RiverAmbush.unity
- Assets/Scenes/StoryStages/SealedGate.unity
- Assets/Scenes/StoryStages/SearchCamp.unity
- Assets/Scenes/StoryStages/SluiceFortress.unity
- Assets/Scenes/StoryStages/WindRidge.unity
- Assets/Settings/Mobile_Renderer.asset
- Assets/Settings/PC_Renderer.asset

새 파일은 Assets/Scripts/Rendering, Assets/Environments/SelectiveAtmosphere, 관련 Editor 도구입니다. 원본 씬/설정/빌더 15개는 문서의 결과 폴더 backup에 보관했습니다.
기존 애니메이션, 전투 코드, 스토리/진행 데이터는 이번 작업으로 수정하지 않았습니다. Git commit/push는 수행하지 않았습니다.