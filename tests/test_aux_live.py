import unittest
from pathlib import Path
from unittest.mock import patch
from test_redbool_demo import SyntheticLiveTests
from redbool_demo.live import run as actual_run
class AuxiliaryLiveTests(unittest.TestCase):
    def test_auxiliary_pose_records_and_shuts_down_without_camera(self):
        model=Path(__file__).resolve().parents[1]/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task'
        def run(args):
            args.cross_pose=model
            return actual_run(args)
        with patch('redbool_demo.live.run',side_effect=run):
            SyntheticLiveTests('test_camera_free_full_live_recording_and_clean_shutdown').test_camera_free_full_live_recording_and_clean_shutdown()
