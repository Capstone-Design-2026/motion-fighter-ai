import unittest
from unittest.mock import Mock

import numpy as np

from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixAttack, SixGestureEngine


class LowApproachTests(unittest.TestCase):
    def setUp(self):
        self.unit = SixAttack(SixGestureEngine().config)
        self.unit.baseline = {'RIGHT': {'position': np.array([0., 0., 0.])}}
        self.unit.side = 'RIGHT'
        self.unit.state = 'CANDIDATE'
        self.unit.opposite_guard = self.unit.low_approach_seed = True

    def sample(self, ms, depth, angle, **kw):
        self.unit.capture_ns = None if ms is None else ms * 1_000_000
        arm = {'position': np.array([.46, .28, depth]), 'angle': .5, 'angle3d': angle}
        self.unit._observe_low_approach(kw.get('side', 'RIGHT'), arm, kw.get('blocked', False))
        return self.unit.low_approach_confirmed

    def seed(self):
        self.assertFalse(self.sample(100, 1.3, 1.97))
        self.assertFalse(self.sample(133, .99, 2.02))
        self.assertFalse(self.sample(167, .75, 2.11))

    def test_opening_while_depth_advances_confirms_only_after_four_captures(self):
        self.seed()
        self.assertTrue(self.sample(201, .5, 2.22))

    def test_held_arm_has_no_evidence_of_extension(self):
        for stamp in (100, 133, 167, 201, 234):
            self.assertFalse(self.sample(stamp, .5, 2.22))

    def test_clock_gap_duplicate_stale_block_and_wrong_arm_interrupt_evidence(self):
        for ms, options in ((280, {}), (167, {}), (None, {}), (201, {'blocked': True}),
                            (201, {'side': 'LEFT'})):
            with self.subTest(ms=ms, options=options):
                self.unit.low_approach.clear()
                self.seed()
                self.assertFalse(self.sample(ms, .5, 2.22, **options))
                self.assertLessEqual(len(self.unit.low_approach), 1)

    def test_return_or_departing_depth_interrupt_evidence(self):
        for depth, angle in ((.5, 1.9), (1.2, 2.22)):
            with self.subTest(depth=depth, angle=angle):
                self.unit.low_approach.clear()
                self.seed()
                self.assertFalse(self.sample(201, depth, angle))
                self.assertEqual(len(self.unit.low_approach), 1)

    def test_overlapping_wrist_requires_a_calibrated_opposite_elbow(self):
        engine = SixGestureEngine()
        engine.calibrated = True
        base = {'position': np.array([0., 0., 0.]), 'angle3d': None}
        for side, unit in engine.attack.units.items():
            unit.baseline = {side: dict(base)}
            unit.state = 'READY'
        sample = PoseObservation('P1', 'e', 10, 1_000_000_000, None, None, 640, 480, False)
        arm = dict(base, angle=.2, angle3d=1.7, speed_xy=2.5)
        engine.overlap_tracker = Mock(repaired_side=None)
        engine.overlap_tracker.observe.return_value = 'LEFT'
        engine._prepare_arm_updates(sample, {'LEFT': arm, 'RIGHT': arm}, sample.capture_ns, False, None)
        self.assertFalse(engine.attack.units['LEFT'].overlap_guard_supported)
        engine.attack.units['RIGHT'].baseline['RIGHT']['angle3d'] = 1.65
        engine._prepare_arm_updates(sample, {'LEFT': arm, 'RIGHT': arm}, sample.capture_ns, False, None)
        self.assertTrue(engine.attack.units['LEFT'].overlap_guard_supported)
