import unittest
from dataclasses import replace
from types import SimpleNamespace
from unittest.mock import patch
import numpy as np
from action_engine.types import ActionEvent
from redbool_demo.personal import PersonalTrainer, PersonalGestureEngine


class PersonalCycleHandoffTests(unittest.TestCase):
    def setUp(self):
        self.trainer = PersonalTrainer()
        self.now = 1_000_000_000

    def feed(self, n=1, action='JAB', run='same-person', phase='PRACTICE', **row):
        result = None
        for _ in range(n):
            self.now += 50_000_000
            self.trainer.set_context(dict(run=run,action=action,phase=phase,sent_ns=self.now),self.now)
            result = self.trainer.observe(dict(valid=True,rest=False,target=False,
                amount=.31,conflict=False,**row),self.now) or result
        return result

    def rest(self, n=5, action='JAB', **context):
        result = None
        for _ in range(n):
            self.now += 50_000_000
            self.trainer.set_context(dict(run=context.get('run','same-person'),action=action,
                phase=context.get('phase','PRACTICE'),sent_ns=self.now),self.now)
            result = self.trainer.observe(dict(valid=True,rest=True,shared_rest=True,
                target=False,amount=0,conflict=False),self.now) or result
        return result

    def peak(self, n=5, action='JAB', committed=False, phase='PRACTICE'):
        for i in range(n):
            self.now += 50_000_000
            self.trainer.set_context(dict(run='same-person',action=action,phase=phase,sent_ns=self.now),self.now)
            self.trainer.observe(dict(valid=True,rest=False,target=True,amount=.31,
                conflict=False,verified_commit=committed and i==0),self.now)

    def test_shared_guard_survives_next_prompt_without_extra_hold(self):
        self.rest()
        self.peak(action='HIGH_KICK')
        self.rest(action='HIGH_KICK')
        self.assertEqual(len(self.trainer.samples['HIGH_KICK']),1)

    def test_new_run_cannot_inherit_previous_person_guard(self):
        self.rest()
        self.trainer.set_context(dict(run='new-person',action='HIGH_KICK',phase='PRACTICE',sent_ns=self.now),self.now)
        self.assertFalse(self.trainer.armed)
        self.assertIsNone(self.trainer.rest_since)

    def test_x_release_without_shared_guard_does_not_arm_next_punch(self):
        for _ in range(5):
            self.now += 50_000_000
            self.trainer.set_context(dict(run='same-person',action='SPECIAL',phase='PRACTICE',sent_ns=self.now),self.now)
            self.trainer.observe(dict(valid=True,rest=True,target=False,amount=0),self.now)
        self.trainer.set_context(dict(run='same-person',action='JAB',phase='PRACTICE',sent_ns=self.now),self.now)
        self.assertFalse(self.trainer.armed)

    def test_two_v26_commits_still_need_two_measured_returns(self):
        self.peak(committed=True)
        self.assertFalse(self.trainer.samples)
        self.rest()
        self.assertEqual(len(self.trainer.samples['JAB']),1)
        self.peak(committed=True)
        self.assertEqual(self.rest(),('JAB',.31))

    def test_commit_without_independent_geometry_does_not_start_measurement(self):
        self.feed(verified_commit=True)
        self.assertIsNone(self.trainer.cycle)
        self.rest()
        self.assertFalse(self.trainer.samples)

    def test_return_only_commit_and_hold_cannot_become_two_cycles(self):
        self.peak(committed=True,phase='RETURN')
        self.rest(phase='RETURN')
        self.assertFalse(self.trainer.samples)
        self.peak(n=50,committed=True)
        self.rest()
        self.assertEqual(len(self.trainer.samples['JAB']),1)

    def test_one_peak_frame_is_still_not_enough_for_learning(self):
        self.peak(n=1,committed=True)
        self.rest()
        self.assertFalse(self.trainer.samples)

    def test_invalid_pose_discards_commit_started_cycle(self):
        self.peak(committed=True)
        self.now += 50_000_000
        self.trainer.observe(None,self.now)
        self.rest()
        self.assertFalse(self.trainer.samples)

    def test_geometry_measurement_accepts_only_own_current_matching_event(self):
        engine = PersonalGestureEngine()
        engine.calibrated = True
        engine.identity = ('P2','stream')
        engine.trainer.context = {'action':'JAB'}
        anchor = dict(position=np.zeros(3),angle=.4,angle3d=1.5)
        for side,unit in engine.attack.units.items(): unit.baseline={side:anchor}
        arms = {
            'LEFT':dict(position=np.array([.4,0.,0.]),angle=1.,angle3d=2.5,speed_xy=1.),
            'RIGHT':dict(position=np.zeros(3),angle=.4,angle3d=1.5,speed_xy=.1)}
        obs=SimpleNamespace(frame_id=42)
        state=SimpleNamespace(tracking='VALID',movement='NEUTRAL',block=False)
        event=ActionEvent('one','P2','stream',42,10,20,150_000_020,'ATTACK','LEFT',{},'v26')
        with patch('redbool_demo.personal.joint_validity',return_value={'BLOCK':[]}):
            for variant in [replace(event,source_frame_id=41),replace(event,actor_id='P1'),
                            replace(event,capture_epoch='old'),replace(event,side='RIGHT')]:
                engine.outbox.pending={'one':variant}
                row=engine._measurement(obs,state,arms)
                self.assertTrue(row['target'])
                self.assertFalse(row['verified_commit'])
            engine.outbox.pending={'one':event}
            self.assertTrue(engine._measurement(obs,state,arms)['verified_commit'])


if __name__ == '__main__': unittest.main()
