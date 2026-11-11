"""Same-frame ensemble safety; recorded development checks are not held-out accuracy."""
import unittest
from dataclasses import replace
from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine

class CrossRecoveryTests(unittest.TestCase):
    def trial(self,video='guard',image='cross',mutate=lambda o:o):
        e=SixGestureEngine();e.request_calibration();events=[]
        for i in range(1,141):
            o=pose('guard',i,i*33_333_333)
            a=pose(video if i>50 else 'guard',i,o.capture_ns)
            b=mutate(pose(image if i>50 else 'guard',i,o.capture_ns))
            e.process(o,now_ns=o.capture_ns,special_observation=(a,b));events.extend(e.drain_events(o.capture_ns));e.take_logs()
        return e,events
    def test_fresh_detection_recovers_cross_once(self):
        e,v=self.trial();self.assertEqual([x.reason['gesture'] for x in v],['SPECIAL']);self.assertTrue(e.special.latched)
    def test_existing_video_cross_is_not_replaced(self):
        e,v=self.trial(video='cross',image='guard');self.assertEqual([x.reason['gesture'] for x in v],['SPECIAL'])
    def test_agreeing_sources_do_not_duplicate_special(self):
        e,v=self.trial(video='cross',image='cross');self.assertEqual([x.reason['gesture'] for x in v],['SPECIAL'])
    def test_guard_and_block_do_not_become_cross(self):
        for kind in ['guard','block','left_raise']:
            self.assertEqual(self.trial(video=kind,image=kind)[1],[])
    def test_recovery_rejects_wrong_identity_timestamp_dimensions_and_visibility(self):
        for key,value in [('actor_id','other'),('capture_epoch','other'),('frame_id',0),('capture_ns',0),('width',1280),('source_mirrored',True),('pose_present',False),('visibility',(0.,)*33)]:
            with self.subTest(key=key):self.assertEqual(self.trial(mutate=lambda o:replace(o,**{key:value}))[1],[])
    def test_other_body_is_not_recovery_evidence(self):
        def shifted(o):return replace(o,landmarks=tuple((x+.35,y,z) for x,y,z in o.landmarks))
        self.assertEqual(self.trial(mutate=shifted)[1],[])
    def test_lone_extension_needs_continuity_and_does_not_invent_special(self):
        e=SixGestureEngine();e.request_calibration();events=[]
        for i in range(1,61):
            o=pose('guard' if i<=50 else 'cross',i,i*33_333_333)
            e.process(o,now_ns=o.capture_ns,special_observation=(o,o));events.extend(e.drain_events(o.capture_ns));e.take_logs()
        self.assertTrue(e.special.owned);self.assertFalse(e.special.latched)
        for i in range(61,68):
            o=pose('guard',i,i*33_333_333);p=list(o.landmarks);p[13]=(.78,.35,0);p[15]=(.95,.42,0);o=replace(o,landmarks=tuple(p));missing=replace(o,pose_present=False)
            e.process(o,now_ns=o.capture_ns,special_observation=(missing,missing));events.extend(e.drain_events(o.capture_ns));e.take_logs()
            if i==62:self.assertTrue(e.special.owned)
        self.assertFalse(e.special.owned);self.assertFalse(any(x.reason.get('gesture')=='SPECIAL' for x in events))
