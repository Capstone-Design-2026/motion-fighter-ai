"""Same-frame auxiliary geometry safety; synthetic tests are not accuracy claims."""
import unittest
from dataclasses import replace
from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine
class AuxiliaryCrossTests(unittest.TestCase):
    def trial(self,mutate=lambda o:o):
        e=SixGestureEngine();e.request_calibration();events=[]
        for i in range(1,141):
            o=pose('guard',i,i*33_333_333);aux=mutate(pose('cross' if i>50 else 'guard',i,i*33_333_333))
            e.process(o,now_ns=o.capture_ns,special_observation=aux)
            events.extend(e.drain_events(o.capture_ns));e.take_logs()
        return e,events
    def test_full_can_resolve_x_once_when_lite_stays_guard(self):
        e,events=self.trial();self.assertEqual([v.reason['gesture'] for v in events],['SPECIAL']);self.assertFalse(e.control.block)
    def test_stale_frame_actor_epoch_mirror_and_dimensions_rejected(self):
        for key,value in [('frame_id',0),('capture_ns',0),('actor_id','other'),('capture_epoch','other'),('source_mirrored',True),('width',1280)]:
            with self.subTest(key=key):self.assertEqual(self.trial(lambda o:replace(o,**{key:value}))[1],[])
    def test_missing_and_low_confidence_auxiliary_cannot_trigger(self):
        for change in ({'pose_present':False},{'visibility':(0.,)*33}):
            self.assertEqual(self.trial(lambda o:replace(o,**change))[1],[])
    def test_auxiliary_open_pose_does_not_release_lite_held_cross(self):
        e=SixGestureEngine();e.request_calibration();events=[]
        for i in range(1,161):
            o=pose('guard' if i<=50 else 'cross',i,i*33_333_333);aux=pose('guard',i,i*33_333_333)
            e.process(o,now_ns=o.capture_ns,special_observation=aux);events.extend(e.drain_events(o.capture_ns))
        self.assertEqual([v.reason['gesture'] for v in events],['SPECIAL'])

    def test_different_visible_body_is_not_fused(self):
        def shifted(o):
            p=[list(v) for v in o.landmarks]
            for v in p:v[0]+=.35
            return replace(o,landmarks=tuple(map(tuple,p)))
        self.assertEqual(self.trial(shifted)[1],[])
