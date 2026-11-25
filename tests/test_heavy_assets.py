import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from redbool_demo import backend
from redbool_demo.backend_assets import POSE_SHA256,verify_pose_asset

ROOT=Path(__file__).resolve().parents[1]
def script(name):
    spec=importlib.util.spec_from_file_location('audit_'+name,ROOT/'scripts'/f'{name}.py')
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result


class HeavyAssetTests(unittest.TestCase):
    def test_packaged_default_off_and_explicit_bundled_path_is_absolute(self):
        with tempfile.TemporaryDirectory() as bundle,patch.object(backend.sys,'_MEIPASS',bundle,create=True),\
             patch('redbool_demo.live.main') as live,contextlib.redirect_stdout(io.StringIO()):
            backend.main(['--check'])
            self.assertNotIn('--cross-refine-pose',live.call_args.args[0])
            backend.main(['--check','--cross-refine-pose','bundled'])
            args=live.call_args.args[0];path=Path(args[args.index('--cross-refine-pose')+1])
            self.assertEqual(path,Path(bundle)/'assets/pose_landmarker_heavy.task')
            self.assertTrue(path.is_absolute())

    def test_unverified_heavy_model_cannot_load(self):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'wrong.task';p.write_bytes(b'unverified')
            with self.assertRaisesRegex(ValueError,'SHA'):verify_pose_asset(p,'Heavy')

    def test_setup_download_set_stays_two_unless_explicitly_enabled(self):
        setup=script('setup_demo_models')
        with tempfile.TemporaryDirectory() as folder,contextlib.redirect_stdout(io.StringIO()):
            root=Path(folder);source=root/'source';dest=root/'dest';source.mkdir();dest.mkdir()
            fixture=[]
            for name in ('lite','full','heavy'):
                payload=name.encode();relative=name+'.task';(source/relative).write_bytes(payload)
                fixture.append((relative,'https://invalid.example/',hashlib.sha256(payload).hexdigest()))
            with patch.object(setup,'MODELS',fixture[:2]),patch.object(setup,'HEAVY',fixture[2]):
                setup.setup(dest,source=source)
                self.assertEqual(len(list(dest.glob('*.task'))),2)
                setup.setup(dest,source=source,include_heavy=True)
                self.assertEqual(len(list(dest.glob('*.task'))),3)
                setup.setup(dest,check=True,include_heavy=True)

    def test_build_opt_in_metadata_and_reused_workdir_cannot_accidentally_bundle_heavy(self):
        build=script('build_camera_backend')
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);models=[]
            for name in ('lite','full','heavy'):
                p=root/(name+'.task');p.write_bytes(name.encode());models.append(p)
            args=['build','--pose',str(models[0]),'--cross-pose',str(models[1]),'--work',str(root/'work'),'--check']
            digest=hashlib.sha256(models[2].read_bytes()).hexdigest()
            with patch.dict(POSE_SHA256,Heavy=digest),patch.object(sys,'argv',args+['--cross-refine-pose',str(models[2])]),\
                 contextlib.redirect_stdout(io.StringIO()) as output:
                build.main()
            metadata=json.loads(output.getvalue());self.assertFalse(metadata['optional_cross_refine']['default_enabled'])
            self.assertEqual(metadata['models']['pose_landmarker_heavy.task'],digest)
            with patch.object(sys,'argv',args),contextlib.redirect_stdout(io.StringIO()) as output:build.main()
            metadata=json.loads(output.getvalue())
            self.assertFalse(metadata['optional_cross_refine']['bundled'])
            self.assertNotIn('pose_landmarker_heavy.task',metadata['models'])
            self.assertEqual(models[2].read_bytes(),b'heavy')


if __name__=='__main__':unittest.main()
