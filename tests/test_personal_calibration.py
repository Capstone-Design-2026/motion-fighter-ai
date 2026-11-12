import unittest
import numpy as np
from action_engine.types import PoseObservation, EngineConfig
from redbool_demo.lateral import LateralMovement
from redbool_demo.six_engine import SixAttack, SixGestureEngine
from redbool_demo.high_guard_raise import HighGuardRaise


def pose(x=.5,width=.2,roll=0.):
    p=np.full((33,3),.5);p[11]=[x+width/2,.5+roll,0];p[12]=[x-width/2,.5,0]
    return PoseObservation('A','S',1,0,tuple(map(tuple,p)),(1.,)*33,640,480)

class PersonalCalibrationTests(unittest.TestCase):
    def test_final_frame_shoulder_jitter_does_not_shift_centre(self):
        m=LateralMovement(EngineConfig());m.calibrate_samples([pose()]*18+[pose(.55,.3,.02)])
        self.assertAlmostEqual(m.origin,.5);self.assertAlmostEqual(m.width,.2);self.assertAlmostEqual(m.roll_origin,0)
    def test_small_normal_settling_preserves_existing_movement_anchor(self):
        m=LateralMovement(EngineConfig());m.calibrate_samples([pose()]*18+[pose(.503,.201,.001)])
        self.assertAlmostEqual(m.origin,.503);self.assertAlmostEqual(m.width,.201)
        self.assertEqual(m.calibration_source,'settled_final_frame')
    def test_gradual_body_settling_is_not_a_single_frame_outlier(self):
        m=LateralMovement(EngineConfig());m.calibrate_samples([pose(x) for x in np.linspace(.5,.47,20)])
        self.assertAlmostEqual(m.origin,.47)
        self.assertEqual(m.calibration_source,'settled_final_frame')
    def test_new_person_replaces_previous_size_and_position(self):
        m=LateralMovement(EngineConfig());m.calibrate_samples([pose()]*19)
        m.calibrate_samples([pose(.6,.3)]*19)
        self.assertAlmostEqual(m.origin,.6);self.assertAlmostEqual(m.width,.3)
        for t in (0,200_000_000):m.update(pose(.6,.3),t)
        self.assertEqual(m.state,'NEUTRAL')
    def test_same_relative_lean_across_sizes(self):
        for width in (.12,.2,.35):
            m=LateralMovement(EngineConfig());m.calibrate_samples([pose(width=width)]*19)
            for t in (0,200_000_000):m.update(pose(.5+.25*width,width),t)
            self.assertEqual(m.state,'LEFT')
            for t in (300_000_000,500_000_000):m.update(pose(width=width),t)
            self.assertEqual(m.state,'NEUTRAL')
    def test_manual_calibration_clears_old_person_window(self):
        e=SixGestureEngine();e.calibration_observations.append(pose());e.movement.calibrate(pose())
        e.request_calibration();self.assertFalse(e.calibration_observations);self.assertIsNone(e.movement.origin)
    def test_invalid_window_does_not_keep_previous_person(self):
        m=LateralMovement(EngineConfig());m.calibrate(pose());m.calibrate_samples([])
        self.assertIsNone(m.origin);self.assertEqual(m.state,'NEUTRAL')
    def test_calibrated_relaxed_fold_supports_other_hand_punch(self):
        b={'position':np.array([0.,0.,0.]),'angle':.9,'angle3d':1.5}
        a={**b,'speed_xy':0.}
        self.assertTrue(SixAttack.supporting_guard(a,b))
        self.assertFalse(SixAttack.supporting_guard({**a,'angle':1.4},b))
        self.assertFalse(SixAttack.supporting_guard({**a,'position':np.array([0.,.7,0.])},b))
    def test_relaxed_high_guard_reference_requires_fresh_upstroke(self):
        d=HighGuardRaise();b={s:{'position':np.zeros(3),'angle':.9} for s in ('LEFT','RIGHT')}
        def arms(y):return {s:{'position':np.array([0.,v,0.]),'angle':.9,'speed_xy':0.} for s,v in [('LEFT',y),('RIGHT',-.4)]}
        self.assertFalse(d.step(arms(-.4),b,1,valid=True,blocked=True,excluded=set())[1])
        self.assertEqual(d.step(arms(-.95),b,33_333_334,valid=True,blocked=True,excluded=set())[1],{'LEFT'})
        self.assertFalse(d.step(arms(-.95),b,66_666_667,valid=True,blocked=False,excluded=set())[1])
    def test_extended_reference_cannot_become_raise(self):
        d=HighGuardRaise();b={s:{'position':np.zeros(3),'angle':1.5} for s in ('LEFT','RIGHT')}
        a={s:{'position':np.array([0.,-.4,0.]),'angle':1.6,'speed_xy':0.} for s in b}
        d.step(a,b,1,valid=True,blocked=True,excluded=set());self.assertFalse(d.refs)
