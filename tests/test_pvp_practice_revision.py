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
