"""Candidate-only safety regressions; no camera/Unity/network and no GT fitting."""
import unittest
from dataclasses import replace
import numpy as np
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.static_special import StaticCrossProof

MS = 1_000_000

def pose(kind='guard', frame=1, stamp=0):
    p=np.zeros((33,3));p[:,0]=.5;p[:,1]=.5
    p[11]=(.65,.30,0);p[12]=(.35,.30,0)
    p[13]=(.65,.60,0);p[14]=(.35,.60,0)
    p[15]=(.60,.30,0);p[16]=(.40,.30,0)
    p[23]=(.62,.78,0);p[24]=(.38,.78,0)
    if kind=='cross':p[15]=(.42,.40,0);p[16]=(.58,.40,0)
    if kind=='block':p[15]=(.60,.10,0);p[16]=(.40,.10,0)
    if kind=='raise':p[15]=(.60,.08,0)
    return PoseObservation('DEV01','static-proof-test',frame,stamp,tuple(map(tuple,p)),(1.,)*33,640,480)

class StaticProofSafetyTests(unittest.TestCase):
    def ready(self):
        e=SixGestureEngine(movement=True);e.request_calibration()
        for i in range(1,51):
            o=pose(frame=i,stamp=i*33*MS);e.process(o,now_ns=o.capture_ns);e.drain_events(o.capture_ns)
        self.assertTrue(e.calibrated);self.assertTrue(e.ready)
        # Establish a measured sustained slow source before a static candidate.
        for _ in range(21):self.assertFalse(self.step(e,kind='guard')[1])
        return e

    def step(self,e,kind='cross',gap=140,age=110,mutation=None,drain=True,auxiliary=False):
        o=pose(kind,e.last_frame+1,e.last_capture+gap*MS)
        if mutation:o=mutation(o)
        now=o.capture_ns+age*MS
        for tick in range(e.last_now+10*MS,now,10*MS):e.tick(tick)
        state=e.process(o,now_ns=now,special_observation=o if auxiliary else None)
        return state,e.drain_events(now)if drain else []

    def prime_proof(self):
        proof=StaticCrossProof()
        for i in range(1,22):
            proof.observe(pose('guard',i,i*140*MS),i*140*MS+110*MS,
                calibrated=True,primary_strict=False,valid=True,latched=False)
        return proof

    def test_new_stationary_proof_uses_eight_fresh_samples_and_keeps_all_original_deadlines(self):
        e=self.ready();events=[]
        for i in range(8):
            state,new=self.step(e);events+=new
            if i<7:self.assertFalse(new)
        self.assertEqual(len(events),1);ev=events[0]
        self.assertEqual(ev.reason['method'],'stationary_cross_dwell')
        self.assertEqual(ev.expires_ns-ev.committed_ns,150*MS)
        self.assertEqual(ev.committed_ns-ev.source_capture_ns,110*MS)
        self.assertEqual((e.config.max_gap_ms,e.config.evidence_age_ms,e.config.safety_timeout_ms),(100,120,150))
        self.assertTrue(e.special.latched)
        # Existing controls expire immediately even though independent proof may
        # retain earlier strict observations up to270ms before a fresh result.
        e.control=replace(e.control,block=True,movement='LEFT',movement_strength=1.)
        state=e.tick(e.last_capture+150*MS)
        self.assertEqual((state.tracking,state.block,state.movement),('LOST',False,'NEUTRAL'))
        self.assertFalse(e.outbox.pending)
        e.tick(e.last_capture+220*MS)
        self.assertFalse(e._continuity_pending)

    def test_fast_normal_cross_stays_original_and_static_requires_two_slow_pairs(self):
        e=self.ready();events=[]
        self.step(e,kind='guard',gap=140,age=5)
        for _ in range(30):events+=self.step(e,gap=33,age=5)[1]
        self.assertEqual([x.reason['method']for x in events],['chest_cross_dwell'])
        proof=StaticCrossProof();stamp=0
        for i in range(1,30):
            stamp+=(140 if i==2 else 90)*MS
            o=pose('cross',i,stamp)
            self.assertFalse(proof.observe(o,stamp+20*MS,calibrated=True,primary_strict=True,valid=True,latched=False))
        self.assertEqual(proof.count,0)

    def test_short_bursts_never_join_across_nonstrict_pose_stale_or_missing_arm(self):
        for failure in ('guard','block','raise','stale','missing'):
            with self.subTest(failure=failure):
                e=self.ready()
                for _ in range(6):self.assertFalse(self.step(e)[1])
                if failure=='stale':self.assertFalse(self.step(e,age=121)[1])
                elif failure=='missing':
                    self.assertFalse(self.step(e,mutation=lambda o:replace(o,visibility=tuple(0. if i==15 else v for i,v in enumerate(o.visibility))))[1])
                else:self.assertFalse(self.step(e,kind=failure)[1])
                for _ in range(7):self.assertFalse(self.step(e)[1])
                self.assertEqual([x.reason['gesture']for x in self.step(e)[1]],['SPECIAL'])

    def test_long_gap_and_expired_proof_need_a_whole_new_eight_samples(self):
        for gap in (151,500):
            e=self.ready()
            for _ in range(6):self.assertFalse(self.step(e)[1])
            self.assertFalse(self.step(e,gap=gap)[1])
            for _ in range(6):self.assertFalse(self.step(e)[1])
            self.assertEqual(len(self.step(e)[1]),1)
        proof=self.prime_proof()
        for i in range(22,28):proof.observe(pose('cross',i,i*140*MS),i*140*MS+110*MS,calibrated=True,primary_strict=True,valid=True,latched=False)
        self.assertEqual(proof.count,6)
        proof.expire(27*140*MS+270*MS)
        self.assertEqual((proof.count,proof.confirmed_ns),(0,0))

    def test_recalibration_new_identity_duplicate_order_and_clock_jump_clear_evidence(self):
        e=self.ready()
        for _ in range(6):self.assertFalse(self.step(e)[1])
        duplicate=pose('cross',e.last_frame,e.last_capture)
        e.process(duplicate,now_ns=e.last_now+MS)
        self.assertEqual(e.static_special.confirmed_ns,0)
        for _ in range(7):self.assertFalse(self.step(e)[1])
        e.request_calibration()
        self.assertEqual(e.static_special.confirmed_ns,0)
        for _ in range(12):self.assertFalse(self.step(e)[1])
        self.assertFalse(e.calibrated)
        proof=self.prime_proof()
        for i in range(22,28):proof.observe(pose('cross',i,i*140*MS),i*140*MS+110*MS,calibrated=True,primary_strict=True,valid=True,latched=False)
        self.assertEqual(proof.count,6)
        proof.expire(27*140*MS)
        self.assertEqual(proof.count,0)
        new=replace(pose('cross',1,4000*MS),capture_epoch='other-person')
        self.assertFalse(proof.observe(new,4110*MS,calibrated=True,primary_strict=True,valid=True,latched=False))
        self.assertEqual(len(proof.capture_gaps),0)
        self.assertEqual(proof.count,0)

    def test_an_expired_unsent_special_never_reappears_until_normal_visible_release(self):
        e=self.ready()
        for _ in range(8):self.step(e,drain=False)
        self.assertEqual(len(e.outbox.pending),1)
        e.tick(e.last_capture+150*MS)
        self.assertFalse(e.outbox.pending)
        for _ in range(12):self.assertFalse(self.step(e)[1])
        self.assertFalse(self.step(e,mutation=lambda o:replace(o,pose_present=False))[1])
        for _ in range(10):self.assertFalse(self.step(e)[1])
        self.assertTrue(e.special.latched)
        # Transition from110ms processing age without reversing arrival time.
        self.step(e,kind='guard',gap=140,age=5)
        for _ in range(7):self.step(e,kind='guard',gap=60,age=5)
        self.assertFalse(e.special.latched)
        events=[]
        for _ in range(8):events+=self.step(e)[1]
        self.assertEqual(len(events),1)

    def test_moving_cross_has_no_static_credit(self):
        e=self.ready();events=[]
        for i in range(30):
            def movement(o):
                p=np.asarray(o.landmarks).copy();p[[15,16],1]+=.05 if i%2 else-.05
                return replace(o,landmarks=tuple(map(tuple,p)))
            events+=self.step(e,mutation=movement)[1]
        self.assertFalse(events)

    def test_same_frame_full_path_never_uses_static_proof_even_if_slow(self):
        e=self.ready()
        for _ in range(30):self.assertFalse(self.step(e,auxiliary=True)[1])
        self.assertEqual(e.static_special.count,0)
        self.assertEqual(len(e.static_special.capture_gaps),0)

    def test_only_static_latch_releases_from_three_fresh_visible_separated_slow_samples(self):
        e=self.ready();events=[]
        for _ in range(8):events+=self.step(e)[1]
        self.assertEqual(len(events),1);self.assertTrue(e.static_latched)
        for i in range(3):
            self.assertFalse(self.step(e,kind='guard')[1])
            self.assertEqual(e.special.latched,i<2)
        self.assertFalse(e.static_latched);self.assertFalse(e.special.owned)
        for _ in range(7):self.assertFalse(self.step(e)[1])
        self.assertEqual(len(self.step(e)[1]),1)
        # An original fast-path X has no static ownership, even if sampling
        # slows down later. It keeps the old release rule without alteration.
        e=self.ready();self.step(e,kind='guard',gap=140,age=5)
        events=[]
        for _ in range(30):events+=self.step(e,gap=33,age=5)[1]
        self.assertEqual([x.reason['method']for x in events],['chest_cross_dwell'])
        self.assertFalse(e.static_latched)
        for _ in range(5):self.assertFalse(self.step(e,kind='guard')[1])
        self.assertTrue(e.special.latched)

    def test_static_release_cannot_bridge_missing_wrist_stale_or_long_gap(self):
        for failure in ('missing','stale','long_gap'):
            with self.subTest(failure=failure):
                e=self.ready()
                for _ in range(8):self.step(e)
                for _ in range(2):self.step(e,kind='guard')
                if failure=='missing':
                    self.step(e,kind='guard',mutation=lambda o:replace(o,visibility=tuple(0. if i==15 else v for i,v in enumerate(o.visibility))))
                elif failure=='stale':self.step(e,kind='guard',age=121)
                else:self.step(e,kind='guard',gap=300)
                self.assertTrue(e.special.latched)
                # Long-gap's current fresh guard starts a new count of one.
                remaining=1 if failure=='long_gap' else 2
                for _ in range(remaining):
                    self.assertFalse(self.step(e,kind='guard')[1]);self.assertTrue(e.special.latched)
                self.assertFalse(self.step(e,kind='guard')[1]);self.assertFalse(e.special.latched)

if __name__=='__main__':unittest.main()
