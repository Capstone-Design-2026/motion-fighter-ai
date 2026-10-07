# Movement 캘리브레이션 가이드

`feature/shoulder-movement`의 좌/우 이동 threshold를 **각자 자기 환경에서 측정해서 보내는** 절차입니다.
소요 시간 약 3분, 실제 측정은 45초입니다.

> **왜 각자 해야 하나요?**
> MediaPipe의 z(깊이) 값은 카메라 모델, 거리, 조명, 설치 높이에 따라 스케일이 달라집니다.
> 한 사람 값을 그대로 쓰면 다른 사람 환경에서는 이동이 아예 안 잡히거나 팔만 흔들어도 잡힙니다.

---

## 1. 준비물 확인

| 항목 | 확인 방법 |
|---|---|
| 저장소 최신화 | `git pull` 후 `feature/shoulder-movement` 브랜치 |
| 가상환경 | `.venv` 폴더가 있는지 |
| MediaPipe 모델 | 프로젝트 루트에 `pose_landmarker.task` 파일이 있는지 |
| 촬영 공간 | 상체가 다 나오고, 좌우로 몸을 돌릴 수 있는 공간 |

`pose_landmarker.task`가 없으면 README의 *MediaPipe Model Download* 항목을 먼저 진행하세요.

**카메라 세팅 권장값**

- 거리: 1.5 ~ 2m (상체와 양팔이 화면에 다 들어오게)
- 높이: 가슴 ~ 눈높이, 정면
- 조명: 얼굴과 어깨에 그림자가 심하게 지지 않는 정도
- 배경: 사람이 지나다니지 않는 곳

> 측정할 때의 카메라 위치를 **실제 게임할 때와 똑같이** 두세요. 위치가 바뀌면 값도 바뀝니다.

---

## 2. 실행

**Windows**

```bat
.venv\Scripts\activate
python src\calibrate_movement.py
```

**macOS**

```bash
source .venv/bin/activate
python src/calibrate_movement.py
```

MLP 모델은 로드하지 않으므로 `models/` 폴더가 비어 있어도 실행됩니다.

---

## 3. 화면 안내대로 3단계 진행

창이 뜨면 자동으로 진행됩니다. 각 단계는 `GET READY 3` 카운트다운 → 자세 잡는 시간 1.5초 → 기록 순서입니다.

| 단계 | 화면 표시 | 해야 할 일 | 기록 시간 |
|---|---|---|---|
| 1/3 | `NEUTRAL` | 기본 전투 자세를 유지하면서 **공격·방어·점프 팔 동작을 섞어서** 합니다. **이동은 하지 마세요.** | 15초 |
| 2/3 | `LEFT` | **왼쪽 어깨를 카메라 쪽으로** 내밀고 왼쪽으로 이동한 자세를 **그대로 유지**합니다. | 10초 |
| 3/3 | `RIGHT` | **오른쪽 어깨를 카메라 쪽으로** 내밀고 오른쪽으로 이동한 자세를 **그대로 유지**합니다. | 10초 |

### 꼭 지켜야 할 것

- **1단계에서 팔을 반드시 흔드세요.** 팔만 움직였을 때 이동으로 오인식되는지가 이 측정의 핵심입니다. 가만히 서 있기만 하면 threshold가 너무 낮게 잡힙니다.
- **2·3단계는 왔다갔다 반복하지 말고 자세를 유지하세요.** 반복하면 중간의 어정쩡한 프레임이 섞여서 값이 낮게 나옵니다.
- 실제 게임에서 쓸 만큼의 크기로 돌리세요. 과장하면 나중에 그만큼 돌려야 인식됩니다.

### 조작키

| 키 | 동작 |
|---|---|
| `R` | 현재 단계를 처음부터 다시 (그 단계 데이터만 삭제) |
| `Q` | 중단 (리포트는 생성되지 않습니다) |

자세가 꼬였거나 중간에 방해받았으면 그냥 `R`을 누르세요.

3단계가 끝나면 창이 자동으로 닫히고 터미널에 리포트가 출력됩니다.

---

## 4. 결과물

```
data/calibration/report_20260907_183300.txt   ← 팀에 보낼 것
data/calibration/rot_log_20260907_183300.csv  ← 원본 데이터 (첨부)
```

