# Session recording / replay — schema v1

Camera-independent foundation in [session_recording.py](../src/session_recording.py)
and [replay_session.py](../src/replay_session.py). No calibration integration in
this task: existing CSV recording, inference, thresholds and models stay unchanged.
No extra dependencies. This records observations, not camera images; changing Pose
Lite/Full later requires original images/video or a fresh capture. Stored landmarks
alone cannot rerun a different pose model.

The separate [Gesture Pilot Collector](gesture-pilot.md) now supplies optional
lossless source video and a `video_frames.jsonl` mapping. The core recorder still
does not encode images. `enable_video_mapping()` registers that extra log with the
same flush/fsync policy; `record_video_frame()` maps selected successful captures
in increasing order, with gaps allowed for omitted READY frames. Video indices
remain contiguous; optional trial_id/trial_state/label identify recorded segments.
`update_manifest()` persists discovered camera backend/resolution and video status.
`finalize(metadata=...)` adds collector metadata to the summary without replacing
the recorder's counts or completion status. These are additive schema v1 fields.

## Files

```text
sessions/<session_id>/
    manifest.json
    frames.jsonl
    annotations.jsonl
    summary.json
    diagnostics.jsonl
```

The repository-root `sessions/` directory is ignored by Git. Custom output roots
are supported; callers must keep them outside tracked source directories.
Existing session directories cannot be overwritten or resumed by this API.

## Manifest

`schema_version: 1`; subject/session/take IDs; caller-supplied code commit SHA;
UTC start time; OS and machine/platform information; camera index/backend;
requested/actual `[width, height]` resolution; pose model path/SHA256/variant/mode;
display mirroring; gesture set version; notes; recording configuration; optional
video metadata. Unknown fields default to null, except recording configuration
defaults to an empty object. A readable supplied model file is hashed; no model
is downloaded or changed. Camera/model configuration is supplied by the caller,
never inferred from the environment. Record commit SHA from the running checkout
and record dirty state in recording_config when relevant.

## Frames and diagnostics

Call `record_frame` exactly once per successful camera capture, or
`record_capture_failure` exactly once per failed read, in capture order.
`capture_attempt_id` is a shared contiguous 1-based sequence across both APIs;
`frame_id` is the separate contiguous 1-based successful-capture sequence. Failures
consume only an attempt ID; annotations consume neither. Both methods return their
record, including the source attempt ID. Validation failures are API errors, not
camera attempts, and do not allocate IDs. Capture timestamp is integer monotonic
nanoseconds sampled immediately after capture returns, not exposure time. Pose
start/end use the same clock and must follow capture. Unknown timing is null.
`pose_success` means the pose call completed; a successful call can detect no pose.

Image and world landmarks are independently nullable arrays of `[x, y, z]`, one
pose per record, with original index order. Missing individual coordinates,
visibility and presence become null. Visibility/presence are separate parallel
arrays under `image` and `world`. `pose_validity` records presence, per-landmark
coordinate validity and overall validity; all coordinates must be finite for
overall validity. Visibility/presence are optional and do not invalidate coordinates.
Missing score/calibration values remain null, with per-field `score_validity` flags.
Raw/final movement and calibration
state/baseline/MAD/scores are caller-supplied observations, not recomputed here.
Processing metadata accepts JSON-compatible values. All nonfinite numbers become
null recursively; JSON serialization disallows NaN/Infinity.

`record_capture_failure` writes a diagnostic event with `capture_attempt_id`, `after_frame_id`, timestamp,
notes and `capture_success: false`, without consuming a frame ID. Diagnostics do
not appear in observation replay. A failed pose call on a successful capture still
belongs in frames with `pose_success=False`; include error details in metadata.

Keep `(session_id, capture_attempt_id)` attached to source images and future pose
requests/callbacks. This API currently records synchronously in capture order;
it does not allocate IDs from asynchronous callback completion order. An async
collector will need to carry source identity and order/buffer results explicitly.
VIDEO/LIVE_STREAM and async submission APIs are not implemented here.

