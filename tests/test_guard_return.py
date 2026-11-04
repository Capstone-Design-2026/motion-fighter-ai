"""Regression on preserved captures. Development evidence, not held-out accuracy."""
from dataclasses import replace
import json
import os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine

@unittest.skipUnless(os.environ.get('REDBOOL_GUARD_SESSION'), 'Latest seated capture required')
class GuardReturnCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(os.environ['REDBOOL_GUARD_SESSION'])
        manifest = json.loads((root/'manifest.json').read_text())
        annotations = [json.loads(x) for x in (root/'annotations.jsonl').read_text().splitlines()]
        markers = {a['event_frame_id'] for a in annotations if a['label']=='CALIBRATE'}
        cls.intervals = [a for a in annotations if a['annotation_type']=='interval']
        cls.states = {}; cls.events = []
        engine = SixGestureEngine()
        for line in (root/'frames.jsonl').read_text().splitlines():
            obs = PoseObservation.from_record(json.loads(line), manifest)
            state = engine.process(obs, now_ns=obs.capture_ns)
            cls.events.extend(engine.drain_events(obs.capture_ns))
            cls.states[obs.frame_id] = (state.attack_state,
                engine.vertical_state=='READY' and all(u.state=='READY' for u in engine.attack.units.values()),
                state.block)
            engine.take_logs()
            # Same ordering as the live C key, after processing this frame.
            if obs.frame_id in markers: engine.request_calibration()

    def test_each_punch_and_raise_once_in_its_interval(self):
        targets = {'LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE'}
        for a in self.intervals:
            if a['label'] not in targets: continue
            events = [e.reason['gesture'] for e in self.events
                if a['start_frame_id']<=e.source_frame_id<=a['end_frame_id']]
            with self.subTest(step=a['notes']['step']):
                self.assertEqual(events, [a['label']])
        self.assertEqual(len(self.events), 12)

    def test_all_eighteen_post_action_guards_rearm(self):
        guards = [a for a in self.intervals if a['label']=='GUARD' and 3<=a['notes']['step']<=37]
        self.assertEqual(len(guards),18)
        for a in guards:
            with self.subTest(step=a['notes']['step']):
                self.assertTrue(any(self.states[i][1] for i in range(a['start_frame_id'],a['end_frame_id']+1)))
        # The last prompt was filmed with hands below the original guard.
        last = self.intervals[-1]
        self.assertFalse(any(self.states[i][1] for i in range(last['start_frame_id'],last['end_frame_id']+1)))

    def test_ready_never_hides_an_unrecovered_arm_or_raise(self):
        for fid,(published,fully_ready,_) in self.states.items():
            self.assertEqual(published=='READY', fully_ready, f'frame {fid}')

    def test_block_release_and_negative_actions_remain_safe(self):
        for index,a in enumerate(self.intervals):
            if a['label']=='BLOCK':
                self.assertTrue(any(self.states[i][2] for i in range(a['start_frame_id'],a['end_frame_id']+1)))
                self.assertFalse(self.states[self.intervals[index+1]['end_frame_id']][2])
            if a['label'] not in {'LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE'}:
                self.assertFalse([e for e in self.events if a['start_frame_id']<=e.source_frame_id<=a['end_frame_id']])

@unittest.skipUnless(os.environ.get('REDBOOL_DEMO_SESSION'), 'Earlier seated capture required')
class GuardSafetyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_DEMO_SESSION'])
        cls.manifest=json.loads((p/'manifest.json').read_text())
        cls.frames={f['frame_id']:f for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}

    def replay(self, ids, calibrate=True, missing_after=None):
        e=SixGestureEngine();events=[]
        if calibrate:e.request_calibration()
        first=self.frames[ids[0]]['capture_return_monotonic_ns']
        for index,fid in enumerate(ids):
            o=PoseObservation.from_record(self.frames[fid],self.manifest)
            o=replace(o,frame_id=index+1,capture_ns=first+index*33_333_333)
            if missing_after is not None and index>=missing_after:
                v=list(o.visibility);v[15]=0.;o=replace(o,visibility=tuple(v))
            e.process(o,now_ns=o.capture_ns);events.extend(e.drain_events(o.capture_ns));e.take_logs()
        return e,events

    def test_extended_punch_hold_does_not_rearm_or_repeat(self):
        e,events=self.replay(list(range(8755,8836))+[8835]*180)
        self.assertEqual(len(events),1)
        self.assertFalse(e.ready)
        self.assertNotEqual(e.control.attack_state,'READY')

    def test_same_hand_repeats_only_after_return(self):
        _,events=self.replay(list(range(8755,8811))+list(range(8811,8894))*2)
        self.assertEqual([e.reason['gesture'] for e in events],['LEFT_PUNCH','LEFT_PUNCH'])

    def test_missing_one_wrist_and_timeout_cannot_publish_ready(self):
        e,events=self.replay(list(range(8755,8811))+[8809]*20,missing_after=56)
        self.assertFalse(e.ready);self.assertNotEqual(e.control.attack_state,'READY');self.assertFalse(events)
        e.tick(e.last_capture+200_000_000)
        self.assertFalse(e.ready);self.assertNotEqual(e.control.attack_state,'READY')

    def test_no_calibration_cannot_arm_or_attack(self):
        e,events=self.replay(list(range(8731,9517)),calibrate=False)
        self.assertFalse(e.ready);self.assertFalse(events)

if __name__=='__main__': unittest.main()
