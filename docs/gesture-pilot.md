# REDBOOL Vision V2 Gesture Pilot — pilot-v1

## 목적과 조건

노트북 내장 카메라 앞에 **앉은 자세**, 상반신과 양쪽 어깨·손이 보이는 환경에서
후보 동작의 가시성, Pose dropout, 동작 간 혼동 가능성을 관찰한다. 모델 학습,
게임 입력, 자동 정확도 측정 단계가 아니다. 기존 Action class나 MLP를 사용하지 않는다.
현재 프로젝트의 `pose_landmarker.task`를 **Lite**로 사용하며 교체·다운로드하지 않는다.
실행 전 실제 자산이 Lite인지 확인한다. manifest의 Lite 표기는 이 운용 조건이며
모델 파일의 내용을 자동 판별한 결과가 아니다. SHA256으로 자산을 식별한다.

## 관측 동작과 게임 매핑

좌우는 항상 **수행자 본인의 해부학적 좌우**다. 아래 순서로 각각 5회 수집한다.

| 관측 label | 수행할 동작 | 별도의 초기 게임 매핑(문서상 후보) |
|---|---|---|
| NONE | 편안한 기본 자세·작은 자연스러운 움직임, 의도한 제스처 없음 | 없음 |
| LEFT_PUNCH | 왼손을 앞으로 뻗었다가 기본 자세로 복귀 | JAB |
| RIGHT_PUNCH | 오른손을 앞으로 뻗었다가 복귀 | STRAIGHT |
| LEFT_RAISE | 왼손을 위로 올렸다가 복귀 | HIGH_KICK candidate |
| LEFT_LOWER | 기본 자세에서 왼손을 아래로 내렸다가 복귀 | LOW_KICK candidate A |
| RIGHT_RAISE | 오른손을 위로 올렸다가 복귀 | LOW_KICK candidate B |
| BLOCK | 양팔로 방어 자세를 만들고 기록 시간 동안 유지 | 하나의 Vision input |
| MOVE_LEFT | 앉은 상태에서 왼쪽 어깨를 카메라 쪽으로 내밀어 회전 자세 유지 | 이동 후보 |
| MOVE_RIGHT | 오른쪽 어깨를 카메라 쪽으로 내밀어 회전 자세 유지 | 이동 후보 |

게임 기술명 HIGH_KICK/LOW_KICK/JAB/STRAIGHT는 저장 label로 받지 않는다.
LEFT_LOWER에서 손이 프레임 밖으로 내려가거나 몸통에 가려지는지도 관찰 대상이다.
BLOCK/MOVE는 유지 상태 수집이며, 실제 분류/이동 판정으로 label을 만들지 않는다.

## 실행

프로젝트 루트에서:

```bash
.venv/bin/python src/collect_gesture_pilot.py --subject P01 --camera 0 --repetitions 5
```

기본 설정: 640×480 요청, countdown 3초, 기록 구간 3초, mirror preview 켜짐,
원본 영상 저장 켜짐, skeleton 기본 OFF. 실제 카메라가 협상한 해상도는 별도로 기록한다.

```bash
# 순서·시간·codec을 명시하는 예
.venv/bin/python src/collect_gesture_pilot.py --subject P01 --take T02 \
  --gestures NONE LEFT_PUNCH RIGHT_PUNCH BLOCK --repetitions 5 \
  --countdown 3 --duration 4 --width 640 --height 480 --codec ffv1
```

- `--show-skeleton`: 선택할 때만 preview skeleton 표시. 기본 smoke test는 OFF.
- `--video-queue-size 8`: 대기 영상 프레임 상한(기본 8), 실행 중인 1프레임은 별도.
- `--mirror-preview` / `--no-mirror-preview`: 화면만 변경.
- `--video` / `--no-video`: 기본 enabled. disabled는 개발 진단용이며 RGB A/B 자료가 아니다.
- `--codec auto|ffv1|hfyu|png`: auto는 FFV1/AVI → HFYU/AVI → PNG/MOV 순서로 open 시도.
- `--video-fps 15`: 필요할 때만 쓰는 명시적 override. 생략하면 **auto**이며 30 FPS 고정값을 사용하지 않는다.
- `--output`: 기본 프로젝트 루트의 Git-ignored `sessions/`. 다른 경로 사용 시 Git 추적 주의.
- Subject는 개인정보 대신 P01 같은 식별자를 사용한다. session ID는 매번 새로 생성된다.

## 키와 trial

| 키 | 동작 |
|---|---|
| SPACE | READY 상태에서 현재 trial countdown 시작 |
| R | 진행 중 trial, 또는 READY/DONE에서 직전 trial 재시도 |
| S / N | 현재 trial skip 후 다음 순서 |
| Q | 정상 종료·저장; 완료 전에도 가능 |

