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
- Skips files with a different column count
- Renames the last column to `label`
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
- Rule-based shoulder movement detection
- Separate action and movement stabilization
- Unity UDP transmission when action or movement changes
- Limits repeated `IDLE` / `NONE` sends
- Debug overlay for raw/final action, confidence, raw/final movement,
  rotation score, movement strength, and last Unity payload

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
└─ Shoulder Depth Difference
   ↓
   Rule-based Movement Detection
   ↓
   Hysteresis / Movement Stabilization
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
