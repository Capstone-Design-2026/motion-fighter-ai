import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from redbool_demo.backend_provenance import capabilities, source_provenance, PROVENANCE_FILE


class BackendProvenanceTests(unittest.TestCase):
    def tearDown(self):
        capabilities.cache_clear()

    def fixture(self, root):
        (root/'src/pkg').mkdir(parents=True)
        (root/'src/pkg/a.py').write_bytes(b'x = 1\n')
        (root/'src/pkg/b.py').write_bytes(b'y = 2\n')
        (root/'scripts').mkdir()
        (root/'scripts/camera_backend_entry.py').write_bytes(b'import pkg.a\n')

    def test_fingerprint_tracks_paths_and_exact_bytes_but_not_cache_or_models(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);self.fixture(root)
            before=source_provenance(root)
            self.assertEqual(before['recording_schema'],'redbool.landmarks/1')
            (root/'src/pkg/a.pyc').write_bytes(b'cache');(root/'pose.task').write_bytes(b'model')
            self.assertEqual(source_provenance(root)['source_sha256'],before['source_sha256'])
            (root/'src/pkg/a.py').write_bytes(b'x = 1\r\n')
            self.assertNotEqual(source_provenance(root)['source_sha256'],before['source_sha256'])
            (root/'src/pkg/a.py').write_bytes(b'x = 1\n');(root/'src/pkg/a.py').rename(root/'src/pkg/c.py')
            self.assertNotEqual(source_provenance(root)['source_sha256'],before['source_sha256'])

    def test_no_parent_repo_commit_is_misattributed_to_staged_source(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)/'staged';root.mkdir();self.fixture(root)
            response=subprocess.CompletedProcess([],0,str(root.parent)+'\n','')
            with patch('redbool_demo.backend_provenance.subprocess.run',return_value=response):
                self.assertIsNone(source_provenance(root)['source_commit'])

    def test_frozen_capabilities_require_actual_schema_and_cache_metadata(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);(root/'assets').mkdir()
            path=root/'assets'/PROVENANCE_FILE
            data={'release':'v34','recording_schema':'redbool.landmarks/1','source_sha256':'a'*64,'source_commit':'b'*40,'source_dirty':False}
            path.write_text(json.dumps(data),encoding='utf-8')
            with patch('redbool_demo.backend_provenance.sys.frozen',True,create=True),patch('redbool_demo.backend_provenance.sys._MEIPASS',folder,create=True):
                self.assertTrue(capabilities()['frozen']);self.assertEqual(capabilities()['source_commit'],'b'*40)
                path.unlink();self.assertEqual(capabilities()['source_sha256'],'a'*64)
                capabilities.cache_clear();data['recording_schema']='redbool.landmarks/0';path.write_text(json.dumps(data))
                with self.assertRaisesRegex(ValueError,'schema mismatch'):capabilities()

    def test_check_reports_capability_after_import_check_without_bridge(self):
        from redbool_demo.backend import main
        with patch('redbool_demo.live.main') as live,patch('redbool_demo.local_bridge.LocalBridge',side_effect=AssertionError('opened bridge')),contextlib.redirect_stdout(io.StringIO()) as out:
            main(['--check'])
        self.assertTrue(live.called)
        data=json.loads(out.getvalue().strip().splitlines()[-1])
        self.assertEqual(data['recording_schema'],'redbool.landmarks/1');self.assertFalse(data['camera_opened'])


if __name__=='__main__':unittest.main()
