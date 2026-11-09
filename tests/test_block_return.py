"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_BLOCK_RETURN_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class BlockReturnRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(SESSION);m=json.loads((p/'manifest.json').read_text());frames=list(map(json.loads,(p/'frames.jsonl').read_text().splitlines()))
        markers={r['event_frame_id']:r['label'] for r in map(json.loads,(p/'annotations.jsonl').read_text().splitlines()) if r['label'] in ('CALIBRATE','PAUSE')}
        trace=list(map(json.loads,(p.parent.parent/'trace.jsonl').read_text().splitlines()));clocks={r['frame_id']:r['state']['timestamp_ns'] for r in trace if r['type']=='frame'}
        cls.results=[]
        for mirror in (False,True):
            e=SixGestureEngine(movement=True);events=[];states=[];t0=frames[0]['capture_return_monotonic_ns']
            for f in frames:
                o=PoseObservation.from_record(f,m)
                if mirror and o.landmarks is not None:
                    pts=[list(x) for x in o.landmarks];vis=list(o.visibility)
                    for pt in pts:pt[0]=1-pt[0]
                    for a,b in [(1,4),(2,5),(3,6),(7,8),(9,10)]+[(i,i+1) for i in range(11,32,2)]:pts[a],pts[b]=pts[b],pts[a];vis[a],vis[b]=vis[b],vis[a]
                    o=replace(o,landmarks=tuple(map(tuple,pts)),visibility=tuple(vis))
                now=clocks.get(o.frame_id,o.capture_ns);state=e.process(o,now_ns=now)
                events += [((x.source_capture_ns-t0)/1e9,x.reason.get('gesture',x.side+'_PUNCH')) for x in e.drain_events(now)]
                states.append(((o.capture_ns-t0)/1e9,state.block));e.take_logs()
                if o.frame_id in markers:e.request_calibration();e.calibrating=markers[o.frame_id]!='PAUSE'
            cls.results.append((events,states))
    def gestures(self,a,b,mirror=False):return [g for t,g in self.results[int(mirror)][0] if a<=t<=b]
    def test_high_block_rearms_next_punch(self):
        self.assertEqual(self.gestures(48.3,48.8), ['LEFT_PUNCH'])
        self.assertEqual(self.gestures(48.3,48.8,True), ['RIGHT_PUNCH'])
    def test_low_punch_is_recognized(self):
        self.assertEqual(self.gestures(80.6,81.0), ['RIGHT_PUNCH'])
        self.assertEqual(self.gestures(80.6,81.0,True), ['LEFT_PUNCH'])
    def test_raises_preserved_and_short_rest_quiet(self):
        for a,b,g in [(54.8,55.4,'LEFT_RAISE'),(57.7,58.4,'RIGHT_RAISE'),
                      (121.4,121.9,'RIGHT_RAISE'),(132.5,133.1,'LEFT_RAISE')]:
            self.assertEqual(self.gestures(a,b),[g])
        self.assertEqual(self.gestures(92,95),[])
        self.assertEqual(self.gestures(139.7,142.48),[])
    def test_no_attack_during_reviewed_block_holds(self):
        for a,b in [(32.3,36.3),(44.9,48.3),(49.3,50.5),(112.7,118.9)]:
            self.assertEqual(self.gestures(a,b),[])
            held=[v for t,v in self.results[0][1] if a<=t<=b]
            self.assertTrue(held);self.assertTrue(all(held))

class BlockRecoverySafetyTests(unittest.TestCase):
    def unit(self):
        import numpy as np
        from redbool_demo.six_engine import SixAttack
        u=SixAttack(SixGestureEngine().config)
        u.baseline={'LEFT':dict(position=np.array([0.,-.1,0.]),angle=.3,angle3d=2.)}
        u.state='RETURNING';u.last_commit=0
        return u
    def arm(self,extended=False):
        import numpy as np
        return dict(position=np.array([.1,-.67,0.]),velocity=np.zeros(3),
                    speed_xy=0.,angle=1.5 if extended else .3,
                    angle3d=2.8 if extended else 2.2,angle_velocity=0.)
    def test_folded_block_recovers_without_emitting_attack(self):
        u=self.unit()
        for n in range(30):
            commit,_,_=u.update({'LEFT':self.arm()},1_000_000_000+n*33_333_333,block=True)
            self.assertIsNone(commit)
        self.assertEqual(u.state,'READY')
    def test_extended_hold_never_rearms_even_with_block_flag(self):
        u=self.unit()
        for n in range(60):
            commit,_,_=u.update({'LEFT':self.arm(True)},1_000_000_000+n*33_333_333,block=True)
            self.assertIsNone(commit)
        self.assertNotEqual(u.state,'READY')
    def test_low_forward_rule_does_not_accept_arm_drop(self):
        import numpy as np
        u=self.unit();u.state='READY'
        a=self.arm(True);a.update(position=np.array([.05,.15,-.2]),
            velocity=np.array([0.,2.,0.]),speed_xy=2.)
        row=u.candidates({'LEFT':a})['LEFT']
        self.assertFalse(row['onset_rule']);self.assertFalse(row['commit_rule'])