상태: `READY → COUNTDOWN → GO / RECORDING → READY`, 마지막 trial 이후 `DONE`.
DONE 화면에서도 R로 마지막 trial을 재시도할 수 있다. 종료하려면 Q를 누른다.
자동으로 다음 동작을 시작하지 않는다. preview 창에 포커스를 두고 키를 누른다.

매 성공 capture에 Pose를 실행하고 관측을 저장한다. 영상은 **COUNTDOWN + RECORDING만**
저장하며 READY/DONE은 제외한다. 최초 warm-up에서도 camera/Pose/preview/관측은 동작하지만
RGB를 버퍼링하거나 영상에 저장하지 않는다. READY 상태에서 SPACE를 누른 프레임은 이미
캡처된 READY 프레임이므로 다음 프레임부터 COUNTDOWN 영상에 들어간다. 기록 종료 경계의
마지막 RECORDING 프레임은 interval의 inclusive end와 맞춰 영상에 포함한다.

Auto FPS는 capture_return_monotonic_ns 간격을 **최소 20개(21프레임), 경과 1초 이상**
모은 다음 `1 / median(interval)`로 계산한다. 보통 약 1~2초이며 낮은 FPS에서는 더 걸린다.
CAP_PROP_FPS를 capture cadence로 사용하지 않는다. 최초 WARMUP 표시 중 SPACE/R 시작은
무시되므로 READY가 표시된 뒤 SPACE를 누른다. 실제 writer는 첫 저장 대상 프레임에서 연다.
명시적 FPS override를 쓰면 warm-up 완료를 기다리지 않지만 측정은 계속한다.

manifest.video에는 requested_fps(null=auto), measured_effective_capture_fps(초기 측정),
writer_fps(선택한 값), container_fps(종료 후 decoder가 읽은 실제 header 값),
fps_decision_method를 구분해 저장한다. 저장 trial이 없으면 video 파일은 생성하지 않고
container_fps/timing_verified는 null, video_frames 로그는 빈 상태다.

`source="prompt"` annotation은 countdown 시작, cue_go, recording interval과 종료
marker를 기록한다. label, repetition_index, trial_id, schedule_index는 notes에 포함된다.
cue는 처리된 프레임에 연결해 화면 표시 직전에 기록하며, 화면의 실제 발광 시각이나
사람의 **motion onset/peak/return**을 뜻하지 않는다. interval 경계는 프레임 양끝 포함이고
실제 기록 종료는 설정 시간이 지난 다음 처리 프레임이다. 저속 capture에서는 더 길 수 있다.

Retry는 기존 영상/프레임/annotation을 삭제하지 않는다. 이전 trial에 superseded marker,
새 trial에 retry_of/supersedes 관계를 기록한다. 기존 interval은 수정하지 않으므로 분석 시
superseded marker도 해석해야 한다. Skip·Q·예외 도중의 interval은 해당 outcome으로 닫는다.
영상 검토 후 `source="manual"` 등 별도 수정 annotation을 추가할 수 있는 구조이며,
기존 세션에 대한 annotation 편집 도구는 이번 범위에 포함하지 않는다.

## Session output와 RGB 보존

```text
sessions/<session_id>/
    manifest.json
    frames.jsonl
    annotations.jsonl
    diagnostics.jsonl
    summary.json
    video.avi                 # codec에 따라 video.mov
    video_frames.jsonl
```

영상은 overlay·mirror·resize 없이 **카메라가 반환한 원본 색상 프레임**을 저장한다.
OpenCV의 BGR 입력을 사용하고 Pose에는 동일 프레임의 RGB 변환본을 공급한다.
이는 카메라 센서 RAW나 카메라 내부 압축 이전 데이터를 의미하지 않는다.
preview만 복사본을 mirror하므로 label과 좌표의 해부학적 좌우는 바뀌지 않는다.
`--show-skeleton`을 지정한 경우에만 MediaPipe image landmark skeleton을 **preview 복사본에만** 그린다. 좌표는 mirror 설정에
맞춰 변환하고 결측/nonfinite/화면 밖 점은 그리지 않는다. 저장 영상과 Pose 입력에 skeleton이나
UI text가 들어가지 않으며 preview drawing은 원본 frame을 mutate하지 않는다.

향후 동일한 원본 RGB를 Lite와 Full에 각각 입력하기 위해 lossless codec만 사용한다.
VideoWriter open 실패 시 명확한 오류로 중단하며 landmark-only로 자동 전환하지 않는다.
오류 메시지에 따라 `--codec hfyu` 또는 `--codec png`, 해상도 설정을 바꿔 재실행한다.
codec 지원은 설치된 OpenCV/backend에 따라 다르다. 임의 손실 codec으로 대체하지 않는다.

