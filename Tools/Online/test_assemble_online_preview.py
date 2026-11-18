"""Tiny synthetic fixtures only: never invokes Unity, camera, network or real packages."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import zipfile

sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location('online_package', Path(__file__).with_name('assemble_online_preview.py'))
PACKAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGE)


def write(path, text='fixture'):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')


def json_write(path, data):
    write(path, json.dumps(data))


def commit(root):
    for command in (['init', '-q'], ['add', '.'], ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture']):
        subprocess.run(['git', '-C', str(root), *command], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return PACKAGE.git(root, 'rev-parse', 'HEAD').decode().strip()


class PackageChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='redbool-preview-package-fixture-')
        self.root = Path(self.temp.name).resolve()
        self.u = self.root / 'unity'; self.py = self.root / 'python'
        write(self.u / 'Assets/Scripts/Runtime.cs', '// synthetic runtime')
        write(self.u / 'Assets/RedboolVision/Online/Session.cs', '// synthetic online runtime')
        write(self.u / 'Assets/Photon/PhotonRealtime/Code/Photon.Realtime.asmdef', '{}')
        write(self.u / 'Assets/Photon/PhotonLibs/PhotonClient.dll', 'synthetic dll')
        write(self.u / 'ProjectSettings/ProjectSettings.asset', '  Standalone: PHOTON_REALTIME_5_OR_NEWER\n')
        write(self.u / '.gitignore', PACKAGE.CONFIG + '\n' + PACKAGE.CONFIG + '.meta\n')
        self.app_id = '11111111-1111-4111-8111-111111111111'
        json_write(self.u / PACKAGE.CONFIG, {'appId': self.app_id})
        write(self.u / (PACKAGE.CONFIG + '.meta'))
        self.ucommit = commit(self.u)
        write(self.py / 'src/core.py', 'answer = 42\n')
        write(self.py / 'scripts/camera_backend_entry.py', 'pass\n')
        self.pycommit = commit(self.py)
        self.player = self.root / 'build/REDBOOL.exe'
        write(self.player, 'synthetic exe')
        write(self.player.parent / 'UnityPlayer.dll')
        for name in ('Assembly-CSharp.dll', 'Photon.Realtime.dll', 'PhotonClient.dll'):
            write(self.player.parent / 'REDBOOL_Data/Managed' / name)
        self.backend = self.root / 'backend/REDBOOLCamera'
        write(self.backend / 'REDBOOLCamera.exe', 'synthetic backend')
        count, variants = PACKAGE.source_hashes(self.py, self.pycommit)
        self.models = {'pose_landmarker_lite.task': 'lite', 'pose_landmarker_full.task': 'full', 'pose_landmarker_heavy.task': 'heavy'}
        for name, content in self.models.items(): write(self.backend / '_internal/assets' / name, content)
        self.model_hashes = {name: PACKAGE.sha(self.backend / '_internal/assets' / name) for name in self.models}
        self.meta = dict(release='v36.3', recording_schema='redbool.landmarks/1', source_commit=self.pycommit,
                         source_sha256=variants['committed'], source_dirty=False, source_file_count=count,
                         build_variant='online-candidate', platform='Windows', architecture='AMD64',
                         optional_cross_refine={'bundled': True, 'default_enabled': False}, models=self.model_hashes,
                         command=['must not be distributed'])
        json_write(self.backend / 'backend-build.json', self.meta)
        json_write(self.backend / '_internal/assets/backend-provenance.json', self.meta)
        self.sdk_archive = self.root / 'official-fixture.zip'
        with zipfile.ZipFile(self.sdk_archive, 'w') as archive:
            for path in (self.u / 'Assets/Photon').rglob('*'):
                if path.is_file(): archive.write(path, path.relative_to(self.u).as_posix())
        self.sdk_sha = PACKAGE.sha(self.sdk_archive)
        self.gates = self.root / 'gates.json'
        self.args = SimpleNamespace(unity_project=self.u, python_project=self.py, player=self.player,
                                    backend=self.backend, target='windows', gates=self.gates,
                                    output=self.root / 'OnlinePreview-Windows', archive=None)
        current = PACKAGE.capture(self.u)
        json_write(self.player.parent / 'source-preservation.json', dict(passed=True, exit_code=0, source_unchanged=True,
                    before=current, after=current, changed_preexisting_files=[], automatic_restore_performed=False))
        json_write(self.player.parent / 'build-info.json', dict(release='OnlinePreview', git_commit=self.ucommit, runtime_sha256=PACKAGE.runtime_sha(self.u), view_protocol=34))
        identities, _ = PACKAGE.fingerprint(self.u, self.py, self.player, self.backend, 'windows')
        capabilities = {**self.meta, 'frozen': True, 'camera_opened': False, 'game_packets': 0}
        inference = dict(passed=True, frozen=True, camera_opened=False, game_packets=0, platform='Windows', architecture='AMD64',
                         models=[dict(variant='Heavy', mode='IMAGE', detect_ms=[1, 2, 3])])
        native = dict(passed=True, camera_opened=False, game_packets=0, capabilities=capabilities,
                      inference=inference, refinement_inference=inference)
        reports = dict(backend_native=native, backend_online=dict(passed=True, camera_opened=False, game_packets=0, capabilities=capabilities),
                       protocol=dict(passed=True, assertions=121), anticipation=dict(passed=True, checks=1467),
                       cloud=dict(passed=True, error='', sdkVersion=PACKAGE.SDK_VERSION, probesReceivedByPeer=24, echoedWithin1000ms=24,
                                  checks=[dict(passed=True)]))
        result = dict(passed=True, failures=[], camera_backend_launch_seen=False, combat_outcomes={'P1:APPLIED': 1, 'P2:APPLIED': 1},
                      snapshot_agreement=dict(matched_snapshots=3, mismatch_sequences=[], trace_parse_errors=0, guest_sequence_strictly_increasing=True))
        reports['native_game'] = dict(passed=True, build_sha256=PACKAGE.sha(self.player), platform='win32',
                                     results=[dict(result, mode='lan'), dict(result, mode='online', actual_cloud_peer_evidence={'host': True, 'guest': True})])
        rows = {}
        for name, report in reports.items():
            path = self.root / (name + '.json'); json_write(path, report)
            rows[name] = {'path': path.name, 'sha256': PACKAGE.sha(path)}
        run_path = self.root / 'ci.json'; artifact_path = self.root / 'artifact.json'
        json_write(run_path, dict(headSha=self.pycommit, status='completed', conclusion='success', databaseId=123))
        json_write(artifact_path, dict(name='REDBOOLCamera-OnlineCandidate-Windows-x64', expired=False,
                                      workflow_run=dict(head_sha=self.pycommit, id=123)))
        self.gate = dict(schema=PACKAGE.SCHEMA, release=PACKAGE.RELEASE, identities=identities, reports=rows, windows_game_validation='passed_native_windows',
                         sdk=dict(official_source='https://www.photonengine.com/sdks', version=PACKAGE.SDK_VERSION,
                                  archive_path=self.sdk_archive.name, archive_sha256=self.sdk_sha),
                         windows_ci=dict(run_path=run_path.name, run_sha256=PACKAGE.sha(run_path),
                                         artifact_path=artifact_path.name, artifact_sha256=PACKAGE.sha(artifact_path)))
        self.save_gate()
        self.patch_models = mock.patch.object(PACKAGE, 'MODELS', self.model_hashes)
        self.patch_sdk = mock.patch.object(PACKAGE, 'SDK_ARCHIVE_SHA', self.sdk_sha)
        self.patch_models.start(); self.patch_sdk.start()

    def tearDown(self):
        self.patch_sdk.stop(); self.patch_models.stop(); self.temp.cleanup()

    def save_gate(self): json_write(self.gates, self.gate)

    def change_report(self, name, mutation):
        path = self.root / self.gate['reports'][name]['path']
        data = PACKAGE.read(path); mutation(data); json_write(path, data)
        self.gate['reports'][name]['sha256'] = PACKAGE.sha(path); self.save_gate()

    def test_validation_is_read_only_and_matching_fixture_passes(self):
        before = PACKAGE.tree_state(self.root)
        PACKAGE.validate(self.args)
        self.assertFalse(self.args.output.exists())
        self.assertEqual(before, PACKAGE.tree_state(self.root))

    def test_false_cloud_and_offline_simulation_fail_closed(self):
        self.change_report('cloud', lambda data: data.update(passed=False))
        with self.assertRaisesRegex(ValueError, 'cloud'): PACKAGE.validate(self.args)
        self.change_report('cloud', lambda data: data.update(passed=True))
        self.change_report('native_game', lambda data: data.clear() or data.update(passed=True, scope='offline simulation'))
        with self.assertRaisesRegex(ValueError, 'executable'): PACKAGE.validate(self.args)

    def test_wrong_ci_head_and_old_artifact_fail_closed(self):
        path = self.root / 'ci.json'; run = PACKAGE.read(path); run['headSha'] = '0' * 40; json_write(path, run)
        self.gate['windows_ci']['run_sha256'] = PACKAGE.sha(path); self.save_gate()
        with self.assertRaisesRegex(ValueError, 'exact-HEAD'): PACKAGE.validate(self.args)

    def test_ignored_config_change_is_detected_without_exposing_value(self):
        json_write(self.u / PACKAGE.CONFIG, {'appId': '22222222-2222-4222-8222-222222222222'})
        with self.assertRaisesRegex(ValueError, 'inputs changed') as caught: PACKAGE.validate(self.args)
        self.assertNotIn(self.app_id, str(caught.exception))

    def test_missing_sdk_and_dirty_python_are_rejected(self):
        write(self.py / 'src/core.py', 'changed')
        with self.assertRaisesRegex(ValueError, 'worktree must be clean'): PACKAGE.validate(self.args)
        write(self.py / 'src/core.py', 'answer = 42\n')
        (self.player.parent / 'REDBOOL_Data/Managed/PhotonClient.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'contain the official Photon'): PACKAGE.validate(self.args)

    def test_stable_backend_cannot_be_relabelled(self):
        stable = copy.deepcopy(self.meta); stable['build_variant'] = 'stable'
        online = dict(camera_opened=False, game_packets=0, capabilities={**stable, 'frozen': True})
        with self.assertRaisesRegex(ValueError, 'online-candidate'):
            PACKAGE.validate_backend(stable, stable, online, online, 'windows', self.pycommit)

    def test_model_bytes_are_checked_and_backend_variant_is_kept(self):
        portable = PACKAGE.portable_backend(self.meta)
        self.assertEqual(portable['build_variant'], 'online-candidate')
        self.assertNotIn('command', portable)
        write(self.backend / '_internal/assets/pose_landmarker_full.task', 'wrong model')
        with self.assertRaisesRegex(ValueError, 'exact source'): PACKAGE.validate(self.args)

    def test_fresh_output_and_windows_folder_copy_only_tiny_fixture(self):
        validated = PACKAGE.validate(self.args)
        manifest = PACKAGE.assemble(self.args, validated)
        self.assertEqual(manifest['release'], 'OnlinePreview')
        self.assertTrue((self.args.output / 'REDBOOL.exe').exists())
        self.assertTrue((self.args.output / 'REDBOOL_Data/Managed/PhotonClient.dll').exists())
        text = (self.args.output / '시작 안내.txt').read_text()
        self.assertIn('PvP → 온라인', text); self.assertIn('ZIP을 먼저 모두 풀고', text)
        self.assertNotIn(self.app_id, text + (self.args.output / 'package-manifest.json').read_text())
        with self.assertRaisesRegex(ValueError, 'fresh output'): PACKAGE.validate(self.args)

    def test_external_symlink_is_rejected(self):
        os.symlink(self.player, self.backend / 'escape')
        with self.assertRaisesRegex(ValueError, 'escapes'): PACKAGE.tree_state(self.backend)

    def test_no_recursive_windows_copy_or_archive_inside_original_inputs(self):
        self.args.output = self.player.parent / 'REDBOOL_Data/OnlinePreview'
        with self.assertRaisesRegex(ValueError, 'outside every input tree'): PACKAGE.validate(self.args)
        self.args.output = self.root / 'OnlinePreview-Windows'
        self.args.archive = self.backend / 'OnlinePreview.zip'
        with self.assertRaisesRegex(ValueError, 'outside every input tree'): PACKAGE.validate(self.args)

    def test_windows_preview_can_explicitly_await_external_game_validation(self):
        mac = self.root / 'mac-reference/REDBOOL.app'
        write(mac / 'Contents/MacOS/Game', 'different synthetic Mac executable')
        with (mac / 'Contents/Info.plist').open('wb') as stream: plistlib.dump({'CFBundleExecutable': 'Game'}, stream)
        for name in ('Assembly-CSharp.dll', 'Photon.Realtime.dll', 'PhotonClient.dll'):
            write(mac / 'Contents/Resources/Data/Managed' / name)
        for name in ('source-preservation.json', 'build-info.json'):
            write(mac.parent / name, (self.player.parent / name).read_text())
        self.gate['windows_game_validation'] = 'pending_external_windows_game_validation'
        self.gate['native_mac_reference'] = dict(player=str(mac), player_tree_sha256=PACKAGE.canonical_sha(PACKAGE.player_tree(mac, 'mac')),
            source_preservation_sha256=PACKAGE.sha(mac.parent / 'source-preservation.json'), build_info_sha256=PACKAGE.sha(mac.parent / 'build-info.json'))
        self.change_report('native_game', lambda data: data.update(platform='darwin', build_sha256=PACKAGE.sha(mac / 'Contents/MacOS/Game')))
        validated = PACKAGE.validate(self.args)
        manifest = PACKAGE.assemble(self.args, validated)
        self.assertFalse(manifest['production_ready'])
        self.assertEqual(manifest['validation']['native_windows_game'], 'pending_external_windows_game_validation')
        self.assertEqual(manifest['validation']['native_mac_game'], 'passed')
        self.assertIn('Windows판은 실제 게임 실행과 화면 속도를 사용자 PC에서 확인할 시험본', (self.args.output / '시작 안내.txt').read_text())

    @unittest.skipUnless(sys.platform == 'darwin', 'Mac ditto check')
    def test_mac_zip_preserves_internal_symlink_on_tiny_fixture(self):
        output = self.root / 'tiny-mac'; write(output / 'Example.app/Contents/Frameworks/Versions/A/lib', 'library')
        link = output / 'Example.app/Contents/Frameworks/Versions/Current'; os.symlink('A', link)
        destination = self.root / 'tiny.zip'
        PACKAGE.archive(output, destination, 'mac')
        PACKAGE.verify_archive_symlinks(output, destination)
        with self.assertRaisesRegex(ValueError, 'already exists'): PACKAGE.archive(output, destination, 'mac')

    @unittest.skipUnless(sys.platform == 'darwin', 'Mac bundle assembly')
    def test_mac_display_branding_keeps_executable_and_save_identity(self):
        mac = self.root / 'mac-branding/REDBOOL.app'
        write(mac / 'Contents/MacOS/Game', 'synthetic executable')
        original_info = {'CFBundleExecutable': 'Game', 'CFBundleName': 'Vision Striker',
                         'CFBundleIdentifier': 'example.unchanged-save-identity'}
        with (mac / 'Contents/Info.plist').open('wb') as stream:
            plistlib.dump(original_info, stream)
        data = mac / 'Contents/Resources/Data'
        for name in ('Assembly-CSharp.dll', 'Photon.Realtime.dll', 'PhotonClient.dll'):
            write(data / 'Managed' / name)
        write(data / 'app.info', 'DefaultCompany\nVision Striker\n')
        write(self.backend / 'REDBOOLCamera', 'synthetic Mac backend')
        original_tree = PACKAGE.tree_state(mac)
        self.args.player, self.args.target = mac, 'mac'
        self.args.output = self.root / 'branded-mac'
        # Native signing is outside this tiny fixture; exercise the real copying
        # and plist transformation without treating fake bytes as a signed app.
        with mock.patch.object(PACKAGE.subprocess, 'run'):
            PACKAGE.assemble(self.args, (self.gate, self.gate['identities'], self.meta))
        bundled = self.args.output / 'REDBOOL-OnlinePreview.app'
        with (bundled / 'Contents/Info.plist').open('rb') as stream:
            info = plistlib.load(stream)
        self.assertEqual(info['CFBundleName'], '흑운협')
        self.assertEqual(info['CFBundleDisplayName'], '흑운협')
        self.assertEqual(info['LSMinimumSystemVersion'], '14.0')
        self.assertEqual(info['CFBundleExecutable'], original_info['CFBundleExecutable'])
        self.assertEqual(info['CFBundleIdentifier'], original_info['CFBundleIdentifier'])
        self.assertEqual((bundled / 'Contents/Resources/Data/app.info').read_bytes(),
                         (data / 'app.info').read_bytes())
        self.assertEqual((bundled / 'Contents/MacOS/Game').read_bytes(),
                         (mac / 'Contents/MacOS/Game').read_bytes())
        self.assertEqual(PACKAGE.tree_state(mac), original_tree)


if __name__ == '__main__': unittest.main()
