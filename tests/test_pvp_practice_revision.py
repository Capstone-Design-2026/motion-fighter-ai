import tempfile
import unittest
from pathlib import Path
import json
from redbool_demo.personal import PersonalTrainer


class PendingCycleTests(unittest.TestCase):
    def setUp(self): self.t=PersonalTrainer(); self.now=1_000_000_000
    def frames(self,n,action='JAB',rest=False,target=False,run='same',phase='PRACTICE'):
        result=None
        for _ in range(n):
            self.now+=33_333_333
            self.t.set_context(dict(run=run,action=action,phase=phase,sent_ns=self.now),self.now)
            result=self.t.observe(dict(valid=True,rest=rest,target=target,amount=.31),self.now) or result
        return result
    def test_next_prompt_does_not_lose_second_measured_return(self):
        self.frames(8,rest=True);self.frames(8,target=True);self.frames(8,rest=True)
        self.frames(8,target=True)
        self.assertEqual(self.frames(8,action='STRAIGHT',rest=True),('JAB',.31))
        self.assertEqual(len(self.t.samples['JAB']),2)
        self.assertNotIn('STRAIGHT',self.t.samples)
    def test_completed_tutorial_finishes_last_actual_cycle(self):
        self.frames(8,action='SPECIAL',rest=True);self.frames(8,action='SPECIAL',target=True)
        self.frames(8,action='',phase='COMPLETE',rest=True)
        self.assertEqual(len(self.t.samples['SPECIAL']),1)
    def test_new_run_cannot_finish_previous_person_cycle(self):
        self.frames(8,rest=True);self.frames(8,target=True)
        self.frames(8,rest=True,action='STRAIGHT',run='new')
        self.assertFalse(self.t.samples)
    def test_no_return_times_out_without_fabricated_learning(self):
        self.frames(8,rest=True);self.frames(8,target=True)
        self.frames(70,action='STRAIGHT')
        self.assertFalse(self.t.samples)
        self.assertEqual(self.t.context['action'],'STRAIGHT')


class CompactRecordingTests(unittest.TestCase):
    def test_real_codec_count_timestamps_and_lossy_contract(self):
        import cv2, numpy as np
        from session_recording import SessionRecorder
        from collect_gesture_pilot import AsyncPilotVideo
        from redbool_demo.compact_video import CompactVideo
        with tempfile.TemporaryDirectory() as folder:
            r=SessionRecorder(folder);v=AsyncPilotVideo(CompactVideo(cv2,r,(64,48)),120)
            for i in range(90):
                # Video mapping can use an immutable observation identity.
                frame=r.record_frame(capture_return_monotonic_ns=1_000_000_000+i*33_333_334,
                    pose_success=True,image_landmarks=[],world_landmarks=[])
                bgr=np.full((48,64,3),i,np.uint8);v.write(bgr,cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB),frame)
            v.close();r.finalize()
            m=json.loads((r.directory/'manifest.json').read_text())['video']
            self.assertFalse(m['lossless_verified']);self.assertTrue(m['decode_verified'])
            self.assertEqual(m['encoded_frame_count'],45)
            rows=[json.loads(x) for x in (r.directory/'video_frames.jsonl').read_text().splitlines()]
            self.assertEqual(len(rows),45);self.assertEqual(rows[1]['frame_id'],3)
            self.assertEqual(len((r.directory/'frames.jsonl').read_text().splitlines()),90)
            self.assertFalse((r.directory/'video.avi').exists())

