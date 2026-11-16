"""A geometric transition must not erase a still-valid measured punch cycle."""
from types import SimpleNamespace
from unittest.mock import patch
import unittest
import numpy as np
from redbool_demo.personal import PersonalGestureEngine


class PersonalDiagonalCycleTests(unittest.TestCase):
    def make(self, action='JAB'):
        e=PersonalGestureEngine()
        e.calibrated=True;e.identity=('person','current')
        for side,u in e.attack.units.items():
            u.baseline={side:dict(position=np.zeros(3),angle=.2,angle3d=1.9)}
        self.engine=e;self.now=1_000_000_000;self.frame=0;self.action=action
        return e

    def feed(self, x=0., y=0., *, angle3d=1.9, angle=.2, speed=.1, block=False):
        e=self.engine;self.now+=50_000_000;self.frame+=1
        side='LEFT' if self.action=='JAB' else 'RIGHT'
        other='RIGHT' if side=='LEFT' else 'LEFT'
        e.trainer.set_context(dict(run='same-person',action=self.action,phase='PRACTICE',sent_ns=self.now),self.now)
        arms={side:dict(position=np.array([x,y,0.]),angle=angle,angle3d=angle3d,speed_xy=speed),
              other:dict(position=np.zeros(3),angle=.2,angle3d=1.9,speed_xy=.1)}
        with patch('redbool_demo.personal.joint_validity',return_value={'BLOCK':[]}):
            row=e._measurement(SimpleNamespace(frame_id=self.frame),
                SimpleNamespace(tracking='VALID',movement='NEUTRAL',block=block),arms)
        return row,e.trainer.observe(row,self.now)

    def rest(self):
        result=None
        for _ in range(5):result=self.feed()[1] or result
        return result

    def test_diagonal_transition_can_precede_two_strict_punch_cycles(self):
        for action,sign in [('JAB',1),('STRAIGHT',-1)]:
            self.make(action);self.rest()
            for cycle in range(2):
                row,_=self.feed(sign*.473,-.365,angle=0.79,angle3d=2.4,speed=1.4)
                self.assertFalse(row['target'])
                self.assertFalse(row['onset'])
                self.assertFalse(row['conflict'])
                self.assertTrue(self.engine.trainer.armed)
                for _ in range(4):
                    row,_=self.feed(sign*.70,-.25,angle=1.,angle3d=2.45,speed=1.5)
                    self.assertTrue(row['target'])
                result=self.rest()
                self.assertEqual(len(self.engine.trainer.samples[action]),cycle+1)
            self.assertEqual(result[0],action)
            self.assertFalse(self.engine.personal_changes)  # measurement alone cannot emit/fix an action

    def test_upward_raise_cancels_even_if_a_lateral_pose_follows(self):
        self.make();self.rest()
        row,_=self.feed(.15,-.60,angle=1.,angle3d=2.4,speed=1.4)
        self.assertTrue(row['conflict']);self.assertFalse(self.engine.trainer.armed)
        for _ in range(4):self.feed(.7,-.25,angle=1.,angle3d=2.45,speed=1.5)
        self.rest();self.assertFalse(self.engine.trainer.samples)

    def test_diagonal_transition_alone_never_trains(self):
        self.make();self.rest()
        for _ in range(5):self.feed(.473,-.365,angle=.79,angle3d=2.4,speed=1.4)
        self.rest();self.assertFalse(self.engine.trainer.samples)

    def test_block_remains_a_conflict_during_diagonal_transition(self):
        self.make();self.rest()
        row,_=self.feed(.473,-.365,angle=.79,angle3d=2.4,speed=1.4,block=True)
        self.assertTrue(row['conflict']);self.assertFalse(self.engine.trainer.armed)

    def test_special_remains_a_conflict_during_diagonal_transition(self):
        self.make();self.rest();self.engine.special=SimpleNamespace(owned=True,latched=False)
        row,_=self.feed(.473,-.365,angle=.79,angle3d=2.4,speed=1.4)
        self.assertTrue(row['conflict']);self.assertFalse(self.engine.trainer.armed)


if __name__=='__main__':unittest.main()