## Annotations

`record_annotation` accepts interval/event/marker, label, source and notes. Each
record has a unique annotation ID, subject/session/take IDs and UTC timestamp
(annotation creation time by default). Interval bounds are inclusive and reference
already recorded frames. Events and markers require one `event_frame_id`; unused
bounds are null. No automatic labeling or inference is performed.

## Durability and summary

Each JSONL append opens, writes, flushes and closes the file immediately, moving
the line out of Python's user-space buffer. Physical sync is configurable through
`SessionRecorder(fsync_every_n_records=60)`, recorded in manifest recording_config.
The default syncs all changed logs after 60 combined frame/annotation/diagnostic
records (about two seconds at 30 capture records/sec without other records). This
reduces blocking sync frequency; it is not a time guarantee or a measured latency
target. Use `1` for every record or `None` for finalize-only syncing. Positive
integers and None are the only accepted values. The counter resets after sync.
Periodic syncing remains synchronous and its latency requires hardware testing.

`finalize()` always flushes/fsyncs all three JSONL files before writing the summary,
including incomplete finalization. Manifest and summary writes retain fsync.
An abnormal termination may leave the last unsynced records missing or partially
written; a process-only crash usually leaves flushed kernel buffers intact, but
OS/power/storage failure can lose them. The policy deliberately trades recent
record durability for lower capture overhead. No claim of per-frame physical
durability is made; a killed process cannot run finalize.
Initial summary is incomplete. `finalize()` atomically replaces summary with final
counts, start/end UTC, monotonic duration in seconds and pose processing p50/p95 in
milliseconds (linear interpolation). `valid_landmark_count` counts finite coordinate
triples across both coordinate spaces, not frames. `pose_missing_count` counts
frames with neither image nor world landmarks, independently of pose call success.
Unknown timing produces null percentiles. Context-manager normal exit completes;
exception exit finalizes incomplete. A killed process can leave a stale incomplete
summary; frame log is authoritative. No hard-crash durability guarantee is made for
an in-flight write or filesystem/hardware failure.

## Usage and replay

```python
from session_recording import SessionRecorder

with SessionRecorder(subject_id="P01", take_id="T01") as recorder:
    # Supply actual capture/pose timestamps and observations from the caller.
    recorder.record_frame(capture_return_monotonic_ns=1000,
                          pose_success=True, image_landmarks=None)
    recorder.record_annotation(label="start", annotation_type="marker",
                               event_frame_id=1, source="manual")
```

```bash
.venv/bin/python src/replay_session.py sessions/<session_id> --mode fast
```

`iter_frames(path)` reloads manifest and streams original records in order with no
camera access or sleep. `iter_timed_frames(path)` returns relative seconds, gap
seconds and the original record for a future realtime scheduler. Repeated reads
of an unchanged log return the same sequence, including null/missing frames.
Summary is optional and never gates replay. Unsupported versions fail in
`check_schema`, used for both manifest and every frame. Unknown additional fields
within v1 are preserved; no migration framework is introduced.
`capture_attempt_id` and the manifest fsync policy are additive v1 fields. Replay
preserves attempt IDs and validates strictly increasing positive IDs when present
(gaps represent failed attempts). Legacy v1 frames without the field are accepted
without inventing IDs. Frame IDs must still be contiguous. Replay does not join or
cross-validate diagnostics; consumers needing the full attempt timeline can join
the two logs by attempt ID.

A malformed final line without newline is skipped with a warning by default,
allowing recovery of preceding frames after interruption. `--strict-tail` rejects
it. Other malformed JSON, nonstandard constants, invalid frame IDs or decreasing
timestamps fail. Replay an inactive session for a stable sequence.

Before Gesture Pilot: connect a capture loop, fill real camera/model metadata,
choose subject/take/gesture identifiers and annotation boundaries, then smoke-test
throughput, pose loss, capture failures and interrupted sessions on hardware.
