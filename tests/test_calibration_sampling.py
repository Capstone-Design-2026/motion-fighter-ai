"""Slow basic calibration must not relax live attacks or accept invalid guards."""
from dataclasses import asdict, replace
import unittest

import numpy as np

from action_engine.experimental import ChannelSignals
from redbool_demo.six_engine import SixGestureEngine
from test_chest_special import pose


class Fixture:
    def __init__(self, engine=None):
        self.engine = engine or SixGestureEngine()
        self.engine.run_id = 'calibration-sampling'
        self.engine.request_calibration()
        self.frame = 0
        self.stamp = 1_000_000_000
        self.events = []

    def step(self, *, gap_ns=100_000_000, age_ns=0, kind='guard', transform=None):
        self.frame += 1
        self.stamp += gap_ns
        observation = pose(kind, self.frame, self.stamp)
        if transform is not None:
            observation = transform(observation)
        state = self.engine.process(observation, now_ns=self.stamp + age_ns)
        self.events.extend(self.engine.drain_events(self.stamp + age_ns))
        self.engine.take_logs()
        return state


class LegacyCalibration(SixGestureEngine):
    """Old calibration sampling bound for the normal-cadence comparison only."""
    def _components(self):
        super()._components()
        self.calibration_signals = ChannelSignals(self.config)


