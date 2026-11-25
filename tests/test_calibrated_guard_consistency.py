"""A calibrated chest guard can re-arm without widening body-punch evidence."""
import copy
import unittest
import numpy as np
from redbool_demo.six_engine import SixAttack,SixGestureEngine


class CalibratedGuardConsistencyTests(unittest.TestCase):
    def setup_guard(self,height=.35):
        unit=SixAttack(SixGestureEngine().config);unit.state='READY'
        anchor={'position':np.array([-.1,height,-1.]),'angle':.6,'angle3d':1.9}
        unit.baseline={'LEFT':anchor}
        arm={**copy.deepcopy(anchor),'velocity':np.zeros(3),'speed_xy':.1,'angle_velocity':0.}
        return unit,arm

    def test_stable_personal_chest_guard_is_a_valid_ready_pose(self):
        for height in (.21,.28,.35,.40):
            with self.subTest(height=height):
                unit,arm=self.setup_guard(height)
                self.assertTrue(unit.guard_neutral({'LEFT':arm}))
                # Rearming is not extra proof for the opposite hand's body punch.
                self.assertFalse(unit.supporting_guard(arm,unit.baseline['LEFT']))

    def test_return_cannot_drift_below_personal_guard_into_hands_down(self):
        unit,arm=self.setup_guard(.30)
        for height in (.39,.55,.80):
            with self.subTest(height=height):
                arm['position'][1]=height
                self.assertFalse(unit.guard_neutral({'LEFT':arm}))

    def test_lowest_calibrated_guard_still_has_absolute_height_limit(self):
        unit,arm=self.setup_guard(.40);arm['position'][1]=.46
        self.assertFalse(unit.guard_neutral({'LEFT':arm}))

    def test_extension_fast_motion_or_missing_elbow_cannot_rearm(self):
        for changes in ({'angle':1.7},{'angle3d':2.6},{'angle3d':None},
                        {'speed_xy':SixGestureEngine().config.neutral_speed+.1}):
            with self.subTest(changes=changes):
                unit,arm=self.setup_guard();arm.update(changes)
                self.assertFalse(unit.guard_neutral({'LEFT':arm}))

    def test_lateral_extended_position_cannot_become_new_guard_anchor(self):
        unit,arm=self.setup_guard();before=copy.deepcopy(unit.baseline)
        arm['position'][0]+=.5
        for _ in range(10):self.assertFalse(unit.guard_neutral({'LEFT':arm}))
        np.testing.assert_array_equal(unit.baseline['LEFT']['position'],before['LEFT']['position'])

    def test_existing_high_guard_and_stable_chest_hold_do_not_emit_attacks(self):
        unit,arm=self.setup_guard(-.40);arm['position'][1]=-.10
        self.assertTrue(unit.guard_neutral({'LEFT':arm}))
        unit,arm=self.setup_guard(.35);commits=[]
        for i in range(60):
            event,_,_=unit.update({'LEFT':arm},1_000_000_000+i*33_333_333)
            if event:commits.append(event)
        self.assertEqual(commits,[]);self.assertEqual(unit.state,'READY')

    def test_recorded_borderline_support_does_not_enable_new_low_punch_route(self):
        # Windows225436 frame3509: widening support to the personal floor added
        # an unreviewed RIGHT_PUNCH. This hand is only READY/return evidence.
        unit,arm=self.setup_guard()
        unit.baseline['LEFT']={'position':np.array([-.0932763964,.1461005211,-3.0987324715]),
                               'angle':.1788127422,'angle3d':2.1681587141}
        arm.update(position=np.array([-.0055508558,.2025959939,-3.3450105190]),
                   angle=.1165729314,angle3d=2.1322916334,speed_xy=.2607412636)
        self.assertTrue(unit.guard_neutral({'LEFT':arm}))
        self.assertFalse(unit.supporting_guard(arm,unit.baseline['LEFT']))

if __name__=='__main__':unittest.main()
