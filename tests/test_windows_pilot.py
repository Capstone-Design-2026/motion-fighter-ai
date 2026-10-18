"""Windows contracts without opening a real camera or requiring Windows."""

import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
import config
import collect_gesture_pilot as collector
from pose_capture import open_camera, require_pose_model
from package_pilot_session import TRANSFER_FILES, package_session
from session_recording import SessionRecorder
from test_gesture_pilot import FakeCV, FakeCamera, FakePose


class CameraBackendTest(unittest.TestCase):
    def cv(self, results):
        return SimpleNamespace(CAP_DSHOW=700, CAP_MSMF=1400, CAP_AVFOUNDATION=1200,
                               VideoCapture=Mock(side_effect=results))

    def camera(self, opened):
        return Mock(isOpened=Mock(return_value=opened))

    def test_windows_auto_falls_back_to_msmf(self):
        rejected, accepted = self.camera(False), self.camera(True)
        cv = self.cv([rejected, accepted])
        with patch("pose_capture.platform.system", return_value="Windows"):
            self.assertIs(open_camera(cv, 0), accepted)
        self.assertEqual([c.args for c in cv.VideoCapture.call_args_list], [(0, 700), (0, 1400)])
        rejected.release.assert_called_once()
        accepted.release.assert_not_called()

    def test_windows_first_success_stops(self):
        accepted = self.camera(True)
        cv = self.cv([accepted])
        with patch("pose_capture.platform.system", return_value="Windows"):
            self.assertIs(open_camera(cv, 0), accepted)
        cv.VideoCapture.assert_called_once_with(0, 700)

    def test_windows_exception_then_default_fallback(self):
        rejected, accepted = self.camera(False), self.camera(True)
        cv = self.cv([RuntimeError("driver error"), rejected, accepted])
        with patch("pose_capture.platform.system", return_value="Windows"):
            self.assertIs(open_camera(cv, 2), accepted)
        self.assertEqual([c.args for c in cv.VideoCapture.call_args_list], [(2, 700), (2, 1400), (2,)])
        rejected.release.assert_called_once()

    def test_windows_all_fail_explains_permissions_and_attempts(self):
        caps = [self.camera(False) for _ in range(3)]
        with patch("pose_capture.platform.system", return_value="Windows"):
            with self.assertRaisesRegex(RuntimeError, "desktop app camera access") as error:
                open_camera(self.cv(caps), 0)
        self.assertIn("msmf: not opened", str(error.exception))
        self.assertIn("dshow: not opened", str(error.exception))
        for cap in caps:
            cap.release.assert_called_once()

    def test_explicit_backend_does_not_silently_fallback(self):
        cv = self.cv([self.camera(False)])
        with patch("pose_capture.platform.system", return_value="Windows"):
            with self.assertRaises(RuntimeError):
                open_camera(cv, 0, "msmf")
        cv.VideoCapture.assert_called_once_with(0, 1400)

    def test_macos_and_linux_preserve_default_call(self):
        for system in ("Darwin", "Linux"):
            with self.subTest(system=system):
                cv = self.cv([self.camera(True)])
                with patch("pose_capture.platform.system", return_value=system):
                    open_camera(cv, 0)
                cv.VideoCapture.assert_called_once_with(0)

    def test_explicit_avfoundation_and_wrong_platform(self):
        cv = self.cv([self.camera(True)])
        with patch("pose_capture.platform.system", return_value="Darwin"):
            open_camera(cv, 0, "avfoundation")
            with self.assertRaisesRegex(ValueError, "not supported"):
                open_camera(cv, 0, "dshow")
        cv.VideoCapture.assert_called_once_with(0, 1200)

    def test_macos_failure_permission_message(self):
        with patch("pose_capture.platform.system", return_value="Darwin"):
            with self.assertRaisesRegex(RuntimeError, "Privacy & Security"):
                open_camera(self.cv([self.camera(False)]), 0)

    def test_calibration_keeps_no_argument_entrypoint(self):
        import calibrate_movement
        with patch("calibrate_movement.open_pose_camera", return_value="camera") as opened:
            self.assertEqual(calibrate_movement.open_camera(), "camera")
        opened.assert_called_once_with(calibrate_movement.cv2, config.CAMERA_INDEX)


class WindowsPilotTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "경로 with spaces"
        self.root.mkdir()

    def test_model_path_is_independent_of_cwd(self):
        previous = Path.cwd()
        try:
            os.chdir(self.root)
            self.assertEqual(config.POSE_MODEL_PATH, Path(config.__file__).resolve().parents[1] / "pose_landmarker.task")
            with patch("pose_capture.Path.is_file", return_value=True):
                self.assertEqual(require_pose_model(), config.POSE_MODEL_PATH)
        finally:
            os.chdir(previous)

    def test_missing_model_stops_before_camera_and_imports(self):
        with patch.object(collector, "POSE_MODEL_PATH", self.root / "missing.task"):
            with patch.object(collector, "run_collector") as run:
                with self.assertRaisesRegex(FileNotFoundError, "official Lite download"):
                    collector.main(["--subject", "P02"])
        run.assert_not_called()

    def test_model_hash_preserved_for_unicode_path(self):
        path = self.root / "pose_landmarker.task"
        path.write_bytes(b"synthetic model")
        recorder = SessionRecorder(self.root, pose_model_path=require_pose_model(path))
        self.assertEqual(recorder.manifest["pose_model_sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
        recorder.finalize()

    def test_collector_passes_backend_and_keeps_uppercase_keys(self):
        opts = collector.parse_args(["--subject", "P02", "--output", str(self.root),
                                     "--camera-backend", "msmf", "--no-video"])
        cv = FakeCV([" ", "R", "S", "N", "Q"])
        cap = FakeCamera([np.zeros((6, 8, 3), dtype=np.uint8)] * 5)
        with patch.object(collector, "open_camera", return_value=cap) as opened:
            with contextlib.redirect_stdout(io.StringIO()):
                directory = collector.run_collector(opts, cv=cv, pose_factory=FakePose, image_factory=lambda x: x)
        opened.assert_called_once_with(cv, 0, "msmf")
        summary = json.loads((directory / "summary.json").read_text())
        self.assertEqual(summary["metadata"]["exit_reason"], "user_quit")
        self.assertEqual([t["outcome"] for t in summary["metadata"]["trials"]], ["superseded", "skipped", "skipped"])

    def test_windows_codec_auto_fallback_and_roundtrip(self):
        for failures, expected in (((), "ffv1"), (("FFV1",), "hfyu"), (("FFV1", "HFYU"), "png")):
            with self.subTest(expected=expected):
                cv = FakeCV([], reject_codecs=failures)
                cv.CAP_FFMPEG = 1900
                writer_factory = cv.VideoWriter
                calls = []
                def writer(*args):
                    calls.append(args)
                    return writer_factory(args[0], *args[2:]) if len(args) == 5 else writer_factory(*args)
                cv.VideoWriter = writer
                recorder = SessionRecorder(self.root)
                with patch("collect_gesture_pilot.platform.system", return_value="Windows"):
                    video = collector.PilotVideo(cv, recorder, (8, 6), 30)
                self.assertEqual(calls[0][1:3], (1900, "FFV1"))
                frame = np.arange(144, dtype=np.uint8).reshape(6, 8, 3)
                for i in range(7):
                    row = recorder.record_frame(capture_return_monotonic_ns=i * 33_333_333, pose_success=True)
                    video.write(frame, cv.cvtColor(frame, 1), row, trial_id="trial-1", trial_state="RECORDING", label="NONE")
                video.close()
                recorder.finalize()
                self.assertEqual(video.metadata["codec"], expected)
                self.assertTrue(video.metadata["lossless_verified"])
                self.assertTrue(video.metadata["timing_verified"])
                self.assertTrue(all(w.released for w in cv.writers))

    def test_windows_ffmpeg_exception_tries_default_same_codec(self):
        cv = FakeCV([])
        cv.CAP_FFMPEG = 1900
        factory = cv.VideoWriter
        def writer(*args):
            if len(args) == 5:
                raise RuntimeError("FFmpeg plugin unavailable")
            return factory(*args)
        cv.VideoWriter = writer
        recorder = SessionRecorder(self.root)
        with patch("collect_gesture_pilot.platform.system", return_value="Windows"):
            video = collector.PilotVideo(cv, recorder, (8, 6), 30)
        self.assertEqual(video.metadata["codec"], "ffv1")
        self.assertEqual(video.metadata["writer_api_requested"], "default")
        self.assertIn("FFmpeg plugin unavailable", video.metadata["writer_open_failures"][0])
        video.close()
        recorder.finalize()

    def test_all_lossless_codecs_fail_without_lossy_fallback(self):
        cv = FakeCV([], reject_codecs=("FFV1", "HFYU", "png "))
        recorder = SessionRecorder(self.root)
        with patch("collect_gesture_pilot.platform.system", return_value="Windows"):
            with self.assertRaisesRegex(RuntimeError, "requirements-pilot-windows.txt"):
                collector.PilotVideo(cv, recorder, (8, 6), 30)
        self.assertEqual(len(cv.writers), 3)
        self.assertTrue(all(w.released for w in cv.writers))
        recorder.finalize(completed=False)

    def session(self, subject="P02"):
        recorder = SessionRecorder(self.root, subject_id=subject)
        recorder.enable_video_mapping()
        recorder.finalize()
        return recorder.directory

    def test_zip_allowlist_excludes_video_and_paths(self):
        directory = self.session()
        for name in ("video.avi", "video.mov", "video.mp4", "private-name.txt"):
            (directory / name).write_bytes(b"excluded")
        destination = package_session(directory)
        self.assertEqual(destination.name, f"P02_{directory.name}_metadata.zip")
        with zipfile.ZipFile(destination) as archive:
            self.assertEqual(archive.namelist(), list(TRANSFER_FILES))
            for name in TRANSFER_FILES:
                self.assertEqual(archive.read(name), (directory / name).read_bytes())
        self.assertTrue((directory / "video.avi").is_file())
        with self.assertRaises(FileExistsError):
            package_session(directory)

    def test_zip_missing_log_fails_before_creating_archive(self):
        directory = self.session()
        (directory / "frames.jsonl").unlink()
        with self.assertRaisesRegex(ValueError, "frames.jsonl"):
            package_session(directory)
        self.assertEqual(list(self.root.glob("*.zip")), [])

    def test_zip_rejects_personal_subject_name(self):
        with self.assertRaisesRegex(ValueError, "anonymous"):
            package_session(self.session("Participant Name"))


if __name__ == "__main__":
    unittest.main()
