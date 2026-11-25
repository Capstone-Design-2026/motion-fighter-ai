"""Synthetic recorder/replay contracts; no camera or vision dependencies."""

import hashlib
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from session_recording import SessionRecorder, load_manifest
from replay_session import iter_frames, iter_timed_frames


class SessionRecordingTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.recorder = SessionRecorder(self.root, session_id="synthetic", subject_id="subject", take_id="take")

    def frame(self, **options):
        options.setdefault("capture_return_monotonic_ns", 1_000_000_000)
        options.setdefault("pose_success", True)
        return self.recorder.record_frame(**options)

    def read(self, filename):
        return json.loads((self.recorder.directory / filename).read_text())

    def test_manifest_round_trip_and_unknowns(self):
        manifest = load_manifest(self.recorder.directory)
        self.assertEqual(manifest, self.recorder.manifest)
        self.assertEqual(manifest["schema_version"], 1)
        self.assertEqual(manifest["subject_id"], "subject")
        for field in ("camera_backend", "actual_resolution", "code_commit_sha", "pose_model_variant"):
            self.assertIsNone(manifest[field])
        self.assertEqual(self.read("summary.json")["status"], "incomplete")

    def test_model_hash_and_supplied_metadata(self):
        model = self.root / "synthetic.task"
        model.write_bytes(b"synthetic-model")
        recorder = SessionRecorder(self.root, session_id="metadata", pose_model_path=model,
                                   camera_index=0, requested_resolution=[640, 480],
                                   recording_config={"optional": float("nan")})
        self.assertEqual(recorder.manifest["pose_model_sha256"], hashlib.sha256(model.read_bytes()).hexdigest())
        self.assertIsNone(recorder.manifest["recording_config"]["optional"])
        self.assertEqual(recorder.manifest["requested_resolution"], [640, 480])

    def test_normal_pose_preserves_indices_and_attributes(self):
        points = [SimpleNamespace(x=1, y=2, z=3, visibility=.8, presence=.9),
                  SimpleNamespace(x=4, y=5, z=6, visibility=.7, presence=.6)]
        row = self.frame(image_landmarks=points, world_landmarks=points,
                         neutral_baseline=.2, calibration_mad=.01, raw_rotation_score=.5,
                         corrected_rotation_score=.3, raw_movement="RIGHT", final_movement="RIGHT")
        saved = list(iter_frames(self.recorder.directory))[0]
        self.assertEqual(saved, row)
        self.assertEqual(saved["image_landmarks"], [[1, 2, 3], [4, 5, 6]])
        self.assertEqual(saved["landmark_presence"]["world"], [.9, .6])
        self.assertTrue(saved["pose_validity"]["valid"])

    def test_missing_pose_uses_null_not_zero(self):
        self.frame()
        row = list(iter_frames(self.recorder.directory))[0]
        self.assertIsNone(row["image_landmarks"])
        self.assertIsNone(row["world_landmarks"])
        self.assertIsNone(row["raw_rotation_score"])
        self.assertFalse(row["pose_validity"]["pose_present"])

    def test_nonfinite_sanitization_is_strict_json(self):
        self.frame(image_landmarks=[dict(x=float("nan"), y=float("inf"), z=float("-inf"), visibility=float("nan"))],
                   raw_rotation_score=float("nan"), corrected_rotation_score=float("inf"),
                   processing_metadata={"nested": [float("-inf")]})
        raw = (self.recorder.directory / "frames.jsonl").read_text()
        def reject(value):
            self.fail(f"Nonstandard JSON: {value}")
        row = json.loads(raw, parse_constant=reject)
        self.assertEqual(row["image_landmarks"], [[None, None, None]])
        self.assertEqual(row["pose_validity"]["image_landmarks_valid"], [False])
        self.assertIsNone(row["raw_rotation_score"])
        self.assertFalse(row["pose_validity"]["valid"])
        self.assertFalse(row["score_validity"]["raw_rotation_score"])

    def test_image_only_without_visibility_presence(self):
        row = self.frame(image_landmarks=[SimpleNamespace(x=0, y=1, z=2)])
        self.assertIsNone(row["world_landmarks"])
        self.assertEqual(row["landmark_visibility"]["image"], [None])
        self.assertEqual(row["landmark_presence"]["image"], [None])
        self.assertTrue(row["pose_validity"]["valid"])

    def test_world_only(self):
        row = self.frame(world_landmarks=[dict(x=1, y=2, z=3)])
        self.assertIsNone(row["image_landmarks"])
        self.assertEqual(row["world_landmarks"], [[1, 2, 3]])

    def test_both_missing_and_pose_call_failure(self):
        row = self.frame(pose_success=False)
        self.assertFalse(row["pose_success"])
        self.assertIsNone(row["image_landmarks"])
        self.assertIsNone(row["world_landmarks"])

    def test_capture_failure_does_not_consume_frame_id(self):
        self.frame()
        self.recorder.record_capture_failure(capture_return_monotonic_ns=1_100_000_000, notes="read failed")
        self.frame(capture_return_monotonic_ns=1_200_000_000)
        self.assertEqual([r["frame_id"] for r in iter_frames(self.recorder.directory)], [1, 2])
        diagnostic = self.read("diagnostics.jsonl")
        self.assertEqual(diagnostic["after_frame_id"], 1)
        self.assertFalse(diagnostic["capture_success"])
        self.assertNotIn("frame_id", diagnostic)
        self.assertEqual(self.recorder.finalize()["capture_failure_count"], 1)

    def test_annotations_interval_event_marker(self):
        self.frame()
        self.frame(capture_return_monotonic_ns=2_000_000_000)
        interval = self.recorder.record_annotation(label="gesture", annotation_type="interval", start_frame_id=1, end_frame_id=2)
        event = self.recorder.record_annotation(label="onset", annotation_type="event", event_frame_id=1)
        marker = self.recorder.record_annotation(label="note", annotation_type="marker", event_frame_id=2)
        rows = [json.loads(line) for line in (self.recorder.directory / "annotations.jsonl").read_text().splitlines()]
        self.assertEqual(rows, [interval, event, marker])
        self.assertEqual(rows[0]["subject_id"], "subject")
        self.assertEqual(rows[0]["take_id"], "take")

    def test_invalid_annotation_range_rejected(self):
        self.frame()
        with self.assertRaises(ValueError):
            self.recorder.record_annotation(label="bad", annotation_type="interval", start_frame_id=1, end_frame_id=2)
        with self.assertRaises(ValueError):
            self.recorder.record_annotation(label="bad", annotation_type="event")

    def test_summary_counts_and_processing_percentiles(self):
        self.frame(image_landmarks=[dict(x=1, y=2, z=3)],
                   pose_start_monotonic_ns=1_000_000_000, pose_end_monotonic_ns=1_010_000_000)
        self.frame(capture_return_monotonic_ns=2_000_000_000, pose_success=False,
                   pose_start_monotonic_ns=2_000_000_000, pose_end_monotonic_ns=2_030_000_000)
        summary = self.recorder.finalize()
        self.assertEqual(summary, self.read("summary.json"))
        self.assertEqual(summary["status"], "completed")
        self.assertEqual(summary["captured_frame_count"], 2)
        self.assertEqual(summary["pose_success_count"], 1)
        self.assertEqual(summary["pose_missing_count"], 1)
        self.assertEqual(summary["valid_landmark_count"], 1)
        self.assertEqual(summary["processing_time_ms"]["p50"], 20)
        self.assertEqual(summary["processing_time_ms"]["p95"], 29)
        self.assertIsNotNone(summary["ended_at"])
        self.assertGreaterEqual(summary["duration"], 0)

    def test_partial_session_without_finalize_or_summary(self):
        row = self.frame()
        self.assertEqual(list(iter_frames(self.recorder.directory)), [row])
        (self.recorder.directory / "summary.json").unlink()
        self.assertEqual(list(iter_frames(self.recorder.directory)), [row])

    def test_repeat_replay_preserves_missing_and_timing(self):
        self.frame(image_landmarks=[dict(x=1, y=2, z=3)])
        self.frame(capture_return_monotonic_ns=1_200_000_000)
        self.frame(capture_return_monotonic_ns=1_500_000_000)
        first = list(iter_frames(self.recorder.directory))
        self.assertEqual(first, list(iter_frames(self.recorder.directory)))
        self.assertEqual(len(first), 3)
        self.assertIsNone(first[1]["image_landmarks"])
        timed = list(iter_timed_frames(self.recorder.directory))
        self.assertEqual([(r, g) for r, g, _ in timed], [(0, 0), (.2, .2), (.5, .3)])

    def test_truncated_tail_recovery_and_strict_mode(self):
        row = self.frame()
        with (self.recorder.directory / "frames.jsonl").open("ab") as stream:
            stream.write(b'{"schema_version":1,"frame_id":')
        with self.assertWarns(RuntimeWarning):
            self.assertEqual(list(iter_frames(self.recorder.directory)), [row])
        with self.assertRaises(json.JSONDecodeError):
            list(iter_frames(self.recorder.directory, allow_truncated_tail=False))

    def test_replay_rejects_frame_order_and_schema(self):
        row = self.frame()
        path = self.recorder.directory / "frames.jsonl"
        for field, value in (("frame_id", 2), ("schema_version", 99), ("capture_return_monotonic_ns", -1)):
            bad = dict(row, **{field: value})
            path.write_text(json.dumps(bad) + "\n")
            with self.assertRaises(ValueError):
                list(iter_frames(self.recorder.directory))

    def test_replay_rejects_corrupt_complete_line(self):
        self.frame()
        with (self.recorder.directory / "frames.jsonl").open("a") as stream:
            stream.write('{bad}\n')
        with self.assertRaises(json.JSONDecodeError):
            list(iter_frames(self.recorder.directory))

    def test_rejects_decreasing_timestamps_without_appending(self):
        self.frame()
        with self.assertRaises(ValueError):
            self.frame(capture_return_monotonic_ns=0)
        self.assertEqual(len(list(iter_frames(self.recorder.directory))), 1)

    def test_existing_session_cannot_be_overwritten(self):
        self.frame()
        with self.assertRaises(FileExistsError):
            SessionRecorder(self.root, session_id="synthetic")
        self.assertEqual(len(list(iter_frames(self.recorder.directory))), 1)

    def test_exception_marks_incomplete_and_keeps_frames(self):
        with self.assertRaisesRegex(RuntimeError, "synthetic failure"):
            with self.recorder:
                self.frame()
                raise RuntimeError("synthetic failure")
        self.assertEqual(self.read("summary.json")["status"], "incomplete")
        self.assertEqual(len(list(iter_frames(self.recorder.directory))), 1)

    def test_finalized_recorder_rejects_writes(self):
        self.recorder.finalize()
        with self.assertRaises(RuntimeError):
            self.frame()

    def test_empty_landmark_arrays_are_missing(self):
        row = self.frame(image_landmarks=[], world_landmarks=[])
        self.assertIsNone(row["image_landmarks"])
        self.assertIsNone(row["world_landmarks"])
        self.assertFalse(row["pose_validity"]["valid"])

    def test_replay_rejects_decreasing_timestamp_in_saved_log(self):
        first = self.frame()
        second = dict(first, frame_id=2, capture_return_monotonic_ns=0)
        with (self.recorder.directory / "frames.jsonl").open("a") as stream:
            stream.write(json.dumps(second) + "\n")
        with self.assertRaises(ValueError):
            list(iter_frames(self.recorder.directory))

    def test_manifest_version_validation(self):
        path = self.recorder.directory / "manifest.json"
        manifest = self.read("manifest.json")
        manifest["schema_version"] = 2
        path.write_text(json.dumps(manifest))
        with self.assertRaises(ValueError):
            list(iter_frames(self.recorder.directory))

    def test_mixed_capture_attempt_ids_and_replay(self):
        failure1 = self.recorder.record_capture_failure(capture_return_monotonic_ns=1)
        frame1 = self.frame(capture_return_monotonic_ns=2)
        failure2 = self.recorder.record_capture_failure(capture_return_monotonic_ns=3)
        frame2 = self.frame(capture_return_monotonic_ns=4)
        self.assertEqual([r["capture_attempt_id"] for r in (failure1, frame1, failure2, frame2)], [1, 2, 3, 4])
        self.assertEqual([frame1["frame_id"], frame2["frame_id"]], [1, 2])
        self.assertEqual([failure1["after_frame_id"], failure2["after_frame_id"]], [0, 1])
        replayed = list(iter_frames(self.recorder.directory))
        self.assertEqual(replayed, [frame1, frame2])
        self.assertEqual([r["capture_attempt_id"] for r in replayed], [2, 4])

    def test_default_flushes_immediately_without_per_frame_fsync(self):
        with patch("session_recording.os.fsync") as sync:
            for stamp in range(59):
                self.frame(capture_return_monotonic_ns=stamp)
            sync.assert_not_called()
            self.assertEqual(len(list(iter_frames(self.recorder.directory))), 59)
            self.frame(capture_return_monotonic_ns=59)
            self.assertEqual(sync.call_count, 1)
            self.frame(capture_return_monotonic_ns=60)
            self.assertEqual(sync.call_count, 1)

    def test_finalize_syncs_every_log_before_summary(self):
        self.frame()
        synced_paths = []
        original_open = Path.open
        handles = {}
        def track_open(path, *args, **kwargs):
            stream = original_open(path, *args, **kwargs)
            handles[stream.fileno()] = path.name
            return stream
        with patch.object(Path, "open", track_open), patch("session_recording.os.fsync", side_effect=lambda fd: synced_paths.append(handles[fd])):
            self.recorder.finalize()
        self.assertEqual(synced_paths, ["frames.jsonl", "annotations.jsonl", "diagnostics.jsonl", "summary.tmp"])

    def test_configured_policy_counts_all_jsonl_records(self):
        self.recorder = SessionRecorder(self.root, session_id="policy", fsync_every_n_records=3)
        self.assertEqual(load_manifest(self.recorder.directory)["recording_config"]["fsync_every_n_records"], 3)
        with patch("session_recording.os.fsync") as sync:
            self.frame()
            self.recorder.record_capture_failure(capture_return_monotonic_ns=1_100_000_000)
            sync.assert_not_called()
            self.recorder.record_annotation(label="mark", annotation_type="marker", event_frame_id=1)
            self.assertEqual(sync.call_count, 3)
            self.frame(capture_return_monotonic_ns=1_200_000_000)
            self.assertEqual(sync.call_count, 3)

    def test_fsync_policy_one_and_finalize_only(self):
        for policy, expected in ((1, 2), (None, 0)):
            self.recorder = SessionRecorder(self.root, session_id=f"policy-{policy}", fsync_every_n_records=policy)
            with patch("session_recording.os.fsync") as sync:
                self.frame()
                self.frame()
                self.assertEqual(sync.call_count, expected)
                self.recorder.finalize(completed=False)
                self.assertEqual(sync.call_count, expected + 4)

    def test_invalid_fsync_policy(self):
        for policy in (0, -1, True, 1.5, "60"):
            with self.subTest(policy=policy), self.assertRaises(ValueError):
                SessionRecorder(self.root, fsync_every_n_records=policy)

    def test_legacy_v1_without_attempt_id_is_preserved(self):
        row = self.frame()
        del row["capture_attempt_id"]
        (self.recorder.directory / "frames.jsonl").write_text(json.dumps(row) + "\n")
        self.assertEqual(list(iter_frames(self.recorder.directory)), [row])

    def test_replay_rejects_duplicate_or_invalid_attempt_id(self):
        first = self.frame()
        path = self.recorder.directory / "frames.jsonl"
        for attempt in (1, 0, None, True):
            second = dict(first, frame_id=2, capture_attempt_id=attempt)
            path.write_text(json.dumps(first) + "\n" + json.dumps(second) + "\n")
            with self.subTest(attempt=attempt), self.assertRaises(ValueError):
                list(iter_frames(self.recorder.directory))


if __name__ == "__main__":
    unittest.main()
