"""Run with Python 3.11; fixtures use private temporary Git repositories only."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('build_inputs', Path(__file__).resolve().parents[1] / 'export_build_inputs.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)


class BuildInputsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.repo = self.root / 'source'; self.repo.mkdir()
        self.git('init', '-q'); self.git('config', 'user.name', 'Fixture'); self.git('config', 'user.email', 'fixture@example.invalid')
        self.write('Assets/data.txt', b'original\n'); self.write('Assets/keep.bin', b'unchanged')
        self.write('Assets/Scripts/Runtime.cs', b'public class Runtime {}\n')
        self.write('Packages/manifest.json', b'{}\n')
        self.write('ProjectSettings/ProjectSettings.asset', b'metroCertificatePassword: \n  ordinarySetting: 1\n')
        self.write('.vscode/settings.json', b'{}')
        self.git('add', '.'); self.git('commit', '-qm', 'fixture base')
        self.archive = self.root / 'inputs.zip'

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.repo, stderr=subprocess.PIPE)

    def write(self, name, data):
        path = self.repo / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)

    def clone(self):
        clone = self.root / 'restored'
        subprocess.check_call(['git', '-c', 'core.autocrlf=false', 'clone', '-q', str(self.repo), str(clone)])
        return clone

    def test_overlay_restores_all_hashes_without_copying_unchanged_assets_or_user_settings(self):
        self.write('Assets/data.txt', b'changed\n'); self.write('.vscode/settings.json', b'local-private-setting')
        self.write('ProjectSettings/ProjectSettings.asset', b'metroCertificatePassword: \n  ordinarySetting: 2\n')
        for name in m.RQL: self.write(name, b'required DLL or meta')
        before = self.git('status', '--porcelain')
        report = m.export(self.repo, self.archive)
        self.assertEqual(self.git('status', '--porcelain'), before)
        self.assertEqual(len(report['included_untracked_files']), 6)
        with zipfile.ZipFile(self.archive) as z:
            self.assertNotIn('overlay/Assets/keep.bin', z.namelist())
            self.assertFalse(any('.vscode' in name for name in z.namelist()))
        self.assertEqual(report['excluded_files']['.vscode/settings.json'], 'outside_build_inputs')
        clone = self.clone(); m.restore(clone, self.archive)
        self.assertTrue(m.verify_tree(clone, report))
        self.assertEqual((clone / '.vscode/settings.json').read_bytes(), b'{}')

    def test_deleted_input_restores_as_tombstone(self):
        (self.repo / 'Assets/data.txt').unlink()
        report = m.export(self.repo, self.archive); clone = self.clone()
        self.assertEqual(report['deleted_files'], ['Assets/data.txt'])
        m.restore(clone, self.archive); self.assertFalse((clone / 'Assets/data.txt').exists())

    def test_archive_tamper_and_extra_entry_rejected(self):
        self.write('Assets/data.txt', b'changed'); m.export(self.repo, self.archive)
        for name, content in [('overlay/Assets/data.txt', b'tampered'), ('../escape.txt', b'bad')]:
            damaged = self.root / ('tampered' if name.startswith('overlay/') else 'traversal')
            with zipfile.ZipFile(self.archive) as source, zipfile.ZipFile(damaged, 'w') as dest:
                for item in source.infolist():
                    dest.writestr(item.filename, content if item.filename == name else source.read(item))
                if name.startswith('..'): dest.writestr(name, content)
            with self.assertRaises(ValueError): m.verify_archive(damaged)

    def test_secret_and_linked_dirty_inputs_refused(self):
        self.write('ProjectSettings/ProjectSettings.asset', b'metroCertificatePassword: DO_NOT_EXPORT\n')
        with self.assertRaisesRegex(ValueError, 'credential'): m.export(self.repo, self.archive)
        self.assertFalse(self.archive.exists())
        self.write('ProjectSettings/ProjectSettings.asset', b'metroCertificatePassword: \n')
        target = self.repo / 'Assets/data.txt'; target.unlink(); target.symlink_to(self.repo / '.vscode/settings.json')
        with self.assertRaisesRegex(ValueError, 'Linked'): m.export(self.repo, self.archive)

    def test_unknown_untracked_build_input_refused(self):
        self.write('Assets/unknown.dll', b'not-reviewed')
        with self.assertRaisesRegex(ValueError, 'unexpected untracked'): m.export(self.repo, self.archive)
        self.assertFalse(self.archive.exists())

    def test_restore_refuses_dirty_checkout_and_untracked_collision(self):
        self.write('Assets/data.txt', b'changed')
        for name in m.RQL: self.write(name, b'plugin')
        m.export(self.repo, self.archive); clone = self.clone()
        path = clone / 'Assets/keep.bin'; path.write_bytes(b'user work')
        with self.assertRaisesRegex(ValueError, 'clean checkout'): m.restore(clone, self.archive)
        self.assertEqual(path.read_bytes(), b'user work')
        path.write_bytes(b'unchanged')
        collision = clone / sorted(m.RQL)[0]; collision.parent.mkdir(parents=True); collision.write_bytes(b'local DLL')
        with self.assertRaisesRegex(ValueError, 'Untracked file'): m.restore(clone, self.archive)
        self.assertEqual(collision.read_bytes(), b'local DLL')

    def test_archive_never_overwrites_previous_result_and_tree_detects_drift(self):
        report = m.export(self.repo, self.archive); original = self.archive.read_bytes()
        with self.assertRaises(FileExistsError): m.export(self.repo, self.archive)
        self.assertEqual(self.archive.read_bytes(), original)
        self.write('Assets/keep.bin', b'import changed it')
        with self.assertRaisesRegex(ValueError, 'hash_mismatch'): m.verify_tree(self.repo, report)

    def test_restore_checks_unrelated_untracked_build_input_before_writing(self):
        self.write('Assets/data.txt', b'changed'); m.export(self.repo, self.archive)
        clone = self.clone(); extra = clone / 'Assets/user-work.txt'; extra.write_bytes(b'preserve')
        with self.assertRaisesRegex(ValueError, 'Unexpected untracked'): m.restore(clone, self.archive)
        self.assertEqual((clone / 'Assets/data.txt').read_bytes(), b'original\n')
        self.assertEqual(extra.read_bytes(), b'preserve')


if __name__ == '__main__': unittest.main()
