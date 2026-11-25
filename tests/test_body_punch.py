"""Video-reviewed development regressions; mirrored replay is synthetic, not new accuracy."""
from dataclasses import replace
import json,os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation,EngineConfig
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import extended_cross_arm

SESSION=os.environ.get('REDBOOL_BODY_PUNCH_SESSION')
@unittest.skipUnless(SESSION,'Latest reviewed session required')
class BodyPunchRecordingTests(unittest.TestCase):
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
    def test_reviewed_low_punch_misses_are_recovered(self):
        for a,b in [(71.8,72.2),(74.1,74.55),(76.4,77),(83.6,84.3),(85.7,86.2),(88.4,89.2)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
            self.assertEqual(self.gestures(a,b,True),['LEFT_PUNCH'])
    def test_high_block_to_punch_sequences_preserved(self):
        for a,b in [(34.1,34.8),(39.2,39.8),(43.6,44.4),(126.7,127.5),(130.8,131.7),(133.4,134.2)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])

    def test_short_low_straight_fires_during_extension_on_either_side(self):
        # Video shows first movement at 85.51–85.61 s. R6 waited until 86.04 s.
        for mirror, expected in ((False, 'RIGHT_PUNCH'), (True, 'LEFT_PUNCH')):
            with self.subTest(mirror=mirror):
                self.assertEqual(self.gestures(85.50, 85.87, mirror), [expected])
                self.assertEqual(self.gestures(85.87, 86.30, mirror), [])
    def test_first_event_precedes_extended_hold_not_seconds_later(self):
        for a,b in [(99.3,100),(106.6,107.3),(112.3,113)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
        for a,b in [(100,105.8),(107.3,111.8),(113,117)]:self.assertEqual(self.gestures(a,b),[])
    def test_each_extended_sweep_fires_only_initial_punch(self):
        for a,b in [(137.4,143),(143.7,149.5),(152.3,157.5)]:
            self.assertEqual(self.gestures(a,b),['RIGHT_PUNCH'])
            self.assertEqual(self.gestures(a,b,True),['LEFT_PUNCH'])
    def test_long_block_and_final_rest_are_quiet(self):
        self.assertEqual(self.gestures(45,69),[])
        self.assertEqual(self.gestures(158,164),[])
        self.assertEqual(self.gestures(158,164,True),[])
    def test_only_reviewed_right_punches_in_complete_recording(self):
        self.assertEqual([g for _,g in self.results[0][0]],['RIGHT_PUNCH']*22)
        self.assertEqual([g for _,g in self.results[1][0]],['LEFT_PUNCH']*22)

class BodyPunchSafetyTests(unittest.TestCase):
    def setup_arm(self):
        import numpy as np
        from redbool_demo.six_engine import SixAttack
        u=SixAttack(SixGestureEngine().config);u.state='READY'
        u.baseline={'RIGHT':dict(position=np.array([0.,-.1,0.]),angle=.3,angle3d=2.)}
        a=dict(position=np.array([.7,.55,0.]),velocity=np.array([2.,2.,0.]),
               speed_xy=2.8,angle=2.8,angle3d=2.2,angle_velocity=3.)
        return u,a
    def test_lower_planar_route_requires_opposite_guard(self):
        u,a=self.setup_arm()
        self.assertFalse(u.candidates({'RIGHT':a})['RIGHT']['commit_rule'])
        u.opposite_guard=True
        self.assertTrue(u.candidates({'RIGHT':a})['RIGHT']['commit_rule'])
    def test_vertical_arm_drop_is_not_punch_even_with_other_hand_guarding(self):
        u,a=self.setup_arm();u.opposite_guard=True;a['position'][0]=.05
        r=u.candidates({'RIGHT':a})['RIGHT']
        self.assertFalse(r['onset_rule']);self.assertFalse(r['commit_rule'])
    def test_extended_lower_hold_does_not_repeat(self):
        import numpy as np
        u,a=self.setup_arm();u.opposite_guard=True;events=[]
        for i in range(150):
            if i==2:a.update(velocity=np.zeros(3),speed_xy=0.,angle_velocity=0.)
            commit,_,_=u.update({'RIGHT':a},1_000_000_000+i*33_333_333)
            if commit:events.append(commit)
        self.assertEqual(events,['RIGHT'])
