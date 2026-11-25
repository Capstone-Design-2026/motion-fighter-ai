from types import SimpleNamespace
import unittest
from redbool_demo.framing import framing_status


class FramingEdgeTests(unittest.TestCase):
    def observation(self):
        return SimpleNamespace(pose_present=True,landmarks=[[.5,.5,0.] for _ in range(33)],visibility=[1.]*33)

    def test_both_tracked_wrists_below_image_get_specific_guidance(self):
        o=self.observation();o.landmarks[15][1]=1.1;o.landmarks[16][1]=1.05
        result=framing_status(o)
        self.assertEqual(result['outside_edges'],['BOTTOM'])
        self.assertEqual(result['joints'],[15,16]);self.assertEqual(result['code'],'OUTSIDE')
        self.assertIn('BELOW',result['message'])
        self.assertEqual(o.landmarks[15][1],1.1)

    def test_mixed_edges_keep_general_guidance(self):
        o=self.observation();o.landmarks[15][:2]=[-.1,1.1];o.landmarks[16][1]=-.1
        result=framing_status(o)
        self.assertEqual(result['outside_edges'],['BOTTOM','LEFT','TOP'])
        self.assertNotIn('BELOW',result['message'])

    def test_inside_uncertain_pose_does_not_report_an_outside_direction(self):
        o=self.observation();o.visibility[16]=.1
        result=framing_status(o)
        self.assertEqual(result['code'],'UNCERTAIN');self.assertNotIn('outside_edges',result)


if __name__=='__main__':unittest.main()
