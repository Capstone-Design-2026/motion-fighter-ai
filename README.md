# REDBOOL / Motion Fighter AI

웹캠으로 상체 동작을 인식해 Unity 무협 격투 게임을 조작합니다. 현재 개발판은 **v34.2 보정·처리 지연·공통 메뉴 보완 시험 버전**입니다. 게임을 실행하면 전투·수련·PvP에서 해당 게임이 관리하는 카메라가 자동으로 켜지고, 게임 안에서 미리보기와 보정을 조작합니다.

기존 스토리와 v26 기반 인식에 v32의 검증된 보완을 유지합니다. v33의 카메라 통합·대각선 펀치 개인 수련·화면 밖 손목 안내를 유지합니다. v34는 원본 영상 없이 처리한 관절 좌표와 엔진 처리 순서를 압축 기록합니다. 새 근거 없는 인식 기준 완화나 모델 변경은 하지 않았습니다. 모든 사람의 실시간 인식률이나 지연이 검증 완료됐다는 의미는 아닙니다.

v34.2는 낮은 프레임 환경에서 기본 보정 진행률이 반복 초기화되는 문제를 줄입니다. 보정에만 관측 간격 200ms를 허용하고, 12개 유효 표본과 자세·신선도 조건은 유지합니다. 보정 중에는 Lite만 실행합니다. 게이지는 유지 시간과 표본 수가 모두 충족돼야 100%가 됩니다.

전투에서 보조 모델 처리로 관측이 계속 늦거나 끊기는 PC는 자동으로 Lite 속도 우선 모드로 전환합니다. 기존 동작 판정과 입력 만료 조건은 유지하며, 여유 있는 PC는 기존 Full 보조 경로를 계속 씁니다. 속도 우선 모드에서도 Lite의 X자 인식은 동작하지만 Full이 보완하던 가려진 X자에는 불리할 수 있습니다. 게임·카메라 실측이 필요하며, 모든 PC의 성능 개선을 보장하지 않습니다.

Unity v34.2는 모든 화면에서 Esc로 공통 메뉴를 열고, 메뉴 복귀 시 기록·카메라·PvP 연결을 정리합니다. 메뉴를 열어도 전투는 계속 진행됩니다. 미리보기는 최대15fps이며 부하 시8fps로 낮추고, 고해상도3D 화면 처리량을 제한합니다.

## 체험자는 게임 패키지를 실행

1. 전달받은 운영체제별 v34.2 게임 패키지를 **폴더 전체로** 풀고, Mac은 `REDBOOL.app`, Windows는 `REDBOOL.exe`를 실행합니다. Python이나 Unity Editor를 따로 설치할 필요는 없습니다.
2. 스토리·결투 테스트로 들어가거나, 두 노트북에서 `PvP 대전`을 선택합니다. 한쪽은 `방 만들기`, 다른 쪽은 `방 찾기` 또는 호스트 IPv4 입력 후 `대전 참가`를 누릅니다. 양쪽에 같은 결투장이 표시됩니다.
3. 카메라 안내에서 `보정` 또는 `C`를 누르고 중앙의 편한 가드 자세를 유지합니다. 사람이 바뀌면 다시 보정합니다. 손목과 팔꿈치가 화면 안에 보여야 합니다.
4. 미리보기 `접기`는 화면만 접습니다. 인식은 계속됩니다. 문제가 있으면 `다시 연결`, 종료 후 메인 메뉴의 `진단 기록 저장`을 사용합니다.

Windows 실행 파일 옆의 지원 폴더를 분리하지 마세요. GitHub의 **REDBOOLCamera-Windows-x64**는 게임에 넣을 카메라 부품이며 완성 게임 패키지가 아닙니다. Mac 배포본은 현재 로컬 시험용 서명이며 공증된 일반 배포본은 아닙니다.

## 현재 동작과 개인 보정

| 실제 동작 | 게임 입력 |
|---|---|
| 왼손 펀치 | 잽 |
| 오른손 펀치 | 스트레이트 |
| 왼손 레이즈 | 하이킥 |
| 오른손 레이즈 | 로우킥 |
| 양손을 얼굴 앞에 올려 유지 | 블락; 자세를 풀면 해제 |
| 상체를 좌우로 기울임 / 중앙 복귀 | 이동 / 정지; 기울인 채 공격 가능 |
| 가슴 앞에서 양팔 X자 유지 후 풀기 | 충전된 파운장 |

