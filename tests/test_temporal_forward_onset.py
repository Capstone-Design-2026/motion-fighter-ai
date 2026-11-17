"""Research contract: timing evidence only acquires onset, never commits alone."""
import unittest
import numpy as np
from redbool_demo.temporal_forward import TemporalForwardOnset
from redbool_demo.six_engine import SixGestureEngine,SixAttack

class TemporalForwardOnsetTests(unittest.TestCase):
    def setUp(self):
        self.base={'position':np.array([-.2058,.2067,-2.9419]),'angle':.2111,'angle3d':2.0586}
        self.start=1_000_000_000
    def arm(self,i):
        rows=[(-.2256,-.2616,-.4116,.5443,2.1966,.54),(-.2389,-.2987,-.4549,.6372,2.3074,.93),(-.2487,-.2860,-.4929,.7130,2.4023,.39),(-.2990,-.2796,-.5373,.7490,2.3692,1.20)]
        x,y,z,a2,a3,speed=rows[i]
        return {'upper_projection':(.818,.696,.627,.670)[i], 'position':self.base['position']+np.array([x,y,z]),'angle':a2,'angle3d':a3,'velocity':np.array([-speed,0.,-1.]),'speed_xy':speed,'angle_velocity':2.}
    def submit(self,h,i,**kw):
        return h.observe(self.arm(i),self.base,self.start+i*41_666_667,available=kw.get('available',True),forward=kw.get('forward',i==2),independent=kw.get('independent',i==2),guard_origin=kw.get('guard_origin',i==0))
    def test_three_real_samples_and_existing_current_forward_are_required(self):
        h=TemporalForwardOnset();self.assertFalse(self.submit(h,0));self.assertFalse(self.submit(h,1));self.assertTrue(self.submit(h,2))
    def test_cannot_start_from_depth_or_extension_static_hold(self):
        h=TemporalForwardOnset()
        for i in range(100):self.assertFalse(h.observe(self.arm(2),self.base,self.start+i*33_000_000,available=True,forward=True,independent=True,guard_origin=True))
    def test_current_existing_routing_and_independent_opening_gate(self):
        for flag in ('forward','independent'):
            h=TemporalForwardOnset();self.submit(h,0);self.submit(h,1);self.assertFalse(self.submit(h,2,**{flag:False}))
    def test_earlier_bent_guard_is_required(self):
        h=TemporalForwardOnset()
        for i in range(3):
            a=self.arm(i);a['angle']+=.25
            self.assertFalse(h.observe(a,self.base,self.start+i*41_666_667,available=True,forward=True,independent=True,guard_origin=True))
    def test_single_joint_depth_jitter_is_not_extension(self):
        for field in ('angle','angle3d'):
            h=TemporalForwardOnset()
            for i in range(3):
                a=self.arm(i);a[field]=self.arm(0)[field]
                self.assertFalse(h.observe(a,self.base,self.start+i*41_666_667,available=True,forward=True,independent=True,guard_origin=True))
    def test_intermediate_reversal_cancels_continuous_opening(self):
        for field in ('angle','angle3d','depth'):
            h=TemporalForwardOnset();self.submit(h,0)
            a=self.arm(1)
            if field=='depth':a['position'][2]=self.arm(0)['position'][2]+.1
            else:a[field]=self.arm(0)[field]-.1
            self.assertFalse(h.observe(a,self.base,self.start+41_666_667,available=True,forward=True,independent=True,guard_origin=True));self.assertFalse(self.submit(h,2))
    def test_downward_upward_and_wide_sweep_excluded(self):
        for axis,value in ((0,.36),(1,-.4),(1,.121)):
            h=TemporalForwardOnset();self.submit(h,0);self.submit(h,1);a=self.arm(2);a['position'][axis]=self.base['position'][axis]+value
            self.assertFalse(h.observe(a,self.base,self.start+83_333_334,available=True,forward=True,independent=True,guard_origin=True))
    def test_same_frame_out_of_order_and_gap_clear_proof(self):
        for stamp in (self.start+41_666_667,self.start,self.start+141_666_668):
            h=TemporalForwardOnset();self.submit(h,0);self.submit(h,1)
            self.assertFalse(h.observe(self.arm(2),self.base,stamp,available=True,forward=True,independent=True,guard_origin=True));self.assertEqual(len(h.samples),0)
    def test_reset_and_unavailable_delete_pending_proof(self):
        for explicit in (False,True):
            h=TemporalForwardOnset();self.submit(h,0);self.submit(h,1)
            if explicit:h.reset()
            else:self.submit(h,2,available=False)
            self.assertFalse(self.submit(h,2));self.assertIsNone(h.stamp)
    def unit(self):
        u=SixAttack(SixGestureEngine().config);u.state='READY';u.baseline={'RIGHT':self.base};u.opposite_guard=True
        return u
    def step(self,u,i):
        u.forward_hint=i==2;u.independent_elbow_opening=i>=2;u.upper_arm_projection=self.arm(i)['upper_projection']
        return u.update({'RIGHT':self.arm(i)},self.start+i*41_666_667)
    def test_onset_still_needs_original_later_commit(self):
        u=self.unit()
        for i in range(3):self.assertIsNone(self.step(u,i)[0])
        self.assertEqual(u.state,'CANDIDATE');self.assertTrue(u.temporal_forward_pending)
        event,rules,_=self.step(u,3);self.assertEqual(event,'RIGHT');self.assertTrue(rules['RIGHT']['commit_rule']);self.assertTrue(u.centered_depth_committed)
    def test_block_reservation_or_invalidated_arm_does_not_start(self):
        for kind in ('block','inhibited','opposite','invalidated'):
            u=self.unit();self.step(u,0);self.step(u,1);u.forward_hint=True;u.independent_elbow_opening=True
            if kind=='inhibited':u.inhibited=True
            if kind=='opposite':u.opposite_guard=False
            if kind=='invalidated':u.invalidate(self.start+60_000_000)
            self.assertIsNone(u.update({'RIGHT':self.arm(2)},self.start+83_333_334,block=kind=='block')[0]);self.assertNotEqual(u.state,'CANDIDATE')
    def test_held_new_punch_after_visibility_loss_requires_physical_return(self):
        u=self.unit()
        for i in range(4):self.step(u,i)
        u.invalidate(self.start+150_000_000);u.forward_hint=False;u.independent_elbow_opening=False
        for i in range(100):
            a=self.arm(3);a['speed_xy']=0.;a['velocity']=np.zeros(3)
            self.assertIsNone(u.update({'RIGHT':a},self.start+200_000_000+i*33_000_000)[0]);self.assertNotEqual(u.state,'READY')
    def test_candidates_queries_cannot_add_votes(self):
        u=self.unit();self.step(u,0);self.step(u,1);u.forward_hint=True;u.independent_elbow_opening=True
        for _ in range(30):u.candidates({'RIGHT':self.arm(2)})
        self.assertEqual(len(u.temporal_forward.samples),2);self.assertIsNone(u.temporal_forward.stamp)

    def test_commit_permission_and_existing_candidate_deadline_are_preserved(self):
        u=self.unit()
        for i in range(3):self.step(u,i)
        u.forward_hint=False
        self.assertIsNone(u.update({'RIGHT':self.arm(3)},self.start+125_000_001,allow_commit=False)[0])
        self.assertEqual(u.state,'CANDIDATE')
        self.assertIsNone(u.update({'RIGHT':self.arm(3)},self.start+800_000_000)[0])
        self.assertFalse(u.temporal_forward_pending)

    def ready_engine(self):
        from test_chest_special import pose
        e=SixGestureEngine();e.request_calibration()
        for i in range(1,51):
            o=pose(frame=i,stamp=i*33_000_000);e.process(o,now_ns=o.capture_ns);e.drain_events(o.capture_ns)
        self.assertTrue(e.ready)
        e.attack.units['RIGHT'].temporal_forward.samples.append((e.last_capture,.4,2.1,-.4))
        return e

    def test_engine_stale_gap_joint_loss_and_new_person_clear_evidence(self):
        from dataclasses import replace
        from test_chest_special import pose
        for condition in ('gap','stale','arm','person'):
            e=self.ready_engine();stamp=e.last_capture+(101_000_000 if condition=='gap' else 33_000_000)
            o=pose(frame=e.last_frame+1,stamp=stamp)
            if condition=='arm':o=replace(o,visibility=tuple(0. if i==16 else v for i,v in enumerate(o.visibility)))
            if condition=='person':o=replace(o,capture_epoch='new-person')
            now=stamp+(121_000_000 if condition=='stale' else 0)
            e.process(o,now_ns=now)
            self.assertEqual(len(e.attack.units['RIGHT'].temporal_forward.samples),0,condition)
            self.assertFalse(e.drain_events(now))

    def test_calibration_and_watchdog_do_not_reuse_old_onset_proof(self):
        e=self.ready_engine();stamp=e.last_capture
        state=e.tick(stamp+150_000_000);self.assertEqual(state.tracking,'LOST');self.assertFalse(e.drain_events(stamp+160_000_000))
        e.tick(stamp+220_000_000);self.assertEqual(len(e.attack.units['RIGHT'].temporal_forward.samples),0)
        e=self.ready_engine();e.request_calibration();self.assertEqual(len(e.attack.units['RIGHT'].temporal_forward.samples),0)

    def test_projection_without_shortening_or_with_reversal_cannot_seed(self):
        for projection in ((.8,.8,.8),(.818,.60,.627),(.7,.65,.627)):
            h=TemporalForwardOnset()
            for i in range(3):
                self.assertFalse(h.observe(self.arm(i),self.base,self.start+i*41_666_667,available=True,forward=True,independent=True,guard_origin=True,upper_projection=projection[i]))

    def test_missing_or_invalid_current_projection_clears_proof(self):
        for projection in (None,0.,float('nan'),float('inf')):
            h=TemporalForwardOnset();self.submit(h,0);self.submit(h,1);arm=self.arm(2);arm.pop('upper_projection')
            self.assertFalse(h.observe(arm,self.base,self.start+83_333_334,available=True,forward=True,independent=True,guard_origin=True,upper_projection=projection));self.assertEqual(len(h.samples),0)

    def test_later_guard_cannot_replace_actual_first_guard(self):
        h=TemporalForwardOnset();self.submit(h,0,guard_origin=False)
        self.assertFalse(self.submit(h,1,guard_origin=True));self.assertFalse(self.submit(h,2,guard_origin=True))

    def test_ready_flag_alone_does_not_prove_guard_before_new_onset(self):
        u=self.unit();a=self.arm(0);a['position'][1]=self.base['position'][1]+.10
        self.assertFalse(u.guard_neutral({'RIGHT':a}))
        u.upper_arm_projection=a['upper_projection'];u.forward_hint=False;u.independent_elbow_opening=False
        self.assertIsNone(u.update({'RIGHT':a},self.start)[0]);self.assertEqual(u.state,'READY')
        self.assertIsNone(self.step(u,1)[0]);self.assertIsNone(self.step(u,2)[0]);self.assertEqual(u.state,'READY')
        self.assertFalse(u.temporal_forward_pending)

if __name__=='__main__':unittest.main()
