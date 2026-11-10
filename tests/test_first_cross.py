"""First deliberate X after valid calibration or keyboard use must not need a retry."""
import unittest,os,json
from pathlib import Path
from redbool_demo.special import CrossSpecial
from redbool_demo.six_engine import SixGestureEngine
from action_engine.types import PoseObservation

class FirstCrossLifecycleTests(unittest.TestCase):
    def test_fired_hold_survives_gap_without_second_event(self):
        s=CrossSpecial();t=0
        def frames(n,valid=True,neutral=False,cross=True):
            nonlocal t
            count=0
            for _ in range(n):
                t+=33_333_333
                count+=s.step(t,calibrated=True,valid=valid,neutral=neutral,approach=cross,strict=cross)
            return count
        self.assertEqual(frames(30),1)
        self.assertEqual(frames(20,valid=False),0)
        self.assertEqual(frames(90),0)
        self.assertEqual(frames(10,neutral=True,cross=False),0)
        self.assertEqual(frames(30),1)
    def test_pre_attempt_keyboard_gap_requires_fresh_complete_dwell(self):
        s=CrossSpecial();t=0
        for _ in range(30):
            t+=33_333_333
            self.assertFalse(s.step(t,calibrated=True,valid=False,neutral=False,approach=False,strict=False))
        for _ in range(20):
            t+=33_333_333
            self.assertFalse(s.step(t,calibrated=True,valid=True,neutral=False,approach=True,strict=True))
        fired=0
        for _ in range(10):
            t+=33_333_333
            fired+=s.step(t,calibrated=True,valid=True,neutral=False,approach=True,strict=True)
        self.assertEqual(fired,1)

@unittest.skipUnless(os.environ.get('REDBOOL_FIRST_X_SESSION'),'Reviewed November 8 capture required')
class RecordedFirstCrossTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_FIRST_X_SESSION']);m=json.loads((p/'manifest.json').read_text());marks={a['event_frame_id'] for a in map(json.loads,(p/'annotations.jsonl').read_text().splitlines()) if a['label']=='CALIBRATE'}
        e=SixGestureEngine();cls.events=[];t0=None
        for line in (p/'frames.jsonl').read_text().splitlines():
            o=PoseObservation.from_record(json.loads(line),m);t0=t0 or o.capture_ns
            e.process(o,now_ns=o.capture_ns)
            cls.events.extend(((v.source_capture_ns-t0)/1e9,v.reason['gesture']) for v in e.drain_events(o.capture_ns));e.take_logs()
            if o.frame_id in marks:e.request_calibration()
    def test_previously_missed_first_visible_cross_is_not_a_punch(self):
        self.assertEqual([g for t,g in self.events if 96.7<=t<=99.3],['SPECIAL'])
    def test_held_cross_does_not_repeat_and_reviewed_rest_has_no_attack(self):
        for lo,hi in [(96.7,99.3),(103.9,106.2),(122.2,124.6),(147.8,150.7),(169.9,171.7)]:
            self.assertEqual([g for t,g in self.events if lo<=t<=hi],['SPECIAL'])
        self.assertFalse([(t,g) for t,g in self.events if 209<=t<=216])

if __name__=='__main__':unittest.main()
