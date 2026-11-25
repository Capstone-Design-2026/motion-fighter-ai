"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_DIRECTED_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class DirectedPunchRecordingTests(unittest.TestCase):
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
    def test_cross_body_left_is_one_jab_not_special_or_opposite_punch(self):
        self.assertEqual(self.gestures(128.5,129.6),['LEFT_PUNCH'])
    def test_mirrored_cross_body_is_one_straight_not_opposite_punch(self):
        self.assertEqual(self.gestures(128.5,129.6,True),['RIGHT_PUNCH'])
    def test_camera_directed_extension_is_punch_not_raise(self):
        self.assertEqual(self.gestures(162.6,163.6),['LEFT_PUNCH'])
        self.assertEqual(self.gestures(162.6,163.6,True),['RIGHT_PUNCH'])
    def test_four_reviewed_x_requests_preserved(self):
        self.assertEqual([round(t,3) for t,g in self.results[0][0] if g=='SPECIAL'],[185.714,221.452,223.651,237.020])
    def test_tutorial_attacks_and_two_blocks_preserved(self):
        self.assertEqual(self.gestures(58,91),['LEFT_PUNCH']*2+['RIGHT_PUNCH']*2+['LEFT_RAISE']*2+['RIGHT_RAISE']*2)
        for a,b in [(96,96.9),(97.6,98.5)]:self.assertTrue(any(v for t,v in self.results[0][1] if a<=t<=b))

class DegenerateElbowTests(unittest.TestCase):
    def test_zero_length_forearm_does_not_fake_extended_cross(self):
        points=[(.5,.5,0.)]*33;points[11]=(.7,.4,0.);points[12]=(.3,.4,0.)
        points[13]=points[15]=(.6,.6,0.);points[14]=points[16]=(.4,.6,0.)
        o=PoseObservation('test','test',1,1,tuple(points),(1.,)*33,640,480)
        self.assertFalse(extended_cross_arm(o,EngineConfig()))
