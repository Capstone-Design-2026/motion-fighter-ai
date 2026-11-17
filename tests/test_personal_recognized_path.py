import unittest
from types import SimpleNamespace
from unittest.mock import patch
import numpy as np
from action_engine.types import ActionEvent
from redbool_demo.personal import PersonalGestureEngine

class RecognizedPersonalPathTests(unittest.TestCase):
    def setUp(self):
        self.e=PersonalGestureEngine();self.e.calibrated=True;self.e.identity=('P1','current')
        self.now=1_000_000_000;self.frame=0
        for side,u in self.e.attack.units.items():
            u.baseline={side:dict(position=np.zeros(3),angle=.2,angle3d=1.8)}
    def feed(self,n=1,*,raised=False,commit=False,event_actor='P1',block=False,valid=True,run='current-run'):
        result=None
        for i in range(n):
            self.now+=33_333_333;self.frame+=1
            self.e.tutorial_context(dict(action='JAB',run=run,phase='PRACTICE',sent_ns=self.now),self.now)
            a=dict(position=np.array([.27,.16,0.]) if raised else np.zeros(3),
                   angle=.6 if raised else .2,angle3d=2.4 if raised else 1.8,speed_xy=.7 if raised else .1)
            other=dict(position=np.zeros(3),angle=.2,angle3d=1.8,speed_xy=.1)
            self.e.outbox.pending={}
            if commit and i==0:
                self.e.outbox.pending['event']=ActionEvent('event',event_actor,'current',self.frame,self.now,self.now,
                    self.now+150_000_000,'ATTACK','LEFT',{'coordinate_rule':{'commit_rule':True}},'test')
            with patch('redbool_demo.personal.joint_validity',return_value={'BLOCK':[] if valid else ['missing']}):
                row=self.e._measurement(SimpleNamespace(frame_id=self.frame,capture_ns=self.now),
                    SimpleNamespace(tracking='VALID',movement='NEUTRAL',block=block),{'LEFT':a,'RIGHT':other})
            previous=self.e.trainer.cycle
            value=self.e.trainer.observe(row,self.now)
            if previous is not None and self.e.trainer.cycle is None:self.e.personal_punch_proof.clear()
            if value:self.e._apply_personal(*value);result=value
        return result
    def test_two_verified_low_punch_cycles_fit_bounded_range(self):
        for i in range(2):
            self.feed(8);self.feed(5,raised=True,commit=True);self.feed(8)
            self.assertEqual(len(self.e.trainer.samples['JAB']),i+1)
        self.assertEqual(self.e.profile()['effective_overrides']['JAB'],.28)
        self.assertEqual(self.e.attack.units['RIGHT'].cfg.commit_displacement,.32)
    def test_prompt_or_wrong_actor_does_not_label_low_pose(self):
        for actor in ('P2',None):
            self.setUp()
            for _ in range(2):
                self.feed(8);self.feed(5,raised=True,commit=actor is not None,event_actor=actor);self.feed(8)
            self.assertFalse(self.e.trainer.samples)
    def test_one_peak_hold_and_missing_joints_do_not_make_two_cycles(self):
        self.feed(8);self.feed(1,raised=True,commit=True);self.feed(8)
        self.assertFalse(self.e.trainer.samples)
        self.feed(8);self.feed(5,raised=True,commit=True);self.feed(1,valid=False);self.feed(8)
        self.assertFalse(self.e.trainer.samples)
    def test_completed_proof_is_consumed(self):
        self.feed(8);self.feed(5,raised=True,commit=True);self.feed(8)
        self.feed(5,raised=True);self.feed(8)
        self.assertEqual(len(self.e.trainer.samples['JAB']),1)
    def test_block_new_run_and_calibration_drop_proof(self):
        for kind in ('block','run','C'):
            self.setUp();self.feed(8);self.feed(2,raised=True,commit=True)
            if kind=='C':self.e.request_calibration();self.assertFalse(self.e.personal_punch_proof);continue
            self.feed(1,raised=True,block=kind=='block',run='new' if kind=='run' else 'current-run')
            self.feed(8);self.assertFalse(self.e.trainer.samples)
    def test_x_confirmation_keeps_existing_applied_status(self):
        self.e._apply_personal('JAB',.31);self.e._apply_personal('SPECIAL',1.)
        self.assertIn('PERSONAL RANGE APPLIED',self.e.personal_status)
        self.assertIn('JAB',self.e.profile()['effective_overrides'])
        self.e.request_calibration();self.assertNotIn('PERSONAL RANGE APPLIED',self.e.personal_status)
    def test_raise_timing_requires_two_confirmed_completed_cycles(self):
        for count,confirmed in ((1,True),(2,False),(2,True)):
            self.setUp()
            self.e.trainer.cycle_details['HIGH_KICK']=[dict(peak=.8,duration=.4,speed=1.,confirmed=confirmed)]*count
            self.e._apply_personal('HIGH_KICK',.8)
            self.assertEqual('LEFT' in self.e.personal_fast_raise,count==2 and confirmed)
        self.assertEqual(self.e.profile()['effective_overrides']['HIGH_KICK_DWELL_MS'],120.)
        self.assertNotIn('RIGHT',self.e.personal_fast_raise)

if __name__=='__main__':unittest.main()
