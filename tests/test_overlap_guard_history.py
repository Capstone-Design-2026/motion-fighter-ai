import unittest
from dataclasses import replace

from action_engine.types import PoseObservation
from redbool_demo.overlap_punch import OverlapPunch
from redbool_demo.six_engine import SixGestureEngine


class GuardHistoryTests(unittest.TestCase):
    frame_interval = 32_000_000
    gates = ((True, True), (True, True))

    def sample(self, frame, right_wrist=.38, right_elbow=.57):
        points = [(0.5, 0.28, 0.0)] * 33
        points[7], points[8] = (0.54, .28, 0.), (0.46, .28, 0.)
        points[11:17] = [(.65, .40, 0.), (.35, .40, 0.),
                         (.66, .57, 0.), (.34, right_elbow, 0.),
                         (.60, .38, 0.), (.40, right_wrist, 0.)]
        return PoseObservation('test', 'source', frame, self.frame_interval * frame,
                               tuple(points), (1.0,) * 33, 640, 640)

    def play(self, tracker, *samples):
        for obs in samples:
            self.assertIsNone(tracker.observe(obs, self.gates))
            self.assertIsNone(tracker.repaired_side)

    def setUp(self):
        self.tracker = OverlapPunch()
        self.guard = [self.sample(frame) for frame in range(1, 5)]
        self.jump = self.sample(5, right_wrist=.68)

    def test_only_closed_same_wrist_excursion_changes_the_past_guard_view(self):
        self.play(self.tracker, *self.guard, self.jump)
        self.assertEqual(self.tracker.repair_origin, [None, None])
        during = self.sample(6, right_wrist=.58)
        self.play(self.tracker, during, self.sample(7))
        self.assertEqual(self.tracker.repair_origin, [None, self.jump.capture_ns])
        raw = list(self.tracker.history)
        corrected = self.tracker._corrected_guard(1, raw)
        before = next(row for row in raw if row[0] == self.jump.capture_ns)
        after = next(row for row in corrected if row[0] == self.jump.capture_ns)
        self.assertAlmostEqual(before[2][11], .68 - .40)
        self.assertAlmostEqual(after[2][11], .38 - .40)
        self.assertEqual(before[2][:10], after[2][:10])
        self.assertEqual(self.tracker._corrected_guard(0, raw), raw)
        self.assertEqual(corrected[-1], raw[-1])
        self.assertEqual(list(self.tracker.history), raw)

    def test_held_arm_and_late_voluntary_return_do_not_repair_guard(self):
        self.play(self.tracker, *self.guard, self.jump)
        self.play(self.tracker, *(self.sample(frame, right_wrist=.68) for frame in range(6, 15)))
        self.play(self.tracker, self.sample(15))
        self.assertEqual(self.tracker.repair_origin, [None, None])

    def test_elbow_motion_vetoes_repair_during_excursion_and_at_return(self):
        for during, at_return in ((.70, .57), (.57, .70)):
            with self.subTest(during=during, at_return=at_return):
                tracker = OverlapPunch()
                self.play(tracker, *self.guard, self.jump, self.sample(6, .58, during),
                          self.sample(7, .38, at_return), self.sample(8))
                self.assertEqual(tracker.repair_origin, [None, None])

    def test_stale_source_and_invalid_joints_discard_a_completed_repair(self):
        for next_sample in (self.sample(30), self.sample(3), replace(self.sample(8), pose_present=False)):
            with self.subTest(frame=next_sample.frame_id, present=next_sample.pose_present):
                tracker = OverlapPunch()
                self.play(tracker, *self.guard, self.jump, self.sample(6, .58), self.sample(7))
                self.assertIsNotNone(tracker.repair_origin[1])
                self.play(tracker, next_sample)
                self.assertEqual(tracker.repair_origin, [None, None])
                self.assertEqual(tracker.guard_corrections, [{}, {}])

    def test_calibration_handoff_cannot_reuse_previous_guard_repair(self):
        engine = SixGestureEngine()
        self.play(engine.overlap_tracker, *self.guard, self.jump, self.sample(6, .58), self.sample(7))
        self.assertIsNotNone(engine.overlap_tracker.repair_origin[1])
        engine.request_calibration()
        self.assertEqual(engine.overlap_tracker.repair_origin, [None, None])
        self.assertEqual(engine.overlap_tracker.guard_corrections, [{}, {}])


if __name__ == '__main__':
    unittest.main()
