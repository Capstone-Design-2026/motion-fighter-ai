"""Pilot contracts using synthetic RGB, fake camera/pose/writer; no real camera."""

import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from collect_gesture_pilot import (
    GESTURES, TrialController, PilotVideo, make_schedule, parse_args, run_collector,
    effective_fps, video_timing, draw_skeleton,
)
from session_recording import SessionRecorder, load_manifest
from replay_session import iter_frames
from pose_capture import open_camera, create_pose_landmarker


class FakeCamera:
    def __init__(self, frames):
        self.frames = list(frames)
        self.released = False
        self.settings = {}

    def isOpened(self):
        return True

    def read(self):
        if not self.frames:
            return False, None
        frame = self.frames.pop(0)
        if isinstance(frame, Exception):
            raise frame
        return frame is not None, None if frame is None else frame.copy()

    def set(self, key, value):
        self.settings[key] = value
        return True

    def getBackendName(self):
        return "FAKE"

    def get(self, key):
        return self.settings.get(key, 0)

    def release(self):
        self.released = True


class FakePose:
    def __init__(self, *, missing=False, error=False, world_only=False):
        self.inputs = []
        self.closed = False
        self.missing, self.error, self.world_only = missing, error, world_only

    def detect(self, rgb):
        self.inputs.append(rgb.copy())
        if self.error:
            raise RuntimeError("synthetic pose error")
        point = SimpleNamespace(x=.1, y=.2, z=.3, visibility=.9)
        return SimpleNamespace(pose_landmarks=[] if self.missing or self.world_only else [[point]],
                               pose_world_landmarks=[[point]] if self.world_only else [])

    def close(self):
        self.closed = True


class FakeWriter:
    def __init__(self, cv, path, opened):
        self.cv, self.path, self.opened = cv, path, opened
        self.released = False
        if opened:
            cv.videos[path] = []

    def isOpened(self):
        return self.opened

    def write(self, frame):
        self.cv.videos[self.path].append(frame.copy())

    def release(self):
        self.released = True


class FakeCV:
    COLOR_BGR2RGB = 1
    CAP_PROP_FRAME_WIDTH = 2
    CAP_PROP_FRAME_HEIGHT = 3
    CAP_PROP_BUFFERSIZE = 4
    FONT_HERSHEY_SIMPLEX = 0
    LINE_AA = 0
    CAP_DSHOW = 700
    CAP_PROP_FPS = 5

    def __init__(self, keys, *, reject_codecs=(), corrupt_decode=False):
        self.keys = list(keys)
        self.reject_codecs = reject_codecs
        self.corrupt_decode = corrupt_decode
        self.videos = {}
        self.writers = []
        self.decoders = []
        self.previews = []
        self.windows_closed = False
        self.video_rates = {}
        self.drawn_points = []

    def VideoWriter_fourcc(self, *chars):
        return "".join(chars)

    def VideoWriter(self, path, codec, fps, size):
        self.video_rates[path] = fps
        writer = FakeWriter(self, path, codec not in self.reject_codecs)
        self.writers.append(writer)
        return writer

    def VideoCapture(self, path):
        if not isinstance(path, str):
            raise AssertionError("Tests must never open a camera")
        frames = self.videos.get(path, [])
        if self.corrupt_decode:
            frames = [np.zeros_like(frame) for frame in frames]
        decoder = FakeCamera(frames)
        decoder.settings[self.CAP_PROP_FPS] = self.video_rates.get(path, 0)
        self.decoders.append(decoder)
        return decoder

    def cvtColor(self, frame, code):
        return frame[:, :, ::-1].copy()

    def flip(self, frame, axis):
        return frame[:, ::-1, :].copy()

    def putText(self, *args):
        pass

    def line(self, display, a, b, color, thickness):
        display[a[1], a[0]] = color

    def circle(self, display, point, radius, color, thickness):
        self.drawn_points.append(point)
        display[point[1], point[0]] = color

    def imshow(self, title, display):
        self.previews.append(display.copy())

    def waitKey(self, delay):
        key = self.keys.pop(0) if self.keys else "q"
        return ord(key) if key else -1

    def destroyAllWindows(self):
        self.windows_closed = True


class GesturePilotTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.frame = np.arange(8 * 6 * 3, dtype=np.uint8).reshape(6, 8, 3)

    def options(self, *args):
        return parse_args(["--subject", "P01", "--output", str(self.root), *args])

    def run_fake(self, *, keys=(" ", "q"), frames=None, pose=None, cv=None, args=(), auto=False):
        cv = cv or FakeCV(keys)
        pose = pose or FakePose()
        cap = FakeCamera(frames if frames is not None else [self.frame] * len(keys))
        stamp = 0
        def clock():
            nonlocal stamp
            stamp += 100_000_000
            return stamp
        self.cv, self.pose, self.cap = cv, pose, cap
        with contextlib.redirect_stdout(io.StringIO()):
            directory = run_collector(self.options(*(() if auto else ("--video-fps", "15")), *args), cv=cv, pose_factory=lambda: pose,
                                      image_factory=lambda rgb: rgb, camera_factory=lambda _: cap,
                                      clock_ns=clock, code_commit_sha="synthetic", code_dirty=False)
        return directory

    def rows(self, directory, name="annotations.jsonl"):
        return [json.loads(line) for line in (directory / name).read_text().splitlines()]

    def controller(self, *, duration=2):
        recorder = SessionRecorder(self.root, session_id="trial")
        controller = TrialController(recorder, make_schedule(["LEFT_PUNCH", "BLOCK"], 1), countdown=1, duration=duration)
        def frame(now):
            row = recorder.record_frame(capture_return_monotonic_ns=int(now * 1e9), pose_success=True)
            return row["frame_id"]
        return recorder, controller, frame

    def test_default_gesture_order(self):
        self.assertEqual(GESTURES, ("NONE", "LEFT_PUNCH", "RIGHT_PUNCH", "LEFT_RAISE", "LEFT_LOWER",
                                    "RIGHT_RAISE", "BLOCK", "MOVE_LEFT", "MOVE_RIGHT"))

    def test_repetition_schedule(self):
        self.assertEqual(len(make_schedule()), 45)
        self.assertEqual(make_schedule(["BLOCK", "NONE"], 2), [("BLOCK", 1), ("BLOCK", 2), ("NONE", 1), ("NONE", 2)])

    def test_no_game_actions_in_labels(self):
        for label in ("HIGH_KICK", "LOW_KICK", "JAB", "STRAIGHT"):
            with self.assertRaises(ValueError):
                make_schedule([label], 1)

    def test_cue_and_interval_are_prompt_annotations(self):
        recorder, trial, frame = self.controller()
        trial.handle_key(" ", frame(0), 0)
        trial.on_frame(frame(1), 1)
        self.assertEqual(trial.state, "RECORDING")
        trial.on_frame(frame(3), 3)
        rows = self.rows(recorder.directory)
        self.assertEqual([r["notes"]["event"] for r in rows], ["countdown_start", "cue_go", "recording_interval", "recording_complete"])
        self.assertEqual((rows[2]["start_frame_id"], rows[2]["end_frame_id"]), (2, 3))
        self.assertTrue(all(r["label"] == "LEFT_PUNCH" and r["source"] == "prompt" for r in rows))
        self.assertEqual(rows[1]["notes"]["repetition_index"], 1)

    def test_retry_preserves_previous_annotations(self):
        recorder, trial, frame = self.controller()
        trial.handle_key(" ", frame(0), 0)
        trial.on_frame(frame(1), 1)
        trial.on_frame(frame(3), 3)
        original = self.rows(recorder.directory)
        trial.handle_key("r", frame(4), 4)
        rows = self.rows(recorder.directory)
        self.assertEqual(rows[:len(original)], original)
        self.assertEqual(trial.label, "LEFT_PUNCH")
        self.assertEqual(trial.trial["retry_of"], "trial-0001")
        self.assertEqual(rows[-1]["notes"]["supersedes"], "trial-0001")

    def test_retry_active_trial_closes_partial_interval(self):
        recorder, trial, frame = self.controller()
        trial.handle_key(" ", frame(0), 0)
        trial.on_frame(frame(1), 1)
        trial.handle_key("r", frame(1.5), 1.5)
        intervals = [r for r in self.rows(recorder.directory) if r["annotation_type"] == "interval"]
        self.assertEqual(intervals[0]["notes"]["outcome"], "superseded")
        self.assertEqual(trial.state, "COUNTDOWN")

    def test_skip_advances_and_records_reason(self):
        recorder, trial, frame = self.controller()
        trial.handle_key("s", frame(0), 0)
        self.assertEqual(trial.label, "BLOCK")
        self.assertEqual(self.rows(recorder.directory)[0]["notes"]["event"], "skipped")

    def test_mirror_changes_preview_only_not_pose_video_or_label(self):
        directory = self.run_fake(keys=(" ", "q"), args=("--countdown", "0", "--gestures", "LEFT_PUNCH"))
        np.testing.assert_array_equal(self.pose.inputs[0], self.frame[:, :, ::-1])
        np.testing.assert_array_equal(next(iter(self.cv.videos.values()))[0], self.frame)
        expected = self.frame[:, ::-1, :].copy()
        np.testing.assert_array_equal(self.cv.previews[0], expected)
        np.testing.assert_array_equal(self.frame, np.arange(8 * 6 * 3, dtype=np.uint8).reshape(6, 8, 3))
        self.assertTrue(all(row["label"] == "LEFT_PUNCH" for row in self.rows(directory)))
        manifest = load_manifest(directory)
        self.assertTrue(manifest["mirror_display"])
        self.assertFalse(manifest["video"]["mirror"])

    def test_no_mirror_preview(self):
        directory = self.run_fake(args=("--no-mirror-preview",), pose=FakePose(missing=True))
        np.testing.assert_array_equal(self.cv.previews[0], self.frame)
        self.assertFalse(load_manifest(directory)["mirror_display"])

    def test_skeleton_default_off(self):
        self.assertFalse(self.options().show_skeleton)
        self.run_fake()
        self.assertEqual(self.cv.drawn_points, [])

    def test_show_skeleton_preview_only(self):
        self.run_fake(args=("--show-skeleton",))
        self.assertTrue(self.cv.drawn_points)
        np.testing.assert_array_equal(next(iter(self.cv.videos.values()))[0], self.frame)

    def test_video_queue_size_validation(self):
        self.assertEqual(self.options().video_queue_size, 8)
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            self.options("--video-queue-size", "0")

    def test_video_mapping_after_capture_failure(self):
        directory = self.run_fake(keys=(" ", "", "", "q"), frames=[self.frame, self.frame, None, self.frame])
        rows = self.rows(directory, "video_frames.jsonl")
        self.assertEqual([r["video_frame_index"] for r in rows], [0, 1])
        self.assertEqual([r["frame_id"] for r in rows], [2, 3])
        self.assertEqual([r["capture_attempt_id"] for r in rows], [2, 4])
        self.assertEqual([r["capture_return_monotonic_ns"] for r in rows],
                         [r["capture_return_monotonic_ns"] for r in iter_frames(directory)][1:])

    def test_pose_missing_frame_keeps_rgb_and_observation(self):
        directory = self.run_fake(pose=FakePose(missing=True))
        row = list(iter_frames(directory))[0]
        self.assertTrue(row["pose_success"])
        self.assertIsNone(row["image_landmarks"])
        self.assertIsNone(row["world_landmarks"])
        self.assertEqual(len(self.rows(directory, "video_frames.jsonl")), 1)

    def test_world_only_and_optional_presence(self):
        directory = self.run_fake(pose=FakePose(world_only=True))
        row = list(iter_frames(directory))[0]
        self.assertIsNone(row["image_landmarks"])
        self.assertEqual(row["world_landmarks"], [[.1, .2, .3]])
        self.assertEqual(row["landmark_presence"]["world"], [None])

    def test_early_quit_is_finalized_and_replayable(self):
        directory = self.run_fake()
        summary = json.loads((directory / "summary.json").read_text())
        self.assertEqual(summary["status"], "completed")
        self.assertFalse(summary["metadata"]["all_trials_visited"])
        self.assertEqual(len(list(iter_frames(directory))), 2)
        self.assertTrue(self.cap.released and self.pose.closed and self.cv.windows_closed)
        self.assertTrue(all(w.released for w in self.cv.writers))

    def test_video_disabled_is_explicit(self):
        directory = self.run_fake(args=("--no-video",))
        self.assertFalse(load_manifest(directory)["video"]["enabled"])
        self.assertFalse((directory / "video_frames.jsonl").exists())
        self.assertEqual(self.cv.writers, [])

    def test_writer_open_failure_aborts_and_preserves_partial_log(self):
        cv = FakeCV([" ", "q"], reject_codecs=("FFV1", "HFYU", "png "))
        with self.assertRaisesRegex(RuntimeError, "landmark-only fallback is disabled"):
            self.run_fake(cv=cv)
        directory = next(self.root.iterdir())
        self.assertEqual(len(list(iter_frames(directory))), 2)
        self.assertEqual(json.loads((directory / "summary.json").read_text())["status"], "incomplete")
        self.assertTrue(self.cap.released and self.pose.closed and cv.windows_closed)
        self.assertTrue(all(w.released for w in cv.writers))

    def test_codec_fallback(self):
        cv = FakeCV([" ", "q"], reject_codecs=("FFV1",))
        directory = self.run_fake(cv=cv)
        self.assertEqual(load_manifest(directory)["video"]["codec"], "hfyu")
        self.assertTrue(load_manifest(directory)["video"]["lossless_verified"])

    def test_lossy_or_corrupt_video_marks_incomplete(self):
        with self.assertRaisesRegex(RuntimeError, "RGB round-trip mismatch"):
            self.run_fake(cv=FakeCV([" ", "q"], corrupt_decode=True))
        directory = next(self.root.iterdir())
        self.assertEqual(json.loads((directory / "summary.json").read_text())["status"], "incomplete")
        self.assertFalse(load_manifest(directory)["video"]["lossless_verified"])

    def test_pose_exception_records_failed_pose_and_cleans_up(self):
        with self.assertRaisesRegex(RuntimeError, "synthetic pose error"):
            self.run_fake(pose=FakePose(error=True))
        directory = next(self.root.iterdir())
        self.assertFalse(list(iter_frames(directory))[0]["pose_success"])
        self.assertEqual(len(self.rows(directory, "video_frames.jsonl")), 0)
        self.assertTrue(self.cap.released and self.pose.closed)

    def test_capture_exception_records_diagnostic(self):
        with self.assertRaisesRegex(RuntimeError, "camera broke"):
            self.run_fake(frames=[RuntimeError("camera broke")])
        directory = next(self.root.iterdir())
        self.assertEqual(self.rows(directory, "diagnostics.jsonl")[0]["capture_attempt_id"], 1)
        self.assertEqual(list(iter_frames(directory)), [])

    def test_pose_abort_during_recording_drains_video(self):
        pose = FakePose()
        detect = pose.detect
        def fail_second(rgb):
            if pose.inputs:
                raise RuntimeError("recording pose abort")
            return detect(rgb)
        pose.detect = fail_second
        with self.assertRaisesRegex(RuntimeError, "recording pose abort"):
            self.run_fake(pose=pose, args=("--countdown", "0"))
        directory = next(self.root.iterdir())
        mapping = self.rows(directory, "video_frames.jsonl")
        self.assertEqual([row["frame_id"] for row in mapping], [2])
        self.assertTrue(load_manifest(directory)["video"]["lossless_verified"])
        summary = json.loads((directory / "summary.json").read_text())
        self.assertEqual(summary["status"], "incomplete")
        self.assertEqual(summary["metadata"]["video_worker"]["video_pending_count"], 0)
        self.assertEqual(summary["metadata"]["trials"][0]["outcome"], "exception")

    def test_capture_fps_and_metadata(self):
        directory = self.run_fake(keys=("", "q"))
        summary = json.loads((directory / "summary.json").read_text())
        self.assertGreater(summary["metadata"]["capture_fps"], 0)
        manifest = load_manifest(directory)
        self.assertEqual(manifest["actual_resolution"], [8, 6])
        self.assertEqual(manifest["camera_backend"], "FAKE")
        self.assertEqual(manifest["gesture_set_version"], "pilot-v1")
        self.assertEqual(manifest["pose_model_variant"], "Lite")

    def test_cli_validation_and_defaults(self):
        opts = self.options()
        self.assertTrue(opts.video and opts.mirror_preview)
        self.assertEqual((opts.repetitions, opts.countdown, opts.duration), (5, 3, 3))
        with contextlib.redirect_stderr(io.StringIO()):
            for args in (("--duration", "nan"), ("--repetitions", "0"), ("--countdown", "-1")):
                with self.assertRaises(SystemExit):
                    self.options(*args)

    def test_quit_during_recording_closes_prompt_interval(self):
        directory = self.run_fake(keys=(" ", "q"), args=("--countdown", "0"))
        intervals = [r for r in self.rows(directory) if r["annotation_type"] == "interval"]
        self.assertEqual(len(intervals), 1)
        self.assertEqual(intervals[0]["notes"]["outcome"], "user_quit")
        self.assertEqual(intervals[0]["end_frame_id"], 2)

    def test_done_can_retry_final_trial_without_deleting_history(self):
        recorder, trial, frame = self.controller()
        trial.handle_key("s", frame(0), 0)
        trial.handle_key("s", frame(1), 1)
        self.assertEqual(trial.state, "DONE")
        before = self.rows(recorder.directory)
        trial.handle_key("r", frame(2), 2)
        self.assertEqual(trial.label, "BLOCK")
        self.assertEqual(trial.state, "COUNTDOWN")
        self.assertEqual(self.rows(recorder.directory)[:len(before)], before)

    def test_pose_cleanup_error_still_releases_video_and_finalizes(self):
        pose = FakePose()
        def broken_close():
            raise RuntimeError("close failed")
        pose.close = broken_close
        with self.assertRaisesRegex(RuntimeError, "close failed"):
            self.run_fake(pose=pose)
        directory = next(self.root.iterdir())
        self.assertTrue(self.cap.released and self.cv.windows_closed)
        self.assertTrue(all(w.released for w in self.cv.writers))
        self.assertEqual(json.loads((directory / "summary.json").read_text())["status"], "incomplete")

    def test_mapping_rejects_duplicate_frame(self):
        recorder = SessionRecorder(self.root, session_id="mapping")
        recorder.enable_video_mapping()
        row = recorder.record_frame(capture_return_monotonic_ns=1, pose_success=True)
        recorder.record_video_frame(row, source_rgb_sha256="synthetic")
        with self.assertRaises(ValueError):
            recorder.record_video_frame(row, source_rgb_sha256="synthetic")

    def test_shared_camera_windows_fallback(self):
        rejected = SimpleNamespace(isOpened=lambda: False, release=lambda: None)
        accepted = FakeCamera([])
        calls = []
        def capture(*args):
            calls.append(args)
            return rejected if len(calls) == 1 else accepted
        cv = SimpleNamespace(VideoCapture=capture, CAP_DSHOW=700)
        with patch("pose_capture.platform.system", return_value="Windows"):
            self.assertIs(open_camera(cv, 2), accepted)
        self.assertEqual(calls, [(2, 700), (2,)])

    def test_shared_pose_factory_retains_image_options(self):
        python = SimpleNamespace(BaseOptions=lambda **kw: kw)
        vision = SimpleNamespace(PoseLandmarkerOptions=lambda **kw: kw,
                                 RunningMode=SimpleNamespace(IMAGE="IMAGE"),
                                 PoseLandmarker=SimpleNamespace(create_from_options=lambda options: options))
        result = create_pose_landmarker(python, vision, Path("lite.task"))
        self.assertEqual(result, {"base_options": {"model_asset_path": "lite.task"}, "running_mode": "IMAGE", "num_poses": 1})

    def test_default_fps_is_auto_and_timestamp_estimate(self):
        self.assertIsNone(self.options().video_fps)
        self.assertAlmostEqual(effective_fps([0, 66_666_667, 133_333_334]), 15, places=5)

    def test_fps_estimate_is_robust_to_long_interval(self):
        self.assertAlmostEqual(effective_fps([0, 100_000_000, 200_000_000, 900_000_000, 1_000_000_000]), 10)
        self.assertIsNone(effective_fps([1, 1]))

    def test_auto_warmup_blocks_start_and_uses_measured_fps(self):
        keys = (" ",) + ("",) * 20 + (" ", "q")
        directory = self.run_fake(keys=keys, auto=True)
        video = load_manifest(directory)["video"]
        self.assertIsNone(video["requested_fps"])
        self.assertEqual(video["fps_decision_method"], "warmup_median_capture_interval")
        self.assertAlmostEqual(video["container_fps"], video["measured_effective_capture_fps"])
        mapping = self.rows(directory, "video_frames.jsonl")
        self.assertEqual([row["frame_id"] for row in mapping], [23])
        self.assertEqual(len(list(iter_frames(directory))), 23)

    def test_explicit_fps_override_is_recorded(self):
        directory = self.run_fake(args=("--video-fps", "12.5"))
        video = load_manifest(directory)["video"]
        self.assertEqual(video["requested_fps"], 12.5)
        self.assertEqual(video["container_fps"], 12.5)
        self.assertEqual(video["fps_decision_method"], "explicit_override")

    def test_ready_only_creates_no_writer_or_encoded_frames(self):
        directory = self.run_fake(keys=("", "", "q"))
        self.assertEqual(self.cv.writers, [])
        self.assertEqual(self.rows(directory, "video_frames.jsonl"), [])
        self.assertEqual(len(list(iter_frames(directory))), 3)

    def test_countdown_and_recording_mapping_states(self):
        directory = self.run_fake(keys=(" ", "", "", "", "q"), args=("--countdown", "1", "--duration", "10"))
        mappings = self.rows(directory, "video_frames.jsonl")
        states = [row["trial_state"] for row in mappings]
        self.assertIn("COUNTDOWN", states)
        self.assertIn("RECORDING", states)
        self.assertNotIn("READY", states)
        self.assertTrue(all(row["trial_id"] == "trial-0001" and row["label"] == "NONE" for row in mappings))

    def test_retry_skip_ready_gaps_preserve_video_identity(self):
        directory = self.run_fake(keys=(" ", "r", "s", "", " ", "q"), args=("--countdown", "10"))
        mapping = self.rows(directory, "video_frames.jsonl")
        self.assertEqual([r["frame_id"] for r in mapping], [2, 3, 6])
        self.assertEqual([r["capture_attempt_id"] for r in mapping], [2, 3, 6])
        self.assertEqual([r["video_frame_index"] for r in mapping], [0, 1, 2])
        self.assertEqual([r["trial_id"] for r in mapping], ["trial-0001", "trial-0002", "trial-0003"])

    def test_timing_verification_excludes_between_trial_gaps(self):
        recorder = SessionRecorder(self.root, session_id="timing")
        cv = FakeCV([])
        video = PilotVideo(cv, recorder, [8, 6], 10, measured_fps=10)
        stamps = [0, 100_000_000, 200_000_000, 300_000_000,
                  50_000_000_000, 50_100_000_000, 50_200_000_000, 50_300_000_000]
        for i, stamp in enumerate(stamps):
            row = recorder.record_frame(capture_return_monotonic_ns=stamp, pose_success=True)
            video.write(self.frame, cv.cvtColor(self.frame, 1), row,
                        trial_id="a" if i < 4 else "b", trial_state="RECORDING", label="NONE")
        video.close()
        metadata = load_manifest(recorder.directory)["video"]
        self.assertTrue(metadata["timing_verified"])
        self.assertEqual(metadata["interval_count"], 6)
        self.assertEqual(metadata["relative_difference"], 0)
        self.assertTrue(metadata["lossless_verified"])

    def test_timing_sanity_flags_double_speed_and_insufficient_samples(self):
        metadata = video_timing([66_666_667] * 6, 30)
        self.assertFalse(metadata["timing_verified"])
        self.assertAlmostEqual(metadata["relative_difference"], 1, places=5)
        self.assertIsNone(video_timing([66_666_667], 15)["timing_verified"])
        self.assertIsNone(video_timing([66_666_667] * 6, None)["timing_verified"])

    def test_skeleton_coordinates_respect_preview_mirror_and_do_not_mutate_source(self):
        cv = FakeCV([])
        source = self.frame.copy()
        points = [SimpleNamespace(x=.25, y=.4), SimpleNamespace(x=float("nan"), y=.4)]
        for mirror in (False, True):
            display = self.frame.copy()
            draw_skeleton(cv, display, points, mirror)
            self.assertEqual(cv.drawn_points[-1], (5 if mirror else 2, 2))
        np.testing.assert_array_equal(self.frame, source)

    def test_stage_timing_summary_has_pose_encode_interval_and_read(self):
        directory = self.run_fake()
        summary = json.loads((directory / "summary.json").read_text())
        stages = summary["metadata"]["stage_timing_ms"]
        for name in ("pose_processing", "video_write_encode", "video_enqueue",
                     "video_worker_processing", "frame_interval", "camera_read"):
            self.assertGreater(stages[name]["count"], 0)
            self.assertGreaterEqual(stages[name]["p95"], stages[name]["p50"])


if __name__ == "__main__":
    unittest.main()
