"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_HELD_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class HeldGuardRecordingTests(unittest.TestCase):
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
    def test_forward_punch_no_longer_becomes_raise(self):
        self.assertEqual(self.gestures(224.7,225.6),['LEFT_PUNCH'])
        self.assertEqual(self.gestures(224.7,225.6,True),['RIGHT_PUNCH'])
    def test_high_diagonal_punch_is_recovered(self):
        self.assertEqual(self.gestures(165.6,166.5),['LEFT_PUNCH'])
    def test_reviewed_bent_raises_remain_raises(self):
        for a,b,expected in [(52.2,52.9,'LEFT_RAISE'),(56,56.8,'LEFT_RAISE'),
            (160.1,160.8,'LEFT_RAISE'),(161.3,162.3,'LEFT_RAISE'),(178,178.8,'RIGHT_RAISE'),
            (180,180.7,'RIGHT_RAISE'),(206.6,207.2,'LEFT_RAISE'),(209.5,210.3,'LEFT_RAISE')]:
            self.assertEqual(self.gestures(a,b),[expected])
    def test_three_specials_and_reviewed_rest_preserved(self):
        self.assertEqual([round(t,3) for t,g in self.results[0][0] if g=='SPECIAL'],[74.94,215.702,270.025])
        self.assertEqual(self.gestures(80,150),[])

class PoseGuardReleaseTests(unittest.TestCase):
    def test_valid_release_has_no_exit_tail(self):
        import numpy as np
        from redbool_demo.six_engine import SixBlock
        b=SixBlock(SixGestureEngine().config)
        anchors={s:{'position':np.zeros(3)} for s in ('LEFT','RIGHT')}
        raised={s:{'position':np.array([0.,-.6,0.]),'angle':.5} for s in anchors}
        b.update_relative(raised,anchors,1_000_000_000)
        b.update_relative(raised,anchors,1_500_000_000)
        self.assertTrue(b.held)
        lowered={s:{'position':np.zeros(3),'angle':.5} for s in anchors}
        b.update_relative(lowered,anchors,1_533_333_333)
        self.assertFalse(b.held)
    def test_probe_rejects_timed_expiry_during_held_pose(self):
        from redbool_demo.probe import guard_check
        held=[{'type':'send','packet':{'kind':'STATE','sequence':1,'block_active':True}},
              {'type':'ack','ack':{'sequence':1,'guards_started':1,'game_guard_active':False,
                  'guard_mode':'held_pose_immediate_release'}}]
        self.assertFalse(guard_check(0,held,held)['passed'])
        held[1]['ack']['game_guard_active']=True
        release={'type':'ack','ack':{'guards_started':1,'game_guard_active':False}}
        self.assertTrue(guard_check(0,held+[release],held)['passed'])
    def test_recalibration_clears_forward_route_proof(self):
        e=SixGestureEngine();e.forward_proof={'LEFT':100};e.request_calibration()
        self.assertEqual(e.forward_proof,{})
