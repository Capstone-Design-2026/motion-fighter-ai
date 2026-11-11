"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_NEW_PARTICIPANT_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class NewParticipantRecordingTests(unittest.TestCase):
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
    def test_five_reviewed_high_guard_raises_recovered(self):
        for a,b,g in [(112.7,113.5,'RIGHT_RAISE'),(115.1,116.2,'LEFT_RAISE'),(122.8,123.8,'LEFT_RAISE'),(128.7,129.5,'LEFT_RAISE'),(161.9,162.8,'LEFT_RAISE')]:
            self.assertEqual(self.gestures(a,b),[g])
            other=g.replace('LEFT','RIGHT') if g.startswith('LEFT') else g.replace('RIGHT','LEFT')
            self.assertEqual(self.gestures(a,b,True),[other])
    def test_hold_does_not_repeat(self):
        self.assertEqual(self.gestures(122.8,125.4),['LEFT_RAISE'])
        self.assertEqual(self.gestures(161.9,166),['LEFT_RAISE'])
    def test_seated_conversation_without_action_is_quiet(self):
        self.assertEqual(self.gestures(132,142),[])
        self.assertEqual(self.gestures(132,142,True),[])
    def test_punches_after_block_preserved(self):
        for a,b in [(100.5,101.2),(104.3,105),(110.4,111)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
