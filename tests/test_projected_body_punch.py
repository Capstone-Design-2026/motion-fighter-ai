"""Bounded projection completion: synthetic geometry, not accuracy labels."""
import unittest
import numpy as np
from redbool_demo.six_engine import SixAttack, SixGestureEngine

class ProjectedBodyPunchTests(unittest.TestCase):
    def unit(self, side='LEFT'):
        u=SixAttack(SixGestureEngine().config);u.state='READY';u.opposite_guard=True
        u.baseline={side:dict(position=np.array([0.,-.1,0.]),angle=.03,angle3d=1.96)}
        return u
    def arm(self, dx, dy, angle, angle3d, side='LEFT', outward=True):
        sign=1. if side=='LEFT' else -1.
        return dict(position=np.array([sign*dx,-.1+dy,.8]),
            velocity=np.array([sign*(2. if outward else -2.),0.,0.]),speed_xy=2.,
            angle=angle,angle3d=angle3d,angle_velocity=1.)
    def start(self,u,side='LEFT'):
        self.assertIsNone(u.update({side:self.arm(.38,.14,.34,2.08,side)},1_000_000_000)[0])
        self.assertEqual(u.state,'CANDIDATE')
        self.assertIsNone(u.update({side:self.arm(.77,.47,1.88,1.94,side)},1_050_000_000)[0])
        return self.arm(.87,.36,2.18,1.78,side)
    def test_two_observations_complete_existing_onset_on_either_arm(self):
        for side in ('LEFT','RIGHT'):
            with self.subTest(side=side):
                u=self.unit(side);a=self.start(u,side);commit,signals,_=u.update({side:a},1_150_000_000)
                self.assertEqual(commit,side);self.assertTrue(signals[side]['projected_body_confirmation'])
                self.assertEqual(u.projected_body_samples,[])
    def test_one_pose_and_repeated_queries_cannot_manufacture_confirmation(self):
        u=self.unit();a=self.start(u);u.projected_body_samples.clear()
        for _ in range(20):self.assertFalse(u.candidates({'LEFT':a})['LEFT']['commit_rule'])
        self.assertIsNone(u.update({'LEFT':a},1_150_000_000)[0])
        self.assertIsNone(u.update({'LEFT':a},1_150_000_000)[0])
        self.assertEqual(len(u.projected_body_samples),1)
    def test_confirmation_minimum_interval_and_expiry(self):
        for stamp in (1_079_999_999,1_170_000_001):
            with self.subTest(stamp=stamp):
                u=self.unit();a=self.start(u);self.assertIsNone(u.update({'LEFT':a},stamp)[0])
    def test_opposite_guard_block_and_reserved_gesture_gates(self):
        for gate in ('other_hand','raise_or_cross','block'):
            with self.subTest(gate=gate):
                u=self.unit();a=self.start(u)
                if gate=='other_hand':u.opposite_guard=False
                if gate=='raise_or_cross':u.inhibited=True
                self.assertIsNone(u.update({'LEFT':a},1_150_000_000,block=gate=='block')[0])
                self.assertEqual(u.projected_body_samples,[])
    def test_small_travel_drop_weak_opening_and_inward_return_rejected(self):
        for dx,dy,a2,outward in [(.4,.36,2.18,True),(.1,.5,2.18,True),(.87,.36,1.7,True),(.87,.36,2.18,False)]:
            with self.subTest(pose=(dx,dy,a2,outward)):
                u=self.unit();self.start(u);a=self.arm(dx,dy,a2,1.78,outward=outward)
                self.assertIsNone(u.update({'LEFT':a},1_150_000_000)[0])
    def test_invalidation_clears_evidence_and_requires_rearm(self):
        u=self.unit();a=self.start(u);u.invalidate(1_060_000_000)
        self.assertEqual(u.projected_body_samples,[])
        self.assertIsNone(u.update({'LEFT':a},1_150_000_000)[0]);self.assertNotEqual(u.state,'ACTIVE')
    def test_recalibration_replaces_evidence(self):
        e=SixGestureEngine();u=self.unit();self.start(u);e.attack.units['LEFT']=u;e.request_calibration()
        self.assertEqual(e.attack.units['LEFT'].projected_body_samples,[]);self.assertFalse(e.calibrated)
    def ready_engine(self):
        from test_chest_special import pose
        e=SixGestureEngine();e.request_calibration()
        for i in range(1,51):
            o=pose(frame=i,stamp=i*33_000_000)
            e.process(o,now_ns=o.capture_ns);e.drain_events(o.capture_ns)
        self.assertTrue(e.ready)
        e.attack.units['LEFT'].projected_body_samples=[e.last_now]
        return e

    def test_engine_gap_stale_arm_loss_and_identity_change_clear_samples(self):
        from dataclasses import replace
        from test_chest_special import pose
        for condition in ('gap','stale','missing_arm','person'):
            with self.subTest(condition=condition):
                e=self.ready_engine()
                gap=101_000_000 if condition=='gap' else 33_000_000
                o=pose(frame=e.last_frame+1,stamp=e.last_capture+gap)
                if condition=='missing_arm':
                    o=replace(o,visibility=tuple(0. if i==15 else v for i,v in enumerate(o.visibility)))
                if condition=='person':o=replace(o,capture_epoch='another-person')
                now=o.capture_ns+(121_000_000 if condition=='stale' else 0)
                e.process(o,now_ns=now)
                self.assertEqual(e.attack.units['LEFT'].projected_body_samples,[])
                self.assertFalse(e.drain_events(now))

    def test_output_expiry_never_publishes_evidence_and_final_deadline_clears(self):
        e=self.ready_engine();stamp=e.last_capture
        state=e.tick(stamp+150_000_000)
        self.assertEqual(state.tracking,'LOST');self.assertFalse(e.ready)
        self.assertFalse(e.drain_events(stamp+160_000_000))
        e.tick(stamp+220_000_000)
        self.assertEqual(e.attack.units['LEFT'].projected_body_samples,[])

    def test_extended_sweep_does_not_repeat_without_physical_return(self):
        u=self.unit();a=self.start(u);self.assertEqual(u.update({'LEFT':a},1_150_000_000)[0],'LEFT')
        events=[]
        for i in range(100):
            a=self.arm(.87+(i%2)*.05,.36,2.18,1.78,outward=i%2==0)
            result=u.update({'LEFT':a},1_180_000_000+i*33_333_333)[0]
            if result:events.append(result)
        self.assertEqual(events,[])
if __name__=='__main__':unittest.main()
