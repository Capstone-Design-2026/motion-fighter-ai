"""Regression for lower guard recovery and keyboard preparation, not general accuracy."""
import json,os,unittest
from pathlib import Path
from dataclasses import replace
from redbool_demo.guided import GuidedCheck
from redbool_demo.six_engine import SixGestureEngine
from action_engine.types import PoseObservation

class PreparationTests(unittest.TestCase):
    def setUp(self):self.rows=[]
    def record_annotation(self,**r):self.rows.append(r)
    def test_prepare_hides_next_attack_and_only_action_phase_says_go(self):
        g=GuidedCheck(self_paced=True,focus='left-return')
        self.assertNotIn('GO',g.update(1,0,True,self))
        g.advance(2_000_000_000)
        text=g.update(2,2_100_000_000,True,self,ready=False)
        self.assertIn('WAIT',text);self.assertNotIn('LEFT',text);self.assertNotIn('GO',text)
        g.update(3,4_000_000_000,True,self,ready=True)
        text=g.update(4,4_800_000_000,True,self,ready=True)
        self.assertIn('GO: YOUR LEFT HAND UP',text)
        g.advance(6_000_000_000)
        self.assertNotIn('GO',g.update(5,6_100_000_000,True,self,ready=False))

    def test_keyboard_progress_waits_for_recovered_guard_without_claiming_success(self):
        g=GuidedCheck(self_paced=True)
        g.update(1,0,True,self);g.advance(2_000_000_000)
        self.assertIn('PREPARE',g.update(2,2_100_000_000,True,self,ready=False))
        g.advance(9_000_000_000);self.assertFalse(g.advance_requested)
        self.assertIn('PREPARE',g.update(3,10_000_000_000,True,self,ready=False))
        self.assertIn('PREPARE',g.update(4,11_000_000_000,True,self,ready=True))
        g.update(5,11_500_000_000,True,self,ready=False)
        g.update(6,12_000_000_000,True,self,ready=True)
        self.assertIn('PREPARE',g.update(7,12_600_000_000,True,self,ready=True))
        self.assertIn('GO',g.update(8,12_800_000_000,True,self,ready=True))
        self.assertEqual(self.rows[-1]['label'],'PREPARE')
        self.assertEqual(self.rows[-1]['end_frame_id'],7)
        self.assertEqual(g.start_frame,8)
        g.finish(self)
        self.assertTrue(all(r['notes']['ground_truth_status']=='NOT_REVIEWED' for r in self.rows))

@unittest.skipUnless(os.environ.get('REDBOOL_CHIN_SESSION'),'16:17 regression capture required')
class ChinGuardCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_CHIN_SESSION']);cls.manifest=json.loads((p/'manifest.json').read_text())
        cls.frames={f['frame_id']:f for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}
        cls.ann=list(map(json.loads,(p/'annotations.jsonl').read_text().splitlines()))
        marks={a['event_frame_id'] for a in cls.ann if a['label']=='CALIBRATE'}
        e=SixGestureEngine(movement=True);cls.events=[];cls.states={}
        for f in cls.frames.values():
            o=PoseObservation.from_record(f,cls.manifest);s=e.process(o,now_ns=o.capture_ns)
            cls.events.extend(e.drain_events(o.capture_ns));cls.states[o.frame_id]=(e.ready,s.block,s.movement)
            e.take_logs()
            if o.frame_id in marks:e.request_calibration()

    def test_both_previously_missed_first_left_raises_and_retries_fire_once(self):
        for lo,hi in [(6780,6863),(6900,6955),(9480,9653),(9710,9762)]:
            found=[e.reason['gesture'] for e in self.events if lo<=e.source_frame_id<=hi]
            self.assertEqual(found,['LEFT_RAISE'])
        self.assertEqual(len(self.events),16)
        self.assertEqual(sum(e.reason['gesture']=='LEFT_PUNCH' for e in self.events),3)
        self.assertEqual(sum(e.reason['gesture']=='RIGHT_PUNCH' for e in self.events),3)
        self.assertEqual(sum(e.reason['gesture']=='RIGHT_RAISE' for e in self.events),5)

    def test_all_guards_recover_but_actual_rest_never_arms(self):
        for a in self.ann:
            if a['annotation_type']=='interval' and a['label']=='GUARD':
                self.assertTrue(any(self.states[i][0] for i in range(a['start_frame_id'],a['end_frame_id']+1)))
        for i in range(10820,10910):
            self.assertEqual(self.states[i],(False,False,'NEUTRAL'))

    def test_each_reviewed_held_raise_stays_latched_without_duplicate(self):
        for start,peak in [(1885,2020),(2872,2950),(6695,6810),(9399,9540)]:
            e=SixGestureEngine();e.request_calibration();events=[]
            ids=list(range(1280,1350))+list(range(start,peak+1))+[peak]*100
            first=self.frames[1280]['capture_return_monotonic_ns']
            for index,fid in enumerate(ids):
                o=PoseObservation.from_record(self.frames[fid],self.manifest)
                o=replace(o,frame_id=index+1,capture_ns=first+index*33_333_333)
                e.process(o,now_ns=o.capture_ns);events.extend(e.drain_events(o.capture_ns));e.take_logs()
            with self.subTest(peak=peak):
                self.assertEqual(len(events),1);self.assertFalse(e.ready)
