"""Synthetic lifecycle/safety checks; these poses are not intent/accuracy labels."""
from dataclasses import replace
import unittest

import numpy as np

from redbool_demo.six_engine import SixAttack, SixGestureEngine


class CenteredDepthPunchTests(unittest.TestCase):
    START = 1_000_000_000

    def unit(self, side='LEFT'):
        unit = SixAttack(SixGestureEngine().config)
        unit.state = 'READY'
        unit.forward_hint = unit.opposite_guard = True
        unit.independent_elbow_opening = True
        unit.baseline = {side: dict(position=np.array([0., -.1, 0.]),
                                   angle=.05, angle3d=2.14)}
        return unit

    def arm(self, side='LEFT', *, dx=.27, dy=-.03, dz=-.83,
            angle=.43, angle3d=2.486, outward=False, speed=.7):
        sign = 1. if side == 'LEFT' else -1.
        return dict(position=np.array([sign * dx, -.1 + dy, dz]),
                    velocity=np.array([sign * (speed if outward else -speed), 0., 0.]),
                    speed_xy=speed, angle=angle, angle3d=angle3d, angle_velocity=0.)

    def onset(self, unit, side='LEFT', stamp=None):
        stamp = self.START if stamp is None else stamp
        pose = self.arm(side, dx=.28, dy=-.05, dz=-.56, angle=.48,
                        angle3d=2.48, outward=True, speed=3.5)
        event, signals, _ = unit.update({side: pose}, stamp)
        self.assertIsNone(event)
        self.assertEqual(unit.state, 'CANDIDATE')
        self.assertTrue(signals[side]['strict_onset_rule'])
        return pose

    def complete(self, unit, side='LEFT'):
        self.onset(unit, side)
        event, signals, _ = unit.update({side: self.arm(side)}, self.START + 33_000_000)
        self.assertEqual(event, side)
        self.assertTrue(signals[side]['centered_depth_confirmation'])

    def test_existing_onset_completes_on_both_arms_when_xy_reverses(self):
        for side in ('LEFT', 'RIGHT'):
            with self.subTest(side=side):
                unit = self.unit(side)
                self.complete(unit, side)
                self.assertEqual(unit.state, 'ACTIVE')
                self.assertTrue(unit.centered_depth_committed)
                self.assertEqual(unit.centered_depth_samples, [])

    def test_depth_pose_alone_does_not_acquire_an_onset(self):
        unit = self.unit()
        for i in range(30):
            event, signals, _ = unit.update({'LEFT': self.arm()}, self.START + i * 33_000_000)
            self.assertIsNone(event)
            self.assertFalse(signals['LEFT']['onset_rule'])
        self.assertEqual(unit.state, 'READY')
        self.assertEqual(unit.centered_depth_samples, [])

    def test_queries_do_not_create_votes_or_mutate_evidence(self):
        unit = self.unit()
        self.onset(unit)
        unit.centered_depth_samples.clear()
        unit.now += 33_000_000
        for _ in range(20):
            self.assertFalse(unit.candidates({'LEFT': self.arm()})['LEFT']['centered_depth_confirmation'])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_repeated_capture_timestamp_does_not_confirm(self):
        unit = self.unit()
        self.onset(unit)
        for _ in range(20):
            event, signals, _ = unit.update({'LEFT': self.arm(speed=2.)}, self.START)
            self.assertIsNone(event)
            self.assertFalse(signals['LEFT']['centered_depth_confirmation'])
        self.assertEqual(len(unit.centered_depth_samples), 1)

    def test_confirmation_time_bounds(self):
        for interval, expected in ((19_999_999, False), (20_000_000, True),
                                   (100_000_000, True), (100_000_001, False)):
            with self.subTest(interval=interval):
                unit = self.unit()
                self.onset(unit)
                event, _, _ = unit.update({'LEFT': self.arm()}, self.START + interval)
                self.assertEqual(event == 'LEFT', expected)

    def test_depth_return_or_angle_collapse_cannot_confirm(self):
        for changes in ({'dz': -.55}, {'angle3d': 2.37}):
            with self.subTest(changes=changes):
                unit = self.unit()
                self.onset(unit)
                self.assertIsNone(unit.update({'LEFT': self.arm(**changes)}, self.START + 33_000_000)[0])

    def test_angle_continuity_is_required_above_absolute_angle_floor(self):
        unit = self.unit()
        onset = self.arm(dx=.28, dy=-.05, dz=-.56, angle3d=2.60,
                         outward=True, speed=3.5)
        self.assertIsNone(unit.update({'LEFT': onset}, self.START)[0])
        self.assertEqual(unit.state, 'CANDIDATE')
        # 2.49 still passes the 2.4 angle and .30 calibrated-opening floors.
        event, signals, _ = unit.update({'LEFT': self.arm(angle3d=2.49)},
                                        self.START + 33_000_000)
        self.assertIsNone(event)
        self.assertFalse(signals['LEFT']['centered_depth_confirmation'])

    def test_intermediate_angle_collapse_cannot_revive_after_recovery(self):
        unit = self.unit()
        onset = self.arm(dx=.28, dy=-.05, dz=-.56, angle3d=2.60,
                         outward=True, speed=3.5)
        self.assertIsNone(unit.update({'LEFT': onset}, self.START)[0])
        self.assertEqual(unit.state, 'CANDIDATE')
        self.assertIsNone(unit.update({'LEFT': self.arm(angle3d=2.49, speed=2.)},
                                      self.START + 10_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])
        # All current geometry recovers within 100 ms; interrupted onset proof
        # must still be gone rather than revived or replaced by a later seed.
        self.assertIsNone(unit.update({'LEFT': self.arm(angle3d=2.52, dz=-1.)},
                                      self.START + 33_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_intermediate_extension_then_depth_return_cannot_confirm(self):
        unit = self.unit()
        self.onset(unit)
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-1., speed=2.)},
                                      self.START + 10_000_000)[0])
        # Still deeper than onset, but retracting from the latest observation.
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-.8)},
                                      self.START + 33_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_late_depth_evidence_cannot_seed_old_candidate(self):
        unit = self.unit()
        unit.forward_hint = False
        self.onset(unit)
        self.assertEqual(unit.centered_depth_samples, [])
        unit.forward_hint = True
        self.assertIsNone(unit.update({'LEFT': self.arm(speed=2.)}, self.START + 200_000_000)[0])
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-1.)}, self.START + 233_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_failed_evidence_does_not_reseed_same_candidate(self):
        unit = self.unit()
        self.onset(unit)
        # Keep the candidate alive using speed, but invalidate its depth proof.
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-.2, speed=2.)},
                                      self.START + 10_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])
        self.assertIsNone(unit.update({'LEFT': self.arm(speed=2.)}, self.START + 40_000_000)[0])
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-1.)}, self.START + 73_000_000)[0])

    def test_completion_never_bypasses_reserved_gesture_or_support_gates(self):
        for gate in ('inhibited', 'block', 'forward_hint', 'opposite_guard',
                     'independent_elbow_opening'):
            with self.subTest(gate=gate):
                unit = self.unit()
                self.onset(unit)
                if gate == 'inhibited':
                    unit.inhibited = True
                elif gate != 'block':
                    setattr(unit, gate, False)
                event = unit.update({'LEFT': self.arm()}, self.START + 33_000_000,
                                    block=gate == 'block')[0]
                self.assertIsNone(event)
                self.assertEqual(unit.centered_depth_samples, [])

    def test_geometry_bounds_reject_unsupported_poses(self):
        cases = ({'dx': .09}, {'dx': .36}, {'dy': -.81}, {'dy': .13},
                 {'dz': -.44}, {'angle': .39}, {'angle3d': 2.39},
                 {'angle3d': 2.43}, {'dx': .20, 'dy': -.01})
        for changes in cases:
            with self.subTest(changes=changes):
                unit = self.unit()
                self.onset(unit)
                event, signals, _ = unit.update({'LEFT': self.arm(**changes)},
                                                self.START + 33_000_000)
                self.assertIsNone(event)
                self.assertFalse(signals['LEFT']['centered_depth_confirmation'])

    def test_folded_upward_boundary_is_excluded_from_new_completion(self):
        for dy, expected in ((-.400000001, False), (-.40, False),
                             (-.399999999, True)):
            with self.subTest(dy=dy):
                unit = self.unit()
                self.onset(unit)
                event, signals, _ = unit.update({'LEFT': self.arm(dy=dy)},
                                                self.START + 33_000_000)
                self.assertEqual(event == 'LEFT', expected)
                self.assertEqual(signals['LEFT']['centered_depth_confirmation'], expected)

    def test_later_lift_into_raise_region_clears_proof_without_reseeding(self):
        unit = self.unit()
        self.onset(unit)
        self.assertIsNone(unit.update({'LEFT': self.arm(dy=-.40, speed=2.)},
                                      self.START + 10_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])
        self.assertIsNone(unit.update({'LEFT': self.arm(dz=-1.)},
                                      self.START + 33_000_000)[0])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_neutral_veto_requires_current_bounded_confirmation(self):
        for case in ('valid', 'no_samples', 'expired', 'too_early', 'ready', 'returned'):
            with self.subTest(case=case):
                unit = self.unit()
                self.onset(unit)
                unit.now = self.START + 33_000_000
                arm = self.arm()
                if case == 'no_samples':
                    unit.centered_depth_samples.clear()
                elif case == 'expired':
                    unit.now = self.START + 100_000_001
                elif case == 'too_early':
                    unit.now = self.START + 19_999_999
                elif case == 'ready':
                    unit.state = 'READY'
                elif case == 'returned':
                    arm = self.arm(dx=0., dy=0., dz=0., angle=.05, angle3d=2.14, speed=0.)
                self.assertEqual(unit.guard_neutral({'LEFT': arm}), case != 'valid')

    def test_allow_commit_false_is_respected(self):
        unit = self.unit()
        self.onset(unit)
        event, signals, _ = unit.update({'LEFT': self.arm()}, self.START + 33_000_000,
                                        allow_commit=False)
        self.assertTrue(signals['LEFT']['centered_depth_confirmation'])
        self.assertIsNone(event)
        self.assertFalse(unit.centered_depth_committed)

    def test_uncommitted_confirmation_does_not_refresh_original_deadline(self):
        unit = self.unit()
        self.onset(unit)
        for interval, depth in ((33, -.8), (66, -.9), (99, -1.)):
            event = unit.update({'LEFT': self.arm(dz=depth, speed=2.)},
                                self.START + interval * 1_000_000, allow_commit=False)[0]
            self.assertIsNone(event)
            self.assertEqual(unit.centered_depth_samples[0][0], self.START)
        event, signals, _ = unit.update({'LEFT': self.arm(dz=-1.1, speed=2.)},
                                        self.START + 100_000_001)
        self.assertIsNone(event)
        self.assertFalse(signals['LEFT']['centered_depth_confirmation'])
        self.assertEqual(unit.centered_depth_samples, [])

    def test_held_depth_does_not_repeat_even_after_invalidation(self):
        for invalidate in (False, True):
            with self.subTest(invalidate=invalidate):
                unit = self.unit()
                self.complete(unit)
                if invalidate:
                    unit.invalidate(self.START + 40_000_000)
                unit.forward_hint = unit.opposite_guard = False
                unit.independent_elbow_opening = False
                for i in range(1, 100):
                    arm = self.arm(dx=.27 + .01 * (i % 2), outward=bool(i % 2))
                    self.assertIsNone(unit.update({'LEFT': arm}, self.START + 50_000_000 + i * 33_000_000)[0])
                    self.assertNotEqual(unit.state, 'READY')
                self.assertTrue(unit.centered_depth_committed)

    def test_physical_return_allows_next_punch_and_retains_anchor(self):
        unit = self.unit()
        anchor = unit.baseline['LEFT']['position'].copy()
        self.complete(unit)
        unit.invalidate(self.START + 40_000_000)
        guard = self.arm(dx=0., dy=0., dz=0., angle=.05, angle3d=2.14, speed=0.)
        for i in range(20):
            unit.update({'LEFT': guard}, self.START + 100_000_000 + i * 33_000_000)
        self.assertEqual(unit.state, 'READY')
        self.assertFalse(unit.centered_depth_committed)
        np.testing.assert_array_equal(unit.baseline['LEFT']['position'], anchor)
        self.onset(unit, stamp=self.START + 1_000_000_000)
        self.assertEqual(unit.update({'LEFT': self.arm()}, self.START + 1_033_000_000)[0], 'LEFT')

    def ready_engine(self):
        from test_chest_special import pose
        engine = SixGestureEngine()
        engine.request_calibration()
        for i in range(1, 51):
            obs = pose(frame=i, stamp=i * 33_000_000)
            engine.process(obs, now_ns=obs.capture_ns)
            engine.drain_events(obs.capture_ns)
        self.assertTrue(engine.ready)
        engine.attack.units['LEFT'].centered_depth_samples = [(engine.last_now, -.56, 2.48)]
        return engine

    def test_engine_gap_stale_arm_loss_and_identity_clear_evidence(self):
        from test_chest_special import pose
        for condition in ('gap', 'stale', 'missing_arm', 'person'):
            with self.subTest(condition=condition):
                engine = self.ready_engine()
                gap = 101_000_000 if condition == 'gap' else 33_000_000
                obs = pose(frame=engine.last_frame + 1, stamp=engine.last_capture + gap)
                if condition == 'missing_arm':
                    obs = replace(obs, visibility=tuple(0. if i == 15 else v
                                                        for i, v in enumerate(obs.visibility)))
                elif condition == 'person':
                    obs = replace(obs, capture_epoch='another-person')
                now = obs.capture_ns + (121_000_000 if condition == 'stale' else 0)
                engine.process(obs, now_ns=now)
                self.assertEqual(engine.attack.units['LEFT'].centered_depth_samples, [])
                self.assertFalse(engine.drain_events(now))

    def test_recalibration_discards_old_completion_and_latch(self):
        engine = self.ready_engine()
        engine.attack.units['LEFT'].centered_depth_committed = True
        engine.request_calibration()
        self.assertFalse(engine.calibrated)
        self.assertEqual(engine.attack.units['LEFT'].centered_depth_samples, [])
        self.assertFalse(engine.attack.units['LEFT'].centered_depth_committed)

    def test_expiry_publishes_no_event_and_clears_evidence(self):
        engine = self.ready_engine()
        stamp = engine.last_capture
        self.assertEqual(engine.tick(stamp + 150_000_000).tracking, 'LOST')
        self.assertFalse(engine.ready)
        self.assertFalse(engine.drain_events(stamp + 160_000_000))
        engine.tick(stamp + 220_000_000)
        self.assertEqual(engine.attack.units['LEFT'].centered_depth_samples, [])


if __name__ == '__main__':
    unittest.main()
