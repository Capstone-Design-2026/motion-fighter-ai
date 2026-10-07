# Motion Fighter AI

OpenCV + MediaPipe Pose 기반 상체 동작 인식 시스템입니다.
웹캠으로 플레이어의 상체 움직임을 인식하고,
Unity 격투 게임에 실시간 입력을 전송하는 것을 목표로 합니다.

---

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

현재 총 **53개 테스트가 통과**했습니다. Median/MAD, 유효 sample 필터링,
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

# Branch Strategy

| Branch | Usage |
|---|---|
| main | Stable version |
| feature/ai-vision | AI vision development |
| feature/shoulder-movement | Shoulder-based movement channel |
| feature/unity-test | Unity communication testing |

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

# Current Development Goal

## Current Status

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
