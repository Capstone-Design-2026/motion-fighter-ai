"""Camera-independent session schema v1 and durable observation recording.

Landmark arrays represent one pose and preserve the caller's landmark indices.
Call once per successful capture, including captures with no detected pose.
"""

from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import time
import uuid


SCHEMA_VERSION = 1
DEFAULT_SESSION_ROOT = Path(__file__).resolve().parents[1] / "sessions"


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def sanitize(value):
    """Replace nonfinite numbers with null, recursively; reject opaque objects."""
    if value is None or isinstance(value, (str, bool, int)):
        return value
    if isinstance(value, float):
        return value if math.isfinite(value) else None
    if isinstance(value, dict):
        if not all(isinstance(key, str) for key in value):
            raise TypeError("JSON object keys must be strings")
        return {key: sanitize(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [sanitize(item) for item in value]
    raise TypeError(f"Unsupported JSON value: {type(value).__name__}")


def check_schema(record):
    if type(record.get("schema_version")) is not int or record["schema_version"] != SCHEMA_VERSION:
        raise ValueError("Unsupported session schema_version")


def load_json(path):
    def reject_constant(value):
        raise ValueError(f"Nonstandard JSON constant: {value}")
    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=reject_constant)


def load_manifest(session_dir):
    manifest = load_json(Path(session_dir) / "manifest.json")
    check_schema(manifest)
    return manifest


def _write_json(path, value):
    payload = json.dumps(sanitize(value), ensure_ascii=False, allow_nan=False, indent=2)
    temporary = path.with_suffix(".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        stream.write(payload + "\n")
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def _append(path, record):
    payload = json.dumps(sanitize(record), ensure_ascii=False, allow_nan=False)
    with path.open("a", encoding="utf-8") as stream:
        stream.write(payload + "\n")
        stream.flush()


def _timestamp(value, name):
    if type(value) is not int or value < 0:
        raise ValueError(f"{name} must be a nonnegative integer nanosecond timestamp")
    return value


def _landmarks(points):
    if points is None:
        return None, None, None, None
    coordinates, visibility, presence, validity = [], [], [], []
    for point in points:
        def field(name):
            value = point.get(name) if isinstance(point, dict) else getattr(point, name, None)
            if value is None:
                return None
            try:
                value = float(value)
            except (TypeError, ValueError, OverflowError):
                return None
            return value if math.isfinite(value) else None
        xyz = [field(axis) for axis in ("x", "y", "z")]
        coordinates.append(xyz)
        visibility.append(field("visibility"))
        presence.append(field("presence"))
        validity.append(all(value is not None for value in xyz))
    if not coordinates:
        return None, None, None, None
    return coordinates, visibility, presence, validity


def _percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lower = int(position)
    upper = min(lower + 1, len(ordered) - 1)
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)


class SessionRecorder:
    """Append-only single-writer recorder. Existing session IDs never overwrite.

    pose_success means the pose call completed, even if it detected no pose.
    valid_landmark_count counts valid coordinate triples across image and world.
    Explicit finalize() marks completion; exceptions/context exit mark incomplete.
    """

    def __init__(self, root=DEFAULT_SESSION_ROOT, *, session_id=None, subject_id=None,
                 take_id=None, fsync_every_n_records=60, **metadata):
        if fsync_every_n_records is not None and (
            type(fsync_every_n_records) is not int or fsync_every_n_records < 1
        ):
            raise ValueError("fsync_every_n_records must be a positive integer or None")
        self._fsync_every_n_records = fsync_every_n_records
        self._unsynced_records = 0
        self._dirty_logs = set()
        self._capture_attempt_id = 0
        session_id = session_id or uuid.uuid4().hex
        if not isinstance(session_id, str) or session_id in (".", "..") or any(
            char in session_id for char in ("/", "\\")
        ):
            raise ValueError("session_id must be a single directory name")
        self.directory = Path(root) / session_id
        self._started_ns = time.monotonic_ns()
        self._last_capture_ns = None
        self._closed = False
        self._annotation_ids = set()
        self._processing_ms = []
        self._counts = dict(captured_frame_count=0, pose_success_count=0,
                            pose_missing_count=0, capture_failure_count=0,
                            valid_landmark_count=0)
        manifest = dict(
            schema_version=SCHEMA_VERSION, subject_id=subject_id,
            session_id=session_id, take_id=take_id, code_commit_sha=None,
            started_at_utc=utc_now(), os=platform.system(),
            machine_info={"machine": platform.machine(), "platform": platform.platform()},
            camera_index=None, camera_backend=None, requested_resolution=None,
            actual_resolution=None, pose_model_path=None, pose_model_sha256=None,
            pose_model_variant=None, pose_running_mode=None, mirror_display=None,
            gesture_set_version=None, notes=None, recording_config={}, video=None,
        )
        protected = {"schema_version", "started_at_utc", "os", "machine_info"}
        if metadata.keys() - manifest.keys() or metadata.keys() & protected:
            raise ValueError("Unknown or recorder-owned manifest field")
        manifest.update(metadata)
        recording_config = manifest["recording_config"]
        if not isinstance(recording_config, dict):
            raise ValueError("recording_config must be an object")
        manifest["recording_config"] = {
            **recording_config, "fsync_every_n_records": fsync_every_n_records,
        }
        if manifest["pose_model_path"] is not None:
            model_path = Path(manifest["pose_model_path"])
            manifest["pose_model_path"] = str(model_path)
            if model_path.is_file():
                with model_path.open("rb") as stream:
                    manifest["pose_model_sha256"] = hashlib.file_digest(stream, "sha256").hexdigest()
        self.manifest = sanitize(manifest)
        self.directory.mkdir(parents=True, exist_ok=False)
        _write_json(self.directory / "manifest.json", self.manifest)
        for name in ("frames.jsonl", "annotations.jsonl", "diagnostics.jsonl"):
            (self.directory / name).touch(exist_ok=False)
        self._save_summary("incomplete")

    def _ensure_open(self):
        if self._closed:
            raise RuntimeError("Session has already been finalized")

    def _sync_logs(self, *, all_logs=False):
        names = ("frames.jsonl", "annotations.jsonl", "diagnostics.jsonl") if all_logs else sorted(self._dirty_logs)
        for name in names:
            with (self.directory / name).open("a", encoding="utf-8") as stream:
                stream.flush()
                os.fsync(stream.fileno())
        self._dirty_logs.clear()
        self._unsynced_records = 0

    def _append_record(self, name, record):
        _append(self.directory / name, record)
        self._dirty_logs.add(name)
        self._unsynced_records += 1
        if self._fsync_every_n_records is not None and self._unsynced_records >= self._fsync_every_n_records:
            self._sync_logs()

    def record_frame(self, *, capture_return_monotonic_ns, pose_success,
                     pose_start_monotonic_ns=None, pose_end_monotonic_ns=None,
                     image_landmarks=None, world_landmarks=None,
                     calibration_state=None, neutral_baseline=None, calibration_mad=None,
                     raw_rotation_score=None, corrected_rotation_score=None,
                     raw_movement=None, final_movement=None, processing_metadata=None):
        self._ensure_open()
        capture_ns = _timestamp(capture_return_monotonic_ns, "capture_return_monotonic_ns")
        if self._last_capture_ns is not None and capture_ns < self._last_capture_ns:
            raise ValueError("Capture timestamps must be nondecreasing")
        if type(pose_success) is not bool:
            raise ValueError("pose_success must be boolean")
        if (pose_start_monotonic_ns is None) != (pose_end_monotonic_ns is None):
            raise ValueError("Supply both pose timestamps or neither")
        processing_ms = None
        if pose_start_monotonic_ns is not None:
            start = _timestamp(pose_start_monotonic_ns, "pose_start_monotonic_ns")
            end = _timestamp(pose_end_monotonic_ns, "pose_end_monotonic_ns")
            if not capture_ns <= start <= end:
                raise ValueError("Expected capture <= pose start <= pose end")
            processing_ms = (end - start) / 1_000_000
        image, iv, ip, valid_image = _landmarks(image_landmarks)
        world, wv, wp, valid_world = _landmarks(world_landmarks)
        present = bool(image or world)
        valid_count = sum(valid_image or []) + sum(valid_world or [])
        record = sanitize(dict(
            schema_version=SCHEMA_VERSION, frame_id=self._counts["captured_frame_count"] + 1,
            capture_attempt_id=self._capture_attempt_id + 1,
            capture_return_monotonic_ns=capture_ns, capture_success=True,
            pose_start_monotonic_ns=pose_start_monotonic_ns,
            pose_end_monotonic_ns=pose_end_monotonic_ns, pose_success=pose_success,
            pose_validity={"pose_present": present, "image_landmarks_valid": valid_image,
                           "world_landmarks_valid": valid_world,
                           "valid": pose_success and present and all((valid_image or []) + (valid_world or []))},
            image_landmarks=image, world_landmarks=world,
            landmark_visibility={"image": iv, "world": wv},
            landmark_presence={"image": ip, "world": wp},
            calibration_state=calibration_state, neutral_baseline=neutral_baseline,
            calibration_mad=calibration_mad, raw_rotation_score=raw_rotation_score,
            corrected_rotation_score=corrected_rotation_score,
            raw_movement=raw_movement, final_movement=final_movement,
            processing_metadata=processing_metadata or {},
        ))
        record["score_validity"] = {
            name: isinstance(record[name], (int, float)) and not isinstance(record[name], bool)
            for name in ("raw_rotation_score", "corrected_rotation_score",
                         "neutral_baseline", "calibration_mad")
        }
        self._append_record("frames.jsonl", record)
        self._capture_attempt_id += 1
        self._last_capture_ns = capture_ns
        self._counts["captured_frame_count"] += 1
        self._counts["pose_success_count"] += int(pose_success)
        self._counts["pose_missing_count"] += int(not present)
        self._counts["valid_landmark_count"] += valid_count
        if processing_ms is not None:
            self._processing_ms.append(processing_ms)
        return record

    def record_capture_failure(self, *, capture_return_monotonic_ns, notes=None):
        self._ensure_open()
        capture_ns = _timestamp(capture_return_monotonic_ns, "capture timestamp")
        if self._last_capture_ns is not None and capture_ns < self._last_capture_ns:
            raise ValueError("Capture timestamps must be nondecreasing")
        record = dict(schema_version=SCHEMA_VERSION, event_type="capture_failure",
                      capture_attempt_id=self._capture_attempt_id + 1,
                      capture_success=False, after_frame_id=self._counts["captured_frame_count"],
                      capture_return_monotonic_ns=capture_ns,
                      notes=notes)
        self._append_record("diagnostics.jsonl", record)
        self._capture_attempt_id += 1
        self._last_capture_ns = capture_ns
        self._counts["capture_failure_count"] += 1
        return record

    def record_annotation(self, *, label, annotation_type, start_frame_id=None,
                          end_frame_id=None, event_frame_id=None, annotation_id=None,
                          timestamp=None, source=None, notes=None):
        self._ensure_open()
        if annotation_type not in ("interval", "event", "marker"):
            raise ValueError("Unknown annotation_type")
        for value in (start_frame_id, end_frame_id, event_frame_id):
            if value is not None and (type(value) is not int or not 1 <= value <= self._counts["captured_frame_count"]):
                raise ValueError("Annotation must reference an existing captured frame")
        if annotation_type == "interval":
            if start_frame_id is None or end_frame_id is None or start_frame_id > end_frame_id or event_frame_id is not None:
                raise ValueError("Interval requires ordered inclusive start/end frames only")
        elif event_frame_id is None or start_frame_id is not None or end_frame_id is not None:
            raise ValueError("Event/marker requires event_frame_id only")
        annotation_id = annotation_id or uuid.uuid4().hex
        if not isinstance(annotation_id, str) or annotation_id in self._annotation_ids:
            raise ValueError("annotation_id must be a unique string")
        record = sanitize(dict(
            schema_version=SCHEMA_VERSION, annotation_id=annotation_id,
            subject_id=self.manifest["subject_id"], session_id=self.manifest["session_id"],
            take_id=self.manifest["take_id"], label=label, annotation_type=annotation_type,
            start_frame_id=start_frame_id, end_frame_id=end_frame_id,
            event_frame_id=event_frame_id, timestamp=timestamp or utc_now(), source=source, notes=notes,
        ))
        self._append_record("annotations.jsonl", record)
        self._annotation_ids.add(annotation_id)
        return record

    def _save_summary(self, status, ended_at=None):
        summary = dict(schema_version=SCHEMA_VERSION, **self._counts,
                       started_at=self.manifest["started_at_utc"], ended_at=ended_at,
                       duration=(time.monotonic_ns() - self._started_ns) / 1_000_000_000,
                       status=status, processing_time_ms={
                           "count": len(self._processing_ms),
                           "p50": _percentile(self._processing_ms, .5),
                           "p95": _percentile(self._processing_ms, .95)})
        _write_json(self.directory / "summary.json", summary)
        return summary

    def finalize(self, *, completed=True):
        self._ensure_open()
        self._sync_logs(all_logs=True)
        summary = self._save_summary("completed" if completed else "incomplete", utc_now())
        self._closed = True
        return summary

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        if not self._closed:
            self.finalize(completed=exc_type is None)
