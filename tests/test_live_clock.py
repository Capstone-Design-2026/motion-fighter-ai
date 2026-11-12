"""A Windows live session uses one precise epoch, without changing v26 rules."""
import json
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

from realtime_vision import clock
from redbool_demo.camera_diagnostics import CaptureCadence, camera_properties
from redbool_demo.personal import PersonalGestureEngine


class LiveClockTests(unittest.TestCase):
    def test_windows_uses_precise_clock_for_engine_watchdog(self):
        # Give the two clocks unrelated epochs: accidentally mixing them is
        # visible rather than hidden by both clocks being close on this Mac.
        with patch.object(clock.sys, 'platform', 'win32'), \
             patch.object(clock.time, 'perf_counter_ns', return_value=9_000_000_123) as precise, \
             patch.object(clock.time, 'monotonic_ns', return_value=70_000_000):
            engine = PersonalGestureEngine(clock=clock.now_ns)
            state = engine.tick()
            self.assertEqual(state.timestamp_ns, 9_000_000_123)
            self.assertEqual(engine.last_now, 9_000_000_123)
            self.assertEqual(clock.now_ns(), 9_000_000_123)
            self.assertTrue(precise.called)

    def test_mac_retains_existing_monotonic_epoch(self):
        with patch.object(clock.sys, 'platform', 'darwin'), \
             patch.object(clock.time, 'monotonic_ns', return_value=123456789), \
             patch.object(clock.time, 'perf_counter_ns', side_effect=AssertionError('wrong clock')):
            self.assertEqual(clock.now_ns(), 123456789)

    def test_clock_metadata_names_actual_source(self):
        info = SimpleNamespace(implementation='QueryPerformanceCounter()', resolution=1e-7,
                               monotonic=True, adjustable=False)
        with patch.object(clock.sys, 'platform', 'win32'), \
             patch.object(clock.time, 'get_clock_info', return_value=info) as get:
            meta = clock.metadata()
        get.assert_called_once_with('perf_counter')
        self.assertEqual(meta['name'], 'perf_counter_ns')
        self.assertEqual(meta['resolution_ns'], 100)
        json.dumps(meta, allow_nan=False)


class CaptureDiagnosticsTests(unittest.TestCase):
    def test_no_frames_reports_unknown_timing_not_zero_performance(self):
        summary = CaptureCadence().summary()
        self.assertEqual(summary['captured'], 0)
        for key in ('measured_fps', 'first_capture_ns', 'last_capture_ns',
                    'max_capture_gap_ms', 'max_read_ms', 'mean_read_ms'):
            self.assertIsNone(summary[key], key)
        json.dumps(summary, allow_nan=False)

    def test_one_frame_reports_read_duration_but_unknown_fps_and_gap(self):
        cadence = CaptureCadence()
        cadence.observe(7_000_000_000, 6_958_000_000)
        summary = cadence.summary()
        self.assertEqual(summary['captured'], 1)
        self.assertIsNone(summary['measured_fps'])
        self.assertIsNone(summary['max_capture_gap_ms'])
        self.assertEqual(summary['first_capture_ns'], 7_000_000_000)
        self.assertEqual(summary['last_capture_ns'], 7_000_000_000)
        self.assertEqual(summary['max_read_ms'], 42.)
        self.assertEqual(summary['mean_read_ms'], 42.)

    def test_measures_raw_twenty_fps_even_when_consumer_skips(self):
        cadence = CaptureCadence()
        windows = []
        for frame in range(201):
            stamp = 7_000_000_000 + frame * 50_000_000
            row = cadence.observe(stamp, stamp - 42_000_000)
            # There is deliberately no inference/preview count in this path.
            if row: windows.append(row)
        self.assertEqual(len(windows), 2)
        self.assertEqual([r['measured_fps'] for r in windows], [20., 20.])
        summary = cadence.summary()
        self.assertEqual(summary['captured'], 201)
        self.assertEqual(summary['measured_fps'], 20.)
        self.assertEqual(summary['max_capture_gap_ms'], 50.)
        self.assertEqual(summary['mean_read_ms'], 42.)

    def test_missing_camera_properties_cannot_fail_capture(self):
        cv = SimpleNamespace(CAP_PROP_FPS=1, CAP_PROP_EXPOSURE=2)
        cap = Mock()
        cap.getBackendName.side_effect = RuntimeError('unsupported')
        cap.get.side_effect = [float('nan'), RuntimeError('unsupported')]
        values = camera_properties(cv, cap)
        self.assertIsNone(values['backend'])
        self.assertIsNone(values['reported_fps'])
        self.assertIsNone(values['exposure'])
        cap.set.assert_not_called()
        json.dumps(values, allow_nan=False)

    def test_driver_properties_are_read_without_forcing_exposure(self):
        cv = SimpleNamespace(CAP_PROP_FPS=1, CAP_PROP_EXPOSURE=2, CAP_PROP_AUTO_EXPOSURE=3)
        cap = Mock()
        cap.getBackendName.return_value = 'DSHOW'
        cap.get.side_effect = lambda prop: {1:30., 2:-5., 3:1.}[prop]
        values = camera_properties(cv, cap)
        self.assertEqual(values['backend'], 'DSHOW')
        self.assertEqual(values['reported_fps'], 30.)
        self.assertEqual(values['exposure'], -5.)
        self.assertEqual(values['auto_exposure'], 1.)
        cap.set.assert_not_called()


if __name__ == '__main__': unittest.main()
