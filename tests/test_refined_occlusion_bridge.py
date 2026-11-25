"""A single lost primary pose cannot add X dwell or revive ordinary controls."""
from dataclasses import replace
import unittest
from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine

class RefinedOcclusionBridgeTests(unittest.TestCase):
    dt=33_333_333
    def setUp(self):
        self.e=SixGestureEngine(movement=True);self.e.request_calibration()
        self.frame=0;self.stamp=0;self.events=[]
        for _ in range(55):self.feed('guard')
        self.assertTrue(self.e.calibrated)
    def feed(self,kind='guard',heavy=None,missing=False,gap=None,mutate=None,slots=3,age=0):
        self.frame+=1;self.stamp+=gap or self.dt
        o=pose(kind,self.frame,self.stamp);aux=[o,o]
        if heavy is not None:aux.append(pose(heavy,self.frame,self.stamp))
        if mutate:aux=mutate(aux)
        if missing:o=replace(o,pose_present=False,landmarks=None,visibility=None)
        s=self.e.process(o,now_ns=self.stamp+age,special_observation=tuple(aux[:slots]))
        self.events.extend(self.e.drain_events(self.stamp+age));self.e.take_logs();return s
    def seed(self):
        for _ in range(3):self.feed('cross')
        for _ in range(8):self.feed(heavy='cross')
        self.assertTrue(self.e.special.owned and self.e.cross_refinement_used)
        self.assertFalse(self.events)
    def test_current_auxiliary_evidence_preserves_only_earned_dwell(self):
        self.seed();old=self.e.special.confirmed_ns
        s=self.feed(heavy='cross',missing=True)
        self.assertEqual(self.e.special.confirmed_ns,old)
        self.assertEqual(s.tracking,'INVALID');self.assertFalse(s.block)
        self.assertEqual(s.movement,'NEUTRAL');self.assertFalse(self.events)
        self.feed(heavy='cross')
        self.assertEqual(self.e.special.confirmed_ns,old,'Neither side of missing frame earns time')
        for _ in range(30):self.feed(heavy='cross')
        self.assertEqual([x.reason['gesture']for x in self.events],['SPECIAL'])
        self.assertGreaterEqual(self.e.special.confirmed_ns,700_000_000)
    def test_second_missing_primary_resets_instead_of_extending_bridge(self):
        self.seed();self.feed(heavy='cross',missing=True);self.feed(heavy='cross',missing=True)
        self.assertEqual(self.e.special.confirmed_ns,0)
        for _ in range(20):self.feed(heavy='cross')
        self.assertFalse(self.events)
    def test_next_primary_outside_original_geometry_window_starts_new_hold(self):
        self.seed();self.feed(heavy='cross',missing=True)
        self.feed(heavy='cross',gap=50_000_000)
        self.assertEqual(self.e.special.confirmed_ns,0)
    def test_ordinary_tracking_loss_still_requires_full_hold(self):
        for _ in range(12):self.feed('cross')
        self.feed('cross',missing=True)
        self.assertEqual(self.e.special.confirmed_ns,0)
    def test_missing_auxiliary_or_non_cross_heavy_cannot_preserve(self):
        for slots,heavy in ((2,'cross'),(1,'cross'),(3,'guard'),(3,'left_raise')):
            with self.subTest(slots=slots,heavy=heavy):
                self.setUp();self.seed();self.feed(heavy=heavy,missing=True,slots=slots)
                self.assertEqual(self.e.special.confirmed_ns,0)
    def test_wrong_identity_clock_size_or_missing_joint_in_any_slot_rejects(self):
        changes=({'actor_id':'other'},{'capture_epoch':'other'},{'frame_id':0},
                 {'capture_ns':0},{'width':1280},{'source_mirrored':True},
                 {'pose_present':False},{'visibility':(0.,)*33})
        for slot in range(3):
            for change in changes:
                with self.subTest(slot=slot,change=change):
                    self.setUp();self.seed()
                    def mutate(a):a[slot]=replace(a[slot],**change);return a
                    self.feed(heavy='cross',missing=True,mutate=mutate)
                    self.assertEqual(self.e.special.confirmed_ns,0)
    def test_a_different_body_cannot_inherit_previous_dwell(self):
        self.seed()
        def shift(a):
            return [replace(o,landmarks=tuple((x+.35,y,z)for x,y,z in o.landmarks))for o in a]
        self.feed(heavy='cross',missing=True,mutate=shift)
        self.assertEqual(self.e.special.confirmed_ns,0)
    def test_source_format_change_cannot_inherit_previous_dwell(self):
        for change in ({'width':1280},{'source_mirrored':True}):
            with self.subTest(change=change):
                self.setUp();self.seed()
                self.e.cross_previous_primary=replace(self.e.cross_previous_primary,**change)
                self.feed(heavy='cross',missing=True)
                self.assertEqual(self.e.special.confirmed_ns,0)
    def test_visible_extension_in_full_model_rejects_bridge(self):
        self.seed()
        def extending(a):
            p=list(a[0].landmarks);p[15]=(.65,.90,0.)
            a[0]=replace(a[0],landmarks=tuple(p));return a
        self.feed(heavy='cross',missing=True,mutate=extending)
        self.assertEqual(self.e.special.confirmed_ns,0)
    def test_long_source_gap_or_stale_result_cannot_bridge(self):
        for kwargs in ({'gap':100_000_001},{'age':120_000_001}):
            with self.subTest(kwargs=kwargs):
                self.setUp();self.seed();self.feed(heavy='cross',missing=True,**kwargs)
                self.assertEqual(self.e.special.confirmed_ns,0)
    def test_missing_primary_cannot_acquire_x(self):
        for _ in range(40):self.feed(heavy='cross',missing=True)
        self.assertFalse(self.e.special.owned);self.assertFalse(self.events)
    def test_fired_x_keeps_repeat_latch_across_occlusion(self):
        self.seed()
        for _ in range(30):self.feed(heavy='cross')
        self.feed(heavy='cross',missing=True)
        for _ in range(50):self.feed(heavy='cross')
        self.assertEqual([x.reason['gesture']for x in self.events],['SPECIAL'])
    def test_recalibration_and_watchdog_clear_private_continuity(self):
        self.seed();self.feed(heavy='cross',missing=True);self.e.request_calibration()
        self.assertIsNone(self.e.cross_previous_primary);self.assertIsNone(self.e.cross_bridge_until)
        self.assertEqual(self.e.special.confirmed_ns,0)
        self.setUp();self.seed();self.feed(heavy='cross',missing=True)
        self.e.tick(self.stamp+300_000_000)
        self.assertEqual(self.e.special.confirmed_ns,0)

if __name__=='__main__':unittest.main()
