"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_COMPACT_RETURN_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class CompactReturnRecordingTests(unittest.TestCase):
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
    def test_two_quick_folded_returns_rearm_raises(self):
        for a,b in [(41.2,41.9),(60.7,61.4)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_RAISE'])
            self.assertEqual(self.gestures(a,b,True),['LEFT_RAISE'])
    def test_keeping_one_hand_raised_after_block_does_not_create_kick(self):
        self.assertEqual(self.gestures(43.6,44.5),[])
    def test_previous_attacks_special_and_rest_preserved(self):
        from collections import Counter
        self.assertEqual(Counter(g for _,g in self.results[0][0]),
            dict(LEFT_PUNCH=5,RIGHT_PUNCH=2,LEFT_RAISE=5,RIGHT_RAISE=4,SPECIAL=1))
        self.assertEqual(self.gestures(73.7,75),['SPECIAL'])
        self.assertEqual(self.gestures(75.5,83),[])
    def test_long_block_and_release_preserved(self):
        held=[v for t,v in self.results[0][1] if 54.1<=t<=59.69]
        self.assertTrue(held);self.assertTrue(all(held))
        self.assertFalse(any(v for t,v in self.results[0][1] if 59.74<=t<=59.9))
