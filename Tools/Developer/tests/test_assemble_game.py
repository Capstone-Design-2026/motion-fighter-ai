"""Camera-free package integrity checks with private miniature runtime folders."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
spec=importlib.util.spec_from_file_location('tested_assembly',TOOLS/'assemble_game.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)


class AssemblyIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.player=self.root/'player/REDBOOL.exe';self.player.parent.mkdir()
        self.player.write_bytes(b'fixture Unity entry; never executed')
        (self.player.parent/'UnityPlayer.dll').write_bytes(b'fixture runtime')
        (self.player.parent/'REDBOOL_Data').mkdir()
        self.backend=self.root/'backend';assets=self.backend/'_internal/assets';assets.mkdir(parents=True)
        (self.backend/'REDBOOLCamera.exe').write_bytes(b'fixture native entry; never executed')
        self.expected={}
        for name in m.EXPECTED_MODELS:
            body=('fixture '+name).encode();(assets/name).write_bytes(body)
            self.expected[name]=hashlib.sha256(body).hexdigest()
        constants=patch.dict(m.EXPECTED_MODELS,self.expected,clear=True);constants.start();self.addCleanup(constants.stop)
        self.metadata={'platform':'Windows','architecture':'AMD64','release':'v36.3','recording_schema':'redbool.landmarks/1',
            'source_commit':'b'*40,'source_sha256':'c'*64,'source_dirty':False,'source_file_count':71,
            'models':self.expected,'optional_cross_refine':{'bundled':True,'default_enabled':False},'command':['private build path']}
        self.save_metadata()
        self.output=self.root/'package'
        stamp=patch.object(m,'snapshot',return_value={'release':'v36.3','view_protocol':33,'git_commit':'a'*40,'runtime_sha256':'d'*64});stamp.start();self.addCleanup(stamp.stop)

    def save_metadata(self):
        (self.backend/'backend-build.json').write_text(json.dumps(self.metadata))
        keys=('release','recording_schema','source_commit','source_sha256','source_dirty','source_file_count')
        (self.backend/'_internal/assets/backend-provenance.json').write_text(json.dumps({k:self.metadata[k]for k in keys}))

    def assemble(self):return m.assemble(self.player,self.backend,self.output,'windows')

    def test_complete_three_model_package_keeps_optional_policy_and_source_identity(self):
        result=self.assemble();installed=self.output/'REDBOOL_Data/StreamingAssets/RedboolCamera'
        self.assertEqual(result['release'],'v36.3');self.assertEqual(result['lan_protocol'],33)
        self.assertEqual(result['backend']['optional_cross_refine'],{'bundled':True,'default_enabled':False})
        self.assertEqual(result['backend']['source_commit'],'b'*40)
        self.assertNotIn('command',result['backend'])
        self.assertEqual(result['backend'],json.loads((installed/'backend-build.json').read_text()))
        for name,digest in self.expected.items():self.assertEqual(hashlib.sha256((installed/'_internal/assets'/name).read_bytes()).hexdigest(),digest)
        self.assertIn('explicit Unity argument',result['camera_x_refinement'])
        self.assertEqual(json.loads((self.backend/'backend-build.json').read_text()),self.metadata)

    def test_missing_heavy_is_rejected_before_any_package_is_written(self):
        (self.backend/'_internal/assets/pose_landmarker_heavy.task').unlink()
        with self.assertRaisesRegex(ValueError,'fingerprint'):self.assemble()
        self.assertFalse(self.output.exists())

    def test_tampered_heavy_cannot_pass_metadata_hash(self):
        (self.backend/'_internal/assets/pose_landmarker_heavy.task').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError,'fingerprint'):self.assemble()
        self.assertFalse(self.output.exists())

    def test_packaging_metadata_cannot_silently_enable_source_default(self):
        for optional in ({},{'bundled':False,'default_enabled':False},{'bundled':True,'default_enabled':True}):
            with self.subTest(optional=optional):
                self.metadata['optional_cross_refine']=optional;self.save_metadata()
                with self.assertRaisesRegex(ValueError,'source/dev default-off'):self.assemble()
                self.assertFalse(self.output.exists())

    def test_old_backend_and_wrong_platform_are_rejected(self):
        self.metadata['release']='v36.2';self.save_metadata()
        with self.assertRaisesRegex(ValueError,'matching reviewed'):self.assemble()
        self.metadata['release']='v36.3';self.metadata['platform']='Darwin';self.save_metadata()
        with self.assertRaisesRegex(ValueError,'platform'):self.assemble()
        self.assertFalse(self.output.exists())

    def test_embedded_source_commit_mismatch_is_rejected(self):
        p=self.backend/'_internal/assets/backend-provenance.json';v=json.loads(p.read_text());v['source_commit']='e'*40;p.write_text(json.dumps(v))
        with self.assertRaisesRegex(ValueError,'provenance differ'):self.assemble()
        self.assertFalse(self.output.exists())

    def test_existing_package_is_never_overwritten(self):
        self.output.mkdir();sentinel=self.output/'keep';sentinel.write_text('previous package')
        with self.assertRaises(FileExistsError):self.assemble()
        self.assertEqual(sentinel.read_text(),'previous package')
        self.assertEqual(list(self.output.iterdir()),[sentinel])


if __name__=='__main__':unittest.main()
