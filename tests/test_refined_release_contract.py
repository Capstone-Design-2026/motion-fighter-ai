"""Black-box release safety contract; synthetic input, no hardware/network.

Run with the isolated candidate src/tests on PYTHONPATH. These assertions expose
the original blocked-normal-input bug and the proposed external-lock bypass.
They do not claim these synthetic observations are ground-truth field gestures.
"""
from dataclasses import replace
import unittest

from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine


class ReleaseContractTests(unittest.TestCase):
    def setUp(self):
        self.engine = SixGestureEngine(movement=True)
        self.engine.request_calibration()
        self.frame = 0
        self.capture = 0
        self.events = []
        self.feed(55)
        self.assertTrue(self.engine.calibrated)

    def feed(self, count, kind='guard', heavy=None, *, mutate=None, primary_mutate=None,
             gap_ns=33_333_333, age_ns=0):
        states = []
        for _ in range(count):
            self.frame += 1
            self.capture += gap_ns
            obs = pose(kind, self.frame, self.capture)
            if kind == 'definite_raise':
                points = list(obs.landmarks)
                points[15] = (.60, .03, 0.)
                obs = replace(obs, landmarks=tuple(points))
            if kind == 'missing':
                visibility = list(obs.visibility)
                visibility[15] = 0.
                obs = replace(obs, visibility=tuple(visibility))
            if primary_mutate:
                obs = primary_mutate(obs)
            aux = (obs, obs)
            if heavy:
                extra = pose(heavy, self.frame, self.capture)
                if mutate:
                    extra = mutate(extra)
                aux += (extra,)
            now = self.capture + age_ns
            states.append(self.engine.process(obs, now_ns=now, special_observation=aux))
            self.events.extend(self.engine.drain_events(now))
            self.engine.take_logs()
        return states

    def gestures(self):
        return [event.reason.get('gesture', event.side + '_PUNCH') for event in self.events]

    def refined_once(self):
        self.feed(3, 'cross')
        self.feed(40, 'guard', 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def ordinary_release(self):
        # Existing base models see a normal visible guard; this intentionally
        # does not satisfy the stronger external X re-use release boundary.
        self.feed(20, 'guard')

    def test_ordinary_block_is_not_held_by_external_repeat_lock(self):
        self.refined_once()
        states = self.feed(90, 'block')
        self.assertTrue(any(state.block for state in states))
        self.assertTrue(all(state.block for state in states[12:]))
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_base_only_strict_cannot_bypass_external_repeat_lock(self):
        self.refined_once()
        self.ordinary_release()
        self.feed(90, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 1,
                         'The same held X may look temporarily uncrossed to the base models')

    def test_fresh_heavy_cross_reclaims_same_frame_before_ordinary_actions(self):
        self.refined_once()
        self.ordinary_release()
        self.feed(20, 'block')
        before = len(self.events)
        states = self.feed(25, 'block', 'cross')
        self.assertFalse(any(state.block for state in states))
        self.assertTrue(all(state.movement == 'NEUTRAL' for state in states))
        self.assertEqual(len(self.events), before)

    def test_wrong_or_stale_heavy_cannot_reclaim_controls(self):
        changes = (
            lambda o: replace(o, actor_id='other'),
            lambda o: replace(o, capture_epoch='old'),
            lambda o: replace(o, frame_id=o.frame_id-1),
            lambda o: replace(o, capture_ns=o.capture_ns-1),
            lambda o: replace(o, width=1280),
            lambda o: replace(o, source_mirrored=True),
            lambda o: replace(o, visibility=(0.,)*33),
        )
        for change in changes:
            with self.subTest(change=change):
                self.setUp()
                self.refined_once()
                self.ordinary_release()
                self.feed(20, 'block')
                states = self.feed(12, 'block', 'cross', mutate=change)
                self.assertTrue(all(state.block for state in states))
                self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_long_missing_arm_is_not_permission_for_another_x(self):
        self.refined_once()
        self.feed(90, 'missing')
        self.ordinary_release()
        self.feed(90, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_disjoint_strong_release_does_not_accumulate(self):
        self.refined_once()
        # 99.99ms + missing arm + 99.99ms is not continuous180ms.
        self.feed(4, 'definite_raise')
        self.feed(1, 'missing')
        self.feed(4, 'definite_raise')
        self.ordinary_release()
        self.feed(90, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_stale_strong_open_pose_is_not_external_release_evidence(self):
        self.refined_once()
        self.feed(4, 'definite_raise')
        self.feed(1, 'definite_raise', age_ns=121_000_000)
        # Move source time forward enough to keep the real processing clock
        # monotonic; the long source gap must also break prior release credit.
        self.feed(1, 'definite_raise', gap_ns=200_000_000)
        self.feed(3, 'definite_raise')
        self.ordinary_release()
        self.feed(90, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_fresh_definite_release_allows_a_new_baseline_x(self):
        self.refined_once()
        self.feed(50, 'definite_raise')
        self.feed(45, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 2)
        self.assertEqual(self.gestures().count('LEFT_RAISE'), 1)

    def test_ordinary_raise_after_soft_release_does_not_need_external_rearm(self):
        self.refined_once()
        self.ordinary_release()
        self.feed(50, 'left_raise')
        self.assertEqual(self.gestures().count('LEFT_RAISE'), 1)
        self.assertEqual(self.gestures().count('SPECIAL'), 1)

    def test_left_and_right_punch_after_soft_release_keep_single_event(self):
        for side in ('LEFT', 'RIGHT'):
            with self.subTest(side=side):
                self.setUp()
                self.refined_once()
                self.ordinary_release()
                elbow, wrist = (13, 15) if side == 'LEFT' else (14, 16)
                endpoint = (.86, .35, -.5) if side == 'LEFT' else (.14, .35, -.5)
                elbowpoint = (.76, .35, -.2) if side == 'LEFT' else (.24, .35, -.2)
                # Deterministic existing-rule punch, independently validated to
                # commit exactly one event from the original calibrated guard.
                for amount in [.15, .35, .6, .85, 1.] + [1.] * 20 + [.8, .6, .4, .2, 0.] + [0.] * 20:
                    def project(obs):
                        points = list(obs.landmarks)
                        for index, target in ((elbow, elbowpoint), (wrist, endpoint)):
                            points[index] = tuple(points[index][k] * (1-amount) + target[k] * amount for k in range(3))
                        return replace(obs, landmarks=tuple(points))
                    self.feed(1, primary_mutate=project)
                self.assertEqual(self.gestures(), ['SPECIAL', side + '_PUNCH'])

    def test_normal_x_retains_original_release_and_second_cross(self):
        self.feed(40, 'cross')
        self.ordinary_release()
        self.feed(40, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 2)

    def test_c_recalibration_drops_old_repeat_memory(self):
        self.refined_once()
        self.engine.request_calibration()
        self.feed(55, 'guard')
        self.assertTrue(self.engine.calibrated)
        self.feed(40, 'cross')
        self.assertEqual(self.gestures().count('SPECIAL'), 2)


if __name__ == '__main__':
    unittest.main()