`video_frames.jsonl`은 zero-based video_frame_index를 frame_id, capture_attempt_id,
capture_return_monotonic_ns에 연결하고 원본 RGB SHA256, trial_id, trial_state, label도 기록한다.
**실제로 writer에 제출된 프레임만** mapping에 남긴다. video index는 0부터 연속이고,
READY가 생략되므로 mapping 안의 frame_id/capture_attempt_id에는 간격이 생길 수 있다.
원본 frames 로그의 성공 frame_id는 계속 연속이다. Retry/skip으로도 기존 mapping은 삭제하지 않는다.

Q/종료 시 video worker FIFO를 drain하고 join한 뒤 writer를 release한 다음 전체 영상을 디코딩해 각 RGB hash와 프레임 수를 비교한다.
성공해야 manifest.video.lossless_verified가 true가 된다. 실패하면 incomplete로 남기고
오류를 표시한다. 이 검사는 영상 길이에 비례하는 시간이 걸린다. 검증 전에는 매핑이
writer에 제출한 프레임에 대한 기록이고 저장 성공의 최종 보장은 아니다.
영상은 session 전체 타임라인이 아니라 **recorded trial segments를 이어 붙인 review/reprocessing
stream**이다. READY 대기 시간은 재생되지 않는다. 정확한 temporal analysis 및 Lite/Full 재추론은
video_frames.jsonl의 **original capture timestamps가 source of truth**이며 container timestamp만
사용하면 안 된다. trial_id도 함께 사용해 segment 경계를 구분한다.

Lossless 검증과 별도로 timing sanity를 기록한다. 같은 trial_id이고 observation frame_id가
연속인 인접 encoded 프레임의 interval만 모아 median cadence를 계산하므로 READY gap과
서로 다른 retry/trial 경계는 제외된다. 최소 5개 interval이 있을 때
`abs(container_fps - measured_capture_fps) / measured_capture_fps <= 0.20`이면 timing_verified=true다.
상수 `TIMING_MIN_INTERVALS=5`, `TIMING_RELATIVE_TOLERANCE=0.20`은 smoke-test 경고 기준이며
정밀 temporal 일치를 보장하지 않는다. 20% 초과는 false와 console 경고, sample/header FPS가
부족하면 null로 남긴다. timing 불일치만으로 lossless 자료를 폐기하거나 incomplete 처리하지 않는다.
manifest.video와 summary.metadata.video_timing에 measured_capture_fps/container_fps/
relative_difference/timing_verified가 기록된다. Warm-up에는 video encode 부하가 없으므로
trial 실행 시 cadence가 달라질 수 있으며 이 검증으로 확인한다. 전체 session elapsed와
video duration을 비교하지 않는다.

## 종료·성능·결측

Main thread는 read → Pose → observation → video enqueue → preview 순서로 실행한다.
단일 video worker가 원본 BGR/RGB 복사본을 FIFO 순서로 write → SHA256 → mapping append한다.
기본 대기 상한은 8프레임이며 `--video-queue-size`로 조정한다. main의 enqueue는 용량을
기다리지 않는다. queue full이면 즉시 오류로 종료하고 이미 수락한 작업은 drain한다.
무한 backlog나 silent drop은 없다. worker 오류는 다음 capture 전 또는 close에서 전달하며
session은 incomplete다. 실패 job과 남은 job의 frame/capture/trial 식별자는
summary.metadata.video_worker에 보존한다. 정상 종료는 worker join → writer release →
전체 decode/hash 검증 → recorder finalize 순서다. 네이티브 encoder가 영구 정지하면 Python
thread를 안전하게 강제 종료할 수 없으므로 종료도 기다릴 수 있다.

Video mapping은 단일 worker만 쓰고 자체 주기적 fsync를 사용한다. 관측 로그와 fsync
bookkeeping을 공유하지 않으므로 video I/O lock 때문에 capture가 기다리지 않는다.
camera buffer size 1을 요청하지만 backend가 무시할 수 있다.
summary.metadata.capture_fps는 첫/마지막 성공 capture 시각으로 계산한다.
frame interval과 pose start/end는 monotonic clock으로 저장한다.