- **Lite가 기본 관절 추정**, Full은 X자 확인을 돕는 보조 경로입니다. Full로 기본 모델을 자동 전환하지 않습니다. 보정 중이나 속도 우선 모드에서는 Full 보조 처리를 생략합니다.
- 튜토리얼의 개인 보정은 두 번의 독립적이고 일관된 동작·복귀 표본으로 제한된 범위만 조절합니다. 수련 통과 자체를 새 학습 데이터의 정답으로 간주하지 않습니다.
- 부족하거나 불안정한 표본에는 기존 기본값을 유지합니다. X자·관절 신뢰도·복귀 조건을 일괄 완화하지 않습니다. 사람 교체 후 `C`는 기존 개인 범위를 초기화합니다.
- 마지막 Windows 수련 재생에서 29개 이벤트의 분류·발생 프레임을 유지하면서 스트레이트의 유효 개인 측정 표본 1개를 복구했습니다. 그 기록에서는 임계값 변경이 필요하지 않아 인식 정확도가 올라갔다고 주장하지 않습니다.

## 저장과 두 노트북 통신

통합 패키지의 기본 **진단 모드**는 원본 영상 없이 처리한 관절 좌표·신뢰도·시각·인식/보정 처리 순서를 압축 저장합니다. Lite 및 사용된 Full 보조 추정값을 기록하며, 기록만을 위해 인식 프레임을 건너뛰거나 좌표를 반올림하지 않습니다. 압축/저장은 별도 작업 스레드에서 하고 카메라 실행당 압축 관절 기록은 최대 128MiB입니다. 저장 실패·대기열 초과·용량 제한은 명시하며 게임은 계속됩니다.

메인 메뉴에서 기록 마무리가 끝난 뒤 `진단 기록 저장`을 누르면 최근 3개 실행의 닫힌 관절 압축 파일과 진단 로그를 ZIP에 포함합니다. `landmarks.index.json`과 `export_report.json`으로 누락/완전성을 확인합니다. 압축 파일을 중간에서 자르지 않습니다. 좌표는 모델 추정이므로 사람의 실제 의도에 대한 정답 라벨은 아닙니다. 개발자용 `review`·`logs`·`lossless` 모드도 별도로 유지합니다.

각 노트북이 자신의 카메라를 처리합니다. 미리보기 영상은 자기 컴퓨터 안에서만 전달되며 상대에게 보내지 않습니다. PvP는 기존 호스트 중심 LAN UDP 연결을 사용하고 외부 서버가 필요하지 않습니다. P1 입력은 `5065`, P2 입력은 `5066`, 화면 동기화·방 찾기는 `5067`입니다. 같은 공유기여도 무선 사용자 격리나 방화벽이 있으면 연결되지 않을 수 있습니다. v34 화면 통신 규약은 **30**입니다. 양쪽 게임을 함께 업데이트하세요.

## 개발 브랜치와 문서

