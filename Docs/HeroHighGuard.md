# 주인공 방어 동작 — 복싱 하이가드와 무에타이 로우킥 막기

2026-11-12 적용. 주인공(Hero02)의 방어 동작 두 가지를 전용 동작으로 바꿨다.

- **얼굴 막기**: 복싱 하이가드. 두 주먹을 이마 앞으로 모으고 팔뚝을 얼굴 앞에 나란히 세우며, 막는 동안 무릎을 굽혀 자세를 조금 낮춘다.
- **로우킥 막기**: 무에타이 체크. 뒷발에 체중을 옮기고 앞다리 무릎을 들어 정강이로 받는다. 두 주먹은 얼굴 옆에 둔다.

적은 계속 공용 방어 클립(`Unified_Common_GUARD*`)을 쓴다.

## 게임에서의 흐름

`Fighter3DView`의 방어 흐름은 그대로이고, 주인공 컨트롤러의 상태 여덟 개가 재생하는 클립만 바뀌었다. 로우킥 막기는 상태 이름에 `_LOWKICK`이 붙고 `Hero_Guard_Low*` 클립을 쓴다.

| 상태 | 언제 | 클립 | 구간 |
| --- | --- | --- | --- |
| `GUARD` | 방어를 누른 직후 0.27초 | `Hero_Guard_High` | 0.00초부터 (0.10초에 팔이 올라가고 0.18초에 자리 잡음) |
| `GUARD_HOLD` | 누르고 있는 동안 계속 | `Hero_Guard_High_Hold` | 1.6초 반복. 막은 자세로 버티며 숨만 쉰다 |
| `GUARD_HIT` | 막힌 타격을 받았을 때 0.225초 | `Hero_Guard_High` | 0.351초부터 (0.39초에 가장 밀렸다가 0.56초에 복귀) → 다시 `GUARD_HOLD` |
| `GUARD_RELEASE` | 방어를 뗐을 때 0.252초 | `Hero_Guard_High` | 0.648초부터 → `IDLE` |

방어 유지는 `Fighter.IsBlocking`이 참인 동안 `_HOLD`가 반복 재생되는 방식이라, 키보드든 카메라 인식이든 누르고(자세를 잡고) 있는 만큼 자세가 유지된다.

## 방어 높이는 자동으로 바뀐다

누르고만 있으면 된다. 상대가 정한 공격에 맞춰 자세가 바뀐다(`Fighter.DeclareIntent` → `ReceiveIntent`).

- 예고된 공격이 없거나 잽·스트레이트·하이킥이면 얼굴 막기.
- 상대가 로우킥을 정하면 그 순간 로우킥 막기로 바뀐다. 이미 막고 있었다면 팔을 내렸다 다시 올리지 않고 0.14초에 걸쳐 그 자세로 바로 넘어간다(`Fighter3DView.GuardSwitchBlend`).
- 로우킥 판정이 끝나면 0.3초 뒤 얼굴 막기로 돌아온다(`Fighter.LowGuardLinger`). 다리를 든 채로 계속 서 있지 않게 하기 위한 것으로, 이번에 추가했다. 3D 전투에만 적용되고 막기 판정 결과는 달라지지 않는다(다음 공격이 예고되면 높이가 다시 맞춰진다).

높이 전환 두 가지(바로 넘어가기, 얼굴 막기로 복귀)는 공용 코드라 적에게도 똑같이 적용된다.

## 필살기(파운장)를 막았을 때

막은 쪽은 뜨지 않고 가로로만 약하게 밀린다. `SpecialMove.guardKnockback`(처음 속도 2.1 m/s)으로 정하며 실측 약 0.17m다.
이전에는 정타 넉백의 55%(초속 3.6m, 약 0.53m)로 밀렸다. 피해 25%와 방어 해제는 그대로다. 일반 공격을 막았을 때는 지금처럼 밀리지 않는다.

## 파일

- `Assets/Characters/Hero02/ApprovedGuard/Hero_Guard_High.anim`, `Hero_Guard_High_Hold.anim`, `Hero_Guard_Low.anim`, `Hero_Guard_Low_Hold.anim` — Humanoid 클립. 손가락·삼각근 등 Humanoid 밖의 뼈 11개는 Transform 커브로 들어 있다.
- `Assets/Characters/Hero02/ApprovedGuard/Hero_Guard_High.motion.json`, `Hero_Guard_Low.motion.json` — Blender에서 내보낸 원본 동작(뼈 32개, 30fps). 뼈마다 기본자세에서 얼마나 돌았는지를 담는다.
- `Assets/Editor/HeroGuardBaker.cs` — 위 JSON을 주인공 뼈대에 얹어 클립으로 굽고, `Hero_ApprovedMovement.controller`의 방어 상태에 연결한다.
- `Assets/Editor/HeroGuardPlayCheck.cs` — 전투 씬을 재생해 방어 흐름, 높이 자동 전환, 파운장 방어 넉백을 확인하고 화면을 저장한다.

Blender 작업 파일(`guard_tekken_preview.blend`, 동작 이름 `GUARD_FACE_highguard(_hold)`, `GUARD_LOW_check(_hold)`)과 자세를 만드는 스크립트는 저장소 밖(작업자 PC의 문서 폴더 `VisionStriker_GuardMotions_20261112`)에 있다.

## 다시 굽기

동작을 고치면 Blender에서 JSON을 다시 내보낸 뒤 Unity 에디터를 닫고 아래를 실행한다.

```
Unity -batchmode -projectPath <프로젝트> -executeMethod HeroGuardBaker.Bake -heroGuardReport <폴더>
Unity -batchmode -projectPath <프로젝트> -executeMethod HeroGuardPlayCheck.Run -heroGuardReport <폴더> -redbool-keyboard
```

굽기 도구는 승인 기본자세의 두 번째 프레임을 기준으로 Blender와 Unity의 좌표계를 맞추고, 얹은 자세와 구운 클립의 관절 위치를 비교해 어긋나면 실패로 끝난다. `.anim` 파일은 덮어써도 GUID가 유지된다.

## 2026-11-12 검증 결과

- 굽기: 네 클립 모두 Blender 자세와 Unity에 얹은 자세의 관절 위치 차이 최대 0.08%(키 대비), 구운 클립을 재생한 결과와의 차이 최대 0.02%.
- 전투 씬(SealedGate) 재생 확인 39/39 통과.
  - 얼굴 막기: 올리기 → 유지(3초 넘게) → 막힌 타격 반응 → 다시 유지 → 상대 잽 방어 → 풀기 → 기본자세.
  - 로우킥 막기: 막는 중 로우킥 예고에 자동 전환, 잽 예고에 얼굴 막기로 복귀, 상대의 실제 로우킥 방어(피해 없음), 로우킥이 끝난 뒤 얼굴 막기로 복귀, 로우킥이 예고된 상태에서 새로 누르기.
  - 파운장 방어: 0.166m 밀림, 뜨지 않음.
- 회귀: 캠페인 재생 검사 155개, 파운장 검사 33개, 스테이지별 캐릭터 검사 542개 통과.
