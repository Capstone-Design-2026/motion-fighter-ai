"""Mutation tests for the release gate; no camera, game, network, or model loading."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/check_recognition_baseline.py'
SPEC = importlib.util.spec_from_file_location('recognition_baseline_check', SCRIPT)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


class RecognitionBaselineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        (self.repo / 'src').mkdir()
        (self.repo / 'src/rules.py').write_bytes(b'THRESHOLD = .8\n')
        (self.repo / 'src/diagnostics.py').write_bytes(b'LABEL = "ready"\n')
        (self.repo / 'requirements-backend-build.txt').write_bytes(b'mediapipe==0.10.21\n')
        self.files = {'src/rules.py': gate.digest(b'THRESHOLD = .8\n')}
        deps = {'requirements-backend-build.txt': gate.digest(b'mediapipe==0.10.21\n')}
        self.manifest = {
            'schema': gate.SCHEMA, 'source_hash_algorithm': gate.ALGORITHM,
            'baseline_commit': 'test-only',
            'runtime_python_paths': ['src/rules.py', 'src/diagnostics.py'],
            'groups': {'recognition': {'files': self.files, 'sha256': gate.group_digest(self.files)}},
            'dependency_contract': {'files': deps, 'sha256': gate.group_digest(deps)},
            'models': {'pose.task': gate.digest(b'model bytes', False)},
        }

    def result(self, **kwargs):
        return gate.check(self.repo, self.manifest, **kwargs)

    def test_clean_and_windows_crlf_match_but_model_is_explicitly_unchecked(self):
        self.assertTrue(self.result()['passed'])
        (self.repo / 'src/rules.py').write_bytes(b'THRESHOLD = .8\r\n')
        result = self.result()
        self.assertTrue(result['passed'])
        self.assertEqual(result['models'], 'not_checked')

    def test_real_rule_change_fails_with_group_and_path(self):
        (self.repo / 'src/rules.py').write_text('THRESHOLD = .7\n')
        result = self.result()
        self.assertFalse(result['passed'])
        self.assertEqual(result['errors'][0]['kind'], 'source_changed')
        self.assertEqual(result['errors'][0]['group'], 'recognition')
        self.assertEqual(result['errors'][0]['path'], 'src/rules.py')

    def test_backend_dependency_pins_change_fails_but_crlf_and_development_requirements_do_not(self):
        path = self.repo / 'requirements-backend-build.txt'
        path.write_bytes(b'mediapipe==0.10.21\r\n')
        (self.repo / 'requirements.txt').write_text('some-development-only-package')
        self.assertTrue(self.result()['passed'])
        path.write_bytes(b'mediapipe==0.10.22\n')
        self.assertEqual(self.result()['errors'][0]['kind'], 'dependency_contract_changed')

    def test_deleted_protected_source_fails_even_if_other_files_are_good(self):
        (self.repo / 'src/rules.py').unlink()
        result = self.result()
        self.assertFalse(result['passed'])
        self.assertIn('missing_protected_file', {e['kind'] for e in result['errors']})

    def test_added_runtime_module_requires_review_but_docs_and_tests_do_not(self):
        (self.repo / 'README.md').write_text('Documentation update')
        (self.repo / 'tests').mkdir()
        (self.repo / 'tests/new_test.py').write_text('assert True')
        self.assertTrue(self.result()['passed'])
        (self.repo / 'src/new_recognizer.py').write_text('THRESHOLD = .1')
        self.assertEqual(self.result()['errors'][0]['kind'], 'unreviewed_runtime_file')

    def test_unprotected_file_content_not_frozen_but_inventory_deletion_is_detected(self):
        (self.repo / 'src/diagnostics.py').write_text('LABEL = "waiting"')
        self.assertTrue(self.result()['passed'])
        (self.repo / 'src/diagnostics.py').unlink()
        self.assertEqual(self.result()['errors'][0]['kind'], 'missing_runtime_file')

    def test_absolute_traversal_noncanonical_and_windows_paths_rejected(self):
        for name in ('../outside.py', '/tmp/outside.py', 'src/../rules.py',
                     'C:/outside.py', 'src\\rules.py', 'src//rules.py', './src/rules.py'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                gate.checked_path(self.repo, name)

    def test_symlink_cannot_escape_as_protected_input(self):
        path = self.repo / 'src/rules.py'
        path.unlink()
        try:
            path.symlink_to(self.repo / 'src/diagnostics.py')
        except OSError as exc:
            self.skipTest('Host does not permit symlink fixture: ' + str(exc))
        with self.assertRaisesRegex(ValueError, 'Symlink'):
            self.result()

    def test_symlink_parent_cannot_escape(self):
        outside = self.repo / 'external'
        outside.mkdir()
        try:
            (self.repo / 'linked').symlink_to(outside, target_is_directory=True)
        except OSError as exc:
            self.skipTest('Host does not permit symlink fixture: ' + str(exc))
        with self.assertRaisesRegex(ValueError, 'Symlink'):
            gate.checked_path(self.repo, 'linked/rules.py')

    def test_model_check_requires_real_matching_bytes_and_never_normalizes_binary(self):
        self.assertFalse(self.result(check_models=True)['passed'])
        (self.repo / 'pose.task').write_bytes(b'model bytes')
        self.assertTrue(self.result(check_models=True)['passed'])
        self.assertEqual(self.result(check_models=True)['models'], 'verified')
        (self.repo / 'pose.task').write_bytes(b'model bytes\r\n')
        self.assertEqual(self.result(check_models=True)['errors'][0]['kind'], 'model_changed')

    def test_manifest_group_cannot_claim_a_different_file_inventory(self):
        self.manifest['groups']['recognition']['files']['src/not-listed.py'] = '0' * 64
        self.manifest['groups']['recognition']['sha256'] = gate.group_digest(self.files)
        with self.assertRaisesRegex(ValueError, 'inventory'):
            self.result()

    def test_corrupt_manifest_group_fingerprint_or_schema_fails(self):
        self.manifest['groups']['recognition']['sha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'fingerprint'):
            self.result()
        self.manifest['schema'] = 'unknown'
        with self.assertRaisesRegex(ValueError, 'schema'):
            self.result()

    def test_duplicate_inventory_or_group_membership_fails(self):
        duplicate = copy.deepcopy(self.manifest['groups']['recognition'])
        self.manifest['groups']['other'] = duplicate
        with self.assertRaisesRegex(ValueError, 'multiple groups'):
            self.result()
        del self.manifest['groups']['other']
        self.manifest['runtime_python_paths'].append('src/rules.py')
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            self.result()

    def test_cli_failure_has_nonzero_exit_and_no_traceback(self):
        file = self.repo / 'manifest.json'
        file.write_text(json.dumps(self.manifest))
        (self.repo / 'src/rules.py').write_text('THRESHOLD = .4')
        process = subprocess.run([sys.executable, str(SCRIPT), '--repo', str(self.repo),
                                  '--manifest', str(file)], capture_output=True, text=True)
        self.assertEqual(process.returncode, 1)
        self.assertFalse(json.loads(process.stdout)['passed'])
        self.assertNotIn('Traceback', process.stderr)


if __name__ == '__main__':
    unittest.main()
