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

class ShortForwardPunchTests(unittest.TestCase):
    # A guard-level fist separates towards its own side while the elbow lifts.
    # The same image-plane path can be a small punch or a guard readjustment.
    WRIST_X = (0., .01, .02, .03, .05, .25, .34)
    ELBOW_Y = (.91, .89, .86, .76, .75, .70, .68)
    FORWARD = (0., 0., 0., 0., 0., .25, .27)
    LEVEL = (0.,) * 7

    def run_stroke(self, sides=('RIGHT',), *, wrist_depth=FORWARD, elbow_depth=LEVEL,
                   occluded=None, disabled=None, gap=None, direction=1, wrist_y=.24):
        detector = LowPunchProjection()
        results = []
        for n, x in enumerate(self.WRIST_X):
            p = [(.5, .4, 0.)] * 33
            v = [1.] * 33
            arms = {}
            p[11], p[12] = (.6, .4, 0.), (.4, .4, 0.)
            eligible = []
            for i, side in enumerate(('LEFT', 'RIGHT')):
                active = side in sides
                eligible.append(active and n != disabled)
                if not active:
                    continue
                sx, sy, _ = p[11 + i]
                sign = (1, -1)[i]
                p[13+i] = (sx + sign * .2 * .51, sy + .2 * self.ELBOW_Y[n], -.2 + .2 * elbow_depth[n])
                p[15+i] = (sx + sign * .2 * direction * x, sy + .2 * wrist_y, -.45 - .2 * wrist_depth[n])
                for k in (17, 19, 21):
                    p[k+i] = p[15+i]
                if n == occluded:
                    v[15+i] = .5
                arms[side] = dict(angle=.3, angle3d=2.1, velocity=(sign * 2, 0., 0.))
            stamp = n * 33_000_000 + (150_000_000 if gap is not None and n >= gap else 0)
            obs = PoseObservation('demo', 'one', n, stamp, tuple(p), tuple(v), 640, 640)
            results.append(detector.observe(obs, arms, eligible))
        return results

    def test_forward_motion_is_supported_on_both_anatomical_sides(self):
        for side in ('LEFT', 'RIGHT'):
            self.assertEqual(self.run_stroke((side,)), [None] * 6 + [side])

    def test_sideways_guard_repositioning_and_retreat(self):
        for depth in (self.LEVEL, tuple(-x for x in self.FORWARD)):
            self.assertEqual(self.run_stroke(wrist_depth=depth), [None] * 7)

    def test_only_moving_the_elbow_or_moving_the_arm_rigidly(self):
        self.assertEqual(self.run_stroke(wrist_depth=self.LEVEL, elbow_depth=self.FORWARD), [None] * 7)
        self.assertEqual(self.run_stroke(elbow_depth=tuple(-x for x in self.FORWARD)), [None] * 7)

    def test_single_depth_glitch_and_uncertain_side(self):
        self.assertEqual(self.run_stroke(wrist_depth=(0.,) * 6 + (.5,)), [None] * 7)
        self.assertEqual(self.run_stroke(('LEFT', 'RIGHT')), [None] * 7)
        self.assertEqual(self.run_stroke(direction=-1), [None] * 7)

    def test_context_lost_or_arm_raised(self):
        for option in ({'gap':4}, {'disabled':4}, {'occluded':4}, {'wrist_y':-.1}):
            with self.subTest(option=option):
                self.assertEqual(self.run_stroke(**option), [None] * 7)


if __name__ == '__main__':
    unittest.main()
