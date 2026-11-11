import unittest
import numpy as np
from redbool_demo.high_guard_raise import HighGuardRaise

class HighGuardRaiseTests(unittest.TestCase):
    def setUp(self):
        self.d=HighGuardRaise();self.t=0
        self.anchors={s:{'position':np.array([0.,0.,0.])} for s in ('LEFT','RIGHT')}
    def step(self,left=-.4,right=-.4,la=.2,ra=.2,blocked=True,valid=True,excluded=()):
        self.t+=33_333_333
        arms={s:{'position':np.array([0.,y,0.]),'angle':a,'speed_xy':0.} for s,y,a in [('LEFT',left,la),('RIGHT',right,ra)]}
        return self.d.step(arms,self.anchors,self.t,valid=valid,blocked=blocked,excluded=set(excluded))
    def test_new_upward_stroke_from_high_block(self):
        self.step();owned,started=self.step(left=-.95,la=1.2)
        self.assertEqual(started,{'LEFT'});self.assertEqual(owned,{'LEFT'})
        for _ in range(20):self.assertFalse(self.step(left=-.95,la=1.2)[1])
    def test_lowering_one_hand_is_not_raise(self):
        self.step(left=-.7,right=-.7)
        for _ in range(20):self.assertEqual(self.step(left=-.7,right=0.,blocked=False)[0],set())
    def test_bilateral_block_never_becomes_kick(self):
        self.step()
        for _ in range(20):self.assertFalse(self.step(left=-.95,right=-.95)[0])
    def test_invalid_tracking_cannot_join_strokes(self):
        self.step();self.step(valid=False)
        self.assertFalse(self.step(left=-.95,la=1.2)[0])
    def test_forward_punch_excluded(self):
        self.step();self.assertFalse(self.step(left=-.95,la=1.2,excluded=('LEFT',))[0])
    def test_repeated_raise_requires_actual_return(self):
        self.step();self.step(left=-.95,la=1.2)
        self.step(left=-.75,la=.2);self.assertFalse(self.step(left=-.95,la=1.2)[1])
        self.step(left=-.4,la=.2);self.assertEqual(self.step(left=-.95,la=1.2)[1],{'LEFT'})
