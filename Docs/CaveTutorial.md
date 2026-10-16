# 마른 폭포 동굴 튜토리얼

새 게임 → 사부 납치 프롤로그 → 지도에서 마른 폭포 동굴 선택 → 벽화 수련 → 출발 컷씬 → 기존 지도 탐색.

사부 도윤은 마른 폭포 동굴에서 기본 동작을 다시 익히고 청계역의 연화에게 도움을 청하라고 지시한다. 컷씬은 기존 대화 시스템을 사용하며 캐릭터 초상화를 나중에 연결할 수 있다.

## 동작 계약

| 사용자 동작 | UDP action | 게임 동작 |
|---|---|---|
| 왼손 앞으로 지르기 | JAB (LEFT_PUNCH 별칭) | 잽 |
| 오른손 앞으로 뻗기 | STRAIGHT (RIGHT_PUNCH 별칭) | 스트레이트 |
| 왼손 위로 들기 | HIGH_KICK (LEFT_HAND_UP 별칭) | 하이킥 |
| 왼손 아래로 내리기 | LOW_KICK (LEFT_HAND_DOWN 별칭) | 로우킥 |
| 기존 가드 자세 | BLOCK | 막기 |

기존 localhost UDP 5055, JSON action/confidence를 사용한다. 기본 자세는 IDLE 또는 NONE. confidence 0.7 이상, 각 동작 두 번 성공. 연속 동일 신호는 한 번만 세며 기본 자세 복귀 후 재시도한다. ATTACK은 잽/스트레이트를 구별할 수 없으므로 성공 처리하지 않는다.

Python 인식 모델, 제스처 분류 기준, 실제 전투 기술/애니메이션 분리는 이번 변경 대상이 아니다. 새 신호를 발행하도록 백엔드를 변경해야 카메라 튜토리얼 전체를 완료할 수 있다. 벽화 우측 캐릭터는 입력-기술 대응을 설명하는 도식이다.

## 테스트 / 저장

오른쪽 위 키보드 테스트를 명시적으로 선택한다. Space 기본 자세, 1 잽, 2 스트레이트, 3 하이킥, 4 로우킥, 5 막기. 각 성공 사이 Space를 눌러 두 번 반복한다.

수련 중단 후 현재 단계부터 재개. 완료한 단계만 저장하고 진행 중 단계의 성공 횟수는 초기화한다. 기존 저장에서 동굴이 이미 완료된 경우 강제로 수련하지 않는다. 새 시작 시 초기화된다. 새 흐름을 보려면 새 게임을 시작한다.

## 수정 위치

- Assets/Scripts/Meta/CaveTutorial.cs: 수련 UI/인식/저장
- Assets/Scripts/UI/TrainingPoseGraphic.cs: 벽화 동작 도식
- Assets/Scripts/Server/UdpActionReceiver.cs: 튜토리얼 패킷 전달
- Assets/Resources/Story/Cutscenes/cave_prologue.asset: 납치/사부 지시
- cave_enter.asset: 수련 안내 / cave_visit.asset: 출발
- Assets/Resources/UI/Wuxia/TrainingCave.png: 생성형 동굴 배경

배경 제작 방향: 한국 무협 수묵화 풍, 마른 폭포 동굴 내부, 넓고 낮은 대비의 중앙 석벽, 은은한 호박색 조명과 바깥 빛. 글자/인물 없이 런타임 동작 도식을 올릴 여백을 둠.

## 어깨 이동 추가

막기 뒤 6단계 왼쪽 이동, 7단계 오른쪽 이동을 추가. 각 두 번 성공해야 완료된다.

공격 action과 별개인 movement=LEFT/RIGHT/NEUTRAL 및 movement_strength를 사용한다. 이동 강도 0.25 이상을 유효 입력으로 보며 공격 confidence와 무관하다. NEUTRAL 수신 후 해당 방향을 한 번 인정하고 중앙 복귀가 있어야 다시 인정한다. movement 없는 IDLE 패킷은 중앙 복귀로 인정하지 않는다. 신호 단절 3초 이후에도 다시 중앙 복귀가 필요하다.

키보드 테스트: Space 중앙, 6 왼쪽 이동, 7 오른쪽 이동. 기존 1~5 키 유지. 이미 완료한 저장은 재수련시키지 않으며 진행 중 저장은 기존 단계 순서 그대로 이어진다. 새 게임은 총 7단계. 벽화 네 그림은 유지하며 하단 도식에 어깨 이동 및 이동 방향을 표시한다.