`report_*.txt`를 열어서 **맨 아래 `[ 직접 채워서 보낼 것 ]` 항목을 채운 뒤**, 파일 내용을 통째로 복사해서 보내면 됩니다.

```
[ 직접 채워서 보낼 것 ]
  웹캠 모델        : Logitech C920
  카메라와의 거리  : 170 cm
  카메라 높이      : 가슴높이
  조명             : 보통
  동시 동작 테스트 : ATTACK+RIGHT (O) / BLOCK+LEFT (O) / JUMP+RIGHT (X)
  특이사항         : JUMP 중에 이동이 NEUTRAL로 튐
```

`data/`는 `.gitignore` 대상이므로 **커밋하지 말고 파일로 첨부**해서 보내세요.

---

## 5. 리포트 읽는 법

### `[ 센서 상태 ]`

- `visibility 값이 전부 0.0 입니다` 경고가 뜨면 → **여기서 멈추고 담당자에게 바로 알리세요.** 이 MediaPipe 버전은 visibility를 채우지 않아서, 현재 코드에서는 이동이 **항상 NEUTRAL**이 됩니다. `MOVEMENT_VISIBILITY_THRESHOLD`를 `0.0`으로 내려야 동작합니다.
- `어깨 미인식 프레임 비율`이 10%를 넘으면 → 조명을 밝게 하거나 카메라를 더 정면으로 옮기고 재측정.

### `[ 좌우 반전 판정 ]`

스크립트가 자동으로 판정합니다. `MOVEMENT_MIRROR = True`가 나왔다면 그 환경에서는 좌우가 뒤집혀 있다는 뜻이고, config에 반영해야 합니다.

참고로 코드의 부호 규칙은 이렇습니다.

```
rotation_score = (z_왼쪽어깨 − z_오른쪽어깨) / 어깨폭
MediaPipe z는 음수일수록 카메라에 가까움

오른쪽 어깨가 앞으로 → 양수 → RIGHT
왼쪽  어깨가 앞으로 → 음수 → LEFT
```

### `[ 분리도 ]`

| 결과 | 의미 | 조치 |
|---|---|---|
| 비율 2.0x 이상, `양호` | 중립 노이즈와 실제 이동이 확실히 구분됨 | 권장값 그대로 사용 |
| 비율 2.0x 미만, `주의` | 구분은 되지만 여유가 좁음 | 사용은 가능. 오작동 잦으면 재측정 |
| `분리 불가` 경고 | 팔만 흔들어도 이동으로 잡히는 상태 | **threshold로 해결 안 됨.** 아래 참고 |

`분리 불가`가 나오면 이 중 하나입니다.

1. 2·3단계에서 몸을 충분히 돌리지 않음 → **재측정**
2. 카메라가 너무 멀거나 측면 → 위치 조정 후 재측정
3. 그래도 안 되면 → 이동 동작 규칙 자체를 더 크게 바꿔야 함 (팀 논의 필요)

이 경우 리포트는 권장값 대신 현재 설정값을 그대로 출력합니다. 복사해서 쓰지 마세요.

---

## 6. config.py 반영 (담당자용)

리포트의 `[ 권장 config.py 값 ]` 블록을 `src/config.py`의 해당 줄에 그대로 옮깁니다.

```python
# Movement inference settings
MOVEMENT_VISIBILITY_THRESHOLD = 0.5
MOVEMENT_ENTER_THRESHOLD = 0.24      # ← 리포트 값
MOVEMENT_EXIT_THRESHOLD = 0.15       # ← 리포트 값
MOVEMENT_STABLE_FRAME_COUNT = 4      # ← 리포트 값
MOVEMENT_MIRROR = False              # ← 리포트 값
MOVEMENT_MAX_STRENGTH_DELTA = 0.52   # ← 리포트 값
```

**여러 명의 값이 모였을 때**

- `MOVEMENT_ENTER_THRESHOLD` / `MOVEMENT_EXIT_THRESHOLD` → **가장 큰 값** 채택 (오인식 방지 우선)
- `MOVEMENT_MAX_STRENGTH_DELTA` → **중앙값**
- `MOVEMENT_STABLE_FRAME_COUNT` → **가장 큰 값**
- `MOVEMENT_MIRROR` → 사람마다 다르게 나오면 **카메라 설치 방향이 서로 다른 것**입니다. 값을 고르지 말고 설치 규칙을 먼저 통일하세요.

