"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_HIGH_GUARD_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class HighGuardRecordingTests(unittest.TestCase):
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
    def test_seven_reviewed_punch_misses_are_recovered(self):
        for a,b,g in [(63.3,63.8,'LEFT_PUNCH'),(65.2,65.8,'LEFT_PUNCH'),
                      (101.5,102.2,'RIGHT_PUNCH'),(102.7,103.4,'RIGHT_PUNCH'),
                      (113.8,114.4,'RIGHT_PUNCH'),(115.4,116.2,'RIGHT_PUNCH'),(120.5,121.5,'RIGHT_PUNCH')]:
            with self.subTest(window=(a,b)):
                self.assertEqual(self.gestures(a,b),[g])
                mirrored=g.replace('LEFT','TEMP').replace('RIGHT','LEFT').replace('TEMP','RIGHT')
                self.assertEqual(self.gestures(a,b,True),[mirrored])
    def test_held_punch_sweep_is_not_kick_or_second_punch(self):
        self.assertEqual(self.gestures(117.7,119.6),['RIGHT_PUNCH'])
        self.assertEqual(self.gestures(117.7,119.6,True),['LEFT_PUNCH'])
    def test_actual_raises_and_special_preserved(self):
        for a,b,g in [(76.3,77.2,'RIGHT_RAISE'),(80.1,80.7,'RIGHT_RAISE'),
                      (84.9,85.8,'RIGHT_RAISE'),(87.5,88.3,'RIGHT_RAISE'),
                      (92.6,93.3,'LEFT_RAISE'),(129.5,130.8,'SPECIAL')]:
            self.assertEqual(self.gestures(a,b),[g])
    def test_block_releases_on_proven_extension(self):
        held=[v for t,v in self.results[0][1] if 55.6<=t<=63.2]
        self.assertTrue(held);self.assertTrue(all(held))
        self.assertFalse(any(v for t,v in self.results[0][1] if 63.60<=t<=63.72))
    def test_complete_recording_has_no_extra_events(self):
        from collections import Counter
        self.assertEqual(Counter(g for _,g in self.results[0][0]),
            dict(LEFT_PUNCH=8,RIGHT_PUNCH=12,LEFT_RAISE=1,RIGHT_RAISE=4,SPECIAL=1))
        self.assertEqual(self.gestures(0,52),[])
        self.assertEqual(self.gestures(131,134.28),[])
