"""Deterministic worker contracts: events control I/O, never timing sleeps."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from collect_gesture_pilot import AsyncPilotVideo, PilotVideo, parse_args, run_collector
from session_recording import SessionRecorder
from test_gesture_pilot import FakeCV, FakeCamera, FakePose, FakeWriter


class AsyncVideoTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.recorder = SessionRecorder(self.temp.name, session_id='async', fsync_every_n_records=1)
        self.cv = FakeCV([])
        self.frame = np.arange(144, dtype=np.uint8).reshape(6, 8, 3)
        self.entered = threading.Event()
        self.release = threading.Event()
        self.processed = []
        self.video = PilotVideo(self.cv, self.recorder, (8, 6), 30)
        original = self.video.writer.write
        def slow(frame):
            self.entered.set()
            if not self.release.wait(5):
                raise AssertionError('test did not release writer')
            original(frame)
            self.processed.append(frame.copy())
        self.video.writer.write = slow
        self.worker = AsyncPilotVideo(self.video, 2)
        self.addCleanup(self.cleanup_worker)

    def cleanup_worker(self):
        self.release.set()
        try:
            self.worker.close()
        except RuntimeError:
            pass

    def enqueue(self, number):
        row = self.recorder.record_frame(capture_return_monotonic_ns=number * 33_333_333, pose_success=True)
        self.worker.write(self.frame, self.frame[:, :, ::-1], row,
                          trial_id=f'trial-{number}', trial_state='COUNTDOWN', label='NONE')
        return row

    def test_slow_writer_does_not_block_enqueue_and_owns_source(self):
        self.enqueue(1)
        self.assertTrue(self.entered.wait(5))
        self.enqueue(2)  # returns while the first encode is still held by Event
        self.assertFalse(self.release.is_set())
        self.assertEqual(self.processed, [])
        source = self.frame.copy()
        self.frame[:] = 0
        self.release.set()
        self.worker.close()
        for frame in self.processed:
            np.testing.assert_array_equal(frame, source)

    def test_fifo_mapping_and_close_drains_before_finalize(self):
        self.enqueue(1)
        self.assertTrue(self.entered.wait(5))
        self.recorder.record_capture_failure(capture_return_monotonic_ns=40_000_000)
        self.enqueue(2)
        self.enqueue(3)
        close_started = threading.Event()
        close_done = threading.Event()
        errors = []
        def close():
            close_started.set()
            try:
                self.worker.close()
            except BaseException as exc:
                errors.append(exc)
            finally:
                close_done.set()
        closer = threading.Thread(target=close)
        closer.start()
        try:
            self.assertTrue(close_started.wait(5))
            self.assertFalse(close_done.is_set())
        finally:
            self.release.set()
            closer.join(5)
        self.assertFalse(closer.is_alive())
        self.assertEqual(errors, [])
        self.recorder.finalize()
        rows = [json.loads(line) for line in (self.recorder.directory / 'video_frames.jsonl').read_text().splitlines()]
        self.assertEqual([r['frame_id'] for r in rows], [1, 2, 3])
        self.assertEqual([r['capture_attempt_id'] for r in rows], [1, 3, 4])
        self.assertEqual([r['video_frame_index'] for r in rows], [0, 1, 2])
        self.assertEqual([r['trial_id'] for r in rows], ['trial-1', 'trial-2', 'trial-3'])
        self.assertTrue(all(r['trial_state'] == 'COUNTDOWN' and r['label'] == 'NONE' for r in rows))
        self.assertTrue(self.video.metadata['lossless_verified'])
        self.assertEqual(self.worker.diagnostics()['video_queue_depth_max'], 2)

    def test_saturation_is_explicit_and_accepted_jobs_drain(self):
        self.enqueue(1)
        self.assertTrue(self.entered.wait(5))
        self.enqueue(2)
        self.enqueue(3)
        with self.assertRaisesRegex(RuntimeError, 'saturated at frame 4'):
            self.enqueue(4)
        self.assertEqual(self.worker.queue_full_count, 1)
        self.assertEqual(self.worker.failed_job['capture_attempt_id'], 4)
        self.release.set()
        self.worker.close()
        self.assertEqual(self.video.count, 3)

    def test_worker_exception_propagates_on_enqueue_and_close(self):
        def broken(frame):
            raise RuntimeError('encoder broke')
        self.video.writer.write = broken
        self.enqueue(1)
        self.worker.thread.join(5)
        self.assertFalse(self.worker.thread.is_alive())
        with self.assertRaisesRegex(RuntimeError, 'encoder broke'):
            self.enqueue(2)
        with self.assertRaisesRegex(RuntimeError, 'encoder broke'):
            self.worker.close()
        self.assertEqual(self.worker.failed_job['frame_id'], 1)
        self.assertEqual(self.video.count, 0)
        self.assertTrue(self.cv.writers[0].released)

    def test_collector_captures_next_frame_while_writer_blocked(self):
        entered, release = threading.Event(), threading.Event()
        cv = FakeCV([' ', '', 'q'])
        cap = FakeCamera([self.frame] * 3)
        read = cap.read
        count = 0
        def next_read():
            nonlocal count
            count += 1
            if count == 3:
                self.assertTrue(entered.wait(5))
                release.set()  # main loop reached its NEXT capture before encode finishes
            return read()
        cap.read = next_read
        original = FakeWriter.write
        def slow(writer, frame):
            entered.set()
            if not release.wait(5):
                raise AssertionError('capture was blocked by encoding')
            original(writer, frame)
        opts = parse_args(['--subject', 'fake', '--output', self.temp.name, '--video-fps', '30'])
        with patch.object(FakeWriter, 'write', slow), contextlib.redirect_stdout(io.StringIO()):
            directory = run_collector(opts, cv=cv, pose_factory=FakePose,
                                      image_factory=lambda x: x, camera_factory=lambda _: cap)
        self.assertEqual(count, 3)
        self.assertEqual(json.loads((directory / 'summary.json').read_text())['status'], 'completed')

    def test_collector_worker_failure_never_completes(self):
        cv = FakeCV([' ', 'q'])
        cap = FakeCamera([self.frame] * 2)
        opts = parse_args(['--subject', 'failure', '--output', self.temp.name, '--video-fps', '30'])
        with patch.object(FakeWriter, 'write', side_effect=RuntimeError('encode failed')):
            with contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(RuntimeError, 'encode failed'):
                run_collector(opts, cv=cv, pose_factory=FakePose, image_factory=lambda x: x,
                              camera_factory=lambda _: cap)
        directory = next(p for p in Path(self.temp.name).iterdir() if p.name != 'async')
        summary = json.loads((directory / 'summary.json').read_text())
        self.assertEqual(summary['status'], 'incomplete')
        self.assertIn('encode failed', summary['metadata']['video_worker']['video_worker_error'])

    def test_collector_saturation_records_diagnostic_and_drains(self):
        entered, release = threading.Event(), threading.Event()
        cv = FakeCV([' ', '', '', 'q'])
        cap = FakeCamera([self.frame] * 4)
        read, release_camera = cap.read, cap.release
        count = 0
        def next_read():
            nonlocal count
            count += 1
            if count == 3:
                self.assertTrue(entered.wait(5))
            return read()
        def close_camera():
            release.set()
            release_camera()
        cap.read, cap.release = next_read, close_camera
        original = FakeWriter.write
        def slow(writer, frame):
            entered.set()
            if not release.wait(5):
                raise AssertionError('cleanup failed to release writer')
            original(writer, frame)
        opts = parse_args(['--subject', 'full', '--output', self.temp.name,
                           '--video-fps', '30', '--video-queue-size', '1'])
        with patch.object(FakeWriter, 'write', slow), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, 'saturated'):
                run_collector(opts, cv=cv, pose_factory=FakePose, image_factory=lambda x: x,
                              camera_factory=lambda _: cap)
        directory = next(p for p in Path(self.temp.name).iterdir() if p.name != 'async')
        summary = json.loads((directory / 'summary.json').read_text())
        self.assertEqual(summary['status'], 'incomplete')
        diagnostic = summary['metadata']['video_worker']
        self.assertEqual(diagnostic['video_queue_full_count'], 1)
        self.assertEqual(diagnostic['video_failed_job']['frame_id'], 4)
        self.assertEqual(diagnostic['video_pending_count'], 0)
        self.assertEqual(len((directory / 'video_frames.jsonl').read_text().splitlines()), 2)

    def test_explicit_writer_failure_has_no_mapping(self):
        self.video.writer.write = lambda frame: False
        self.enqueue(1)
        with self.assertRaisesRegex(RuntimeError, 'failed to write'):
            self.worker.close()
        self.assertEqual((self.recorder.directory / 'video_frames.jsonl').read_text(), '')


if __name__ == '__main__':
    unittest.main()