각 값의 산출식은 다음과 같습니다.

```
noise  = NEUTRAL 단계 |rotation_score|의 99 백분위수
motion = LEFT+RIGHT 단계 |rotation_score|의 10 백분위수

ENTER        = noise + 0.5  × (motion − noise)
EXIT         = noise + 0.15 × (motion − noise)
MAX_STRENGTH = LEFT+RIGHT |rotation_score|의 95 백분위수
STABLE_FRAME = round(측정 FPS × 0.2)   # 약 200ms 지연 목표
```

반영 후 `python src/run_inference.py`로 체감 확인하고, 여전히 깜빡이면 `MOVEMENT_STABLE_FRAME_COUNT`를 1씩 올리세요. 반응이 굼뜨면 1씩 내리면 됩니다.

---

## 7. 동시 동작 테스트 (수동, 별도)

캘리브레이션과 별개로 이건 직접 눈으로 확인해야 합니다. config 반영 후 실행하세요.

```bat
python src\run_inference.py
```

| 테스트 | 확인할 것 |
|---|---|
| 오른쪽으로 이동하면서 오른손 지르기 | `FINAL: ATTACK` + `FINAL MOVE: RIGHT` 동시 표시 |
| 왼쪽으로 이동하면서 양손 방어 | `FINAL: BLOCK` + `FINAL MOVE: LEFT` |
| 오른쪽으로 이동하면서 왼손 위로 | `FINAL: JUMP` + `FINAL MOVE: RIGHT` |
| 한쪽 어깨를 손으로 가리기 | `ROT: +0.000`, `FINAL MOVE: NEUTRAL`로 복귀 |
| 프레임 밖으로 나갔다가 복귀 | 이동 상태가 남지 않고 즉시 정상화 |

> **알려진 이슈**: 몸을 크게 돌리면 액션 인식이 흔들릴 수 있습니다.
> `normalize_landmarks()`가 어깨의 x 간격만으로 정규화하는데, 회전하면 이 값이 0에 가까워져
> 입력 feature가 커지기 때문입니다. movement 기능의 문제가 아니라 기존 정규화 코드 이슈이니,
> 오인식이 보이면 "동시 동작 실패"가 아니라 **이 이슈로 보고**해 주세요.

---

## 8. 트러블슈팅

| 증상 | 원인 / 해결 |
|---|---|
| `Cannot open webcam at camera index 0` | 다른 앱(줌, 팀즈)이 카메라 점유 중. 종료 후 재시도. 웹캠이 여러 개면 `config.py`의 `CAMERA_INDEX`를 1, 2로 바꿔보세요 |
| macOS에서 카메라 권한 오류 | 시스템 설정 > 개인정보 보호 및 보안 > 카메라에서 터미널/VS Code 허용 |
| `Missing Python dependency` | 가상환경을 활성화하지 않았습니다. 1번 단계부터 다시 |
| `Pose model not found` | 루트에 `pose_landmarker.task`가 없습니다. README의 다운로드 항목 참고 |
| 화면에 스켈레톤이 안 그려짐 | 상체가 화면에 다 안 들어왔거나 너무 어둡습니다 |
| `ROT` 값이 계속 `+0.000` | visibility 미지원 케이스. 5번 항목 참고 |
| 한글이 화면에 깨져 보임 | 정상입니다. OpenCV는 한글을 못 그려서 화면 안내는 영어이고, 한글 안내는 터미널에 나옵니다 |
| 샘플 수 경고 (`샘플이 N개뿐입니다`) | 그 단계에서 포즈가 거의 안 잡혔습니다. 조명·거리 조정 후 재측정 |

---

## 부록: 파일 목록

| 파일 | 역할 |
|---|---|
| `src/calibrate_movement.py` | 이 가이드에서 실행하는 측정 도구 (MLP 미사용) |
| `src/movement.py` | 실제 이동 판정 로직 (threshold·히스테리시스·안정화) |
| `src/config.py` | 측정한 값을 반영하는 곳 |
| `src/run_inference.py` | 실시간 인식 + Unity 전송 (동시 동작 테스트용) |
| `docs/unity_protocol.md` | UDP 페이로드 규격 |
