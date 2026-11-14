import math
import unittest

from redbool_demo.landmark_replay import ReplayComparison


class ReplayComparisonTests(unittest.TestCase):
    def test_windows_arm64_angle_roundoff_is_reported_without_rounding_input(self):
        expected = {'reason': {'angle': 2.4887405750559974}, 'movement': 0.2}
        actual = {'reason': {'angle': 2.488740575055998}, 'movement': math.nextafter(0.2, 1.)}
        comparison = ReplayComparison()
        self.assertTrue(comparison.matches(expected, actual, 'event'))
        self.assertEqual(comparison.report['tolerated_float_differences'], 2)
        self.assertEqual(comparison.report['sample_paths'], ['event.reason.angle', 'event.movement'])
        self.assertEqual(expected['reason']['angle'], 2.4887405750559974)
        self.assertGreater(comparison.report['max_abs_float_difference'], 0.)

    def test_strict_mode_still_rejects_one_ulp_difference(self):
        comparison = ReplayComparison(strict_floats=True)
        self.assertFalse(comparison.matches(0.2, math.nextafter(0.2, 1.)))
        self.assertEqual(comparison.report['tolerated_float_differences'], 0)

    def test_timestamp_event_identity_order_and_branch_decision_remain_exact(self):
        comparison = ReplayComparison()
        cases = [
            (104409223295000, 104409223295001),
            ({'commit_rule': True}, {'commit_rule': False}),
            ({'commit_rule': True}, {'commit_rule': 1}),
            ({'event_id': 'run:1'}, {'event_id': 'run:2'}),
            (['LEFT_PUNCH', 'RIGHT_PUNCH'], ['RIGHT_PUNCH', 'LEFT_PUNCH']),
            ({'capture_ns': 12}, {'capture_ns': 12.}),
            ({'a': 1}, {'a': 1, 'b': 0}),
        ]
        for expected, actual in cases:
            with self.subTest(expected=expected, actual=actual):
                self.assertFalse(comparison.matches(expected, actual))

    def test_material_float_difference_and_nonfinite_changes_fail(self):
        comparison = ReplayComparison()
        for expected, actual in [(0.2, 0.20000001), (1., float('inf')),
                                 ({'$float': 'nan'}, {'$float': '+inf'})]:
            with self.subTest(expected=expected, actual=actual):
                self.assertFalse(comparison.matches(expected, actual))


if __name__ == '__main__':
    unittest.main()
