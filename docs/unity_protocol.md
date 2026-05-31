# Unity UDP Protocol

## 1. 개요

- Python AI Vision 모듈이 MediaPipe + MLP로 인식한 action을 Unity로 UDP 전송한다.

## 2. Network

- Protocol: UDP
- Host: 127.0.0.1
- Port: 5055

## 3. Payload Format

JSON 예시:

```json
{
  "action": "ATTACK",
  "confidence": 0.91,
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

## 5. Unity Mapping

| Python Action | Unity Behavior |
|---|---|
| ATTACK | Animator Trigger Attack |
| BLOCK | Animator Trigger Block |
| JUMP | Animator Trigger Jump |
| CROUCH | Animator Trigger Crouch |
| IDLE | Idle state |
| NONE | Ignore or Idle |

## 6. Unity Implementation Note

- UDP listener를 별도 스레드에서 실행
- 수신된 action은 main thread에서 Animator에 적용
- ATTACK/JUMP/BLOCK/CROUCH는 trigger 방식 추천
- IDLE/NONE은 상태 유지 또는 무시 가능

## 7. Latency Note

- Python은 action 변경 시 즉시 전송한다.
- 같은 action 반복 전송은 제한한다.
