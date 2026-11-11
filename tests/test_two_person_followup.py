"""Reviewed development captures, not participant-held-out accuracy."""
from dataclasses import replace
import json,os,unittest
from pathlib import Path
import numpy as np
from action_engine.experimental import AblationConfig
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixAttack,SixGestureEngine
class CompactPreparationTests(unittest.TestCase):
    def make(self,state='PREPARING'):
        u=SixAttack(AblationConfig());u.state=state;u.baseline={'RIGHT':{'position':np.array([0.,0.,-3.]),'angle':.1,'angle3d':2.1}};u.unreturned_punch=False;u.now=1_000_000_000;u.folded_low_block_until=2_000_000_000
        arm={'position':np.array([.1,.25,-3.]),'angle':.15,'angle3d':2.05,'speed_xy':1.7}
        return u,arm
    def test_folded_preparation_tolerates_settling_motion(self):
        u,a=self.make();self.assertTrue(u.neutral({'RIGHT':a}))
    def test_no_recent_block_cannot_use_faster_preparation(self):
        u,a=self.make();u.folded_low_block_until=u.now;self.assertFalse(u.neutral({'RIGHT':a}))
    def test_returning_attack_or_unreturned_hold_cannot_use_faster_rearm(self):
        for state in ['RETURNING','EXTENDED','ACTIVE','READY']:
            u,a=self.make(state);self.assertFalse(u.neutral({'RIGHT':a}),state)
        u,a=self.make();u.unreturned_punch=True;self.assertFalse(u.neutral({'RIGHT':a}))
    def test_relaxation_still_requires_compact_personal_elbow_and_height(self):
        for delta in [dict(angle=.8),dict(angle3d=2.5),dict(position=np.array([.5,.25,-3.])),dict(position=np.array([.1,.6,-3.])),dict(speed_xy=2.1)]:
            u,a=self.make();a.update(delta);self.assertFalse(u.neutral({'RIGHT':a}),delta)

SESSION=os.environ.get('REDBOOL_TWO_PERSON_SESSION')
@unittest.skipUnless(SESSION,'Two-person review recording required')
class TwoPersonRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        s=Path(SESSION);m=json.loads((s/'manifest.json').read_text());marks={x['event_frame_id']:x['label'] for x in map(json.loads,(s/'annotations.jsonl').open())};clocks={x['frame_id']:x['state']['timestamp_ns'] for x in map(json.loads,(s.parent.parent/'trace.jsonl').open()) if x['type']=='frame'}
        cache=os.environ.get('REDBOOL_TWO_PERSON_AUX_IMAGE');cache={x['frame']:x for x in map(json.loads,Path(cache).open())} if cache else {}
        e=SixGestureEngine(movement=True);cls.events=[];origin=None
        for f in map(json.loads,(s/'frames.jsonl').open()):
            o=PoseObservation.from_record(f,m);origin=o.capture_ns if origin is None else origin;c=cache.get(o.frame_id,f['processing_metadata']['cross_pose']);aux=replace(o,landmarks=tuple(map(tuple,c['landmarks'])) or None,visibility=tuple(c['visibility']) or None,pose_present=bool(c['landmarks']));now=clocks[o.frame_id];e.process(o,now_ns=now,special_observation=aux)
            cls.events.extend(((v.source_capture_ns-origin)/1e9,v.reason.get('gesture',v.side+'_PUNCH')) for v in e.drain_events(now));e.take_logs()
            if o.frame_id in marks:e.request_calibration();e.calibrating=marks[o.frame_id]=='CALIBRATE'
    def gestures(self,a,b):return [g for t,g in self.events if a<=t<=b]
    def test_block_then_punch_is_recovered(self):self.assertEqual(self.gestures(211.5,212.7),['RIGHT_PUNCH'])
    def test_existing_late_block_followup_preserved(self):self.assertEqual(self.gestures(450.95,451.8),['RIGHT_PUNCH'])
    def test_special_followups_and_original_six_specials_preserved(self):
        for t in [96.174,287.279,292.075,297.603,351.344,438.129]:self.assertEqual(self.gestures(t-.5,t+.5),['SPECIAL'])
        for t,g in [(100.424,'LEFT_RAISE'),(289.702,'LEFT_RAISE'),(300.246,'RIGHT_RAISE'),(354.161,'RIGHT_PUNCH'),(355.74,'LEFT_PUNCH')]:self.assertEqual(self.gestures(t-.4,t+.4),[g])
    def test_no_extra_opposite_hand_near_left_punch(self):self.assertEqual(self.gestures(261,262.2),['LEFT_PUNCH'])
    def test_reviewed_rest_block_and_person_change_have_no_attacks(self):
        for a,b in [(0,47),(53,59),(120,162),(224.9,229),(454,460)]:self.assertEqual(self.gestures(a,b),[],(a,b))