class CalibrationSamplingTests(unittest.TestCase):
    def test_three_six_eight_and_jittery_ten_fps_keep_twelve_sample_guard_requirement(self):
        for gaps in ((333_333_333,), (350_000_000,), (166_666_667,),
                     (125_000_000,), (93_000_000, 107_000_000)):
            with self.subTest(gaps=gaps):
                fixture = Fixture()
                # First observation has no derivative; the next eleven are
                # still too few even though more than 600ms has elapsed.
                for i in range(12):
                    fixture.step(gap_ns=gaps[i % len(gaps)])
                engine = fixture.engine
                self.assertFalse(engine.calibrated)
                self.assertEqual(len(engine.samples), 11)
                self.assertLess(engine.calibration_progress, 1.)
                self.assertFalse(fixture.events)
                fixture.step(gap_ns=gaps[12 % len(gaps)])
                self.assertTrue(engine.calibrated)
                self.assertEqual(len(engine.samples), 12)
                self.assertEqual(engine.calibration_progress, 1.)
                self.assertGreaterEqual(engine.samples[-1][0] - engine.samples[0][0],
                                        600_000_000)
                self.assertFalse(fixture.events)

    def test_more_than_three_hundred_fifty_ms_requires_a_whole_new_guard_window(self):
        fixture = Fixture()
        for _ in range(10):
            fixture.step(gap_ns=125_000_000)
        self.assertEqual(len(fixture.engine.samples), 9)
        fixture.step(gap_ns=350_000_001)
        self.assertFalse(fixture.engine.calibrated)
        self.assertEqual(len(fixture.engine.samples), 0)
        self.assertEqual(fixture.engine.calibration_progress, 0.)
        self.assertEqual(fixture.engine.calibration_feedback.reason, 'JOINT_HISTORY')
        for _ in range(11):
            fixture.step(gap_ns=125_000_000)
        self.assertFalse(fixture.engine.calibrated)
        fixture.step(gap_ns=125_000_000)
        self.assertTrue(fixture.engine.calibrated)
        self.assertFalse(fixture.events)

    def test_older_than_one_hundred_twenty_ms_still_resets_calibration(self):
        fixture = Fixture()
        for _ in range(10):
            fixture.step(gap_ns=125_000_000)
        fixture.step(gap_ns=125_000_000, age_ns=120_000_001)
        self.assertFalse(fixture.engine.calibrated)
        self.assertEqual(len(fixture.engine.samples), 0)
        self.assertEqual(fixture.engine.calibration_progress, 0.)
        self.assertEqual(fixture.engine.calibration_feedback.reason, 'STALE_FRAME')
        self.assertFalse(fixture.events)

    def test_slow_samples_still_require_a_spatially_stable_guard(self):
        def drifting_guard(observation):
            points = np.array(observation.landmarks)
            phase = observation.frame_id % 16
            # Slow enough to pass the speed gate, but the complete window
            # travels too far to define a single stationary guard anchor.
            points[[15, 16], 0] += min(phase, 16 - phase) * .01
            return replace(observation, landmarks=tuple(map(tuple, points)))

        fixture = Fixture()
        for _ in range(48):
            fixture.step(gap_ns=333_333_333, transform=drifting_guard)
        self.assertFalse(fixture.engine.calibrated)
        self.assertGreater(fixture.engine.calibration_feedback.counts['GUARD_SHIFTED'], 0)
        self.assertFalse(fixture.events)

    def test_missing_or_outside_wrist_cannot_be_bridged_by_longer_sample_bound(self):
        def missing(observation):
            visibility = list(observation.visibility)
            visibility[15] = .1
            return replace(observation, visibility=tuple(visibility))

        def outside(observation):
            points = np.array(observation.landmarks)
            points[15, 0] = 1.01
            return replace(observation, landmarks=tuple(map(tuple, points)))

        for transform in (missing, outside):
            with self.subTest(transform=transform.__name__):
                fixture = Fixture()
                for _ in range(9):
                    fixture.step(gap_ns=125_000_000)
                fixture.step(gap_ns=125_000_000, transform=transform)
                self.assertEqual(len(fixture.engine.samples), 0)
                self.assertEqual(fixture.engine.calibration_progress, 0.)
                for _ in range(30):
                    fixture.step(gap_ns=125_000_000, transform=transform)
                self.assertFalse(fixture.engine.calibrated)
                self.assertFalse(fixture.events)

    def test_lowered_extended_far_or_moving_hands_do_not_calibrate_or_attack(self):
        def modified(observation, kind):
            points = np.array(observation.landmarks)
            if kind == 'lowered':
                points[[15, 16], 1] = .55
            elif kind == 'extended':
                points[13], points[14] = (.8, .3, 0), (.2, .3, 0)
                points[15], points[16] = (.95, .3, 0), (.05, .3, 0)
            elif kind == 'far':
                points[13], points[14] = (.95, .6, 0), (.05, .6, 0)
                points[15], points[16] = (.95, .3, 0), (.05, .3, 0)
            elif kind == 'moving':
                points[15, 0] += .06 if observation.frame_id % 2 else -.06
            return replace(observation, landmarks=tuple(map(tuple, points)))

        for kind, reason in (('lowered', 'FISTS_LOW'), ('extended', 'ARMS_EXTENDED'),
                             ('far', 'FISTS_FAR'), ('moving', 'ARMS_MOVING')):
            with self.subTest(kind=kind):
                fixture = Fixture()
                for _ in range(40):
                    fixture.step(gap_ns=125_000_000,
                                 transform=lambda observation: modified(observation, kind))
                self.assertFalse(fixture.engine.calibrated)
                self.assertEqual(fixture.engine.calibration_progress, 0.)
                self.assertGreater(fixture.engine.calibration_feedback.counts[reason], 0)
                self.assertFalse(fixture.events)

    def test_post_calibration_action_gap_and_freshness_are_not_widened(self):
        fixture = Fixture()
        for _ in range(40):
            fixture.step(gap_ns=33_333_333)
        engine = fixture.engine
        self.assertTrue(engine.calibrated)
        self.assertTrue(engine.ready)
        self.assertEqual(engine.config.max_gap_ms, 100)
        self.assertEqual(engine.config.evidence_age_ms, 120)
        for signals in (engine.signals, engine.route_signals, engine.vertical_signals):
            self.assertEqual(signals.cfg.max_gap_ms, 100)
        state = fixture.step(gap_ns=150_000_000)
        self.assertFalse(engine.ready)
        self.assertNotEqual(state.attack_state, 'READY')
        self.assertFalse(fixture.events)

    def test_normal_thirty_fps_guard_cross_and_return_match_old_sampling(self):
        current = Fixture()
        old = Fixture(LegacyCalibration())
        first_calibrated = {}
        for kind, count in (('guard', 40), ('cross', 45), ('guard', 30)):
            for _ in range(count):
                a = current.step(gap_ns=33_333_333, kind=kind)
                b = old.step(gap_ns=33_333_333, kind=kind)
                self.assertEqual(asdict(a), asdict(b))
                self.assertEqual(current.engine.calibration_progress,
                                 old.engine.calibration_progress)
                for name, fixture in (('current', current), ('old', old)):
                    if fixture.engine.calibrated:
                        first_calibrated.setdefault(name, fixture.frame)
        self.assertEqual(first_calibrated['current'], first_calibrated['old'])
        self.assertEqual([asdict(event) for event in current.events],
                         [asdict(event) for event in old.events])
        self.assertEqual([event.reason.get('gesture') for event in current.events], ['SPECIAL'])
        self.assertTrue(current.engine.ready)
        for side in ('LEFT', 'RIGHT'):
            for field in ('position', 'angle', 'angle3d'):
                np.testing.assert_array_equal(current.engine.attack.anchors()[side][field],
                                              old.engine.attack.anchors()[side][field])


if __name__ == '__main__':
    unittest.main()
