"""Seated upper-body observation pilot; no classifier, movement detector or UDP."""

import argparse
import hashlib
import json
import math
import platform
import queue
import threading
from pathlib import Path
import subprocess
import time
from statistics import median

from config import POSE_MODEL_PATH
from pose_capture import CAMERA_BACKENDS, open_camera, create_pose_landmarker, require_pose_model
from session_recording import DEFAULT_SESSION_ROOT, SessionRecorder


GESTURE_SET_VERSION = "pilot-v1"
GESTURES = ("NONE", "LEFT_PUNCH", "RIGHT_PUNCH", "LEFT_RAISE", "LEFT_LOWER",
            "RIGHT_RAISE", "BLOCK", "MOVE_LEFT", "MOVE_RIGHT")
CODECS = {"ffv1": ("FFV1", ".avi"), "hfyu": ("HFYU", ".avi"), "png": ("png ", ".mov")}
KEY_HELP = "SPACE start | R retry current/previous | S/N skip | Q quit"
WARMUP_INTERVALS = 20
WARMUP_SECONDS = 1.0
TIMING_MIN_INTERVALS = 5
TIMING_RELATIVE_TOLERANCE = 0.20
POSE_CONNECTIONS = ((0, 1), (1, 2), (2, 3), (3, 7), (0, 4), (4, 5), (5, 6),
                    (6, 8), (9, 10), (11, 12), (11, 13), (13, 15), (15, 17),
                    (15, 19), (15, 21), (17, 19), (12, 14), (14, 16), (16, 18),
                    (16, 20), (16, 22), (18, 20), (11, 23), (12, 24), (23, 24),
                    (23, 25), (24, 26), (25, 27), (26, 28), (27, 29), (28, 30),
                    (29, 31), (30, 32), (27, 31), (28, 32))


def effective_fps(timestamps):
    intervals = [b - a for a, b in zip(timestamps, timestamps[1:]) if b > a]
    return 1e9 / median(intervals) if intervals else None


def timing_summary(values):
    ordered = sorted(values)
    def percentile(fraction):
        if not ordered:
            return None
        pos = (len(ordered) - 1) * fraction
        lo = int(pos)
        return ordered[lo] + (ordered[min(lo + 1, len(ordered) - 1)] - ordered[lo]) * (pos - lo)
    return {"count": len(values), "p50": percentile(.5), "p95": percentile(.95)}


def video_timing(intervals_ns, container_fps):
    measured = 1e9 / median(intervals_ns) if intervals_ns else None
    difference = abs(container_fps - measured) / measured if measured and container_fps else None
    enough = len(intervals_ns) >= TIMING_MIN_INTERVALS and difference is not None
    return {"timing_verified": difference <= TIMING_RELATIVE_TOLERANCE if enough else None,
            "measured_capture_fps": measured, "container_fps": container_fps,
            "relative_difference": difference, "interval_count": len(intervals_ns),
            "relative_tolerance": TIMING_RELATIVE_TOLERANCE,
            "method": "median within-trial consecutive recorded capture intervals"}


def make_schedule(gestures=GESTURES, repetitions=5):
    if type(repetitions) is not int or repetitions < 1:
        raise ValueError("repetitions must be a positive integer")
    if not gestures or any(label not in GESTURES for label in gestures) or len(set(gestures)) != len(gestures):
        raise ValueError("Use unique pilot-v1 observational labels")
    return [(label, rep) for label in gestures for rep in range(1, repetitions + 1)]


