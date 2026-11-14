# REDBOOL / Motion Fighter AI

웹캠으로 상체 동작을 인식해 Unity 무협 격투 게임을 조작하는 프로젝트입니다. 최신 개발판은 **v34 게임 콘텐츠·관절 진단 기록 통합 시험 버전**입니다.

**현재 `main`의 코드는 초기 MLP 실험 스냅샷입니다.** 최신 게임·인식 코드는 아래 개발 브랜치와 PR에 있습니다. 이 README 갱신은 안내 문서만 바꾼 것이며, 게임 코드나 개발 PR을 `main`에 병합한 것은 아닙니다.

## 최신 개발판 찾기

| 구성 | 개발 브랜치 | 안내 |
|---|---|---|
| Python 인식·통합 카메라 | `codex/tutorial-personal-pvp-20261112` | [현재 README](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/tutorial-personal-pvp-20261112/README.md) · [PR #12](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/12) |
| Unity 스토리·두 화면 PvP·패키징 | `codex/pvp-dual-screen-20261112` | [현재 README](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/pvp-dual-screen-20261112/README.md) · [PR #14](https://github.com/Capstone-Design-2026/motion-fighter-ai/pull/14) |

- [카메라 구성·API·네이티브 빌드 방법](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/tutorial-personal-pvp-20261112/docs/v33-integrated-camera.txt)
- [v34 관절 기록·진단·재생 방법](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/tutorial-personal-pvp-20261112/docs/v34-landmark-diagnostics.txt)
- [Unity 통합 패키지 조립·검증 방법](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/pvp-dual-screen-20261112/Tools/Developer/V34_PACKAGING.txt)
- [Windows 카메라 빌드 workflow](https://github.com/Capstone-Design-2026/motion-fighter-ai/blob/codex/tutorial-personal-pvp-20261112/.github/workflows/camera-backend-windows.yml) · [통과한 실행 기록](https://github.com/Capstone-Design-2026/motion-fighter-ai/actions/runs/43585315988)

## 게임 체험과 2인 대전

1. 전달받은 운영체제별 게임 패키지를 폴더 전체로 풀고 Mac은 `REDBOOL.app`, Windows는 `REDBOOL.exe`를 실행합니다. 지원 폴더를 분리하지 않습니다.
2. 스토리·결투 또는 `PvP 대전`으로 들어갑니다. 두 노트북에서는 한쪽이 `방 만들기`, 다른 쪽이 `방 찾기` 또는 호스트 IPv4 입력 후 `대전 참가`를 선택합니다.
3. 게임 안 카메라 안내에서 `보정` 또는 `C`를 누르고 편한 중앙 가드 자세를 유지합니다. 사람이 바뀌면 다시 보정합니다.

PvP 양쪽은 **v34**를 사용하세요. 최신 캐릭터의 펀치 외형을 맞추는 화면 통신 규약은 **30**이며 이전 버전과 섞어 연결하지 않습니다.

각 노트북에서 자신의 카메라를 처리하고 같은 결투장을 봅니다. 기존 호스트 중심 LAN 연결을 사용하므로 외부 서버가 필요하지 않습니다. 미리보기 영상은 상대에게 보내지 않습니다. 두 노트북의 로컬 통신과 방화벽 허용은 필요합니다.

최종 사용자 패키지는 게임에서 카메라를 함께 실행합니다. 개발자의 Unity Editor·수동 Python 실행 방식도 유지합니다. GitHub 산출물 **REDBOOLCamera-Windows-x64**는 게임에 넣을 카메라 부품이며, 완성 게임 전체가 아닙니다.

## 현재 인식·기록·검증 범위

- v26 기반 인식과 검증된 보완을 유지하며, Lite가 기본 관절 추정이고 Full은 X자 인식 보조입니다.
- 튜토리얼 개인 보정은 독립적이고 일관된 동작·복귀 표본으로 제한된 범위만 조절합니다. 두 번의 수련으로 모든 사람의 동작을 새로 학습했다고 보지 않습니다.
- 통합 패키지는 원본 영상 없이 처리한 관절 좌표와 처리 순서·보정·공격·성능 기록을 압축 저장합니다. 카메라 실행당 관절 기록은 최대 128MiB이며 빠진 구간은 명시합니다. 기록으로 인식 과정을 재생할 수 있지만, 영상이나 의도 라벨 없이 모든 실제 동작의 정답을 확정할 수는 없습니다.
- Windows 네이티브 빌드에서 동봉 모델 추론, 로컬 미리보기·보정 명령·정상 종료 검사를 통과했습니다. 실제 카메라를 사용하지 않은 검사이며 현장 인식률·Wi-Fi·게임 지연의 검증을 대신하지 않습니다.
- v34의 실제 Mac/Windows 카메라 권한·드라이버·발열·동시 플레이와 새 적 필살기 첫 실행 성능 확인은 남아 있습니다. 일반 배포용 서명·공증 완료 버전으로 안내하지 않습니다.

원본 촬영 자료, 학습 데이터, 모델 파일, 가상환경은 Git에 올리지 않습니다. 기존 연구·데이터 수집 문서는 아래에 보존했습니다.

---

<details>
<summary>초기 MLP 연구·데이터 수집 문서 — main 코드의 이전 구성</summary>

아래의 분류명·MLP 학습 명령·브랜치 전략·계획은 초기 실험 당시의 기록입니다. 최신 v34 실행은 위 개발 브랜치 안내를 사용하세요.

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

</details>
