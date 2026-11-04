# Windows UDP 10054 처리 — 2026-11-04

Unity 수신기가 꺼져 있거나 UDP 포트에 수신 프로그램이 없을 때, Windows Python의 recvfrom에서 ConnectionResetError(10054)가 발생할 수 있습니다. 기존 Sender.service는 BlockingIOError만 처리해, 해당 오류가 이벤트 만료 처리까지 중단시켰습니다. 팀원은 test_expired_event_never_sent의 Windows 실패로 이 문제를 보고했습니다.

Microsoft는 UDP에서 WSAECONNRESET이 이전 송신에 대한 ICMP Port Unreachable 응답을 나타낼 수 있다고 설명합니다: https://learn.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-recvfrom

## 수정

src/realtime_vision/transport.py에서 ConnectionResetError만 별도로 처리합니다. receive_reset 카운터와 receive_error 로그에 기록하고 현재 수신 반복을 끝낸 뒤 기존 시계 유효기간 검사와 DemoSender의 이벤트 만료 처리를 계속합니다. 다음 예정된 SYNC에서 연결을 다시 시도할 수 있습니다. 오류를 ACK 성공으로 처리하지 않으며, 다른 OSError는 그대로 전달합니다.

공격의 150ms 만료 시각, 최대 재전송 횟수, 중복 방지, 인식 기준, 학습 모델은 변경하지 않습니다. Unity가 없으면 연결 검사가 No clock ACK로 실패하는 것은 계속 정상입니다. 이 수정이 수신기 부재나 방화벽 문제를 연결 성공으로 바꾸지는 않습니다.

## 검증

- 기존 코드에 Windows 10054를 주입한 새 회귀 검사: 4개 중 3개 오류로 문제 재현.
- 수정 코드: 새 4개 검사 통과. 만료 이벤트 제거, 오래된 시계 만료, 이후 SYNC 복구 및 만료 공격 부활 방지, 다른 소켓 오류 전파를 확인.
- 기존 촬영·UDP·이벤트 만료·모의 카메라·프로토콜 검사를 포함한 81개 검사 통과, skip 0.
- 실행 환경은 macOS입니다. Windows 오류는 모의 소켓으로 재현했으며 Windows 운영체제에서의 실제 실행 확인은 팀원 PC에서 해야 합니다.

Python PR #5의 최신 코드를 받은 뒤 Windows의 저장소 루트 PowerShell에서 기존 Python 환경을 활성화하고:

```powershell
$env:PYTHONPATH = "src;tests"
python -m unittest -v test_udp_receive_reset test_redbool_demo.TransportTests
```

6개 검사가 통과하는지 확인합니다. 다음은 같은 Windows PC의 Unity Play 상태에서 로컬 연결 검사입니다:

```powershell
$env:PYTHONPATH = "src"
python -m redbool_demo.probe --host 127.0.0.1
```

이 명령은 카메라 없이 실제 게임에 네 공격, 시간제 블락, 좌우 이동 입력을 보냅니다. 전투 씬 수신기의 Bind Address가 0.0.0.0, Port가 5065이고 Play 상태일 때 진행합니다. summary.json과 udp.jsonl 경로가 마지막에 출력됩니다.

로컬에서 clock ACK가 오면 수신기/프로토콜 자체가 응답한다는 증거입니다. passed=true는 합성 입력 검사 성공이며 카메라 인식이나 실제 적중·화면 지연까지 검증한 것은 아닙니다. 로컬 ACK는 오는데 Mac에서는 안 오면 Windows 방화벽·서로 다른 네트워크 경로·단말 간 통신 제한을 다음으로 확인합니다. 로컬에서도 안 오면 수신기 상태·실행 포트·Console부터 확인합니다.

## 현재 별도 상태

19:09 촬영의 왼손 레이즈5/5·가드 복귀5/5·11.53초 휴식 오발동 없음 결과는 유지됩니다. Mac에서 팀원 PC로 두 번 시도한 연결 검사는 clock ACK가 없어 공격 검사를 시작하지 못했습니다. 팀원은 Unity Play, Console 오류 없음, WAITING FOR PYTHON :5065, Bind Address 0.0.0.0을 확인했습니다. 따라서 Windows 예외 수정과 실제 PC 간 연결 성공은 별도로 확인해야 합니다.
