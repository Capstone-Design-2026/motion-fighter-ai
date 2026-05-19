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

# Current Action Classes

| Action | Description |
|---|---|
| IDLE | 기본 전투 자세 |
| NONE | 아무 동작도 아님 / 잘못된 자세 |
| ATTACK | 오른손 지르기 |
| BLOCK | 양손 눈 위로 올리기 |
| JUMP | 왼손 위로 올리기 |
| CROUCH | 왼손 오른쪽 아래로 내리기 |

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

# Development Flow

## 1. Collect Dataset

```bash
python src/collect_data.py
```

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

## 2. Merge Dataset

```bash
python src/merge_csv.py
```

## 3. Train Model

```bash
python src/train_mlp.py
```

Outputs:

```text
models/motion_model.keras
models/label_classes.npy
```

## 4. Run Real-time Inference

```bash
python src/run_inference.py
```

Features:

- Real-time prediction
- Confidence threshold
- Rule-based verification
- Multi-frame stabilization

## 5. Unity Communication

```bash
python src/send_unity.py
```

Protocol:

- UDP
- JSON message format

Example:

```json
{
  "action": "ATTACK",
  "confidence": 0.91
}
```

# AI Pipeline

```text
Webcam Input
↓
MediaPipe Pose
↓
Landmark Normalization
↓
MLP Prediction
↓
Rule-based Verification
↓
Frame Stabilization
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
| feature/unity-test | Unity communication testing |

# Planned Features

- MOVE_LEFT / MOVE_RIGHT
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
