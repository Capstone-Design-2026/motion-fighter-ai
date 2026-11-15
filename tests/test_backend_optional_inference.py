"""Native-check wiring tests with fake runtimes; no camera/model/network work."""
from enum import Enum
import hashlib
import importlib.util
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

from redbool_demo.backend_assets import POSE_SHA256


SOURCE = Path(__file__).resolve().parents[1]/'src/redbool_demo/backend_inference_check.py'


class OptionalInferenceCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.assets = Path(self.temp.name)
        self.hashes = {}
        for variant in ('Lite', 'Full', 'Heavy'):
            data = ('fixture-' + variant).encode()
            (self.assets/('pose_landmarker_' + variant.lower() + '.task')).write_bytes(data)
            self.hashes[variant] = hashlib.sha256(data).hexdigest()
        self.created = []
        self.closed = []
        self.calls = []
        owner = self

        class RunningMode(Enum):
            VIDEO = 1
            IMAGE = 2

        class Options:
            def __init__(self, **kwargs):
                self.__dict__.update(kwargs)

        class BaseOptions(Options):
            Delegate = types.SimpleNamespace(CPU='CPU')

        class Model:
            def __init__(self, options):
                self.options = options

            def detect(self, image):
                owner.calls.append(('IMAGE', self.options.base_options.model_asset_path))
                return types.SimpleNamespace(pose_landmarks=[])

            def detect_for_video(self, image, timestamp):
                owner.calls.append(('VIDEO', self.options.base_options.model_asset_path))
                return types.SimpleNamespace(pose_landmarks=[])

            def close(self):
                owner.closed.append(self.options.base_options.model_asset_path)

        class PoseLandmarker:
            @staticmethod
            def create_from_options(options):
                owner.created.append(options)
                return Model(options)

        mp = types.ModuleType('mediapipe')
        mp.__version__ = 'fake-no-native'
        mp.ImageFormat = types.SimpleNamespace(SRGB='RGB')
        mp.Image = lambda **kwargs: kwargs
        base = types.ModuleType('mediapipe.tasks.python')
        base.BaseOptions = BaseOptions
        vision = types.ModuleType('mediapipe.tasks.python.vision')
        vision.PoseLandmarker = PoseLandmarker
        vision.PoseLandmarkerOptions = Options
        vision.RunningMode = RunningMode
        cv = types.ModuleType('cv2')
        cv.__version__ = 'fake-no-camera'
        np = types.ModuleType('numpy')
        np.__version__ = 'fake-no-native'
        np.uint8 = 'uint8'
        np.zeros = lambda shape, dtype: ('synthetic-blank', shape, dtype)
        self.modules = {'cv2': cv, 'mediapipe': mp,
                        'numpy': np,
                        'mediapipe.tasks.python': base, 'mediapipe.tasks.python.vision': vision}
        spec = importlib.util.spec_from_file_location('redbool_demo.optional_inference_check_fixture', SOURCE)
        self.check = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.check)

    def run_check(self, arguments):
        with patch.dict(sys.modules, self.modules), patch.dict(POSE_SHA256, self.hashes):
            return self.check.run(arguments, self.assets)

    def test_default_ignores_even_corrupt_optional_asset_and_checks_original_three(self):
        (self.assets/'pose_landmarker_heavy.task').write_bytes(b'leftover-invalid-optional-file')
        result = self.run_check([])
        self.assertEqual([(row['variant'], row['mode']) for row in result['models']],
                         [('Lite', 'VIDEO'), ('Full', 'VIDEO'), ('Full', 'IMAGE')])
        self.assertEqual((len(self.created), len(self.closed), len(self.calls)), (3, 3, 9))
        self.assertFalse(result['camera_opened'])
        self.assertEqual(result['game_packets'], 0)

    def test_explicit_bundled_heavy_uses_image_mode_and_closes_all_four_models(self):
        result = self.run_check(['--cross-refine-pose', 'bundled'])
        self.assertEqual([(row['variant'], row['mode']) for row in result['models']],
                         [('Lite', 'VIDEO'), ('Full', 'VIDEO'), ('Full', 'IMAGE'), ('Heavy', 'IMAGE')])
        self.assertEqual((len(self.created), len(self.closed), len(self.calls)), (4, 4, 12))
        self.assertEqual(self.created[-1].base_options.model_asset_path,
                         str(self.assets/'pose_landmarker_heavy.task'))
        self.assertTrue(all(options.base_options.delegate == 'CPU' for options in self.created))

    def test_explicit_verified_external_path_is_used_without_auto_discovery(self):
        heavy = self.assets/'verified-copy.task'
        heavy.write_bytes(b'fixture-Heavy')
        (self.assets/'pose_landmarker_heavy.task').unlink()
        result = self.run_check(['--cross-refine-pose', str(heavy)])
        self.assertEqual(result['models'][-1]['variant'], 'Heavy')
        self.assertEqual(self.created[-1].base_options.model_asset_path, str(heavy))

    def test_corrupt_requested_heavy_fails_before_any_model_starts(self):
        (self.assets/'pose_landmarker_heavy.task').write_bytes(b'wrong')
        with self.assertRaisesRegex(ValueError, 'SHA'):
            self.run_check(['--cross-refine-pose', 'bundled'])
        self.assertEqual(self.created, [])
        self.assertEqual(self.calls, [])


if __name__ == '__main__':
    unittest.main()