summary.metadata.stage_timing_ms에는 pose_processing, video_enqueue,
video_worker_processing, video_write_encode, frame_interval, camera_read의 count/p50/p95(ms)를
기록한다. video_enqueue에는 원본 buffer copy와 enqueue가 포함된다.
video_worker_processing은 worker의 write + RGB SHA256 + mapping append/fsync 시간이며
video_write_encode는 T02와 비교하기 위한 동일 값의 호환 key다. 최초 writer open과 종료 후
검증은 제외한다. summary.metadata.video_worker에는 video_queue_capacity,
video_queue_depth_max, video_queue_full_count, video_pending_count와 오류 식별 정보가 있다.
FPS 선택과 20% timing tolerance는 바꾸지 않는다. 실제 cadence 회복은 T03 실기기에서 검증한다.

Pose 미검출 프레임도 관측을 유지하고, COUNTDOWN/RECORDING이면 RGB·매핑도 유지한다. Pose 실행 예외는 해당 성공 capture에
pose_success=false를 기록하고 session을 incomplete로 종료한다. read 예외 및 반복 실패도
diagnostics에 기록하며 연속 실패 5회면 중단한다. Q는 정상 completed 종료지만
all_trials_visited=false일 수 있다. completed는 모든 제스처를 수행했다는 뜻이 아니다.

예외·Ctrl-C 시 자원 해제와 incomplete finalize를 시도한다. 정상/예외 종료 모두 카메라,
Pose, 창, video writer를 해제한다. JSONL은 즉시 flush하고 Recorder 기본 60-record fsync
정책을 사용하며 video mapping도 같은 정책에 포함된다. finalize에서 모두 fsync한다.
강제 kill/전원 장애에는 최근 기록 또는 영상 container의 마무리 정보가 유실될 수 있고,
RGB 복구는 보장하지 않는다. summary·영상이 불완전해도 기존 observation replay는 가능하다.

## P01 첫 smoke test

1. Mac 카메라 권한에서 실행 앱(Terminal/VS Code)을 허용하고 다른 카메라 앱을 종료한다.
2. 양쪽 어깨와 손이 잘 보이도록 앉는다. 위 기본 명령을 실행한다.
3. WARMUP 후 READY를 기다린다. P01, label, repetition, 상태, skeleton OFF, pose detected/missing, interval을 확인한다.
4. 각 gesture마다 SPACE → countdown → GO를 보고 수행한다. 3초간 동작·복귀 또는 유지.
5. 본인의 왼손 동작을 LEFT로 수행하며 preview mirror가 label을 뒤집지 않는지 확인한다.
6. 한 번 R 재시도, S skip을 시험하고 기록이 삭제되지 않는지 확인한다. 이후 필요한 회차를 재수집한다.
7. 최종 DONE에서 Q. 검증 완료 후 video.lossless_verified와 summary 상태를 확인한다.
8. 실제 저장 영상은 preview와 반대인 비반전 원본이며 overlay가 없어야 한다.
9. 기록 구간에서 자세를 놓친 프레임이 영상과 frames 로그에 모두 남는지 확인한다.
10. READY에서 길게 기다려도 video mapping이 늘지 않는지, trial 재생 속도가 실제 동작과 비슷한지 확인한다.
11. measured_effective_capture_fps/container_fps/measured_capture_fps 및 timing_verified를 확인한다.
12. LEFT_LOWER의 하단 clipping과 RIGHT_PUNCH의 과도한 옆 뻗기는 반복 pilot에서 관찰한다.
    이 smoke 결과만으로 label/game mapping을 바꾸지 않으며 LEFT_LOWER를 계속 수집한다.

이 단계의 결과는 후보 제스처 검토 자료다. 5회 pilot만으로 정확도·혼동행렬·모델 우열을
주장하지 않는다. 실제 가시성, dropout, cue 반응, fsync/codec 지연, 장시간 성능과
macOS 카메라 backend buffering은 실기기 검증이 남아 있다.

## T03 async video smoke test (수동 실행)

```bash
.venv/bin/python src/collect_gesture_pilot.py --subject P01 --take T03 \
  --camera 0 --width 640 --height 480 --codec ffv1 --video-queue-size 8 \
  --gestures NONE LEFT_PUNCH RIGHT_PUNCH BLOCK --repetitions 5 --countdown 3 --duration 4
```

`--video-fps`를 생략해 WARMUP auto를 유지하고 skeleton도 OFF로 둔다. T02와 동작/시간을
동일하게 맞춰 비교한다. WARMUP FPS(29.748), recorded FPS(14.981), container FPS,
timing_verified/relative_difference(0.9857), pose/camera_read p50/p95, enqueue/worker p50/p95,
queue depth max/full count, captured/encoded/mapping frame 수, capture failure(0),
lossless_verified와 summary status를 확인한다. recorded FPS가 WARMUP 근처로 회복하고
full count가 0인지, 영상 속도·READY 제외·retry/skip·원본 overlay 없음도 확인한다.
