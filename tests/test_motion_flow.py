"""Changed gameplay contract: separate pose release from a full guard reset."""
from dataclasses import replace
import unittest
import numpy as np
from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine, SixAttack
from redbool_demo.special import CrossSpecial, chest_cross
from action_engine.types import EngineConfig

class MotionFlowTests(unittest.TestCase):
    def setUp(self):
        self.e=SixGestureEngine();self.e.request_calibration();self.i=0;self.events=[]
        self.feed(55)
        self.assertTrue(self.e.calibrated)
    def feed(self,n,kind='guard',left=None,right=None):
        for _ in range(n):
            self.i+=1;o=pose(kind,self.i,self.i*33_333_333);p=np.array(o.landmarks)
            if left is not None:p[15,1]=left
            if right is not None:p[16,1]=right
            o=replace(o,landmarks=tuple(map(tuple,p)))
            self.e.process(o,now_ns=o.capture_ns);self.events.extend(self.e.drain_events(o.capture_ns));self.e.take_logs()
    def gestures(self):return [e.reason['gesture'] for e in self.events]
    def test_alternating_raises_with_other_hand_lowered_no_guard_between(self):
        self.feed(40,left=.05,right=.48)
        self.assertEqual(self.gestures(),['LEFT_RAISE'])
        self.feed(40,left=.48,right=.05)
        self.assertEqual(self.gestures(),['LEFT_RAISE','RIGHT_RAISE'])
        self.feed(40,left=.05,right=.48)
        self.assertEqual(self.gestures(),['LEFT_RAISE','RIGHT_RAISE','LEFT_RAISE'])
        self.feed(90,left=.05,right=.48)
        self.assertEqual(len(self.events),3)
    def test_cross_then_raise_without_guard_and_raise_then_cross(self):
        self.feed(50,'cross');self.assertEqual(self.gestures(),['SPECIAL'])
        self.feed(60,left=.05,right=.48)
        self.assertEqual(self.gestures(),['SPECIAL','LEFT_RAISE'])
        self.feed(50,'cross')
        self.assertEqual(self.gestures(),['SPECIAL','LEFT_RAISE','SPECIAL'])
    def test_chin_cross_supported_but_face_block_distinct(self):
        o=pose('cross');p=np.array(o.landmarks);p[[15,16],1]=.22
        self.assertEqual(chest_cross(replace(o,landmarks=tuple(map(tuple,p))),EngineConfig()),(True,True))
        self.assertEqual(chest_cross(pose('block'),EngineConfig()),(False,False))
    def test_relaxed_single_hand_retraction_is_not_extended_hold(self):
        u=SixAttack(EngineConfig());u.baseline={'LEFT':{'position':np.array([0.,0.,0.]),'angle3d':1.8}}
        arm={'position':np.array([.1,.40,0.]),'speed_xy':.6,'angle':1.3,'angle3d':2.0}
        self.assertTrue(u.neutral({'LEFT':arm}));self.assertFalse(u.guard_neutral({'LEFT':arm}))
        self.assertFalse(u.neutral({'LEFT':{**arm,'angle':2.5,'angle3d':2.8}}))
    def test_short_geometry_noise_does_not_count_as_hold_or_repeat(self):
        s=CrossSpecial();t=0
        def step(**kw):
            nonlocal t
            t+=33_333_333
            return s.step(t,calibrated=True,valid=True,neutral=False,approach=True,strict=True,**kw)
        for _ in range(10):
            t+=33_333_333;s.step(t,calibrated=True,valid=True,neutral=True,approach=False,strict=False)
        for _ in range(15):self.assertFalse(step())
        t+=33_333_333;self.assertFalse(s.step(t,calibrated=True,valid=True,neutral=False,approach=True,strict=False))
        for _ in range(7):self.assertFalse(step())
        self.assertTrue(any(step() for _ in range(5)))
        self.assertFalse(any(step() for _ in range(60)))


# Retrospective regression of visually reviewed X cores. Not held-out accuracy.
import os,json
from pathlib import Path
from action_engine.types import PoseObservation

@unittest.skipUnless(os.environ.get('REDBOOL_X_SESSION'),'Reviewed November 7 capture required')
class RecordedCrossTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_X_SESSION']);m=json.loads((p/'manifest.json').read_text())
        marks={a['event_frame_id'] for a in map(json.loads,(p/'annotations.jsonl').read_text().splitlines()) if a['label']=='CALIBRATE'}
        e=SixGestureEngine();cls.events=[];t0=None
        for line in (p/'frames.jsonl').read_text().splitlines():
            o=PoseObservation.from_record(json.loads(line),m);t0=t0 or o.capture_ns
            e.process(o,now_ns=o.capture_ns)
            cls.events.extend(((x.source_capture_ns-t0)/1e9,x.reason['gesture']) for x in e.drain_events(o.capture_ns))
            e.take_logs()
            if o.frame_id in marks:e.request_calibration()
    def test_five_final_visible_cross_repetitions_each_fire_once(self):
        for lo,hi in [(262.4,264.7),(265.8,267.4),(268.6,270.6),(271.6,273.5),(274.4,276.3)]:
            with self.subTest(window=(lo,hi)):
                self.assertEqual([g for t,g in self.events if lo<=t<=hi],['SPECIAL'])
    def test_previous_cross_entry_is_not_a_punch(self):
        self.assertEqual([g for t,g in self.events if 198.4<=t<=200],['SPECIAL'])
    def test_reviewed_rest_has_no_attacks_or_specials(self):
        for lo,hi in [(166,180),(215.5,240)]:
            self.assertFalse([(t,g) for t,g in self.events if lo<=t<=hi])

if __name__=='__main__':unittest.main()
