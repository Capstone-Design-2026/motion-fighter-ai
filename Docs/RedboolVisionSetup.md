# REDBOOL 모션 입력 연결 — Python v7 / Unity 수신기 v5

이 브랜치에는 연결 코드가 `Assets/RedboolVision`에 들어 있습니다. ZIP을 따로 복사하지 않습니다.
`CaveTutorial`과 `TrainingPoseGraphic`도 오른손 레이즈 → 로우킥 및 새 수신기로 연결되어 있습니다.

확인한 게임 기준: `feature/unity`의 `e1af7d3` (Unity 6000.4.5f1).
전투 API는 `55844cd`와 같으므로 수신기 버전은 `unity-55844cd-v5`입니다.
Python 쪽은 짝이 되는 [앉은 자세 데모 v7 PR #5](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/5)의 코드를 사용합니다. 기존 5055 송신기는 이 수신기와 호환되지 않습니다.

## 팀원 PC에서

1. Unity PR #4가 합쳐진 feature/unity를 pull하고 Unity에서 엽니다. 아직 검토 중에는 변경 없는 별도 작업 폴더에서 `codex/unity-vision-demo-v5-20261104` 브랜치를 사용합니다. Python PR #5는 별도 feature/vision-v2 대상이며 자동으로 함께 합쳐지지 않습니다.
2. Console 컴파일 오류가 없는지 확인합니다.
3. `Tools > REDBOOL > Run Input Adapter Self Check`를 실행합니다.
4. 테스트할 전투 씬을 열고 `Tools > REDBOOL > Add Vision Demo Receiver`를 한 번 실행한 뒤 씬을 저장합니다. 사용할 다른 전투 씬도 동일하게 설정합니다.
5. Play를 누르고 Python PC와 같은 LAN에서 UDP 5065를 허용합니다.
6. Python PC에서 실행합니다. 아래 IP는 실제 Unity PC 주소로 바꿉니다.

```bash
bash scripts/check_unity_connection.command --host 192.168.0.10
bash scripts/run_seated_demo.command --host 192.168.0.10 --movement --seconds 600
```

첫 명령은 카메라 없이 실제 게임에 공격·방어·이동 입력을 보냅니다. 두 번째 명령은 카메라를 사용하고 촬영을 저장합니다.
Python PC에서 `C`로 가드를 보정합니다. 상체를 자기 왼쪽/오른쪽으로 기울이면 캐릭터가 화면 좌/우로 이동하고 중앙으로 돌아오면 정지합니다.
기존 5055 수신기는 새 수신기가 활성화되는 동안 중지됩니다. 같은 씬에 새 수신기를 중복 배치하지 않습니다.

## 동작과 게임 규칙

| 몸동작 | 게임 입력 |
|---|---|
| 왼손 펀치 | JAB |
| 오른손 펀치 | STRAIGHT |
| 왼손 레이즈 | HIGH_KICK |
| 오른손 레이즈 | LOW_KICK |
| 양손을 올려 블락 | 시간제 방어 1회 |
| 좌우로 상체 기울이기 | LEFT / RIGHT |
| 중앙 복귀 | NEUTRAL |

최신 Fighter는 방어를 시작하면 기본 1.5초 유지하며 손을 내려도 즉시 취소하지 않습니다. 성공/실패/피격에 따른 종료와 후딜은 게임 규칙을 따릅니다.
손을 계속 올려도 새 수신기는 방어를 반복 발동하지 않습니다. 손을 내려 다시 올려야 다음 방어 입력이 됩니다.
바쁜 동안 손을 계속 들고 있는 경우에만 방어 시작을 기다립니다. 놓거나 연결이 끊기면 기다림은 취소됩니다.
공격이 `GAME_BUSY`로 거절되면 나중에 지연 재생하지 않습니다. 연결이 끊기면 이동 입력은 해제되지만 진행 중인 시간제 방어를 강제 취소하지 않습니다.

`APPLIED`는 Fighter 공격 시작 확인이며 적에게 맞았다는 뜻이 아닙니다. `TUTORIAL_APPLIED`는 수련 입력 전달이며 실제 공격 성공과 다릅니다.
카메라가 돌아간 경우의 좌우 이동은 최신 PlayerController의 화면 방향 변환을 사용합니다.

## 검증 범위와 남은 확인

Python v7은 실제 분류기 학습과 Lite/Full 재생을 비교한 뒤 Lite + 보정 규칙을 기본으로 선택했습니다. 최신 16:17 개발 자료에서 왼손 레이즈 3/5→5/5, 전체 공격 16/16, 블락3/3, 이동/정지7/7을 확인했습니다. 69개 로컬 검사가 통과했습니다. 이미 수정에 참고한 자료이므로 독립 인식률이나 Unity 실행 검증은 아닙니다. 새 짧은 촬영은 `bash scripts/run_left_raise_check.command`로 진행하며 PREPARE에서 가드 복귀 후 GO를 기다립니다. 자세한 비교는 Python PR의 `docs/seated-demo-v7.md`에 있습니다.
Python v7는 작은 중심 이동과 같은 방향 어깨 기울기를 함께 사용하며, 이 수신기의 기존 60ms 평활화와 호환됩니다. 이동 최소 세기는 .35로 바뀌었으므로 실제 화면에서 속도도 확인합니다.
연결 검사의 `passed`는 네 공격 기술명, 수신기 버전, 방어 1회 발동과 종료를 확인합니다. 이동·HP·접촉·애니메이션·실제 지연은 화면에서 확인합니다.
이번 변경을 준비한 Mac에는 Unity Editor가 없어 C# 컴파일, 위 메뉴 검사, Play Mode 검사는 아직 실행하지 못했습니다.

- 네 공격이 각각 한 번 시작되는지
- 방어를 3초 유지해도 한 번만 발동하는지, 내려서 다시 올리면 새 방어가 가능한지
- 좌우 이동과 중앙 정지, Python 종료 후 이동 해제
- 최신 카메라 시점에서 이동 방향이 뒤집히지 않는지
- 수련 7단계와 매번 준비 자세 복귀

로컬 촬영 영상·개인 세션·학습 모델은 이 PR에 포함하지 않습니다.
