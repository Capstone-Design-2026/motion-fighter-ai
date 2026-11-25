"""Video-reviewed upward strokes, not tutorial prompts used as ground truth."""
from dataclasses import replace
import json
import os
from pathlib import Path
import unittest

from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine


SESSION = os.environ.get('REDBOOL_FOUR_PERSON_SESSION')


@unittest.skipUnless(SESSION, 'Reviewed November 12 field recording required')
class FoldedRaiseRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(SESSION)
        manifest = json.loads((path / 'manifest.json').read_text())
        # Keep the original C-to-calibration sequence, plus continuous guard,
        # onset, hold and return around each reviewed stroke. No resampling.
        windows = ((720, 1080), (7200, 7350), (10070, 10370),
                   (14540, 14650), (15080, 15330), (17230, 17400))
        frames = []
        with (path / 'frames.jsonl').open() as source:
            for line in source:
                frame = json.loads(line)
                if any(a <= frame['frame_id'] <= b for a, b in windows):
                    frames.append(frame)
        cls.events = []
        for mirrored in (False, True):
            engine = SixGestureEngine(movement=True)
            events = []
            for frame in frames:
                obs = PoseObservation.from_record(frame, manifest)
                if mirrored and obs.landmarks is not None:
                    points = [list(p) for p in obs.landmarks]
                    visibility = list(obs.visibility)
                    for p in points:
                        p[0] = 1 - p[0]
                    for a, b in [(1,4),(2,5),(3,6),(7,8),(9,10)] + [(i,i+1) for i in range(11,32,2)]:
                        points[a], points[b] = points[b], points[a]
                        visibility[a], visibility[b] = visibility[b], visibility[a]
                    obs = replace(obs, landmarks=tuple(map(tuple, points)), visibility=tuple(visibility))
                now = obs.capture_ns + 20_000_000
                engine.process(obs, now_ns=now)
                events.extend((event.source_frame_id, event.reason.get('gesture', event.side + '_PUNCH'))
                              for event in engine.drain_events(now))
                engine.take_logs()
                if obs.frame_id in (729, 10077):
                    engine.request_calibration()
            cls.events.append(events)

    def gestures(self, start, end, mirrored=False):
        return [gesture for frame, gesture in self.events[int(mirrored)] if start <= frame <= end]

    def test_three_bent_upward_strokes_are_raises_not_straights(self):
        for start, end in ((14577, 14610), (15268, 15305), (17270, 17310)):
            with self.subTest(window=(start, end)):
                self.assertEqual(self.gestures(start, end), ['RIGHT_RAISE'])
                self.assertEqual(self.gestures(start, end, True), ['LEFT_RAISE'])

    def test_two_reviewed_missing_raises_are_recovered(self):
        for start, end in ((7280, 7320), (15165, 15200)):
            with self.subTest(window=(start, end)):
                self.assertEqual(self.gestures(start, end), ['RIGHT_RAISE'])
                self.assertEqual(self.gestures(start, end, True), ['LEFT_RAISE'])

    def test_nearby_forward_straight_and_separate_returns_are_preserved(self):
        self.assertEqual(self.gestures(15100, 15150), ['RIGHT_PUNCH'])
        self.assertEqual(self.gestures(15100, 15150, True), ['LEFT_PUNCH'])
        for start, end in ((7240, 7260), (7330, 7350), (15201, 15240), (17315, 17375)):
            self.assertEqual(self.gestures(start, end), ['RIGHT_RAISE'])

