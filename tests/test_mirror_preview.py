"""Preview reflection must not mutate source pixels or anatomical pose labels."""
import unittest
from unittest.mock import patch
import numpy as np
from continuous_events.viewer import overlay

class MirrorPreviewTests(unittest.TestCase):
    def test_reflects_preview_pixels_only(self):
        raw=np.zeros((48,80,3),np.uint8);raw[:,:20]=[20,80,150];before=raw.copy()
        frame={'frame_id':1,'capture_return_monotonic_ns':1,'image_landmarks':None}
        with patch('continuous_events.viewer.JOINTS',{}),patch('continuous_events.viewer.joint_states',return_value={}):
            original=overlay(raw,frame,mirror=False);mirror=overlay(raw,frame,mirror=True)
        np.testing.assert_array_equal(mirror[:48],original[:48,::-1])
        np.testing.assert_array_equal(mirror[48:],original[48:])
        np.testing.assert_array_equal(raw,before)
    def test_joint_labels_keep_anatomical_ids_at_reflected_positions(self):
        points=[[.5,.5,0.]]*33;points[13]=[.25,.5,0.]
        frame={'frame_id':1,'capture_return_monotonic_ns':1,'image_landmarks':points}
        before=[p[:] for p in points]
        with patch('continuous_events.viewer.JOINTS',{13:'LEFT_ELBOW'}),patch('continuous_events.viewer.joint_states',return_value={13:['VALID']}),patch('continuous_events.viewer.cv2.putText') as text,patch('continuous_events.viewer.cv2.circle') as circle:
            overlay(np.zeros((48,80,3),np.uint8),frame,mirror=True)
            label=next(c for c in text.call_args_list if c.args[1]=='13')
            self.assertEqual(label.args[2],(60,23));self.assertEqual(circle.call_args.args[1],(60,23))
        self.assertEqual(points,before)
    def test_cli_defaults_to_mirror_and_can_explicitly_disable(self):
        from redbool_demo import live
        for argv,expected in [(['live','--check'],True),(['live','--check','--mirror'],True),(['live','--check','--no-mirror'],False)]:
            with patch('sys.argv',argv),patch.object(live,'run') as run:
                live.main();self.assertIs(run.call_args.args[0].mirror,expected)
