from pathlib import Path
import os
import tempfile
import unittest
from unittest.mock import patch
from redbool_demo.backend import main, packaged_assets
from redbool_demo.backend_assets import verify_pose_asset
from redbool_demo.backend_cache import configure_cache


class BackendEntryTests(unittest.TestCase):
    def test_check_keeps_camera_off_and_uses_bundled_absolute_defaults(self):
        with tempfile.TemporaryDirectory() as bundle, \
             patch('redbool_demo.backend.sys._MEIPASS',bundle,create=True), \
             patch('redbool_demo.live.main') as live, \
             patch('redbool_demo.local_bridge.LocalBridge',side_effect=AssertionError('check opened local server')):
            main(['--check'])
            args=live.call_args.args[0]
            pose=Path(args[args.index('--pose')+1])
            cross=Path(args[args.index('--cross-pose')+1])
            self.assertTrue(pose.is_absolute());self.assertTrue(cross.is_absolute())
            self.assertEqual(pose,Path(bundle)/'assets'/'pose_landmarker_lite.task')
            self.assertEqual(cross,Path(bundle)/'assets'/'pose_landmarker_full.task')
            self.assertEqual(args[args.index('--recording')+1],'diagnostics')
            self.assertIn('--headless',args);self.assertIn('--personal-calibration',args)
            self.assertIsNone(live.call_args.kwargs['bridge_instance'])

    def test_corrupt_packaged_asset_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            model=Path(td)/'model.task';model.write_bytes(b'wrong model')
            with self.assertRaisesRegex(ValueError,'SHA'):verify_pose_asset(model,'Lite')
            with self.assertRaisesRegex(ValueError,'missing'):verify_pose_asset(model,'Unknown')

    def test_packaged_font_cache_reused_and_separate_for_moved_bundle(self):
        with tempfile.TemporaryDirectory() as td,patch.dict(os.environ,{'REDBOOL_CAMERA_CACHE_DIR':td}), \
             patch('redbool_demo.backend_cache.sys._MEIPASS','/app/one',create=True):
            first=configure_cache();self.assertTrue(first.is_dir())
            self.assertEqual(first,configure_cache())
            self.assertEqual(Path(os.environ['MPLCONFIGDIR']),first.resolve())
            with patch('redbool_demo.backend_cache.sys._MEIPASS','/app/two'):
                self.assertNotEqual(first,configure_cache())


if __name__=='__main__':unittest.main()