class PersonalOnsetTests(unittest.TestCase):
    def test_geometry_onset_can_finish_after_early_return_ack(self):
        t=PersonalTrainer();now=1_000_000_000
        def feed(n,phase='PRACTICE',**row):
            nonlocal now
            for _ in range(n):
                now+=33_333_333
                t.set_context(dict(run='same',action='JAB',phase=phase,sent_ns=now),now)
                t.observe(dict(valid=True,rest=False,target=False,amount=.31,**row),now)
        # Pass distinct fields to avoid overriding duplicate keyword arguments.
        for _ in range(8):
            now+=33_333_333;t.set_context(dict(run='same',action='JAB',phase='PRACTICE',sent_ns=now),now)
            t.observe(dict(valid=True,rest=True,target=False,amount=0),now)
        feed(2,onset=True)
        for _ in range(5):
            now+=33_333_333;t.set_context(dict(run='same',action='JAB',phase='RETURN',sent_ns=now),now)
            t.observe(dict(valid=True,rest=False,target=True,amount=.31),now)
        for _ in range(8):
            now+=33_333_333;t.set_context(dict(run='same',action='JAB',phase='RETURN',sent_ns=now),now)
            t.observe(dict(valid=True,rest=True,target=False,amount=0),now)
        self.assertEqual(t.samples,{'JAB':[.31]})
    def test_onset_without_confirmed_peak_does_not_become_sample(self):
        t=PersonalTrainer();now=1_000_000_000
        for stage in ('rest','onset','rest'):
            for _ in range(8):
                now+=33_333_333;t.set_context(dict(run='same',action='JAB',phase='PRACTICE',sent_ns=now),now)
                t.observe(dict(valid=True,rest=stage=='rest',onset=stage=='onset',target=False,amount=.3),now)
        self.assertFalse(t.samples)
    def test_confirmed_default_is_not_reported_as_effective_adaptation(self):
        from redbool_demo.personal import PersonalGestureEngine
        e=PersonalGestureEngine();e._apply_personal('JAB',.8)
        self.assertEqual(e.profile()['effective_overrides'],{})
        self.assertIn('BASELINE CONFIRMED',e.profile()['status'])
        e._apply_personal('STRAIGHT',.31)
        self.assertEqual(e.profile()['effective_overrides'],{'STRAIGHT':.28})
    def test_slow_complete_repeats_tune_only_own_onset_speed(self):
        from redbool_demo.personal import PersonalGestureEngine
        e=PersonalGestureEngine();t=e.trainer;now=1_000_000_000;fit=None
        for _ in range(2):
            for stage in ('rest','target','rest'):
                for _ in range(8):
                    now+=33_333_333;t.set_context(dict(run='slow',action='JAB',phase='PRACTICE',sent_ns=now),now)
                    fit=t.observe(dict(valid=True,rest=stage=='rest',target=stage=='target',amount=.36,speed=.72),now) or fit
        self.assertEqual(fit,('JAB',.36));e._apply_personal(*fit);e._apply_punch_speed(fit[0])
        self.assertEqual(e.attack.units['LEFT'].cfg.onset_speed,.65)
        self.assertEqual(e.attack.units['RIGHT'].cfg.onset_speed,.8)
        self.assertEqual(e.attack.units['LEFT'].cfg.event_ttl_ms,150)
        self.assertEqual(e.special.hold_ns,700_000_000)
        import numpy as np
        from redbool_demo.six_engine import SixGestureEngine
        original=SixGestureEngine().attack.units['LEFT'];adapted=e.attack.units['LEFT']
        anchor={'position':np.array([0.,0.,-2.]),'angle':.4,'angle3d':1.8}
        arm={'position':np.array([.36,0.,-2.1]),'angle':1.1,'angle3d':2.4,
             'speed_xy':.72,'velocity':np.array([.72,0.,-.2]),'angle_velocity':.3}
        original.baseline=adapted.baseline={'LEFT':anchor}
        self.assertFalse(original.candidates({'LEFT':arm})['LEFT']['onset_rule'])
        self.assertTrue(adapted.candidates({'LEFT':arm})['LEFT']['onset_rule'])
        # A straight arm swept sideways still cannot rearm, even after tuning.
        adapted.state='RETURNING';adapted.now=now
        held={**arm,'angle':2.7,'angle3d':2.9,'speed_xy':.7}
        self.assertFalse(adapted.neutral({'LEFT':held}))
        e.request_calibration();self.assertEqual(e.attack.units['LEFT'].cfg.onset_speed,.8)
    def test_speed_outliers_and_single_cycle_do_not_relax_v26(self):
        from redbool_demo.personal import PersonalGestureEngine
        for speeds in ([.72],[.4,.4],[.6,1.2]):
            e=PersonalGestureEngine();e.trainer.cycle_details['JAB']=[dict(speed=s) for s in speeds]
            e._apply_punch_speed('JAB');self.assertEqual(e.attack.units['LEFT'].cfg.onset_speed,.8)
