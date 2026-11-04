"""Regressions from visually reviewed 14:39 capture; not held-out accuracy."""
from dataclasses import replace
import json
import os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine

@unittest.skipUnless(os.environ.get('REDBOOL_LATEST_SESSION'), 'Latest capture required')
class LatestCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_LATEST_SESSION'])
        cls.manifest=json.loads((p/'manifest.json').read_text())
        cls.frames={f['frame_id']:f for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}
        annotations=list(map(json.loads,(p/'annotations.jsonl').read_text().splitlines()))
        cls.intervals=[a for a in annotations if a['annotation_type']=='interval']
        markers={a['event_frame_id'] for a in annotations if a['label']=='CALIBRATE'}
        e=SixGestureEngine(movement=True);cls.events=[];cls.states={}
        for f in cls.frames.values():
            o=PoseObservation.from_record(f,cls.manifest)
            s=e.process(o,now_ns=o.capture_ns)
            cls.events.extend(e.drain_events(o.capture_ns));cls.states[o.frame_id]=(e.ready,s.movement,s.block)
            e.take_logs()
            if o.frame_id in markers:e.request_calibration()

    def test_all_visually_reviewed_punches_and_raises_without_duplicates(self):
        for a in self.intervals:
            actual='LEFT_RAISE' if a['notes']['step']==2 else a['label']
            events=[e.reason['gesture'] for e in self.events if a['start_frame_id']<=e.source_frame_id<=a['end_frame_id']]
            expected=[actual] if actual in ('LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE') else []
            with self.subTest(step=a['notes']['step']):self.assertEqual(events,expected)
        self.assertEqual(len(self.events),12)

    def test_all_twenty_one_action_returns_and_persistent_block_release(self):
        for i,a in enumerate(self.intervals):
            if a['label']=='GUARD' and 3<=a['notes']['step']<=43:
                with self.subTest(step=a['notes']['step']):
                    self.assertTrue(any(self.states[f][0] for f in range(a['start_frame_id'],a['end_frame_id']+1)))
            if a['label']=='BLOCK':
                self.assertTrue(any(self.states[f][2] for f in range(a['start_frame_id'],a['end_frame_id']+1)))
                self.assertFalse(self.states[self.intervals[i+1]['end_frame_id']][2])
        # Last pose is higher/asymmetric compared with calibrated guard;
        # never force READY just because the prompt says GUARD.
        last=self.intervals[-1]
        self.assertFalse(any(self.states[f][0] for f in range(last['start_frame_id'],last['end_frame_id']+1)))

    def test_actual_lean_direction_and_center_stop_including_late_actions(self):
        for i,a in enumerate(self.intervals):
            if not a['label'].startswith('MOVE_'):continue
            direction='LEFT' if a['notes']['step']==6 else a['label'].split('_')[1]
            end=self.intervals[i+1]['end_frame_id']
            states=[self.states[f][1] for f in range(a['start_frame_id'],end+1)]
            self.assertIn(direction,states)
            self.assertNotIn('RIGHT' if direction=='LEFT' else 'LEFT',states)
            self.assertEqual(states[-1],'NEUTRAL')

    def replay_ids(self,ids):
        e=SixGestureEngine();e.request_calibration();events=[]
        t=self.frames[ids[0]]['capture_return_monotonic_ns']
        for index,fid in enumerate(ids):
            o=PoseObservation.from_record(self.frames[fid],self.manifest)
            o=replace(o,frame_id=index+1,capture_ns=t+index*33_333_333)
            e.process(o,now_ns=o.capture_ns);events.extend(e.drain_events(o.capture_ns));e.take_logs()
        return e,events

    def test_held_extended_punches_never_become_new_guard(self):
        for start,peak in [(1929,1988),(2110,2169),(2471,2531),(3379,3438),(3560,3619),(4462,4521)]:
            with self.subTest(peak=peak):
                e,events=self.replay_ids(list(range(880,950))+list(range(start,peak+1))+[peak]*180)
                self.assertEqual(len(events),1);self.assertFalse(e.ready)

    def test_held_raises_never_repeat_without_return(self):
        for start,peak in [(1025,1084),(2653,2713),(3016,3076),(3921,3980)]:
            with self.subTest(peak=peak):
                e,events=self.replay_ids(list(range(880,950))+list(range(start,peak+1))+[peak]*180)
                self.assertEqual(len(events),1);self.assertFalse(e.ready)

if __name__=='__main__':unittest.main()
