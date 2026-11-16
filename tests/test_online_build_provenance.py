"""Candidate labels and frozen-source checks without building or starting a camera."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]


def script(name):
    spec = importlib.util.spec_from_file_location('online_check_' + name, ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class OnlineBuildProvenanceTests(unittest.TestCase):
    def test_explicit_variant_roundtrips_sidecar_and_embedded_without_source_change(self):
        build = script('build_camera_backend')
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for variant in ('stable', 'online-candidate'):
                lite = root / 'lite.task'; lite.write_bytes(b'lite')
                full = root / 'full.task'; full.write_bytes(b'full')
                args = ['build', '--pose', str(lite), '--cross-pose', str(full),
                        '--work', str(root / 'work'), '--check']
                if variant != 'stable':
                    args += ['--build-variant', variant]
                with patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()) as output:
                    build.main()
                metadata = json.loads(output.getvalue())
                embedded = json.loads((root / 'work/assets/backend-provenance.json').read_text())
                self.assertEqual(metadata['build_variant'], variant)
                self.assertEqual(embedded['build_variant'], variant)
                self.assertEqual(metadata['source_sha256'], embedded['source_sha256'])
                if variant == 'stable':
                    stable_hash = metadata['source_sha256']
                else:
                    self.assertEqual(metadata['source_sha256'], stable_hash)
                self.assertFalse(metadata['optional_cross_refine']['default_enabled'])

    def fixture(self):
        temp = tempfile.TemporaryDirectory(); self.addCleanup(temp.cleanup)
        folder = Path(temp.name)
        data = dict(release='v36.3', recording_schema='redbool.landmarks/1',
                    source_commit='a' * 40, source_sha256='b' * 64,
                    source_dirty=False, source_file_count=71, build_variant='online-candidate')
        (folder / '_internal/assets').mkdir(parents=True)
        sidecar = folder / 'backend-build.json'
        embedded = folder / '_internal/assets/backend-provenance.json'
        for path in (sidecar, embedded):
            path.write_text(json.dumps(data))
        return folder / 'REDBOOLCamera.exe', data, sidecar, embedded

    def result(self, data):
        return subprocess.CompletedProcess([], 0, json.dumps({**data, 'frozen': True,
            'camera_opened': False, 'game_packets': 0}) + '\n', '')

    def test_native_check_uses_actual_online_parser_and_exact_source(self):
        checker = script('check_online_backend')
        exe, data, _, _ = self.fixture()
        with patch.object(checker.subprocess, 'run', return_value=self.result(data)) as run:
            self.assertTrue(checker.verify(exe, 'a' * 40)['passed'])
        self.assertEqual(run.call_args.args[0], [str(exe.resolve()), '--check', '--network-profile', 'online'])
        self.assertNotEqual(run.call_args.kwargs['cwd'], str(exe.parent))

    def test_stable_dirty_or_wrong_commit_candidate_is_rejected_before_launch(self):
        checker = script('check_online_backend')
        for change in ({'build_variant': 'stable'}, {'source_dirty': True}, {'source_commit': 'c' * 40}):
            with self.subTest(change=change):
                exe, data, sidecar, embedded = self.fixture()
                for path in (sidecar, embedded):
                    path.write_text(json.dumps({**data, **change}))
                with patch.object(checker.subprocess, 'run') as run, self.assertRaises(ValueError):
                    checker.verify(exe, 'a' * 40)
                run.assert_not_called()

    def test_sidecar_embedded_or_executed_binary_mismatch_is_rejected(self):
        checker = script('check_online_backend')
        exe, data, _, embedded = self.fixture()
        embedded.write_text(json.dumps({**data, 'source_sha256': 'c' * 64}))
        with self.assertRaisesRegex(ValueError, 'sidecar differ'):
            checker.verify(exe, 'a' * 40)
        embedded.write_text(json.dumps(data))
        with patch.object(checker.subprocess, 'run', return_value=self.result({**data, 'source_sha256': 'c' * 64})), \
                self.assertRaisesRegex(ValueError, 'Executed binary'):
            checker.verify(exe, 'a' * 40)

    def test_online_manifest_only_changes_reviewed_wire_and_diagnostic_inputs(self):
        base = json.loads((ROOT / 'config/recognition_baseline.json').read_text())
        online = json.loads((ROOT / 'config/recognition_baseline_online.json').read_text())
        expected = {'src/redbool_demo/live.py', 'src/redbool_demo/transport.py', 'src/realtime_vision/transport.py',
                    'src/redbool_demo/landmark_recording.py'}
        self.assertEqual(base['runtime_python_paths'], online['runtime_python_paths'])
        self.assertEqual(base['models'], online['models'])
        self.assertEqual(base['dependency_contract'], online['dependency_contract'])
        changed = set()
        self.assertEqual(set(base['groups']), set(online['groups']))
        for group, spec in base['groups'].items():
            self.assertEqual(set(spec['files']), set(online['groups'][group]['files']))
            changed |= {name for name, value in spec['files'].items()
                        if value != online['groups'][group]['files'][name]}
        self.assertEqual(changed, expected)
        gate = script('check_recognition_baseline')
        self.assertTrue(gate.check(ROOT, online)['passed'])


if __name__ == '__main__':
    unittest.main()