class TrialController:
    """Prompt intervals, never inferred motion onset/peak. All history is append-only."""

    def __init__(self, recorder, schedule, countdown=3.0, duration=3.0):
        self.recorder = recorder
        self.schedule = schedule
        self.countdown = countdown
        self.duration = duration
        self.index = 0
        self.state = "READY"
        self.deadline = None
        self.trial = None
        self.previous = None
        self.serial = 0
        self.history = []
        self.start_frame = None

    @property
    def label(self):
        return self.schedule[min(self.index, len(self.schedule) - 1)][0]

    @property
    def repetition(self):
        return self.schedule[min(self.index, len(self.schedule) - 1)][1]

    def remaining(self, now):
        return max(0.0, self.deadline - now) if self.deadline is not None else 0.0

    def _new_trial(self, retry_of=None):
        self.serial += 1
        self.trial = {"trial_id": f"trial-{self.serial:04d}", "schedule_index": self.index,
                      "label": self.label, "repetition_index": self.repetition,
                      "retry_of": retry_of}

    def _marker(self, event, frame_id, now, **extra):
        return self.recorder.record_annotation(
            label=self.label, annotation_type="marker", event_frame_id=frame_id, source="prompt",
            notes={**self.trial, "event": event, "prompt_monotonic_ns": int(now * 1e9), **extra},
        )

    def _finish(self, frame_id, now, outcome):
        if self.state == "RECORDING":
            self.recorder.record_annotation(
                label=self.label, annotation_type="interval", start_frame_id=self.start_frame,
                end_frame_id=frame_id, source="prompt",
                notes={**self.trial, "event": "recording_interval", "outcome": outcome,
                       "end_prompt_monotonic_ns": int(now * 1e9)},
            )
        self._marker(outcome, frame_id, now)
        self.history.append({**self.trial, "outcome": outcome})
        self.previous = dict(self.trial)
        self.start_frame = None
        self.deadline = None

    def on_frame(self, frame_id, now):
        if self.state == "COUNTDOWN" and now >= self.deadline:
            self.state = "RECORDING"
            self.start_frame = frame_id
            self.deadline = now + self.duration
            self._marker("cue_go", frame_id, now)
        elif self.state == "RECORDING" and now >= self.deadline:
            self._finish(frame_id, now, "recording_complete")
            self.index += 1
            self.state = "DONE" if self.index == len(self.schedule) else "READY"

    def handle_key(self, key, frame_id, now):
        if key == " " and self.state == "READY":
            self._new_trial()
            self.state = "COUNTDOWN"
            self.deadline = now + self.countdown
            self._marker("countdown_start", frame_id, now)
        elif key == "r":
            if self.state in ("COUNTDOWN", "RECORDING"):
                old = dict(self.trial)
                self._finish(frame_id, now, "superseded")
            elif self.previous is not None:
                old = dict(self.previous)
                self.index = old["schedule_index"]
                self.trial = old
                self._marker("superseded", frame_id, now)
            else:
                return
            self._new_trial(retry_of=old["trial_id"])
            self._marker("retry", frame_id, now, supersedes=old["trial_id"])
            self.state = "COUNTDOWN"
            self.deadline = now + self.countdown
        elif key in ("s", "n") and self.state != "DONE":
            if self.state == "READY":
                self._new_trial()
            self._finish(frame_id, now, "skipped")
            self.index += 1
            self.state = "DONE" if self.index == len(self.schedule) else "READY"

    def stop(self, frame_id, now, reason):
        if frame_id and self.state in ("COUNTDOWN", "RECORDING"):
            self._finish(frame_id, now, reason)
        self.state = "STOPPED"


