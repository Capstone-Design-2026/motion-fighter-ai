import unittest
from dataclasses import replace
from redbool_demo.personal import PersonalTrainer, PersonalGestureEngine
from test_six_gestures import SixGestureTests


class TutorialRangeTests(unittest.TestCase):
    def setUp(self): self.t=PersonalTrainer();self.now=1_000_000_000;self.results=[]
    def frames(self,count,rest=False,target=False,amount=.31,valid=True,conflict=False,action='JAB',phase='PRACTICE'):
        for _ in range(count):
            self.now+=33_333_333
            self.t.set_context(dict(run='practice-1',action=action,phase=phase,sent_ns=self.now),self.now)
            r=self.t.observe(dict(valid=valid,rest=rest,target=target,amount=amount,conflict=conflict),self.now)
            if r:self.results.append(r)
    def cycle(self,**kw):
        self.frames(8,rest=True,**kw);self.frames(8,target=True,**kw);self.frames(8,rest=True,**kw)
    def test_two_complete_consistent_cycles_required(self):
        self.cycle();self.assertFalse(self.results)
        self.cycle();self.assertEqual(self.results,[('JAB',.31)])
    def test_hold_cannot_count_twice(self):
        self.frames(8,rest=True);self.frames(75,target=True);self.frames(8,rest=True)
        self.assertEqual(len(self.t.samples['JAB']),1);self.assertFalse(self.results)
    def test_prompt_does_not_turn_unrelated_motion_into_sample(self):
        self.frames(8,rest=True);self.frames(8,conflict=True);self.frames(8,rest=True)
        self.assertFalse(self.t.samples)
    def test_missing_joints_drop_partial_cycle(self):
        self.frames(8,rest=True);self.frames(4,target=True);self.frames(1,valid=False);self.frames(8,rest=True)
        self.assertFalse(self.t.samples)
    def test_inconsistent_samples_do_not_fit(self):
        self.cycle(amount=.31);self.cycle(amount=.8);self.assertFalse(self.results)
    def test_demonstration_and_stale_context_cannot_train(self):
        self.cycle(phase='DEMO');self.assertFalse(self.t.samples)
        self.t.set_context(dict(run='p',action='JAB',phase='PRACTICE',sent_ns=0),self.now)
        self.assertIsNone(self.t.context)
    def test_return_phase_finishes_but_does_not_start_cycle(self):
        self.frames(8,rest=True);self.frames(8,target=True);self.frames(8,rest=True,phase='RETURN')
        self.frames(8,target=True,phase='RETURN');self.frames(8,rest=True,phase='RETURN')
        self.assertEqual(len(self.t.samples['JAB']),1)
    def test_profile_is_bounded_side_specific_and_clears_on_C(self):
        e=PersonalGestureEngine(movement=True)
        e._apply_personal('JAB',.1);e._apply_personal('HIGH_KICK',.1);e._apply_personal('MOVE_LEFT',.1)
        self.assertEqual(e.attack.units['LEFT'].cfg.commit_displacement,.28)
        self.assertEqual(e.attack.units['RIGHT'].cfg.commit_displacement,.32)
        self.assertEqual(e.personal_raise,{'LEFT':.36});self.assertEqual(e.movement.personal_enter,{'LEFT':.18})
        self.assertEqual(e.special.hold_ns,700_000_000)
        generation=e.calibration_generation;e.request_calibration()
        self.assertFalse(e.personal_changes);self.assertFalse(e.movement.personal_enter)
        self.assertEqual(e.attack.units['LEFT'].cfg.commit_displacement,.32)
        self.assertGreater(e.calibration_generation,generation)
    def test_participant_profiles_are_independent(self):
        a=PersonalGestureEngine();b=PersonalGestureEngine();a._apply_personal('STRAIGHT',.31)
        self.assertNotEqual(a.attack.units['RIGHT'].cfg.commit_displacement,b.attack.units['RIGHT'].cfg.commit_displacement)


