"""Reviewed development recordings; not held-out participant accuracy."""
from dataclasses import replace
import json,os,unittest
from pathlib import Path
import numpy as np
from action_engine.experimental import AblationConfig
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine,SixAttack
class ForeshortenedSafetyTests(unittest.TestCase):
    def make(self):
        u=SixAttack(AblationConfig(image_3d_extension=True));u.baseline={'LEFT':{'position':np.zeros(3),'angle':.1,'angle3d':2.0}};u.state='CANDIDATE';u.now=1_000_000_000;u.entered=850_000_000;u.opposite_guard=True;u.independent_elbow_opening=True
        a={'position':np.array([.38,.28,0.]),'velocity':np.array([-1.,0.,0.]),'speed_xy':1.,'angle':.2,'angle_velocity':0.,'angle3d':2.25}
        return u,a
    def test_established_onset_can_commit_after_projected_peak(self):
        u,a=self.make();self.assertTrue(u.candidates({'LEFT':a})['LEFT']['commit_rule'])
    def test_cannot_start_repeat_or_override_recent_block(self):
        for state in ['READY','ACTIVE','EXTENDED','RETURNING','PREPARING']:
            u,a=self.make();u.state=state;self.assertFalse(u.candidates({'LEFT':a})['LEFT']['foreshortened_low_commit'])
        u,a=self.make();u.folded_low_block_until=u.now+1;self.assertFalse(u.candidates({'LEFT':a})['LEFT']['commit_rule'])
    def test_requires_onset_duration_other_guard_and_elbow_opening(self):
        for what in ['early','other','elbow','vertical_drop','shared_depth']:
            u,a=self.make()
            if what=='shared_depth':u.independent_elbow_opening=False
            if what=='early':u.entered=u.now-33_000_000
            if what=='other':u.opposite_guard=False
            if what=='elbow':a['angle3d']=2.05
            if what=='vertical_drop':a['position']=np.array([.01,.4,0.])
            self.assertFalse(u.candidates({'LEFT':a})['LEFT']['foreshortened_low_commit'],what)

SESSION=os.environ.get('REDBOOL_THREE_PERSON_SESSION')
@unittest.skipUnless(SESSION,'Reviewed three-person capture required')
class ThreePersonRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        s=Path(SESSION);m=json.loads((s/'manifest.json').read_text());marks={x['event_frame_id']:x['label'] for x in map(json.loads,(s/'annotations.jsonl').read_text().splitlines())}
        clocks={x['frame_id']:x['state']['timestamp_ns'] for x in map(json.loads,(s.parent.parent/'trace.jsonl').read_text().splitlines()) if x['type']=='frame'}
        e=SixGestureEngine(movement=True);cls.events=[];origin=None
        with (s/'frames.jsonl').open() as stream:
            for line in stream:
                f=json.loads(line);o=PoseObservation.from_record(f,m);origin=o.capture_ns if origin is None else origin;c=f['processing_metadata']['cross_pose'];aux=replace(o,landmarks=tuple(map(tuple,c['landmarks'])) or None,visibility=tuple(c['visibility']) or None,pose_present=bool(c['landmarks']))
                now=clocks[o.frame_id];state=e.process(o,now_ns=now,special_observation=aux)
                cls.events.extend(((v.source_capture_ns-origin)/1e9,v.reason['gesture'],state.movement) for v in e.drain_events(now));e.take_logs()
                if o.frame_id in marks:e.request_calibration();e.calibrating=marks[o.frame_id]=='CALIBRATE'
    def gestures(self,lo,hi):return [g for t,g,m in self.events if lo<=t<=hi]
    def test_two_initial_short_punches_recovered(self):
        for lo,hi in [(286.1,286.9),(287.1,287.9)]:self.assertEqual(self.gestures(lo,hi),['LEFT_PUNCH'])
    def test_cross_recovered_without_other_attacks(self):
        for lo,hi in [(348,350.4)]:self.assertEqual(self.gestures(lo,hi),['SPECIAL'])
    def test_raise_after_high_block_is_not_foreshortened_punch(self):self.assertEqual(self.gestures(300.5,302),['LEFT_RAISE'])
    def test_held_extended_arm_and_reviewed_rest_remain_quiet(self):
        for lo,hi in [(292.2,296.9),(0,30),(242,270),(366,374),(488,521)]:self.assertEqual(self.gestures(lo,hi),[],(lo,hi))
    def test_each_action_still_fires_while_leaning(self):
        expected=[(37.5,38.5,'LEFT_PUNCH'),(40.5,41.1,'RIGHT_PUNCH'),(94.5,95.3,'RIGHT_RAISE'),(97.1,98.0,'LEFT_RAISE')]
        for lo,hi,g in expected:self.assertEqual([(a,m!='NEUTRAL') for t,a,m in self.events if lo<t<hi],[(g,True)])

class InwardFistCrossTests(unittest.TestCase):
    def observation(self):
        from test_chest_special import pose
        o=pose();p=np.array(o.landmarks)
        p[15]=(.51,.42,0);p[16]=(.49,.42,0)
        p[[17,19,21]]=(.49,.36,0);p[[18,20,22]]=(.51,.36,0)
        return replace(o,landmarks=tuple(map(tuple,p)))
    def test_inward_fists_meet_even_when_estimated_wrists_do_not_cross(self):
        from redbool_demo.special import chest_cross
        self.assertEqual(chest_cross(self.observation(),AblationConfig()),(True,True))
    def test_merely_close_fists_or_missing_tip_evidence_are_not_cross(self):
        from redbool_demo.special import chest_cross
        o=self.observation();p=np.array(o.landmarks);p[[17,19,21],0]=.51;p[[18,20,22],0]=.49
        self.assertFalse(chest_cross(replace(o,landmarks=tuple(map(tuple,p))),AblationConfig())[1])
        vis=list(o.visibility);vis[19]=.1
        self.assertFalse(chest_cross(replace(o,visibility=tuple(vis)),AblationConfig())[1])
