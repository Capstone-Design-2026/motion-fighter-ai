"""Synthetic geometry tests exercise safety contracts, not recognition accuracy."""
from dataclasses import replace
import json
import os
from pathlib import Path
import unittest
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine

@unittest.skipUnless(os.environ.get('REDBOOL_DEMO_SESSION'), 'Seated fixture required')
class SixGestureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p = Path(os.environ['REDBOOL_DEMO_SESSION'])
        cls.manifest = json.loads((p/'manifest.json').read_text())
        cls.frames = {f['frame_id']: f for f in map(json.loads, (p/'frames.jsonl').read_text().splitlines())}
        cls.base = PoseObservation.from_record(cls.frames[8809], cls.manifest)

    def setUp(self):
        self.engine = SixGestureEngine()
        self.engine.request_calibration()
        self.index = 0
        self.events = []
        self.feed(35)
        self.assertTrue(self.engine.calibrated)

    def feed(self, count, left=0., right=0., missing=False, shift=0., rotate=0., missing_shoulder=False):
        for _ in range(count):
            self.index += 1
            p = [list(v) for v in self.base.landmarks]
            for point in p: point[0] += shift
            p[11][2] += rotate
            p[12][2] -= rotate
            p[15][1] += left
            p[16][1] += right
            v = list(self.base.visibility)
            if missing: v[15] = 0.
            if missing_shoulder: v[11] = 0.
            o = replace(self.base, frame_id=self.index,
                capture_ns=self.base.capture_ns + self.index * 33_333_333,
                landmarks=tuple(map(tuple, p)), visibility=tuple(v))
            self.engine.process(o, now_ns=o.capture_ns)
            self.events.extend(self.engine.drain_events(o.capture_ns))
        return self.engine.control

    def gestures(self):
        return [e.reason.get('gesture', e.side+'_PUNCH') for e in self.events]

    def test_each_vertical_action_once_until_return(self):
        for gesture, left, right in [('LEFT_RAISE',-.20,0), ('RIGHT_RAISE',0,-.20)]:
            self.feed(45,left,right)
            self.assertEqual(self.gestures()[-1:], [gesture])
            self.assertEqual(self.gestures().count(gesture),1)
            self.feed(30)
        self.assertEqual(len(self.events),2)

    def test_left_lower_is_no_longer_a_game_action(self):
        self.feed(45,left=.24)
        self.assertFalse(self.events)
        self.feed(30)
        self.feed(20,right=-.20)
        self.assertEqual(self.gestures(),['RIGHT_RAISE'])

    def test_repeat_after_guard_return(self):
        self.feed(20,-.20); self.feed(30); self.feed(20,-.20)
        self.assertEqual(self.gestures(),['LEFT_RAISE','LEFT_RAISE'])

    def test_block_priority_including_staggered_hands(self):
        self.feed(3,-.20)  # first hand precedes the second by 100 ms
        state=self.feed(30,-.20,-.20)
        self.assertTrue(state.block)
        self.assertEqual(self.events,[])
        self.feed(20,-.20,0)  # releasing one arm must not create a kick
        self.assertEqual(self.events,[])
        self.assertFalse(self.feed(30).block)

    def test_invalid_tracking_cannot_create_or_repeat_kick(self):
        self.feed(20,-.20); self.feed(10,-.20,missing=True); self.feed(30,-.20)
        self.assertEqual(self.gestures(),['LEFT_RAISE'])
        self.feed(30); self.feed(20,-.20)
        self.assertEqual(self.gestures(),['LEFT_RAISE','LEFT_RAISE'])

    def test_cancelled_raise_and_jitter_have_no_event(self):
        for _ in range(8):
            self.feed(2,-.20); self.feed(3,0)
        self.assertEqual(self.events,[])

    def test_right_raise_maps_to_low_kick(self):
        self.feed(20,right=-.20)
        self.assertEqual(self.gestures(),['RIGHT_RAISE'])
        self.assertEqual(self.events[0].reason['game_mapping'],'LOW_KICK')

    def test_real_capture_keeps_punches(self):
        e=SixGestureEngine();e.request_calibration();events=[]
        for fid in range(8731,9517):
            o=PoseObservation.from_record(self.frames[fid],self.manifest)
            e.process(o,now_ns=o.capture_ns);events.extend(e.drain_events(o.capture_ns))
        self.assertEqual([x.reason.get('gesture',x.side+'_PUNCH') for x in events],['LEFT_PUNCH','RIGHT_PUNCH'])

    def test_lateral_movement_and_tracking_loss_preserve_centre(self):
        self.engine.movement_enabled=True
        m=self.engine.movement;shift=.35*m.width*m.left_sign;origin=m.origin
        self.assertEqual(self.feed(20,shift=shift).movement,'LEFT')
        self.assertEqual(self.feed(20).movement,'NEUTRAL')
        self.assertEqual(self.feed(20,shift=-shift).movement,'RIGHT')
        self.engine.tick(self.engine.last_capture+200_000_000)
        self.assertEqual(self.engine.control.movement,'NEUTRAL')
        self.assertEqual(m.origin,origin)
        self.assertFalse(self.events)

    def test_rotation_alone_does_not_move(self):
        self.engine.movement_enabled=True
        self.assertEqual(self.feed(20,rotate=.2).movement,'NEUTRAL')

    def test_movement_rejects_small_sway_and_releases_on_shoulder_loss(self):
        self.engine.movement_enabled=True
        m=self.engine.movement;unit=m.width*m.left_sign;origin=m.origin
        for sign in (-1,1)*4:
            self.assertEqual(self.feed(10,shift=sign*.10*unit).movement,'NEUTRAL')
        self.assertEqual(self.feed(25,shift=.35*unit).movement,'LEFT')
        self.assertEqual(self.feed(1,shift=.35*unit,missing_shoulder=True).movement,'NEUTRAL')
        self.assertEqual(m.origin,origin)
        self.assertEqual(self.feed(25).movement,'NEUTRAL')
        self.assertEqual(self.feed(25,shift=-.35*unit).movement,'RIGHT')
        self.assertEqual(self.feed(25).movement,'NEUTRAL')
        self.assertFalse(self.events)

    def test_wrist_loss_does_not_disable_valid_shoulder_movement(self):
        self.engine.movement_enabled=True
        m=self.engine.movement;shift=.35*m.width*m.left_sign
        self.assertEqual(self.feed(25,shift=shift,missing=True).movement,'LEFT')
        self.assertFalse(self.engine.ready);self.assertFalse(self.events)

if __name__=='__main__': unittest.main()
