# 흑운협 전투 무대 10종

작업 프로젝트: `C:\Unity\VisionStriker`

기존 산길 척후병 무대(ScoutPass)를 유지하고, 나머지 전투 노드 10곳에 전용 씬을 연결했습니다. 지도 노드와 스토리 순서, 적의 체력·공격력, 플레이어 조작, 최신 통합 애니메이션은 기존 설정을 사용합니다.

## 장소별 구성

| 지도 노드 | 전투 씬 | 구성 | 새 소품 |
|---|---|---|---|
| river | RiverAmbush | 계곡 물가, 낮은 폭포, 바위와 소나무, 호송 물자 | 기존 소품 활용 |
| ridge | WindRidge | 열린 산등성이, 난간과 짐수레, 먼 망루 | 기존 소품 활용 |
| old_ferry | OldFerry | 돌 선착장, 목재 부두, 강, 물 위에서 흔들리는 배 | CargoBoat |
| gate | SealedGate | 큰 관문 문짝, 양옆 성벽과 망루, 누각, 돌 마당 | GateDoors |
| quarry | HiddenQuarry | 채석장 단차, 적재 석재, 인양기, 뒤편 운송 통로 | QuarryHoist |
| east | EasternRidge | 소나무 숲, 난간, 감시 망루와 수레 | 기존 소품 활용 |
| camp | SearchCamp | 천막 세 동, 수색 물자, 화로, 빈 우리 | PrisonCage |
| rescue | PrisonStockade | 사방 목책, 포로 우리 두 개, 감시탑, 물 항아리 | PrisonCage, GateDoors |
| entrance | BlackCloudEntrance | 암벽 협곡, 관문, 붉은 깃발, 포로 대기 공간 | GateDoors, PrisonCage |
| final | SluiceFortress | 세 수문, 높은 성벽·누각, 흐르는 물, 넓은 제어 마당 | SluiceWinch |

## 임시 캐릭터

> 2026-11-04 기준: 10개 노드 모두 전용 캐릭터로 교체됨. 02–06은 [StoryCharacters02-06](StoryCharacters02-06.md), 07–11은 [StoryCharacters07-11](StoryCharacters07-11.md) 참고. 아래는 처음 무대를 만들 때의 기록입니다.

10개 노드의 적 외형은 `Assets/Characters/Scout/Scout_Visual.prefab`으로 연결했습니다.

각 스테이지의 기존 이름과 전투 수치는 유지합니다. 나중에 캐릭터가 완성되면 `Assets/Resources/RunConfig.asset`의 **지도 노드별 적 외형**에서 해당 `nodeId`의 프리팹을 교체하면 됩니다. 전투 씬을 다시 만들 필요는 없습니다.

## 파일 위치

- 전투 씬: `Assets/Scenes/StoryStages/`
- 소품·재질·환경 메시: `Assets/Environments/StoryStages/`
- 무대 생성 도구: `Assets/Editor/StoryStagesBuilder.cs`
- 검증·촬영 도구: `Assets/Editor/StoryStagesReview.cs`
- 작은 부유·화로 조명 움직임: `Assets/Scripts/Combat/StoryStageMotion.cs`
- 지도 연결: `Assets/Resources/RunConfig.asset`
- 빌드 씬 등록: `ProjectSettings/EditorBuildSettings.asset`

## 소품 처리

사용자가 Blender에 준비한 5개 모델의 원본을 복사 보관한 뒤 게임용 모델을 따로 내보냈습니다.

- 원본 총 약 917만 면 → 가까운 거리에서 사용하는 모델 합계 약 21만 면.
- 각 소품 LOD: 약 42,000 / 13,000 / 3,800 면.
- 색상 텍스처 최대 2048, 노멀 최대 1024, 밉맵·압축 사용.
- Unity는 경량 FBX와 별도 재질을 사용하며, 원본 Blender 파일은 프로젝트 밖에 보관합니다.
- 배는 작게 부유하고, 붉은 깃발은 바람에 변형됩니다. 하늘 구름·물줄기는 시간에 따라 움직입니다.
- 권양기와 인양기는 완성된 하나의 메시이므로 전체를 억지로 회전시키지 않았습니다.

## 검증

`combat_validation.txt`에 전체 전투 검증 결과, `review/checks.txt`에 최종 화면 검증 결과를 기록했습니다.

- 지도 노드에서 실제 전용 씬 로드.
- 척후병 Humanoid·애니메이터 적용 및 스테이지별 체력 유지.
- 배경 소품이 전투용 2D 충돌을 추가하지 않음.
- 무대마다 72가지 카메라 각도·화면 비율·캐릭터 위치 조합, 총 720가지 구도.
- 좌우 이동 제한, 실제 스트레이트 적중.
- 일반 전투 승리 후 지도 복귀와 해당 노드 클리어.
- 최종 전투 승리 후 후속 컷씬을 거쳐 엔딩.
- 패배 후 시작 화면 복귀·진행 초기화.
- 기존 저장 데이터 복구.

360도 무대와 카메라는 유지되며, 전투 이동·판정은 기존 전투 평면을 사용합니다. 전체 3D 횡이동 전투로 바꾸는 작업은 포함하지 않습니다.

## 보기

프로젝트 밖 리뷰 폴더 `C:\Users\sbpar\Documents\VisionStriker_StoryStages_20261020`:

- `index.html`: 무대별 실제 정면·반대편·전체 배치와 회전 영상
- `00_Unity_Stages_Overview.jpg`: 10종 한눈에 보기
- `StoryStages_Reel.mp4`: 10개 무대 이어보기
- `review/*_front.jpg`: 실제 전투 구도
- `review/*_back.jpg`: 180도 반대편 구도
- `review/*_overview.jpg`: 배치 확인용 높은 시점
- `props/*.png`: 5개 경량 소품 확인 이미지
- `TripoFiveProps_Source.blend`: 사용자가 만든 소품 원본의 안전 복사본

이 리뷰 폴더의 사진·영상은 실제 Unity 렌더링입니다. 적은 아직 척후병 임시 외형입니다.