| 역할 | 브랜치 / 안내 |
|---|---|
| Python 인식·카메라 통합 | `codex/tutorial-personal-pvp-20261112` · [PR #12](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/12) |
| Unity 스토리·두 화면 PvP·통합 패키징 | `codex/pvp-dual-screen-20261112` · [PR #14](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/14) |
| Unity 팀원 콘텐츠 통합 기준 | `feature/unity`; 위 개발 브랜치에서 기능을 비교한 뒤 반영 |
| 공통 기본 브랜치 | `main`; 이 개발판을 자동 병합하지 않음 |

- [v34 관절 기록·재생 안내](docs/v34-landmark-diagnostics.txt)
- [카메라 구성·API·빌드 방법 (v33 기반)](docs/v33-integrated-camera.txt)
- [Unity v34 패키지 조립·검증 방법](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/pvp-dual-screen-20261112/Tools/Developer/V34_PACKAGING.txt)
- [v32 다인원 기록 검토와 남은 한계](docs/field-v32.txt)
- [v31 통신 검사와 실행 안내](docs/network-v31.txt)
- [Windows 네이티브 카메라 빌드 workflow](.github/workflows/camera-backend-windows.yml)

기존 작업 파일을 보존한 상태에서 해당 개발 브랜치를 업데이트하세요. 충돌이 생기면 강제 초기화하지 말고 변경 내용을 확인합니다.

```bash
git fetch origin
git switch codex/tutorial-personal-pvp-20261112
git pull --ff-only
```

## 개발자가 게임과 카메라를 따로 실행할 때

기존 Python 3.11 개발 환경과 Unity Editor 방식은 유지합니다. 새 Windows 카메라 개발 환경은 `requirements-pilot-windows.txt`를 사용하며 기존 Mac·학습 환경을 덮어쓰지 않습니다. 저장소 루트에서 환경을 활성화한 뒤:

```bash
python scripts/setup_demo_models.py
python scripts/run_personal_camera.py --check
```

위 검사는 카메라를 열지 않습니다. Unity 개발 실행을 준비한 뒤 목적에 맞는 **한 줄만** 실행합니다.

```bash
# 스토리 / 로컬 결투
python scripts/run_personal_camera.py --player story
# PvP 호스트 노트북
python scripts/run_personal_camera.py --player 1 --host 127.0.0.1
# PvP 참가 노트북: HOST_IPV4를 호스트의 실제 IPv4로 바꿈
python scripts/run_personal_camera.py --player 2 --host HOST_IPV4
```

이 수동 실행기는 기본적으로 검토용 압축 영상을 저장합니다. 통합 패키지의 기본 진단 모드와 다릅니다. 통합 게임을 사용할 때는 수동 카메라를 중복 실행하지 마세요. 개발 목적으로 패키지 게임과 수동 카메라를 연결하려면 게임에 `--redbool-external-camera`를 지정합니다.

카메라 없이 LAN 왕복 응답을 확인하려면 호스트 게임을 켜고:

```bash
python scripts/check_pvp_network.py --host HOST_IPV4 --seconds 20
```

이는 네트워크와 호스트 게임 갱신 지연 검사이며 인식률이나 카메라→화면 전체 지연 검사는 아닙니다.

## 패키징과 확인된 검증 범위

카메라 실행 파일은 각 운영체제에서 네이티브로 빌드합니다. 별도 빌드 환경에서 `requirements-backend-build.txt`와 [v33 빌드 안내](docs/v33-integrated-camera.txt)를 사용하세요. Unity 게임과 카메라 폴더를 합치는 방법은 위 Unity 개발 문서에 있습니다.

아래 수치는 v33 당시의 기존 검증 근거입니다. v34 관절 기록 검사는 별도 릴리스 결과와 CI에서 확인합니다.

- Python 회귀 검사 249개 및 추가 카메라 통합 검사까지 합쳐 **262개 고유 검사 통과**.
- [Windows 네이티브 빌드 실행](https://github.com/Capstone-Design-2026/motion-fighter-ai/actions/runs/43259318061): 21개 검사, x64 실행 파일 생성, 동봉된 Lite VIDEO·Full VIDEO/IMAGE 실제 추론, 로컬 미리보기·보정 요청·종료 검사 통과.
- Mac 네이티브 카메라 패키지에서도 위 모델 추론·제어 검사를 통과했습니다. 검사는 합성 이미지와 저장된 기록을 사용하며 실제 카메라를 켜지 않습니다.
- 실제 Mac/Windows 카메라 권한·드라이버·발열·동시 플레이는 첫 통합 패키지 현장 확인 대상입니다. 이전 Windows의 약 20fps 카메라 공급이나 게임 프레임 하락이 패키징만으로 해결됐다고 보지 않습니다.

원본 촬영 자료, 학습 데이터, 모델 파일, 가상환경은 Git에 올리지 않습니다.

---

<details>
<summary>초기 MLP 연구·데이터 수집 문서 (이전 구성 보존)</summary>

아래 문서는 이전 MLP 분류·어깨 회전 이동 실험을 보존한 것입니다. `IDLE / ATTACK / JUMP / CROUCH`, UDP `5055`, CSV 학습과 당시 테스트 수는 현재 실행 방법이 아닙니다. 현재 데모는 위의 `redbool_demo` 경로를 사용합니다.

# Project Overview

본 프로젝트는 컴퓨터비전 기반 무협 액션 게임을 위한
실시간 모션 인식 AI 모듈입니다.

플레이어는 웹캠 앞에서 특정 동작을 수행하며,
AI는 이를 인식하여 Unity 게임 내 캐릭터를 조작합니다.

현재 목표:

- 상체 기반 실시간 동작 인식
- 저지연(Unity 실시간 연동)
- 간단하고 안정적인 구조
- 데이터 추가를 통한 지속적 성능 향상

---

# Tech Stack

## AI / Vision

- Python 3.11
- OpenCV
- MediaPipe Pose
- TensorFlow / Keras
- Scikit-learn
- NumPy / Pandas

## Game

- Unity

## Collaboration

- GitHub
- VS Code
- ChatGPT / Codex

---

# Current Action / Movement Channels

MLP action class는 기존 6개만 사용합니다. 좌우 이동은 별도 movement channel로
계산하므로 공격/방어/점프/숙이기와 동시에 전달될 수 있습니다.

| Action | Description |
|---|---|
| IDLE | 기본 전투 자세 |
| NONE | 아무 동작도 아님 / 잘못된 자세 |
| ATTACK | 오른손 지르기 |
| BLOCK | 양손 눈 위로 올리기 |
| JUMP | 왼손 위로 올리기 |
| CROUCH | 왼손 오른쪽 아래로 내리기 |

| Movement | Description |
|---|---|
| LEFT | 왼쪽 이동 |
| RIGHT | 오른쪽 이동 |
| NEUTRAL | 이동 없음 |

## Movement Detection

Movement는 양쪽 shoulder depth(z) 차이를 화면상의 어깨 간 거리(x, y)로
정규화한 rotation score로 `LEFT / NEUTRAL / RIGHT`를 판정합니다.
`MOVEMENT_MIRROR` 설정을 적용한 raw score에서 neutral baseline을 뺀 뒤,
기존 hysteresis와 연속 프레임 stabilization을 적용합니다.
MLP action과 movement는 독립 channel이며, movement strength도 보정된 score로 계산합니다.

## Neutral Baseline Calibration

프로그램을 실행하면 `CALIBRATING` 상태로 시작합니다. 사용자는 양쪽 어깨가
보이도록 정면 기본 자세를 잡고 약 3초간 움직이지 않은 채 `READY`를 기다립니다.

- 양쪽 shoulder visibility가 각각 `MOVEMENT_VISIBILITY_THRESHOLD`(현재 `0.5`)
  이상이고 좌표와 score가 유효한 frame만 수집합니다. `NaN` / `inf`는 제외합니다.
- 첫 유효 sample부터 최소 수집 시간이 지나고 최소 sample 수도 충족해야 완료됩니다.
  유효 sample이 부족하면 3초가 지나도 `CALIBRATING`을 유지하며 수집을 계속합니다.
- 유효 score의 `median`으로 neutral baseline을 계산하고 `READY`로 전환합니다.
- Calibration 중과 완료되는 바로 그 frame은 movement가 항상 `NEUTRAL`이고
  `movement_strength=0.0`입니다. Action inference는 계속 동작하며,
  Unity에 전송되는 movement도 `NEUTRAL/0.0`입니다.
- 다음 유효 frame부터 아래 corrected score로 movement를 판정합니다.

```text
corrected_rotation_score = raw_rotation_score - neutral_baseline
```

Overlay에서 상태, 유효 sample 수, raw score, baseline, corrected score,
calibration MAD를 확인할 수 있습니다. MAD(median absolute deviation)는
디버깅·재측정용이며 calibration 성공/실패 기준으로 사용하지 않습니다.

Pose 손실이나 낮은 shoulder visibility에서는 movement를 `NEUTRAL`로 만들지만,
이미 수집한 sample과 완료된 baseline은 유지합니다. Baseline은 파일에 저장하지
않으며 프로그램을 다시 실행하면 새 calibration을 진행합니다.

현재 `src/config.py` 기본값:

```python
MOVEMENT_CALIBRATION_SECONDS = 3.0
MOVEMENT_CALIBRATION_MIN_SAMPLES = 30
MOVEMENT_ENTER_THRESHOLD = 0.30
MOVEMENT_EXIT_THRESHOLD = 0.15
```

Calibration 설정과 movement threshold는 초기값이며 실제 다인원 재측정 결과에
따라 조정할 수 있습니다. 현재 ENTER / EXIT threshold는 기존 값을 유지합니다.

---

# Project Structure

```text
motion-fighter-ai/
├── README.md
├── requirements.txt
├── .gitignore
│
├── src/
│   ├── config.py
│   ├── collect_data.py
│   ├── merge_csv.py
│   ├── movement.py
│   ├── train_mlp.py
│   ├── run_inference.py
│   └── send_unity.py
│
├── data/
│   ├── raw/
│   ├── merged/
│   └── sample/
│
├── models/
│
├── docs/
│   ├── dataset_rule.md
│   └── unity_protocol.md
│
└── tests/
```

# Environment Setup

## 1. Clone Repository

```bash
git clone https://github.com/Capstone-Design-2026/motion-fighter-ai.git
cd motion-fighter-ai
```

## 2. Create Virtual Environment

```bash
python3.11 -m venv .venv
source .venv/bin/activate
```

## 3. Install Libraries

```bash
pip install -r requirements.txt
```

# MediaPipe Model Download

Download:

https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task

Rename file:

```text
pose_landmarker.task
```

Place file in project root:

```text
motion-fighter-ai/
├── pose_landmarker.task
```

# Quick Start

처음 실행하는 팀원은 아래 순서대로 진행하면 됩니다.

```bash
# 1. 가상환경 활성화
source .venv/bin/activate

# 2. 여러 CSV가 있으면 병합
python src/merge_csv.py

# 3. MLP 모델 학습
python src/train_mlp.py

# 4. 실시간 인식 + Unity UDP 전송
python src/run_inference.py
```

데이터를 새로 모아야 할 때만 아래 명령을 먼저 실행합니다.

```bash
python src/collect_data.py
```

Unity UDP 수신만 따로 테스트하고 싶을 때는 아래 명령을 사용합니다.

```bash
python src/send_unity.py
```

# Development Flow

## 1. Collect Dataset

```bash
python src/collect_data.py
```

Run the command after activating `.venv`. In VS Code, select
`.venv/bin/python` as the Python interpreter if imports such as `cv2` or
`mediapipe` are unresolved.

On macOS, allow camera access for the app that starts Python, usually VS Code
or Terminal, in System Settings > Privacy & Security > Camera.

Collect:

- IDLE
- NONE
- ATTACK
- BLOCK
- JUMP
- CROUCH

Recommended:

- Different camera angles
- Different lighting
- Different speeds
- Multiple people

Output:

```text
data/raw/motion_data.csv
```

팀원이 각자 수집한 파일은 `data/raw/`에 아래처럼 넣으면 됩니다.

```text
data/raw/motion_data_name.csv
```

## 2. Merge Dataset

```bash
python src/merge_csv.py
```

What it does:

- Reads every `.csv` file in `data/raw/`
- Skips empty files
- Fails fast if a file does not match the canonical `x0,y0,z0,v0...x32,y32,z32,v32,label` schema
- Saves one merged CSV

Output:

```text
data/merged/motion_data_merged.csv
```

## 3. Train Model

```bash
python src/train_mlp.py
```

Dataset priority:

1. `data/merged/motion_data_merged.csv`
2. `data/raw/motion_data.csv`

Outputs:

```text
models/motion_model.keras
models/label_classes.npy
```

Training:

- MLP classifier
- 132 input features
- `train_test_split(test_size=0.2, random_state=42, stratify=y)`
- Prints test accuracy, classification report, and confusion matrix

## 4. Run Real-time Inference

```bash
python src/run_inference.py
```

Features:

- Real-time MLP action prediction
- Action confidence threshold
- Rule-based shoulder movement detection with neutral baseline calibration
- Separate action and movement stabilization
- Unity UDP transmission when action or movement changes
- Limits repeated `IDLE` / `NONE` sends
- Debug overlay for raw/final action, confidence, raw/final movement,
  calibration state, raw/corrected rotation score, baseline, calibration MAD,
  movement strength, and last Unity payload

### Movement 확인 절차

가상환경을 활성화하고 모델 파일을 준비한 뒤 위의 `python src/run_inference.py`를 실행합니다.

1. `CALIBRATING` 동안 양쪽 어깨가 보이도록 정면 기본 자세를 유지합니다.
2. 유효 sample 수가 충분해지고 `READY`가 표시되는지 확인합니다.
3. `NEUTRAL → LEFT → NEUTRAL → RIGHT → NEUTRAL` 순서로 자세를 바꾸며
   각 자세를 잠시 유지하고 overlay의 `FINAL MOVE`를 확인합니다.
4. Raw / baseline / corrected score와 MAD를 기록하고, Unity 연결 시에도
   동일한 이동 상태가 전달되는지 확인합니다.

### 자동 테스트

```bash
python -m unittest discover -s tests
```

이 초기 MLP 단계의 기록에서는 총 **53개 테스트가 통과**했습니다. Median/MAD, 유효 sample 필터링,
calibration 시간·sample 수 조건, 완료 frame의 NEUTRAL 유지, offset 보정,
baseline 보존, 기존 hysteresis/stabilization 및 Unity payload를 검증합니다.
실제 다인원 카메라·Unity 재측정은 별도로 진행할 예정입니다.

## 5. Unity Communication

```bash
python src/send_unity.py
```

This script repeatedly sends test action + movement payloads to Unity once per
second:

```text
IDLE/NEUTRAL -> ATTACK/RIGHT -> BLOCK/LEFT -> JUMP/RIGHT -> CROUCH/LEFT -> NONE/NEUTRAL
```

Protocol:

- UDP
- Host: `127.0.0.1`
- Port: `5055`
- JSON message format

Example:

```json
{
  "action": "ATTACK",
  "movement": "RIGHT",
  "confidence": 0.91,
  "movement_strength": 0.61,
  "timestamp": 1710000000.123
}
```

See details:

```text
docs/unity_protocol.md
```

# AI Pipeline

```text
Webcam Input
↓
MediaPipe Pose
├─ Landmark Normalization
│  ↓
│  MLP Action Prediction
│  ↓
│  Action Confidence / Stabilization
│
└─ Shoulder Depth Difference / Raw Rotation Score
   ↓
   Neutral Baseline Calibration (median)
   ↓
   Corrected Rotation Score (raw - baseline)
   ↓
   Hysteresis / Movement Stabilization
   ↓
   LEFT / NEUTRAL / RIGHT
↓
Unity UDP Transmission
```

# Important Rules

## Dataset Rules

- Always include NONE data
- Avoid excessive motion blur
- Do not collect only frontal angles
- Maintain action consistency
- Record multiple body types if possible

## Git Rules

Do NOT upload:

- raw datasets
- trained models
- videos
- virtual environments

Use .gitignore.

# Previous Branch Strategy

초기 연구 단계에서는 AI·어깨 이동·Unity 수신 실험을 별도 feature 브랜치에서 진행했습니다. 현재 개발 브랜치는 위 v33 표를 기준으로 사용하세요.

# Planned Features

- Combo actions
- Skill commands
- Continuous action buffering
- Improved false-positive filtering
- Real-time performance optimization

# Team Roles

## AI Vision

- Dataset collection
- Model training
- Real-time inference
- Unity communication

## Unity

- Character controller
- Animator integration
- Combat system
- UI / Stage implementation

# Previous Development Goal

## 당시 상태

- Shoulder movement 구현 완료
- Neutral baseline calibration 구현 완료
- 실제 다인원 재측정 진행 예정
- [PR #2](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/2)에서 검증 중

## Short-Term Goal

- Stable real-time action recognition
- Unity character control
- Low latency communication

## Long-Term Goal

- Combo system
- Skill system
- Online multiplayer possibility
- Expanded action set

# Contributors

Capstone Design 2026 RedBool

</details>