class PersonalGeometryTests(SixGestureTests):
    """Run the existing synthetic safety suite through the optional wrapper too."""
    def setUp(self):
        self.engine=PersonalGestureEngine(movement=True);self.engine.request_calibration()
        self.index=0;self.events=[];self.feed(35);self.assertTrue(self.engine.calibrated)
    def test_two_small_raises_learn_only_from_geometry_and_return(self):
        # First fit the bounded range using real landmark-derived measurements.
        original=self.feed
        def taught(n,left=0):
            for _ in range(n):
                now=self.base.capture_ns+(self.index+1)*33_333_333
                self.engine.tutorial_context(dict(run='t',action='HIGH_KICK',phase='PRACTICE',sent_ns=now),now)
                original(1,left=left)
        width=abs(self.base.landmarks[11][0]-self.base.landmarks[12][0])
        # Features scale by torso width; move gradually to avoid a capture spike.
        for _ in range(2):
            taught(10)
            for j in range(1,7):taught(1,left=-.20*j/6)
            taught(10,left=-.20)
            for j in range(5,-1,-1):taught(1,left=-.20*j/6)
            taught(20)
        self.assertIn('HIGH_KICK',self.engine.trainer.samples)
        self.assertGreaterEqual(len(self.engine.trainer.samples['HIGH_KICK']),2)
        self.assertIn('HIGH_KICK',self.engine.personal_changes)
        self.assertNotIn('JAB',self.engine.personal_changes)
    def test_short_raise_is_only_accepted_after_two_consistent_cycles(self):
        import numpy as np
        p=np.asarray(self.base.landmarks).copy();p[:,1]*=self.base.height/self.base.width
        width=np.linalg.norm(p[11,:2]-p[12,:2]);delta=-.38*width*self.base.width/self.base.height
        def taught(n,y=0):
            for _ in range(n):
                now=self.base.capture_ns+(self.index+1)*33_333_333
                self.engine.tutorial_context(dict(run='small',action='HIGH_KICK',phase='PRACTICE',sent_ns=now),now)
                self.feed(1,left=y)
        for _ in range(2):
            taught(12)
            for j in range(1,7):taught(1,delta*j/6)
            taught(10,delta)
            for j in range(5,-1,-1):taught(1,delta*j/6)
            taught(20)
        self.assertEqual(self.events,[])
        self.assertEqual(self.engine.personal_raise,{'LEFT':.36})
        taught(15,delta)
        self.assertEqual(self.gestures(),['LEFT_RAISE'])
        self.assertEqual(self.engine.special.hold_ns,700_000_000)

    def test_no_context_has_no_profile_even_after_valid_actions(self):
        self.feed(20,-.2);self.feed(30);self.feed(20,-.2);self.feed(30)
        self.assertFalse(self.engine.personal_changes);self.assertFalse(self.engine.trainer.samples)

class TutorialWireTests(unittest.TestCase):
    def test_context_expires_and_older_ack_cannot_switch_target(self):
        import time
        from unittest.mock import patch
        from realtime_vision.transport import Sender
        from redbool_demo.transport import DemoSender
        s=DemoSender('127.0.0.1',9,'profile','P1')
        try:
            now=time.monotonic_ns()
            def ack(seq,action):return {'type':'ack','ack':dict(status='APPLIED',sequence=seq,sender_ns=now,
                game_can_act=False,special_ready=False,tutorial_run='run',tutorial_action=action,tutorial_phase='PRACTICE',tutorial_hits=0)}
            with patch.object(Sender,'service',return_value=[ack(3,'JAB')]):s.service()
            self.assertEqual(s.tutorial_context(now)['action'],'JAB')
            with patch.object(Sender,'service',return_value=[ack(2,'STRAIGHT')]):s.service()
            self.assertEqual(s.tutorial_context(now)['action'],'JAB')
            self.assertIsNone(s.tutorial_context(now+500_000_001))
        finally:s.close()
    def test_personal_live_wrapper_saves_profile_without_opening_real_camera(self):
        import json
        from unittest.mock import patch
        from redbool_demo.live import run as actual_run
        from test_redbool_demo import SyntheticLiveTests
        checked=[]
        def run(args):
            args.personal_calibration=True
            actual_run(args)
            paths=list(args.output.glob('*/personal_profile.json'))
            self.assertEqual(len(paths),1)
            profile=json.loads(paths[0].read_text())
            self.assertEqual(profile['baseline'],'v26');self.assertFalse(profile['changes']);checked.append(True)
        with patch('redbool_demo.live.run',side_effect=run):
            SyntheticLiveTests('test_camera_free_full_live_recording_and_clean_shutdown').test_camera_free_full_live_recording_and_clean_shutdown()
        self.assertEqual(checked,[True])
