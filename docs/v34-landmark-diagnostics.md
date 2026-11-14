# v34: replayable diagnostics without video

The packaged camera backend uses `--recording diagnostics`. It stores every processed Pose observation plus the ordered engine operations required to reproduce state, calibration, personal context, and emitted events. It does not save RGB, preview JPEGs, or audio. The existing manual `review`, `logs`, and `lossless` modes remain available and retain their existing purposes.

Each camera run contains `recognition/landmarks.index.json` and independently valid `landmarks-000001.jsonl.gz` chunks. The index schema is `redbool.landmarks`, version `1`. Files ending in `.part` or `.tmp` are unfinished and never replayed or exported as complete chunks. The Unity diagnostic exporter copies the index and whole indexed chunks, checking each size and SHA-256. It does not truncate compressed files.

The journal includes:

- Original full-precision primary image/world coordinates, visibility, presence, capture frame identity, dimensions and capture/processing times, including absent or invalid poses.
- Full VIDEO and Full IMAGE auxiliary coordinates, world coordinates, confidence and model timing from the same image. Anatomical left/right and original unmirrored coordinates are retained.
- Exact engine clock reads, process/tick/drain order, calibration/pause/movement commands and tutorial context. Published control state, personal status/threshold changes and emitted engine events are compared during replay.
- Model fingerprints, engine configuration, camera session metadata, explicit coverage gaps and writer health. These observations are not human intent labels.

Primary nonfinite coordinates already normalized by the existing SessionRecorder remain null, exactly as consumed by the engine. Auxiliary NaN/Infinity values consumed by the engine are encoded with standard JSON objects `{"$float":"nan"}`, `{"$float":"+inf"}` or `{"$float":"-inf"}` and restored during offline replay. No finite coordinates are rounded.

## Bounded recording

A producer only queues an immutable snapshot; serialization, compression and file writes happen on one background worker. The queue holds at most512 operations. Each gzip closes after60 processed frames,512 total operations or about2 seconds, and is atomically published with its checksum. The index is also atomically replaced.

A run has a128MiB compressed budget and an8MiB maximum chunk. The recorder never deletes previous runs or rolls over old chunks. A full queue, exhausted budget, disk error or bounded shutdown timeout is reported in `summary.json`, the index, and the game's camera status. Recording omissions do not terminate recognition or gameplay. Once coverage is incomplete, the verifier refuses to present it as an exact replay. If the game is forcibly terminated, previously closed chunks remain available; the final `.part` and unknown tail are reported as incomplete.

Camera capture may outpace inference; the existing latest-frame policy is unchanged. The journal covers processed observations, not every image obtained from the device. Camera frame IDs and the existing capture-drop summary distinguish these skips from recording-queue omissions.

## Offline verification

From the Python repository with its environment installed:

```sh
PYTHONPATH=src python -m redbool_demo.landmark_replay "/path/to/camera/run/recognition"
```

PowerShell:

```powershell
$env:PYTHONPATH = "src"
python -m redbool_demo.landmark_replay "C:\path\to\camera\run\recognition"
```

The verifier checks paths, schema, checksums, limits, sequence coverage, exact state and events using the same engine implementation. It supports SixGestureEngine and PersonalGestureEngine. A different code/configuration version, missing chunks or queue gaps cannot be silently substituted or interpolated. A successful replay establishes reproducibility, not recognition accuracy or the user's intended motion. Raw video remains unnecessary for ordinary support diagnostics; ambiguous intent still needs user description or separately authorized review material.

A paced saved-pose benchmark covered1,801 frames and7,205 operations from an existing60-second recording: all states and8 events matched, with0 queue omissions; compressed size was9.56MiB/minute. That older source lacks auxiliary world coordinates, so it is not a complete estimate for new three-model logs. A separate301-frame timing measured producer snapshot/queue overhead p95 at0.024ms; this excludes model inference. Performance and storage vary by hardware and pose content. A17x-realtime stress replay deliberately overloaded the bounded queue, reported the omissions, and kept engine states/events unchanged.
