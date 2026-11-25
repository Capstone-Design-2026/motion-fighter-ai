import unittest
from types import SimpleNamespace
from redbool_demo.framing import framing_status

class FramingTests(unittest.TestCase):
    def observation(self):
        return SimpleNamespace(pose_present=True,landmarks=[[.5,.5,0.] for _ in range(33)],visibility=[1.]*33)
    def test_inside_and_folded_high_guard_are_clear(self):
        o=self.observation();o.landmarks[15][1]=.2;o.landmarks[16][1]=.2
        self.assertEqual(framing_status(o)['code'],'OK')
    def test_outside_elbow_or_wrist_reports_without_mutation(self):
        for j in (13,14,15,16):
            o=self.observation();o.landmarks[j][1]=1.1
            self.assertEqual(framing_status(o)['joints'],[j]);self.assertEqual(framing_status(o)['code'],'OUTSIDE')
            self.assertEqual(o.landmarks[j][1],1.1)
    def test_warns_before_low_hand_leaves_frame(self):
        o=self.observation();o.landmarks[16][1]=.97
        self.assertEqual(framing_status(o)['code'],'EDGE')
    def test_uncertain_and_missing_pose(self):
        o=self.observation();o.visibility[14]=.1
        self.assertEqual(framing_status(o)['code'],'UNCERTAIN')
        o.pose_present=False;self.assertEqual(framing_status(o)['code'],'NO_POSE')
    def test_invalid_coordinate_cannot_look_clear(self):
        o=self.observation();o.landmarks[16][0]=float('nan')
        self.assertEqual(framing_status(o)['code'],'UNCERTAIN')
