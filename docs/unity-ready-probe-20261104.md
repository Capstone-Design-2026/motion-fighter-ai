# Unity 준비 상태를 확인하는 연결 검사 (2026-11-04)

Windows 로컬 검사에서 잽·스트레이트·하이킥은 APPLIED, 로우킥은 GAME_BUSY였고 가드는 1회 시작·종료했습니다. 이 요약만으로 로우킥 거절 원인이 공격 후딜, 피격, 라운드 종료 중 무엇인지 확정할 수 없습니다.

기존 고정 2.5초 간격 대신 각 입력 전에 새 중립 STATE 요청에 대한 Unity 응답으로 행동 가능 상태를 확인합니다. game_can_act=true, game_guard_active=false가 0.2초간 관측되어야 진행하며, 각 응답은 보낸 지 0.15초 이내여야 합니다. 기본 최대 대기는 단계당 8초입니다. 과거 단계의 응답과 오래된 응답으로 다음 공격을 시작하지 않습니다. 준비 확인 직후 피격되는 경쟁 상황은 여전히 가능하며 GAME_BUSY는 그대로 실패입니다.

각 공격은 준비 확인 후 새로 생성합니다. 150ms 유효기간과 기존 같은 ID 재전송 규칙은 유지하며, 거절된 공격을 새 ID로 재시도하지 않습니다. 대기 중에는 중립 입력을 보내고, 실패 시에도 결과와 수신 상태를 저장합니다. 실제 카메라 인식·모델·Unity 게임 규칙은 변경하지 않습니다.

## 팀원 PC에서 실행

Python 변경을 받은 저장소 루트에서 기존 Python 환경을 활성화합니다. Unity는 체력이 있는 새 전투를 시작하고 Play를 유지합니다. 단독 명령 검사를 위해 적 AI를 잠시 멈췄다면 실제 게임 테스트 전에 다시 켭니다. Unity 수신기는 기존 unity-55844cd-v5를 그대로 사용합니다.

```powershell
$env:PYTHONPATH = "src;tests"
python -m unittest -v test_demo_probe test_udp_receive_reset test_redbool_demo.TransportTests
python -m redbool_demo.probe --host 127.0.0.1
```

검사는 16개입니다. 명령의 --host는 일반 하이픈 두 개입니다. summary에 probe_version=ready-aware-v1이 있어야 수정판입니다. 공격 4개·가드·전체 passed=true인지 확인합니다. 실패하면 failure와 readiness_checks를 함께 전달합니다. 결과는 local_data/demo_probe 아래에 저장됩니다. 준비 불가 상태를 기다릴 필요가 있다면 --ready-timeout 12처럼 제한 시간을 조절할 수 있으나 KO 상태에서는 전투를 다시 시작해야 합니다.

## 검증 범위

로컬 자동 검사 88개 통과(macOS). 새 7개 검사는 신선하고 연속적인 준비 응답, 지연·불일치·과거 응답 거부, 4초 하이킥 복귀 후 로우킥, 영구 KO에서 공격 미생성, 준비 확인 직후 GAME_BUSY를 숨기지 않는지를 확인합니다. 실제 Windows/Unity에서 수정판 검사는 아직 필요합니다.

Windows의 127.0.0.1 검사는 같은 PC 내부 연결입니다. 통과해도 Mac에서 172.31.102.203으로 오는 LAN 연결, 화면의 좌우 이동/중앙 정지, 실제 카메라 인식과 체감 지연은 별도 확인해야 합니다. 기존 Mac 연결에서는 clock ACK가 없었으므로 LAN 성공으로 기록하지 않습니다.
