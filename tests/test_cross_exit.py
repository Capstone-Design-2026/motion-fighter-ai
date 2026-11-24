import unittest
from dataclasses import replace

from action_engine.types import PoseObservation
from redbool_demo.cross_exit import CrossExit


def pose(frame, ms, shape, left_only=False):
    points = [(.5, .5, 0.)] * 33
    visibility = [1.] * 33
    wx, wy, fx, fy = {
        'cross': (-.12, .12, -.31, .02),
        'split': (.24, .40, .08, .28),
        'low': (.52, .69, .34, .54),
        'middle': (.01, .30, .08, .16),
    }[shape]
    for direction, shoulder, elbow, wrist, index in ((1,11,13,15,19),(-1,12,14,16,20)):
        for idx, x, y in ((shoulder,.50,0), (elbow,.72,.78), (wrist,wx,wy), (index,fx,fy)):
            points[idx] = (.5+direction*x*.3, .40+y*.4, 0.)
    if left_only:
        visibility[16] = visibility[20] = 0.
    return PoseObservation('p', 'epoch', frame, int(ms*1e6), tuple(points), tuple(visibility), 640, 480)


class CrossExitTests(unittest.TestCase):
    def setUp(self):
        self.exit = CrossExit()
        self.ms = 0
        self.frame = 0

    def observe(self, shape, *, partial=False, strict=False, mutation=None, eligible=True, gap=33):
        self.frame += 1
        self.ms += gap
        primary = pose(self.frame,self.ms,shape,partial)
        secondary = replace(primary)
        if mutation:
            secondary = mutation(secondary)
        return self.exit.step(primary,secondary,eligible=eligible,strict=strict,now=primary.capture_ns)

    def departure(self, mutation=None):
        self.observe('cross')
        self.observe('split', mutation=mutation)
        for _ in range(4):
            self.observe('low', partial=True, mutation=mutation)

    def test_requires_genuine_split_and_both_sources_on_reentry(self):
        self.departure()
        self.assertFalse(self.observe('cross', gap=500))
        self.assertFalse(self.observe('cross', strict=True, mutation=lambda _:None))
        self.assertTrue(self.observe('cross', strict=True))
        self.assertFalse(self.observe('cross', strict=True))

    def test_held_x_survives_tracking_loss(self):
        self.observe('cross')
        for _ in range(8):
            self.observe('cross', partial=True)
        self.assertFalse(self.observe('cross', strict=True, gap=500))

    def test_one_arm_motion_without_bilateral_split_cannot_release(self):
        self.observe('cross')
        for _ in range(8):
            self.observe('low', partial=True)
        self.assertFalse(self.observe('cross', strict=True, gap=500))

    def test_camera_gap_cannot_count_as_visible_exit(self):
        self.observe('cross')
        self.observe('split')
        self.observe('low', partial=True)
        self.observe('low', partial=True, gap=150)
        self.assertFalse(self.observe('cross', strict=True, gap=500))

    def test_body_mismatch_malformed_source_or_invisible_elbow_cannot_release(self):
        def hidden_elbow(obs):
            confidence = list(obs.visibility)
            confidence[13] = .3
            return replace(obs, visibility=tuple(confidence))
        for mutate in (lambda o:replace(o,capture_epoch='other'),
                       lambda o:replace(o,landmarks=o.landmarks[:15]),
                       lambda o:replace(o,width=0),
                       lambda o:replace(o, frame_id=o.frame_id-1),
                       lambda o:replace(o,landmarks=tuple((x+.14,y,z) for x,y,z in o.landmarks)),
                       hidden_elbow):
            with self.subTest(mutate=mutate):
                self.setUp()
                self.departure(mutate)
                self.assertFalse(self.observe('cross', strict=True, gap=500))

    def test_expired_split_cannot_donate_a_single_low_hand_to_the_next_split(self):
        self.observe('cross')
        self.observe('split')
        for _ in range(8):
            self.observe('middle')
        self.observe('low', partial=True)
        self.observe('middle')
        self.observe('cross')
        self.observe('low')
        self.assertFalse(self.observe('cross', strict=True, gap=500))

    def test_time_limit_and_calibration_reset_both_discard_evidence(self):
        self.departure()
        self.assertFalse(self.observe('cross', strict=True, gap=5100))
        self.departure()
        self.observe('split', eligible=False)
        self.assertFalse(self.observe('cross', strict=True, gap=500))


if __name__ == '__main__':
    unittest.main()
