"""Early confirmation uses multiple fresh observations and the full commit gate."""
import unittest
import numpy as np
from redbool_demo.six_engine import SixAttack,SixGestureEngine

class PunchConfirmationTests(unittest.TestCase):
    def make(self):
        u=SixAttack(SixGestureEngine().config);u.state='READY';u.opposite_guard=True
        u.baseline={'LEFT':dict(position=np.zeros(3),angle=.2,angle3d=1.8)}
        return u
    def arm(self,x,angle3d,speed=1.2):
        return dict(position=np.array([x,0.,-.1]),velocity=np.array([speed,0.,0.]),
                    speed_xy=speed,angle=.6,angle3d=angle3d,angle_velocity=1.)
    def test_two_fresh_extensions_commit_on_first_fully_proved_peak(self):
        u=self.make()
        self.assertIsNone(u.update({'LEFT':self.arm(.15,2.0)},1_000_000_000)[0])
        c,r,_=u.update({'LEFT':self.arm(.4,2.5)},1_033_333_333)
        self.assertEqual(c,'LEFT');self.assertTrue(r['LEFT']['commit_rule'])
        self.assertTrue(r['LEFT']['confirmed_lead_in']);self.assertEqual(u.state,'ACTIVE')
        for i in range(20):self.assertIsNone(u.update({'LEFT':self.arm(.4,2.5,0)},1_066_666_666+i*33_333_333)[0])
    def test_one_frame_peak_keeps_existing_candidate_confirmation(self):
        u=self.make();self.assertIsNone(u.update({'LEFT':self.arm(.4,2.5)},1_000_000_000)[0])
        self.assertEqual(u.state,'CANDIDATE')
    def test_gap_block_permission_or_missing_support_cannot_fast_commit(self):
        for kind in ('gap','block','permission','support','invalidated'):
            u=self.make();u.update({'LEFT':self.arm(.15,2.0)},1_000_000_000)
            if kind=='support':u.opposite_guard=False
            if kind=='invalidated':u.invalidate(1_010_000_000)
            stamp=1_100_000_000 if kind=='gap' else 1_033_333_333
            c,_,_=u.update({'LEFT':self.arm(.4,2.5)},stamp,block=kind=='block',allow_commit=kind!='permission')
            self.assertIsNone(c,kind)
    def test_no_extra_vote_from_candidates_queries(self):
        u=self.make();u.update({'LEFT':self.arm(.15,2.0)},1_000_000_000)
        for _ in range(30):u.candidates({'LEFT':self.arm(.4,2.5)})
        self.assertIsNone(u.update({'LEFT':self.arm(.4,2.5)},1_000_000_000)[0])
    def test_return_clears_previous_lead(self):
        u=self.make();u.update({'LEFT':self.arm(.15,2.0)},1_000_000_000)
        u.update({'LEFT':self.arm(0,1.8,0)},1_033_333_333)
        self.assertIsNone(u.update({'LEFT':self.arm(.4,2.5)},1_066_666_666)[0])

class RaiseConfirmationTests(unittest.TestCase):
    def proof(self,**changes):
        e=SixGestureEngine();e.personal_fast_raise={'LEFT'}
        a=dict(position=np.array([.1,-.8,0.]),velocity=np.zeros(3),angle=1.,angle3d=2.)
        a.update(changes)
        other=dict(position=np.zeros(3),velocity=np.zeros(3),angle=.6,angle3d=2.)
        return e,e._raise_fast_proof('LEFT_RAISE',{'LEFT':a,'RIGHT':other},
                                   {'LEFT':a['position'],'RIGHT':other['position']})
    def test_clear_high_bent_raise_is_eligible(self):self.assertTrue(self.proof()[1])
    def test_small_or_extended_or_sideways_pose_keeps_original_dwell(self):
        for change in ({'position':np.array([.1,-.45,0.])},{'angle3d':2.7},
                       {'position':np.array([.4,-.8,0.])},{'angle':2.0}):
            self.assertFalse(self.proof(**change)[1])
    def test_untrained_side_keeps_original_dwell(self):
        e,_=self.proof();e.personal_fast_raise.clear()
        arms={'LEFT':dict(angle=1.,angle3d=2.,position=np.array([.1,-.8,0.])),
              'RIGHT':dict(angle=.6,position=np.zeros(3))}
        self.assertFalse(e._raise_fast_proof('LEFT_RAISE',arms,{s:a['position'] for s,a in arms.items()}))
    def test_other_hand_pending_punch_keeps_original_raise_order(self):
        for pending in ('candidate','lead'):
            e,_=self.proof();unit=e.attack.units['RIGHT']
            if pending=='candidate':unit.state='CANDIDATE'
            else:unit.punch_lead={'stamp':1}
            arms={'LEFT':dict(angle=1.,angle3d=2.,position=np.array([.1,-.8,0.])),
                  'RIGHT':dict(angle=.6,position=np.zeros(3))}
            self.assertFalse(e._raise_fast_proof('LEFT_RAISE',arms,{s:a['position'] for s,a in arms.items()}))

if __name__=='__main__':unittest.main()
