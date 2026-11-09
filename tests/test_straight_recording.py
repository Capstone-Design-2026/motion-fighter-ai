"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_STRAIGHT_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class StraightRecordingTests(unittest.TestCase):
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
    def test_reviewed_diagonal_straights_recovered_without_duplicates(self):
        for a,b in [(68.2,69.6),(73.7,74.6),(133,134),(134.5,135.5),(144,145),
                    (147.4,148.5),(150.5,151.4),(155,155.7),(162.7,163.5),
                    (169.5,170.5),(177.3,178.4),(179.1,180.3),(186.7,188.2),
                    (190.6,191.8),(233.2,234.5)]:
            with self.subTest(interval=(a,b)):
                self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
                self.assertEqual(self.gestures(a,b,True),['LEFT_PUNCH'])
    def test_jabs_raises_and_one_cross_preserved(self):
        from collections import Counter
        self.assertEqual(Counter(g for _,g in self.results[0][0]),
            dict(LEFT_PUNCH=9,RIGHT_PUNCH=27,LEFT_RAISE=5,RIGHT_RAISE=7,SPECIAL=1))
        self.assertEqual(self.gestures(247.4,249),['SPECIAL'])
    def test_reviewed_foreshortened_punches_and_false_raise(self):
        # Re-reviewed video: these are extended forward punches, not raises.
        for a,b in [(197.9,198.8),(202.7,203.6)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
            self.assertEqual(self.gestures(a,b,True),['LEFT_PUNCH'])
        # The old 171.022 raise was an extended high punch; still not recovered
        # as a punch, but it must not emit a low kick.
        self.assertNotIn('RIGHT_RAISE',self.gestures(170.8,171.6))
    def test_additional_video_reviewed_forward_punches(self):
        # Re-reviewed original video: both attempts visibly extend the elbow.
        for a,b in [(138.8,139.6),(192.5,193.3)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
            self.assertEqual(self.gestures(a,b,True),['LEFT_PUNCH'])
    def test_rest_and_lean_do_not_generate_attacks(self):
        for a,b in [(100,114),(118,125),(181,183),(208,220)]:
            self.assertEqual(self.gestures(a,b),[])
    def test_held_guard_still_detected(self):
        for a,b in [(95,96),(97,98)]:
            self.assertTrue(any(block for t,block in self.results[0][1] if a<=t<=b))