class PilotVideo:
    """Unmirrored lossless video with decode/hash verification after release."""

    def __init__(self, cv, recorder, size, fps, codec="auto", *, measured_fps=None, override_fps=None):
        self.cv, self.recorder, self.size = cv, recorder, tuple(size)
        self.writer = None
        self.count = 0
        self.path = None
        self.intervals_ns = []
        self.previous_record = None
        recorder.enable_video_mapping()
        attempted = []
        for name in CODECS if codec == "auto" else (codec,):
            fourcc, suffix = CODECS[name]
            path = recorder.directory / ("video" + suffix)
            # Windows camera APIs are independent of the file encoder. Prefer
            # FFmpeg explicitly there; retain the existing macOS writer call.
            apis = [("default", None)]
            if platform.system() == "Windows" and hasattr(cv, "CAP_FFMPEG"):
                apis.insert(0, ("FFMPEG", cv.CAP_FFMPEG))
            for api_name, api in apis:
                writer = None
                try:
                    args = (str(path), cv.VideoWriter_fourcc(*fourcc), fps, self.size)
                    writer = cv.VideoWriter(*args) if api is None else cv.VideoWriter(args[0], api, *args[1:])
                    if writer.isOpened():
                        self.writer, self.path = writer, path
                        selected_api = api_name
                        break
                    attempted.append(f"{name}/{api_name}: not opened")
                except Exception as exc:
                    attempted.append(f"{name}/{api_name}: {exc}")
                finally:
                    if writer is not None and self.writer is not writer:
                        writer.release()
            if self.writer is not None:
                break
        if self.writer is None:
            raise RuntimeError(
                f"No lossless VideoWriter opened ({', '.join(attempted)}). "
                "Select --codec ffv1, hfyu or png and/or --width/--height. "
                "RGB recording is required for the pilot; landmark-only fallback is disabled."
                " On Windows install requirements-pilot-windows.txt in a clean Python 3.11 "
                "environment (GUI OpenCV with FFmpeg); see docs/gesture-pilot-windows.md."
            )
        self.metadata = {"enabled": True, "path": self.path.name, "codec": name,
                         "fps": fps, "resolution": list(size), "mirror": False,
                         "writer_api_requested": selected_api, "writer_open_failures": attempted,
                         "requested_fps": override_fps, "measured_effective_capture_fps": measured_fps,
                         "writer_fps": fps, "container_fps": None,
                         "fps_decision_method": "explicit_override" if override_fps is not None else "warmup_median_capture_interval",
                         "recording_scope": ["COUNTDOWN", "RECORDING"],
                         "timing_verified": None,
                         "lossless_verified": False, "color_input": "original BGR; verified as RGB",
                         "timebase": "concatenated trial segments; original capture timestamps in video_frames.jsonl"}
        try:
            recorder.update_manifest(video=self.metadata)
        except BaseException:
            self.writer.release()
            self.writer = None
            raise

    def write(self, bgr, rgb, frame_record, *, trial_id=None, trial_state=None, label=None):
        if (bgr.shape[1], bgr.shape[0]) != self.size:
            raise RuntimeError("Camera resolution changed during video recording")
        if not self.writer.isOpened() or self.writer.write(bgr) is False:
            raise RuntimeError("VideoWriter failed to write frame")
        mapping = self.recorder.record_video_frame(
            frame_record, source_rgb_sha256=hashlib.sha256(rgb.tobytes()).hexdigest(),
            trial_id=trial_id, trial_state=trial_state, label=label)
        previous = self.previous_record
        if (previous is not None and previous["trial_id"] == trial_id
                and previous["frame_id"] + 1 == mapping["frame_id"]):
            interval = mapping["capture_return_monotonic_ns"] - previous["capture_return_monotonic_ns"]
            if interval > 0:
                self.intervals_ns.append(interval)
        self.previous_record = mapping
        self.count += 1

    def close(self):
        if self.writer is None:
            return
        writer, self.writer = self.writer, None
        writer.release()
        decoder = self.cv.VideoCapture(str(self.path))
        try:
            if not decoder.isOpened():
                raise RuntimeError("Recorded video cannot be decoded; session is incomplete")
            count = 0
            progress = getattr(self, "verification_progress", None)
            interval = max(1, self.count // 20)
            if progress: progress(0, self.count)
            with (self.recorder.directory / "video_frames.jsonl").open(encoding="utf-8") as stream:
                for line in stream:
                    mapping = json.loads(line)
                    ok, decoded = decoder.read()
                    if not ok:
                        raise RuntimeError("Video has fewer frames than its observation mapping")
                    rgb = self.cv.cvtColor(decoded, self.cv.COLOR_BGR2RGB)
                    if hashlib.sha256(rgb.tobytes()).hexdigest() != mapping["source_rgb_sha256"]:
                        raise RuntimeError("Video RGB round-trip mismatch; select another --codec/config")
                    count += 1
                    if progress and (count % interval == 0 or count == self.count):
                        progress(count, self.count)
            if decoder.read()[0] or count != self.count:
                raise RuntimeError("Video frame count does not match mapping")
            container_fps = decoder.get(self.cv.CAP_PROP_FPS)
            if not math.isfinite(container_fps) or container_fps <= 0:
                container_fps = None
            timing = video_timing(self.intervals_ns, container_fps)
            self.metadata.update(timing)
            self.metadata.update(lossless_verified=True, encoded_frame_count=count)
            self.recorder.update_manifest(video=self.metadata)
            if timing["timing_verified"] is False:
                print("WARNING: recorded cadence differs from container FPS by more than 20%; use capture timestamps.")
        finally:
            decoder.release()


class AsyncPilotVideo:
    """One FIFO consumer; capture never waits for encoder capacity.

    Only this worker writes video mappings. The main thread owns observations,
    annotations and manifest updates; close joins before verification/finalize.
    """

    def __init__(self, video, max_queue_size=8):
        if type(max_queue_size) is not int or max_queue_size < 1:
            raise ValueError("video queue size must be a positive integer")
        self.video = video
        self.jobs = queue.Queue(maxsize=max_queue_size)
        self.stopping = threading.Event()
        self.error = None
        self.failed_job = None
        self.queue_full_count = 0
        self.queue_depth_max = 0
        self.processing_ms = []
        self.closed = False
        self.thread = threading.Thread(target=self._run, name="pilot-video")
        self.thread.start()

    @property
    def metadata(self):
        return self.video.metadata

    def check_error(self):
        if self.error is not None:
            raise RuntimeError(f"Video worker failed: {self.error}") from self.error

    def write(self, bgr, rgb, frame_record, **metadata):
        self.check_error()
        if self.stopping.is_set():
            raise RuntimeError("Video worker is closing")
        # Own the buffers even if the camera/backend reuses or mutates them.
        identity = {key: frame_record[key] for key in
                    ("frame_id", "capture_attempt_id", "capture_return_monotonic_ns")}
        job = (bgr.copy(), rgb.copy(), identity, dict(metadata))
        try:
            # Queue mutex protects the exact high-water mark, without any I/O.
            with self.jobs.mutex:
                depth = self.jobs._qsize()
                if depth >= self.jobs.maxsize:
                    raise queue.Full
                self.jobs._put(job)
                self.jobs.unfinished_tasks += 1
                self.queue_depth_max = max(self.queue_depth_max, depth + 1)
                self.jobs.not_empty.notify()
        except queue.Full:
            self.queue_full_count += 1
            self.failed_job = {**identity, **metadata, "event": "video_queue_full"}
            raise RuntimeError(f"Video queue saturated at frame {frame_record['frame_id']}; session incomplete")

    def _run(self):
        while not self.stopping.is_set() or not self.jobs.empty():
            try:
                bgr, rgb, row, metadata = self.jobs.get(timeout=.05)
            except queue.Empty:
                continue
            start = time.monotonic_ns()
            try:
                self.video.write(bgr, rgb, row, **metadata)
            except BaseException as exc:
                self.failed_job = {**row, **metadata, "event": "video_worker_error"}
                self.error = exc
                return
            finally:
                self.processing_ms.append((time.monotonic_ns() - start) / 1e6)
                self.jobs.task_done()

    def diagnostics(self):
        # Called after join, so pending identities cannot change underneath us.
        with self.jobs.mutex:
            pending = [{**job[2], **job[3]} for job in self.jobs.queue]
        return {"video_queue_capacity": self.jobs.maxsize,
                "video_queue_depth_max": self.queue_depth_max,
                "video_queue_full_count": self.queue_full_count,
                "video_pending_count": self.jobs.qsize(),
                "video_pending_jobs": pending,
                "video_failed_job": self.failed_job,
                "video_worker_error": str(self.error) if self.error else None}

    def close(self):
        if self.closed:
            self.check_error()
            return
        self.stopping.set()
        self.thread.join()
        self.closed = True
        try:
            self.video.close()
        finally:
            self.check_error()


def preview_frame(cv, original, mirror):
    return cv.flip(original, 1) if mirror else original.copy()


def draw_skeleton(cv, display, landmarks, mirror):
    height, width = display.shape[:2]
    points = {}
    for index, lm in enumerate(landmarks or []):
        x, y = getattr(lm, "x", None), getattr(lm, "y", None)
        if x is None or y is None or not math.isfinite(x) or not math.isfinite(y):
            continue
        if not (0 <= x <= 1 and 0 <= y <= 1):
            continue
        points[index] = (round(((1 - x) if mirror else x) * (width - 1)), round(y * (height - 1)))
    for a, b in POSE_CONNECTIONS:
        if a in points and b in points:
            cv.line(display, points[a], points[b], (0, 255, 0), 1)
    for point in points.values():
        cv.circle(display, point, 2, (0, 200, 255), -1)


def draw_preview(cv, frame, recorder, trial, opts, pose_present, interval, now,
                 landmarks=None, warming_up=False):
    display = preview_frame(cv, frame, opts.mirror_preview)
    if opts.show_skeleton:
        draw_skeleton(cv, display, landmarks, opts.mirror_preview)
    status = "GO / RECORDING" if trial.state == "RECORDING" else trial.state
    if warming_up:
        status = "WARMUP - measuring capture FPS; wait before SPACE"
    lines = [f"Subject: {opts.subject}  Session: {recorder.manifest['session_id']}",
             f"Gesture: {trial.label}  Trial: {trial.repetition}/{opts.repetitions}",
             f"{status}  {trial.remaining(now):.1f}s",
             f"Pose: {'detected' if pose_present else 'MISSING'}  Interval: {interval or 0:.3f}s",
             KEY_HELP, "LEFT/RIGHT = your anatomical side; preview only is mirrored" if opts.mirror_preview
             else "LEFT/RIGHT = your anatomical side; preview not mirrored"]
    for index, line in enumerate(lines):
        cv.putText(display, line, (8, 24 + index * 26), cv.FONT_HERSHEY_SIMPLEX,
                   .45, (0, 255, 255), 1, cv.LINE_AA)
    cv.imshow("REDBOOL Gesture Pilot", display)


def run_collector(opts, *, cv, pose_factory, image_factory, camera_factory=None,
                  clock_ns=time.monotonic_ns, code_commit_sha=None, code_dirty=None):
    """Capture/Pose loop with a bounded, independently encoded video FIFO."""
    schedule = make_schedule(opts.gestures, opts.repetitions)
    recorder = SessionRecorder(
        opts.output, subject_id=opts.subject, take_id=opts.take, code_commit_sha=code_commit_sha,
        camera_index=opts.camera, requested_resolution=[opts.width, opts.height],
        pose_model_path=POSE_MODEL_PATH, pose_model_variant="Lite", pose_running_mode="IMAGE",
        mirror_display=opts.mirror_preview, gesture_set_version=GESTURE_SET_VERSION,
        video={"enabled": opts.video, "lossless_verified": False, "requested_fps": opts.video_fps,
               "measured_effective_capture_fps": None, "container_fps": None, "timing_verified": None,
               "fps_decision_method": "explicit_override" if opts.video_fps is not None else "warmup_median_capture_interval",
               "recording_scope": ["COUNTDOWN", "RECORDING"]},
        recording_config={"gestures": list(opts.gestures), "repetitions": opts.repetitions,
                          "countdown_seconds": opts.countdown, "duration_seconds": opts.duration,
                          "source_mirrored": False, "code_dirty": code_dirty,
                          "codec_requested": opts.codec, "video_fps_requested": opts.video_fps,
                          "camera_backend_requested": opts.camera_backend,
                          "video_queue_size": opts.video_queue_size, "show_skeleton": opts.show_skeleton},
    )
    trial = TrialController(recorder, schedule, opts.countdown, opts.duration)
    cap = pose = video = None
    first_ns = last_ns = None
    captured = 0
    warmup_stamps = []
    measured_fps = None
    stage_ms = {name: [] for name in ("pose_processing", "video_enqueue", "frame_interval", "camera_read")}
    error = None
    reason = "exception"
    cleanup_errors = []
    print(f"Session: {recorder.directory}\n{KEY_HELP}")
    print("Anatomical LEFT/RIGHT. Seated, shoulders and hands visible. No action classifier.")
    try:
        cap = (camera_factory or (lambda index: open_camera(cv, index, opts.camera_backend)))(opts.camera)
        cap.set(cv.CAP_PROP_FRAME_WIDTH, opts.width)
        cap.set(cv.CAP_PROP_FRAME_HEIGHT, opts.height)
        # Backend may ignore this hint; the video FIFO is separate from capture.
        cap.set(cv.CAP_PROP_BUFFERSIZE, 1)
        try:
            backend = cap.getBackendName()
        except Exception:
            backend = None
        recorder.update_manifest(camera_backend=backend)
        pose = pose_factory()
        if opts.video:
            recorder.enable_video_mapping()
        failures = 0
        while True:
            if video is not None:
                video.check_error()
            read_start = clock_ns()
            try:
                ok, frame = cap.read()
            except Exception as exc:
                recorder.record_capture_failure(capture_return_monotonic_ns=clock_ns(), notes=str(exc))
                raise
            capture_ns = clock_ns()
            stage_ms["camera_read"].append((capture_ns - read_start) / 1e6)
            if not ok or frame is None:
                recorder.record_capture_failure(capture_return_monotonic_ns=capture_ns, notes="camera.read returned no frame")
                failures += 1
                if (cv.waitKey(1) & 0xFF) == ord("q"):
                    reason = "user_quit"
                    break
                if failures >= 5:
                    raise RuntimeError("Five consecutive capture failures; check camera permission/device. "
                                       "On Windows retry with --camera-backend msmf or dshow; "
                                       "see docs/gesture-pilot-windows.md")
                continue
            failures = 0
            interval = None if last_ns is None else (capture_ns - last_ns) / 1e9
            if interval is not None:
                stage_ms["frame_interval"].append(interval * 1000)
            last_ns = capture_ns
            if first_ns is None:
                first_ns = capture_ns
                recorder.update_manifest(actual_resolution=[frame.shape[1], frame.shape[0]])
            rgb = cv.cvtColor(frame, cv.COLOR_BGR2RGB)
            pose_start = clock_ns()
            pose_error = None
            result = None
            try:
                result = pose.detect(image_factory(rgb))
            except Exception as exc:
                pose_error = exc
            pose_end = clock_ns()
            stage_ms["pose_processing"].append((pose_end - pose_start) / 1e6)
            image_points = getattr(result, "pose_landmarks", None)
            world_points = getattr(result, "pose_world_landmarks", None)
            row = recorder.record_frame(
                capture_return_monotonic_ns=capture_ns, pose_success=pose_error is None,
                pose_start_monotonic_ns=pose_start, pose_end_monotonic_ns=pose_end,
                image_landmarks=image_points[0] if image_points else None,
                world_landmarks=world_points[0] if world_points else None,
                processing_metadata={"frame_interval_seconds": interval,
                                     "pose_error": str(pose_error) if pose_error else None},
            )
            captured += 1
            if measured_fps is None:
                warmup_stamps.append(capture_ns)
                if (len(warmup_stamps) > WARMUP_INTERVALS
                        and (warmup_stamps[-1] - warmup_stamps[0]) / 1e9 >= WARMUP_SECONDS):
                    measured_fps = effective_fps(warmup_stamps)
                    recorder.update_manifest(video={**recorder.manifest["video"],
                                                    "measured_effective_capture_fps": measured_fps})
                    if video is not None:
                        video.metadata["measured_effective_capture_fps"] = measured_fps
            now = clock_ns() / 1e9
            prior_state = trial.state
            prior_trial = dict(trial.trial) if trial.trial else None
            trial.on_frame(row["frame_id"], now)
            # Preserve the inclusive final RECORDING frame even when on_frame advances to READY.
            video_state = trial.state if trial.state == "RECORDING" else prior_state
            if opts.video and video_state in ("COUNTDOWN", "RECORDING"):
                if video is None:
                    video = AsyncPilotVideo(PilotVideo(cv, recorder, [frame.shape[1], frame.shape[0]],
                                       opts.video_fps or measured_fps, opts.codec,
                                       measured_fps=measured_fps, override_fps=opts.video_fps), opts.video_queue_size)
                encode_start = clock_ns()
                video.write(frame, rgb, row, trial_id=prior_trial["trial_id"],
                            trial_state=video_state, label=prior_trial["label"])
                stage_ms["video_enqueue"].append((clock_ns() - encode_start) / 1e6)
            if pose_error is not None:
                raise pose_error
            warming_up = opts.video and opts.video_fps is None and measured_fps is None
            draw_preview(cv, frame, recorder, trial, opts, bool(image_points or world_points), interval, now,
                         image_points[0] if image_points else None, warming_up)
            key_code = cv.waitKey(1) & 0xFF
            key = chr(key_code).lower() if key_code != 255 else ""
            if key == "q":
                reason = "user_quit"
                break
            if not warming_up or key not in (" ", "r"):
                trial.handle_key(key, row["frame_id"], clock_ns() / 1e9)
            # DONE remains visible so R can retry the final trial; Q finalizes.
    except BaseException as exc:
        error = exc
        reason = "interrupted" if isinstance(exc, KeyboardInterrupt) else "exception"
    finally:
        def cleanup(operation):
            try:
                operation()
            except BaseException as exc:
                cleanup_errors.append(f"{type(exc).__name__}: {exc}")
        cleanup(lambda: trial.stop(captured, clock_ns() / 1e9, reason))
        if cap is not None:
            cleanup(cap.release)
        if pose is not None:
            cleanup(pose.close)
        cleanup(cv.destroyAllWindows)
        if video is not None:
            cleanup(video.close)
        stage_ms["video_worker_processing"] = video.processing_ms if video else []
        # Retain the old key for T02 comparison, now measured on the worker.
        stage_ms["video_write_encode"] = stage_ms["video_worker_processing"]
        elapsed = (last_ns - first_ns) / 1e9 if captured > 1 else None
        cleanup(lambda: recorder.finalize(
            completed=error is None and not cleanup_errors,
            metadata={"exit_reason": reason, "error": str(error) if error else None,
                      "cleanup_errors": list(cleanup_errors), "trials": trial.history,
                      "video_worker": video.diagnostics() if video else None,
                      "capture_fps": (captured - 1) / elapsed if elapsed and elapsed > 0 else None,
                      "all_trials_visited": trial.index == len(schedule),
                      "stage_timing_ms": {name: timing_summary(values) for name, values in stage_ms.items()},
                      "video_timing": {key: recorder.manifest["video"].get(key) for key in
                                       ("timing_verified", "measured_capture_fps", "container_fps", "relative_difference")},
                      "video_enabled": opts.video},
        ))
    if error is not None:
        raise error
    if cleanup_errors:
        raise RuntimeError("Session incomplete: " + "; ".join(cleanup_errors))
    print(f"Saved: {recorder.directory}")
    return recorder.directory


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--subject", required=True)
    parser.add_argument("--take", default="T01")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--camera-backend", choices=["auto", "any", *CAMERA_BACKENDS], default="auto",
                        help="Windows auto: DSHOW, MSMF, default; macOS auto preserves default backend")
    parser.add_argument("--repetitions", type=int, default=5)
    parser.add_argument("--countdown", type=float, default=3.0)
    parser.add_argument("--duration", type=float, default=3.0)
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--mirror-preview", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--show-skeleton", action="store_true", help="Draw skeleton on preview only")
    parser.add_argument("--video-queue-size", type=int, default=8,
                        help="Maximum pending video frames; saturation aborts without silent drops")
    parser.add_argument("--video", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--codec", choices=["auto", *CODECS], default="auto")
    parser.add_argument("--video-fps", type=float, default=None,
                        help="Explicit FPS override; default measures collector capture cadence automatically")
    parser.add_argument("--gestures", nargs="+", choices=GESTURES, default=list(GESTURES))
    parser.add_argument("--output", type=Path, default=DEFAULT_SESSION_ROOT)
    opts = parser.parse_args(argv)
    try:
        make_schedule(opts.gestures, opts.repetitions)
        if opts.video_queue_size < 1:
            raise ValueError("video queue size must be positive")
        if not opts.subject.strip() or opts.camera < 0 or opts.width < 1 or opts.height < 1:
            raise ValueError("Nonempty subject, nonnegative camera and positive resolution required")
        if any(not math.isfinite(v) for v in (opts.countdown, opts.duration)
               + (() if opts.video_fps is None else (opts.video_fps,))):
            raise ValueError("Timing values must be finite")
        if opts.countdown < 0 or opts.duration <= 0 or (opts.video_fps is not None and opts.video_fps <= 0):
            raise ValueError("Countdown >= 0; duration and video FPS > 0 required")
    except ValueError as exc:
        parser.error(str(exc))
    return opts


def main(argv=None):
    opts = parse_args(argv)
    require_pose_model(POSE_MODEL_PATH)
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    root = Path(__file__).resolve().parents[1]
    def git_output(*args):
        try:
            return subprocess.check_output(["git", *args], cwd=root, text=True, stderr=subprocess.DEVNULL, timeout=5).strip()
        except (OSError, subprocess.SubprocessError):
            return None
    status = git_output("status", "--porcelain")
    run_collector(opts, cv=cv2,
                  pose_factory=lambda: create_pose_landmarker(python, vision, POSE_MODEL_PATH),
                  image_factory=lambda rgb: mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb),
                  code_commit_sha=git_output("rev-parse", "HEAD"),
                  code_dirty=None if status is None else bool(status))


if __name__ == "__main__":
    main()
