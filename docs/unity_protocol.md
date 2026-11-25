# Unity UDP Protocol

## 1. 개요

- Python AI Vision 모듈이 MediaPipe + MLP로 인식한 action과
  shoulder depth 기반 rule-based movement를 Unity로 UDP 전송한다.
- action과 movement는 독립 channel이다. `MOVE_LEFT` / `MOVE_RIGHT`는
  MLP action class에 포함하지 않는다.

## 2. Network

- Protocol: UDP
- Host: 127.0.0.1
- Port: 5055

## 3. Payload Format

JSON 예시:

```json
{
  "action": "ATTACK",
  "movement": "RIGHT",
  "confidence": 0.91,
  "movement_strength": 0.61,
  "timestamp": 1710000000.123
}
```

## 4. Action List

| Action | Description |
|---|---|
| IDLE | 기본 전투 자세 |
| NONE | 아무 동작도 아님 / 잘못된 자세 |
| ATTACK | 공격 |
| BLOCK | 방어 |
| JUMP | 점프 |
| CROUCH | 숙이기 |

## 5. Movement List

| Movement | Description |
|---|---|
| LEFT | 왼쪽 이동 입력 |
| RIGHT | 오른쪽 이동 입력 |
| NEUTRAL | 이동 입력 없음 |

## 6. Unity Mapping

| Python Action | Unity Behavior |
|---|---|
| ATTACK | Animator Trigger Attack |
| BLOCK | Animator Trigger Block |
| JUMP | Animator Trigger Jump |
| CROUCH | Animator Trigger Crouch |
| IDLE | Idle state |
| NONE | Ignore or Idle |

| Python Movement | Unity Value |
|---|---|
| LEFT | `CurrentMovementAxis = -1` |
| NEUTRAL | `CurrentMovementAxis = 0` |
| RIGHT | `CurrentMovementAxis = 1` |

## 7. Unity Implementation Note

- UDP listener를 별도 스레드에서 실행
- 수신된 action과 movement는 main thread에서 적용
- ATTACK/JUMP/BLOCK/CROUCH는 trigger 방식 추천
- IDLE/NONE은 상태 유지 또는 무시 가능
- movement는 receiver의 public readable state로 노출한다.
- 실제 좌우 이동은 PlayerController가 `CurrentMovementAxis`,
  `CurrentMovement`, `CurrentMovementStrength`를 읽어 처리한다.
- Animator를 쓰는 프로젝트에서는 선택적으로 `MoveX` float parameter에
  연결할 수 있다.

## 8. Movement Detection Note

- Python은 MediaPipe left shoulder(11), right shoulder(12)의 z 차이를
  shoulder width로 정규화해 rotation score를 계산한다.
- shoulder visibility가 낮으면 movement는 `NEUTRAL`이다.
- LEFT/RIGHT 진입 threshold와 NEUTRAL 복귀 threshold를 분리해 hysteresis를
  적용한다.
- action stabilization과 movement stabilization은 서로 독립이다.
- 카메라 mirror 방향이 반대면 Python config의 `MOVEMENT_MIRROR` 값을 바꾼다.
- threshold 값은 카메라, 거리, 조명에 따라 실제 플레이 테스트로 튜닝해야 한다.

## 9. Latency Note

- Python은 action 또는 movement가 변경되면 즉시 전송한다.
- 같은 action과 movement 반복 전송은 제한한다.
- `IDLE` / `NONE` 반복 전송 rate limit은 유지하지만 movement 변경은 즉시 전송한다.
