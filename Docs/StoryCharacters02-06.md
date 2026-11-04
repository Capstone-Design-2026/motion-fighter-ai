# 초중반 스테이지 캐릭터 적용 · 2026-11-04

프로젝트: `C:\Unity\VisionStriker`

| 번호 | 캐릭터 | 지도 노드 | 전투 씬 | 시각적 신장 | 모델 삼각형 수 |
| --- | --- | --- | --- | ---: | ---: |
| 02 | 계곡 잠복병 | river | RiverAmbush | 1.80m | 10,685 |
| 03 | 고용 산적 | ridge | WindRidge | 1.92m | 10,081 |
| 04 | 옛 나루 약탈자 | old_ferry | OldFerry | 1.72m | 10,566 |
| 05 | 철문 | gate | SealedGate | 1.98m | 9,413 |
| 06 | 채석장 정예병 | quarry | HiddenQuarry | 1.86m | 10,819 |

01 산길 척후병(scout · ScoutPass)은 기존 척후병 모델을 그대로 쓴다. 이로써 01–11 모든 전투 노드가 전용 캐릭터를 갖는다.
07–11은 [후반 스테이지 캐릭터 적용](StoryCharacters07-11.md)을 참고한다.

## 모델

- 원본은 10/11 컨셉 시트(`Documents/VisionStriker_CharacterConcepts_01-11`)를 Tripo Studio에서 생성·텍스처·리깅한 것이다.
- Tripo DCC Bridge로 Blender에 보낸 다섯 모델을 그대로 내보냈다. 모두 Mixamo 65개 뼈대(손가락 포함)이고 미할당 정점이 없어, 07처럼 뼈대를 맞추는 보정은 하지 않았다.
- 정점 영향 뼈대는 최대 4개로 제한·정규화했다. 컬러 텍스처 원본은 4096이고 Unity 런타임 최대 크기는 2048이다.
- 전투 판정과 능력치는 바꾸지 않았다. 신장은 시각 모델에만 적용된다.

## 애니메이션

`Unified_Combat.controller`를 공통 사용한다. 기존 척후병의 현재 승인된 Override 클립을 소스로 하고 체형별 바닥 접촉에 맞춰 `RootT.y`만 보정했다(최대 보정량 0.09–0.13m, 07–11과 같은 범위).

## 설치 도구 변경

- `StoryCharacterInstaller`의 캐릭터 목록을 02–11로 넓히고 전투 씬 이름을 `Scenes`로 함께 관리한다.
- `-characterKeys 02_ValleyAmbusher,03_HiredBandit` 처럼 일부만 설치·씬 반영·검증할 수 있다. 지정하지 않으면 02–11 전체다.
- `ApplyConfiguredVisual`은 02–11 노드 모두에 적용되므로, `StoryStagesBuilder`로 무대를 다시 만들어도 해당 캐릭터가 배치된다.
- `CombatContactProbe`도 02–11 전체 모델을 대상으로 한다.

## 검증

`StoryCharacterVerification`을 02–11 열 명 전체에 대해 실제 Play 모드로 실행했다.

11/4에 합쳐진 전투 규칙 변경에 맞춰 검증 항목을 갱신했다.

- 방어는 시전형이다: 시전 후 유지 → 막기 반응 → 성공 시 즉시 종료, 아무것도 막지 못하면 1.5초 뒤 종료.
- 막힌 타격의 피해는 0이다(이전 검증은 약간의 피해를 기대했다).
- 헛친 공격은 후딜이 0.3초 늘어나므로 복귀 확인 대기 시간을 늘렸다.
- 3D 방어 높이는 자동이다. 로우킥 방어는 상대가 로우킥을 예고(`DeclareIntent`)했을 때만 나오므로, 검사도 예고 후 방어를 시전한다.
- 실제 타격·방어 검사는 중심 간 0.9 거리에서 한다. 3D 접촉 판정 기준 잽 사거리(0.9 적중, 1.2 빗나감)이고 3D 적 AI 공격 거리(1.0) 안이다. 이전 1.1 거리는 접촉 판정 도입 전 기준이었다.
- 방어 검사는 피해 0뿐 아니라 실제로 막힌 타격이 있었는지(`Health.OnDamaged` 가드 여부)까지 확인한다.

검증 결과: 02–11 전체 502개 검사 통과, 실패 0, 실행 오류 0. 검사 전후 사용자 진행 저장이 복원됐다.

검증 기록과 영상: `C:\Users\sbpar\Documents\VisionStriker_Characters02-06_Apply_20261104\index.html`

원본: 같은 폴더의 `Imported_Originals.blend`. 내보내기: `export_models.py`, `Prepared_Characters.blend`.
