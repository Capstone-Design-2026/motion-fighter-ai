import unittest
from dataclasses import replace

import numpy as np

from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixAttack, SixGestureEngine


class OverlapRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.unit = SixAttack(SixGestureEngine().config)
        self.unit.baseline = {'LEFT': {'position': np.array([0., 0., 0.]), 'angle': .1, 'angle3d': 1.6}}
        self.unit.state = 'RETURNING'
        self.unit.neutral_since = 1_000_000_000
        self.unit.overlap_committed = True
        self.unit.overlap_frame = 8
        self.folded = {'position': np.array([.04, -.1, .1]), 'angle': .15, 'angle3d': 1.6, 'speed_xy': 2.2}

    def test_only_one_speed_spike_in_an_existing_return_dwell(self):
        arm = {'LEFT': self.folded}
        self.assertTrue(self.unit.neutral(arm))
        self.assertTrue(self.unit.neutral(arm))  # Same frame is queried twice in normal processing.
        self.unit.overlap_frame += 1
        self.assertFalse(self.unit.neutral(arm))

    def test_speed_spike_cannot_start_a_return_or_rearm_an_extended_arm(self):
        arm = {'LEFT': self.folded}
        self.unit.neutral_since = None
        self.assertFalse(self.unit.neutral(arm))
        self.unit.neutral_since = 1_000_000_000
        self.assertFalse(self.unit.neutral({'LEFT': dict(self.folded, angle3d=2.7)}))
        self.unit.overlap_committed = False
        self.assertFalse(self.unit.neutral(arm))

    def test_actor_reset_and_stale_input_do_not_carry_an_overlap_proof(self):
        engine = SixGestureEngine()
        engine.calibrated = True
        sample = PoseObservation('P1','e',10,1_000_000_000,None,None,640,480,False)
        engine.attack.units['LEFT'].overlap_proof = True
        engine._prepare_arm_updates(sample, {}, sample.capture_ns + 300_000_000, False, None)
        self.assertFalse(engine.attack.units['LEFT'].overlap_proof)
        engine.request_calibration()
        self.assertFalse(engine.overlap_tracker.history)
        self.assertFalse(engine.attack.units['LEFT'].overlap_committed)
        engine.process(replace(sample,actor_id='P2',capture_epoch='f'), now_ns=sample.capture_ns)
        self.assertEqual(engine.drain_events(sample.capture_ns), [])


if __name__ == '__main__':
    unittest.main()
