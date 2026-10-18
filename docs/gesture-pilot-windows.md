# REDBOOL Gesture Pilot — Windows 10/11 참가자 안내

대상은 **Windows x64 + Python 3.11 64-bit**, 참가자 P02/P03/P04/P05다.
Windows ARM64/32-bit는 이번 설치 조합의 검증 대상이 아니다.
관리자가 배포한 같은 `feature/vision-v2` revision으로 진행한다.
**이 안내의 Windows 호환 변경은 배포(commit/push) 후 clone에서 사용할 수 있다.**
관리자에게 호환 버전 배포 완료와 commit SHA를 확인한다.

카메라 앞에 앉아 상반신·양쪽 어깨·손이 보이도록 한다. 실명, 학번 등을 subject나
파일명에 넣지 않는다. 담당자가 지정한 P02/P03/P04/P05 중 **자기 번호 하나만** 쓴다.
모델 학습이나 게임 입력은 실행하지 않는다.

## 1. 설치와 GitHub clone

[Git for Windows](https://git-scm.com/downloads/win)와
[Python Windows 다운로드](https://www.python.org/downloads/windows/)에서 Python **3.11 64-bit**를
설치한다. Python 설치 시 launcher(`py`)도 설치한다. 설치 후 PowerShell을 새로 연다.
Python 3.11 설치 프로그램이 필요한 경우 공식 3.11.9 Windows installer를 사용할 수 있다.

아래 블록을 PowerShell에 실행한다. 명령 앞에 `PS>` 같은 표시를 붙이지 않는다.
저장소가 비공개이면 GitHub 로그인 또는 팀 저장소 접근 권한이 필요하다.

```powershell
git --version
py -3.11 --version
py -3.11 -c "import struct, platform; print(platform.machine(), struct.calcsize('P') * 8)"
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\REDBOOL" | Out-Null
Set-Location "$env:USERPROFILE\REDBOOL"
git clone --branch feature/vision-v2 --single-branch https://github.com/Capstone-Design-2026/motion-fighter-ai.git
Set-Location .\motion-fighter-ai
git branch --show-current
git rev-parse HEAD
git status --short
Test-Path .\requirements-pilot-windows.txt
```

버전은 Python 3.11.x, architecture는 AMD64/64, branch는 `feature/vision-v2`여야 한다.
마지막 명령은 `True`, status 출력은 없어야 한다. SHA를 담당자의 배포 SHA와 비교한다.
이미 clone했다면 새로 clone하지 말고 기존 폴더에서 branch/status를 먼저 확인한다.
변경이 없는 `feature/vision-v2`에서만 `git pull --ff-only origin feature/vision-v2`로 갱신한다.

## 2. 가상환경과 dependency 설치

이후 명령은 모두 저장소 루트에서 실행한다. **새 가상환경**을 사용한다.
기존 `.venv`가 있다면 담당자에게 확인하고, macOS 환경이나 학습용 환경에 덮어 설치하지 않는다.

```powershell
py -3.11 -m venv .venv
& .\.venv\Scripts\python.exe -m pip install --upgrade pip
& .\.venv\Scripts\python.exe -m pip install --only-binary=:all: -r .\requirements-pilot-windows.txt
& .\.venv\Scripts\python.exe -m pip check
& .\.venv\Scripts\python.exe -c "import cv2, mediapipe, numpy; print('OpenCV', cv2.__version__); print('MediaPipe', mediapipe.__version__); print('NumPy', numpy.__version__)"
```

가상환경 Python을 직접 호출하므로 Activate.ps1 실행이나 PowerShell execution policy 변경은
필요 없다. `pip check`는 `No broken requirements found.`여야 한다.
수집 전용 파일은 기존 [requirements.txt](../requirements.txt)와 별개다. TensorFlow/MLP를 설치하지
않으며 GUI가 있는 `opencv-contrib-python` **한 종류만** 사용한다. `opencv-python`, headless
패키지를 추가 설치하지 않는다. 직접 의존성 버전은 macOS 기준 환경과 맞췄고, 전이 의존성은
pip가 해결하므로 전체 lockfile은 아니다. Windows wheel dependency dry-run은 통과했지만
실제 설치/import/카메라 동작은 각 노트북에서 확인해야 한다.

DLL load 오류가 나면 공식 Microsoft Visual C++ Redistributable x64와, Windows N/KN의 경우
Media Feature Pack 설치 여부를 담당자와 확인한다. 오류 메시지를 그대로 전달하고 임의로
패키지 버전이나 모델을 바꾸지 않는다.

## 3. Pose Lite 모델 준비

`pose_landmarker.task`는 Git-ignored이므로 clone에 포함되지 않는다. **우선 담당자로부터 P01과
동일한 모델 파일을 받아** 저장소 루트의 `pose_landmarker.task`로 저장한다. Git에 올리지 않는다.

공식 대안은 Google MediaPipe **Pose Landmarker Lite float16 version 1** 다운로드다.
기존 파일은 덮어쓰지 않으며, 내려받은 모델도 아래 SHA256과 반드시 비교한다.

```powershell
if (-not (Test-Path .\pose_landmarker.task)) {
    Invoke-WebRequest -Uri "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task" -OutFile .\pose_landmarker.task
}
$expectedModelHash = "59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a"
$actualModelHash = (Get-FileHash -LiteralPath .\pose_landmarker.task -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualModelHash -ne $expectedModelHash) { throw "P01 model SHA256 mismatch: stop and contact the coordinator." }
Get-Item .\pose_landmarker.task | Select-Object Name, Length
Get-FileHash -LiteralPath .\pose_landmarker.task -Algorithm SHA256
```

위 해시는 P01 기준 저장소의 로컬 자산 SHA256이다. 다르면 수집하지 말고 담당자에게 문의한다.
Full/Heavy 모델로 대체하지 않는다. Python은 실행 위치가 아니라 소스 파일의 저장소 루트에서
모델을 찾는다. 한글·공백 경로는 따옴표로 감싼다. 매 session manifest에도 SHA256이 기록된다.
공식 출처: [Pose Landmarker](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker),
[공식 Lite 모델 URL 예제](https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/tasks/web/vision/README.md).

## 4. webcam 권한 확인

- Windows 11: 설정 → 개인정보 및 보안 → 카메라.
- Windows 10: 설정 → 개인정보 → 카메라.
- 카메라 액세스, 앱 액세스, **데스크톱 앱의 카메라 액세스**를 허용한다.
- 노트북 물리 셔터/Fn 카메라 차단을 해제하고 Teams/Zoom/Windows 카메라 앱을 종료한다.
- 기본 카메라는 index 0이다. 외장 카메라를 선택할 때만 `--camera 1` 등을 담당자와 확인한다.

Windows auto는 DSHOW → MSMF → OpenCV default 순서로 **open 성공** 여부를 확인한다.
열리지만 검은 화면/연속 read 실패가 발생하는 장치는 자동으로 중간 전환하지 않는다.
종료하고 `--camera-backend msmf` 또는 `--camera-backend dshow`로 새 smoke session을 실행한다.
명시적으로 지정한 backend 실패 시 다른 backend로 조용히 전환하지 않는다.
macOS auto는 기존 OpenCV 기본 선택(보통 AVFOUNDATION)을 유지한다.

## 5. 1회 smoke test (9 gestures × 1)

아래 P02를 **본인에게 배정된 P02/P03/P04/P05**로 바꾼다. PowerShell을 다시 열면 `$subject`를
다시 지정해야 한다. 640×480, auto FPS, skeleton OFF, 무손실 video ON을 유지한다.

```powershell
$subject = "P02"
& .\.venv\Scripts\python.exe .\src\collect_gesture_pilot.py --subject $subject --take SMOKE01 --camera 0 --camera-backend auto --width 640 --height 480 --codec auto --repetitions 1 --countdown 3 --duration 3
```

1. WARMUP이 끝나 READY가 될 때까지 기다린다. **OpenCV preview 창을 클릭**하여 포커스를 준다.
2. 영문 입력 상태에서 SPACE를 눌러 countdown 후 GO에 동작한다. 다음 READY에서 다시 SPACE.
3. LEFT/RIGHT는 화면 방향이 아니라 **본인 몸의 좌우**다. preview는 기본 mirror다.
4. SPACE 시작, R 현재/직전 trial 재시도, S 또는 N skip, Q 종료. 키를 길게 누르지 않는다.
5. 마지막 DONE에서 Q를 누르고 콘솔의 `Saved:`가 나올 때까지 기다린다. 전체 영상 decode/hash
   검증이 있으므로 창이 닫힌 직후 PowerShell을 종료하지 않는다.
6. 최초 smoke에서 R/S도 시험할 수 있으나, skip한 횟수는 정상 동작 완료 횟수가 아니다.

기존 pilot-v1 순서: NONE, LEFT_PUNCH, RIGHT_PUNCH, LEFT_RAISE, LEFT_LOWER,
RIGHT_RAISE, BLOCK, MOVE_LEFT, MOVE_RIGHT. 동작 설명은 [기존 안내](gesture-pilot.md#관측-동작과-게임-매핑)를 따른다.
READY에서 오래 기다려도 video는 늘지 않아야 한다. COUNTDOWN/RECORDING만 저장한다.

`--codec auto`는 FFV1/AVI → HFYU/AVI → PNG/MOV 순서다. Windows는 각 codec에서 FFmpeg를
먼저 지정하고 실패하면 OpenCV 기본 writer를 시도한다. camera backend와 video encoder는
별개다. [OpenCV 문서](https://docs.opencv.org/4.x/dd/d9e/classcv_1_1VideoWriter.html)는 FFV1/HFYU를
무손실 codec으로 명시하며, [공식 wheel 안내](https://pypi.org/project/opencv-contrib-python/4.13.0.92/)는
FFmpeg 포함을 설명한다. 그래도 장치별 설치에서의 실제 지원은 smoke 결과로 확인한다.
모든 무손실 writer가 실패하면 오류로 종료한다. 손실 codec이나 `--no-video`로 본 수집을
진행하지 않는다. open 이후 write/hash 검증 실패도 incomplete이며 새 smoke를 수행해야 한다.

## 6. summary / manifest 확인

콘솔 `Saved:`에 나온 **정확한 session 경로**를 복사한다. 아래 값은 실제 경로로 바꾼다.
자동으로 가장 최근 폴더를 선택하지 않는다. smoke와 본 데이터를 혼동할 수 있다.

```powershell
$session = ".\sessions\여기에_Saved의_session_id"
$manifest = Get-Content -LiteralPath (Join-Path $session "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$summary = Get-Content -LiteralPath (Join-Path $session "summary.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$manifest | Select-Object subject_id, take_id, session_id, code_commit_sha, camera_backend, actual_resolution, pose_model_sha256
$manifest.video | Format-List
$summary | Select-Object status, captured_frame_count, capture_failure_count, pose_missing_count
$summary.metadata.video_worker | Format-List
$summary.metadata.video_timing | Format-List
$summary.metadata.stage_timing_ms | ConvertTo-Json -Depth 5
$summary.metadata.trials | Group-Object label, outcome | Select-Object Name, Count
```

본 데이터 전 합격 기준:

- subject/take/SHA/해상도와 backend가 의도한 값이다.
- `status=completed`, `lossless_verified=true`, `timing_verified=true`.
- queue full count=0, worker error=null, pending count=0, capture failure=0.
- recorded FPS가 warmup/container FPS와 비슷하다. P01은 약 28~30 FPS였다.
- pose missing, queue depth와 processing p50/p95를 확인하고 지속 결측/큰 지연은 담당자에게 알린다.
- encoded frame count와 `video_frames.jsonl` 줄 수가 같다. 실제 영상은 원본 비반전이며
  overlay가 없고 동작 속도가 자연스럽다. 기본 Windows 플레이어가 FFV1을 못 열어도 collector의
  decoder 검증과는 별개다. 필요 시 담당자와 호환 플레이어를 확인한다. 영상을 변환하지 않는다.

`completed`는 Q로 일찍 끝내도 가능하고 `all_trials_visited`는 skip해도 true가 될 수 있다.
본 데이터는 label마다 최종 유효 5회인지 확인한다. retry의 superseded history까지 단순 합산하지 않는다.
timing false/null, lossless false, incomplete이면 임의 수정하지 말고 담당자에게 전달한다.

## 7. 5회 본 데이터 수집 (9 × 5 = 45 trials)

smoke가 합격한 같은 카메라/backend/환경에서 실행한다. smoke에서 `msmf`가 필요했다면 아래
auto도 `msmf`로 바꾼다. auto video FPS를 유지하며 `--video-fps`를 추가하지 않는다.

```powershell
& .\.venv\Scripts\python.exe .\src\collect_gesture_pilot.py --subject $subject --take T01 --camera 0 --camera-backend auto --width 640 --height 480 --codec auto --repetitions 5 --countdown 3 --duration 3
```

각 trial을 SPACE로 시작한다. 마지막 DONE에서 Q → Saved 완료까지 기다린다.
새 Saved 경로로 `$session`을 다시 지정하고 6절 확인 명령을 실행한다. 동일한 9종 gesture,
5 repetitions, 앉은 자세를 유지한다. 재수집이면 take를 T02 등으로 구분한다.

## 8. 영상 제외 ZIP 생성과 전달

**sessions 폴더를 Git add/push하지 않는다.** 다음 명령은 allowlist의 6개 파일만 ZIP으로
만든다. wildcard 압축이나 sessions 전체 압축을 하지 않는다.

```powershell
& .\.venv\Scripts\python.exe .\src\package_pilot_session.py "$session"
```

출력은 `sessions\P02_<session_id>_metadata.zip` 형태다. 이름은 P02/P03/P04/P05와 자동 생성된
session ID뿐이다. 같은 ZIP이 이미 있으면 덮어쓰지 않고 오류가 난다. 새로 만들 필요가 있으면
기존 전달본과 담당자 확인 후 별도로 보관한다.

ZIP 구성: `manifest.json`, `summary.json`, `frames.jsonl`, `annotations.jsonl`,
`video_frames.jsonl`, `diagnostics.jsonl` **정확히 6개**.

`video.avi`(PNG fallback이면 `video.mov`)는 ZIP에서 제외한다. 참가자는 **원본 session 폴더 전체와
원본 영상을 로컬에 보관**한다. 삭제·재인코딩·편집하지 않는다. ZIP은 메타데이터 전달용이며
향후 원본 RGB 재추론에는 참가자가 보관한 영상이 필요하다.
압축 파일을 담당자(수집 요청자)가 지정한 전달 채널로 보내고, 본인 P번호와 take만 알려준다.
실명/학번을 ZIP 파일명에 넣지 않는다. 프로그램은 자동 업로드하지 않는다.

## 참고 및 검증 범위

- [MediaPipe 0.10.35 Windows wheel](https://pypi.org/project/mediapipe/0.10.35/)
- [OpenCV contrib 4.13.0.92 Windows wheel](https://pypi.org/project/opencv-contrib-python/4.13.0.92/)
- 카메라 backend와 codec fallback은 mock으로 검사했으며 실제 Windows UI·드라이버·DLL·FPS는
  P02~P05 각 노트북의 smoke test로 확인한다. macOS에서 성공했다는 사실만으로 Windows 수집
  성공을 가정하지 않는다.
