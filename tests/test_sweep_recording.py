"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_SWEEP_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class SweepRecordingTests(unittest.TestCase):
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
    def test_high_block_and_short_low_misses_recovered(self):
        for a,b,g in [(36.8,37.2,'LEFT_PUNCH'),(47.7,48.3,'LEFT_PUNCH'),
                         (73.2,73.7,'LEFT_PUNCH'),(79.0,79.4,'LEFT_PUNCH')]:
            self.assertEqual(self.gestures(a,b),[g])
            self.assertEqual(self.gestures(a,b,True),['RIGHT_PUNCH'])
    def test_forward_punch_is_not_low_kick(self):
        self.assertEqual(self.gestures(119.6,120.3),['RIGHT_PUNCH'])
        self.assertEqual(self.gestures(119.6,120.3,True),['LEFT_PUNCH'])
    def test_left_sweep_does_not_trigger_guarding_hand(self):
        self.assertEqual(self.gestures(183.5,190.2),['LEFT_PUNCH'])
        self.assertEqual(self.gestures(183.5,190.2,True),['RIGHT_PUNCH'])
    def test_right_extended_sweep_has_no_kick_or_second_punch(self):
        self.assertEqual(self.gestures(196.4,206),['RIGHT_PUNCH'])
        self.assertEqual(self.gestures(196.4,206,True),['LEFT_PUNCH'])
        self.assertEqual(self.gestures(208.7,212.7),['LEFT_PUNCH'])
    def test_actual_raises_preserved(self):
        from collections import Counter
        self.assertEqual(Counter(g for t,g in self.results[0][0] if 134<t<165 and g.endswith('RAISE')),
                         dict(LEFT_RAISE=5,RIGHT_RAISE=8))
    def test_reviewed_idle_and_final_short_rest_are_quiet(self):
        for a,b in [(166,182),(212.8,216.2)]:
            self.assertEqual(self.gestures(a,b),[])
            self.assertEqual(self.gestures(a,b,True),[])
    def test_held_high_block_has_no_attacks(self):
        for a,b in [(43.5,47.2),(64.5,72.5),(125,133)]:
            self.assertEqual(self.gestures(a,b),[])
            held=[v for t,v in self.results[0][1] if a<=t<=b]
            self.assertTrue(held);self.assertTrue(all(held))

class SweepRoutingResetTests(unittest.TestCase):
    def test_recalibration_clears_occlusion_and_compact_route(self):
        e=SixGestureEngine();e.compact_forward_proof['LEFT']=1;e.occlusion_until['RIGHT']=2
        e.request_calibration()
        self.assertEqual(e.compact_forward_proof,{})
        self.assertEqual(e.occlusion_until,{})
    def test_tracking_loss_clears_occlusion_and_compact_route(self):
        e=SixGestureEngine();e.compact_forward_proof['LEFT']=1;e.occlusion_until['RIGHT']=2
        e._safe(3,'test_tracking_loss')
        self.assertEqual(e.compact_forward_proof,{})
        self.assertEqual(e.occlusion_until,{})
