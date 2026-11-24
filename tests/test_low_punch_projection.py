import unittest

from action_engine.types import PoseObservation
from redbool_demo.low_punch import LowPunchProjection


class LowPunchProjectionTests(unittest.TestCase):
    def setUp(self):
        self.detector = LowPunchProjection()
        self.arms = {'RIGHT': {'angle': .22, 'angle3d': 1.9, 'velocity': (0.,1.,0.)}}

    def observe(self, ms, n, *, elbow_slope=-.14, spread_slope=-.12, eligible=True, visibility=1.0):
        # Project both joints in shoulder-width units into a camera image.
        p = [(.50, .4, 0.)] * 33
        p[11], p[12] = (.60, .4, 0.), (.40, .4, 0.)
        p[14] = (.33, .4 + .2 * (.88 + elbow_slope * n), 0.)
        wrist = (.39, .4 + .2*(.14 + .19*n), 0.)
        p[16] = wrist
        for i in (18,20,22):
            p[i] = (wrist[0] + .2*(.24 + spread_slope*n), wrist[1], 0.)
        v = [1.] * 33
        v[16] = visibility
        obs = PoseObservation('p', 'stream', ms, ms*1_000_000, tuple(p), tuple(v), 480, 480, True)
        self.arms['RIGHT'].update(angle=.22+.30*n, angle3d=1.9+.18*n)
        return self.detector.observe(obs, self.arms, (False, eligible))

    def test_four_observations_of_wrist_descending_elbow_lifting(self):
        for ms,n in ((10,0.),(45,.3),(80,.65)):
            self.assertIsNone(self.observe(ms,n))
        self.assertEqual(self.observe(115,1.),'RIGHT')

    def test_lowering_the_whole_arm_and_unchanged_fist_projection(self):
        for adjustment in ({'elbow_slope':.12}, {'spread_slope':.0}):
            with self.subTest(adjustment):
                self.setUp()
                self.assertTrue(all(self.observe(ms,n,**adjustment) is None
                    for ms,n in ((10,0.),(45,.3),(80,.65),(115,1.))))

    def test_unsupported_occluded_or_discontinuous_samples_clear_history(self):
        for adjustment in ({'eligible':False}, {'visibility':.6}, {'gap':True}):
            with self.subTest(adjustment):
                self.setUp()
                self.observe(10,0.); self.observe(45,.3); self.observe(80,.6)
                if adjustment.get('gap'):
                    self.assertIsNone(self.observe(220,1.))
                else:
                    self.assertIsNone(self.observe(115,1.,**adjustment))
                    self.assertIsNone(self.observe(150,1.))

if __name__ == '__main__':
    unittest.main()
