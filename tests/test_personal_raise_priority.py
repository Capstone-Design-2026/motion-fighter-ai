"""Personal onset speed must not steal a pending raised-arm gesture."""
from dataclasses import replace
from contextlib import ExitStack
from unittest.mock import patch
import unittest
import numpy as np
from redbool_demo.six_engine import SixGestureEngine
from test_chest_special import pose

class PersonalRaisePriorityTests(unittest.TestCase):
    def setup_engine(self, speed=.65, pending=True):
        e=SixGestureEngine();o=pose(frame=1,stamp=1_000_000_000)
        e.process(o,now_ns=o.capture_ns);e.take_logs();e.calibrated=True;e.calibrating=False
        for side,u in e.attack.units.items():
            u.baseline={side:dict(position=np.array([0.,-.1,0.]),angle=.3,angle3d=2.)}
            u.state='READY';u.cfg=replace(u.cfg,onset_speed=speed)
        e.raise_armed={'LEFT':True,'RIGHT':True}
        e.vertical_state='PENDING' if pending else 'READY'
        e.vertical_candidate='RIGHT_RAISE' if pending else None
        e.vertical_since=850_000_000 if pending else None
        return e
    def arms(self,speed):
        # A mostly upward folded projection with a depth angle crossing the
        # forward-route threshold. The other hand stays at its compact guard.
        return {'LEFT':dict(position=np.array([0.,-.1,0.]),velocity=np.zeros(3),
                speed_xy=0.,angle=.3,angle3d=2.,angle_velocity=0.),
            'RIGHT':dict(position=np.array([-.16,-.97,-.8]),velocity=np.array([-.1,-speed,0.]),
                speed_xy=speed,angle=1.41,angle3d=2.61,angle_velocity=.1)}
    def feed(self,e,frame,speed):
        o=pose(frame=frame,stamp=1_000_000_000+frame*33_000_000)
        with ExitStack() as stack:
            for signals in (e.route_signals,e.signals,e.vertical_signals):
                stack.enter_context(patch.object(signals,'update',return_value=self.arms(speed)))
            e.process(o,now_ns=o.capture_ns)
        return [x.reason['gesture']for x in e.drain_events(o.capture_ns)]
    def test_personal_only_slow_takeover_keeps_actual_pending_raise(self):
        e=self.setup_engine();self.assertEqual(self.feed(e,2,.77),['RIGHT_RAISE'])
        self.assertNotIn('RIGHT',e.forward_sides)
        self.assertEqual(e.attack.units['RIGHT'].state,'READY')
    def test_personal_slow_forward_punch_without_raise_ownership_is_preserved(self):
        e=self.setup_engine(pending=False)
        self.assertEqual(self.feed(e,2,.77),[])
        self.assertEqual(e.attack.units['RIGHT'].state,'CANDIDATE')
        self.assertEqual(self.feed(e,3,.60),['RIGHT_PUNCH'])
    def test_baseline_speed_takeover_can_still_become_a_forward_punch(self):
        e=self.setup_engine()
        self.assertEqual(self.feed(e,2,.80),[])
        self.assertEqual(e.attack.units['RIGHT'].state,'CANDIDATE')
        self.assertEqual(self.feed(e,3,.60),['RIGHT_PUNCH'])
    def test_baseline_profile_retains_original_routing(self):
        e=self.setup_engine(speed=.8)
        self.assertEqual(self.feed(e,2,.77),[])
        self.assertIn('RIGHT',e.forward_sides)
        self.assertEqual(e.attack.units['RIGHT'].state,'READY')
if __name__=='__main__':unittest.main()
