"""Augmented existing landmarks, not a new real-person recognition trial."""
import unittest,os,json
from pathlib import Path
from dataclasses import replace
import numpy as np
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine
@unittest.skipUnless(os.environ.get('REDBOOL_DEMO_SESSION'),'Recorded fixture required')
class LeanActionsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        s=Path(os.environ['REDBOOL_DEMO_SESSION']);cls.m=json.loads((s/'manifest.json').read_text());cls.frames={f['frame_id']:f for f in map(json.loads,(s/'frames.jsonl').read_text().splitlines())}
    def trial(self,sign):
        e=SixGestureEngine(movement=True);e.request_calibration();ev=[];i=0
        base=PoseObservation.from_record(self.frames[8809],self.m)
        def feed(o,angle=0,shift=0):
            nonlocal i
            i+=1;p=np.array(o.landmarks);p[:,1]*=o.height/o.width;c=p[[11,12],:2].mean(axis=0)
            r=np.array([[np.cos(angle),-np.sin(angle)],[np.sin(angle),np.cos(angle)]])
            p[:,:2]=(p[:,:2]-c)@r.T+c;p[:,0]+=shift;p[:,1]/=o.height/o.width
            o=replace(o,landmarks=tuple(map(tuple,p)),frame_id=i,capture_ns=base.capture_ns+i*33_333_333)
            state=e.process(o,now_ns=o.capture_ns);ev.extend(e.drain_events(o.capture_ns));e.take_logs();return state
        for _ in range(45):feed(base)
        for k in range(30):feed(base,sign*np.deg2rad(12)*k/29,sign*.1*k/29)
        for _ in range(30):state=feed(base,sign*np.deg2rad(12),sign*.1)
        self.assertNotEqual(state.movement,'NEUTRAL');self.assertEqual(ev,[])
        for fid in range(8830,9517):feed(PoseObservation.from_record(self.frames[fid],self.m),sign*np.deg2rad(12),sign*.1)
        for _ in range(35):feed(base,sign*np.deg2rad(12),sign*.1)
        for wrist in [15,16]:
            p=np.array(base.landmarks);p[wrist,1]-=.20;o=replace(base,landmarks=tuple(map(tuple,p)))
            for _ in range(25):feed(o,sign*np.deg2rad(12),sign*.1)
            for _ in range(35):feed(base,sign*np.deg2rad(12),sign*.1)
        self.assertEqual([v.reason['gesture'] for v in ev],['LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE'])
        for _ in range(30):state=feed(base)
        self.assertEqual(state.movement,'NEUTRAL')
    def test_left_lean_all_four_actions(self):self.trial(1)
    def test_right_lean_all_four_actions(self):self.trial(-1)
