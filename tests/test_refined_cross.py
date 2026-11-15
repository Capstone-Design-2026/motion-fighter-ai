"""Optional crossed-arm evidence cannot bypass acquisition or repeat a hold."""
from dataclasses import replace
import unittest
from test_chest_special import pose
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.refined_special import definite_cross_release


class RefinedCrossTests(unittest.TestCase):
    def setUp(self):
        self.engine = SixGestureEngine()
        self.engine.request_calibration()
        self.frame = 0
        self.events = []
        self.feed(55, 'guard')
        self.assertTrue(self.engine.calibrated)

    def feed(self, count, kind, refinement=None, mutate=lambda p: p, gap=33_333_333):
        for _ in range(count):
            self.frame += 1
            stamp = getattr(self, 'stamp', 0) + gap
            self.stamp = stamp
            obs = pose(kind, self.frame, stamp)
            if kind == 'high_raise':
                points = list(obs.landmarks)
                points[15] = (.60, .03, 0.)
                obs = replace(obs, landmarks=tuple(points))
            aux = (obs, obs)
            if refinement is not None:
                extra = mutate(pose(refinement, self.frame, stamp))
                aux += (extra,)
            self.engine.process(obs, now_ns=stamp, special_observation=aux)
            self.events.extend(self.engine.drain_events(stamp))
            self.engine.take_logs()

    def specials(self):
        return [e for e in self.events if e.reason.get('gesture') == 'SPECIAL']

    def seed(self):
        self.feed(3, 'cross')
        self.assertTrue(self.engine.special.owned)
        self.assertFalse(self.engine.special.latched)

    def test_refinement_alone_cannot_acquire_x(self):
        self.feed(100, 'guard', 'cross')
        self.assertFalse(self.specials())

    def test_same_frame_refinement_completes_existing_x_once(self):
        self.seed()
        self.feed(90, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 1)
        self.assertTrue(self.engine.cross_refinement_latched)
        self.assertEqual(self.specials()[0].expires_ns-self.specials()[0].committed_ns, 150_000_000)

    def test_missing_refinement_cannot_make_ambiguous_held_x_repeat(self):
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.feed(80, 'guard')
        self.assertTrue(self.engine.cross_refinement_latched)
        self.assertFalse(self.engine.special.owned)
        self.feed(80, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 1)

    def test_fresh_refinement_release_then_new_seed_rearms(self):
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.feed(10, 'guard', 'guard')
        self.assertFalse(self.engine.special.owned)
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 2)

    def test_definite_raise_releases_if_optional_worker_is_unavailable(self):
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.feed(50, 'high_raise')
        self.assertFalse(self.engine.special.latched)
        self.assertEqual(sum(e.reason.get('gesture') == 'LEFT_RAISE' for e in self.events), 1)
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 2)

    def test_wrong_source_and_missing_wrist_refinement_do_not_complete(self):
        for change in ({'actor_id': 'other'}, {'capture_epoch': 'other'},
                       {'frame_id': 0}, {'capture_ns': 0}, {'width': 1280},
                       {'source_mirrored': True}, {'pose_present': False},
                       {'visibility': (0.,)*33}):
            with self.subTest(change=change):
                self.setUp()
                self.seed()
                self.feed(60, 'guard', 'cross', lambda p: replace(p, **change))
                self.assertFalse(self.specials())

    def test_stale_open_refinement_is_not_a_release(self):
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.feed(50, 'guard', 'guard', lambda p: replace(p, capture_ns=p.capture_ns-1))
        self.assertTrue(self.engine.cross_refinement_latched)
        self.assertFalse(self.engine.special.owned)
        self.feed(40, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 1)

    def test_recalibration_clears_refinement_latch(self):
        self.seed()
        self.feed(40, 'guard', 'cross')
        self.engine.request_calibration()
        self.assertFalse(self.engine.cross_refinement_latched)
        self.assertFalse(self.engine.special.latched)

    def test_missing_wrist_is_never_definite_release(self):
        p = pose('left_raise')
        v = list(p.visibility)
        v[16] = 0
        self.assertFalse(definite_cross_release(replace(p, visibility=tuple(v)), self.engine.config))

    def test_normal_x_retains_original_guard_release(self):
        self.feed(40, 'cross')
        self.assertEqual(len(self.specials()), 1)
        self.assertFalse(self.engine.cross_refinement_latched)
        self.feed(10, 'guard')
        self.assertFalse(self.engine.special.latched)


if __name__ == '__main__':
    unittest.main()
