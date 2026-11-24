#!/usr/bin/env python3
"""Prepare/validate a distinct OnlinePreview; assemble only with an explicit action.

Never builds, launches games/cameras, downloads, restores source or overwrites an
existing output. Validation is read-only. Gate JSON is a coordinator attestation
binding actual check reports to these exact source/build fingerprints; it is not
created automatically from a passing boolean in an unrelated older report.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import stat
import subprocess
import sys
from urllib.parse import urlparse
import uuid
import zipfile

sys.dont_write_bytecode = True
RELEASE = 'OnlinePreview'
VARIANT = 'online-candidate'
SCHEMA = 'redbool.online-preview-gates/1'
SDK_VERSION = '5.1.21'
SDK_ARCHIVE_SHA = '5c35ac02fa1bba0d9e9a2eba3ac4158bca1582c8b516cbcd1f39799589a0d09a'
CONFIG = 'Assets/Resources/RedboolOnlineConfig.json'
EXTRA_ASSETS = (CONFIG, CONFIG + '.meta')
BACKEND_FIELDS = ('release', 'recording_schema', 'source_commit', 'source_sha256',
                  'source_dirty', 'source_file_count', 'build_variant')
MODELS = {
    'pose_landmarker_lite.task': '59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a',
    'pose_landmarker_full.task': '5134a3aad27a58b93da0088d431f366da362b44e3ccfbe3462b3827a839011b1',
    'pose_landmarker_heavy.task': '64437af838a65d18e5ba7a0d39b465540069bc8aae8308de3e318aad31fcbc7b',
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def canonical_sha(data):
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(',', ':'),
                                     ensure_ascii=False).encode()).hexdigest()


def git(project, *args):
    return subprocess.check_output(['git', '-C', str(project), *args], stderr=subprocess.DEVNULL)


def relative(name):
    path = Path(name)
    require(not path.is_absolute() and '..' not in path.parts, 'Unsafe relative input path')
    return path.as_posix()


def file_state(path):
    if path.is_symlink():
        return {'kind': 'symlink', 'target': os.readlink(path)}
    if path.is_file():
        return {'kind': 'file', 'bytes': path.stat().st_size, 'sha256': sha(path)}
    return {'kind': 'other' if path.exists() else 'missing'}


def capture(project, extra=EXTRA_ASSETS):
    # Matches the source-preserving build helper; includes ignored config only
    # through explicit extra_assets. Reports contain hashes, never file contents.
    tracked = {relative(x.decode()) for x in git(project, 'ls-files', '-z').split(b'\0') if x}
    imported = {relative(x.decode()) for x in git(project, 'ls-files', '--others', '--exclude-standard', '-z').split(b'\0')
                if x and x.decode().startswith(('Assets/', 'Packages/'))}
    specified = {relative(x) for x in extra}
    return {'project': str(project), 'head': git(project, 'rev-parse', 'HEAD').decode().strip(),
            'tracked_count': len(tracked), 'imported_untracked': sorted(imported), 'extra_assets': sorted(specified),
            'tracked_status': git(project, 'status', '--porcelain', '--untracked-files=no').decode(),
            'files': {name: file_state(project / name) for name in sorted(tracked | imported | specified)}}


def runtime_sha(project):
    digest = hashlib.sha256()
    for folder in ('Assets/Scripts', 'Assets/RedboolVision'):
        for path in sorted((project / folder).rglob('*.cs')):
            if 'Editor' not in path.relative_to(project).parts:
                digest.update(path.relative_to(project).as_posix().encode())
                digest.update(b'\0' + path.read_bytes() + b'\0')
    return digest.hexdigest()


def tree_state(root):
    require(root.is_dir(), 'Missing artifact tree')
    result = {}
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in sorted(dirs + files):
            path = Path(directory) / name
            if path.is_symlink() or path.is_file():
                if path.is_symlink():
                    resolved = path.resolve()
                    require(resolved == root or root in resolved.parents, 'Artifact symlink escapes its tree')
                    require(path.exists(), 'Artifact contains a broken symlink')
                state = file_state(path)
                state['mode'] = stat.S_IMODE(path.lstat().st_mode)
                result[path.relative_to(root).as_posix()] = state
    return result


def player_parts(player, target):
    if target == 'mac':
        require(player.suffix == '.app' and (player / 'Contents/Info.plist').is_file(), 'Expected a fresh native Mac .app')
        with (player / 'Contents/Info.plist').open('rb') as stream:
            info = plistlib.load(stream)
        executable = player / 'Contents/MacOS' / relative(info['CFBundleExecutable'])
        data = player / 'Contents/Resources/Data'
        roots = [player]
    else:
        require(player.suffix.lower() == '.exe' and player.is_file(), 'Expected a fresh native Windows .exe')
        executable = player
        data = player.with_name(player.stem + '_Data')
        require(data.is_dir() and (player.parent / 'UnityPlayer.dll').is_file(), 'Windows Unity support files missing')
        roots = [p for p in sorted(player.parent.iterdir())
                 if p.is_file() and (p.name in (player.name, 'UnityCrashHandler64.exe') or p.suffix.lower() == '.dll')
                 or p.is_dir() and p.name in (data.name, 'MonoBleedingEdge', 'D3D12')]
    require(executable.is_file() and data.is_dir(), 'Incomplete Unity native player')
    require(all((data / 'Managed' / name).is_file() for name in ('Assembly-CSharp.dll', 'Photon.Realtime.dll', 'PhotonClient.dll')),
            'New native player must actually contain the official Photon assemblies')
    require(not (data / 'StreamingAssets/RedboolCamera').exists(), 'Player already contains a backend; use a new unassembled build')
    return executable, data, roots


def player_tree(player, target):
    _, _, roots = player_parts(player, target)
    return {p.name: tree_state(p) if p.is_dir() else file_state(p) for p in roots}


def fingerprint(project, python_project, player, backend, target, extra=EXTRA_ASSETS):
    require((backend / ('REDBOOLCamera' if target == 'mac' else 'REDBOOLCamera.exe')).is_file()
            and (backend / '_internal').is_dir(), 'Complete native camera backend required')
    current = capture(project, extra)
    sdk = {p.relative_to(project).as_posix(): sha(p) for p in sorted((project / 'Assets/Photon').rglob('*'))
           if p.is_file() and p.suffix in ('.cs', '.dll', '.asmdef')}
    require(any(name.endswith('PhotonClient.dll') for name in sdk)
            and any(name.endswith('Photon.Realtime.asmdef') for name in sdk), 'Actual Photon SDK inputs are required')
    require(re.search(r'^\s+Standalone: [^\n]*\bPHOTON_REALTIME_5_OR_NEWER\b',
                      (project / 'ProjectSettings/ProjectSettings.asset').read_text(), re.MULTILINE),
            'Standalone Photon SDK define is missing')
    config = read(project / CONFIG)
    try:
        configured = uuid.UUID((config.get('appId') or config.get('photonAppId') or '').strip()).int != 0
    except (ValueError, AttributeError):
        configured = False
    require(configured and (project / (CONFIG + '.meta')).is_file(), 'Embedded online configuration is incomplete')
    for name in (*sdk, 'ProjectSettings/ProjectSettings.asset', *EXTRA_ASSETS):
        require(current['files'].get(name) == file_state(project / name), 'Build capture must include every SDK/define/config input')
    return {'unity_commit': current['head'], 'unity_runtime_sha256': runtime_sha(project),
            'unity_inputs_sha256': canonical_sha(current['files']), 'sdk_tree_sha256': canonical_sha(sdk),
            'embedded_config_sha256': sha(project / CONFIG), 'player_tree_sha256': canonical_sha(player_tree(player, target)),
            'python_commit': git(python_project, 'rev-parse', 'HEAD').decode().strip(),
            'backend_tree_sha256': canonical_sha(tree_state(backend))}, current


def source_hashes(project, commit):
    names = sorted(n for n in git(project, 'ls-tree', '-r', '--name-only', commit, '--', 'src', 'scripts/camera_backend_entry.py').decode().splitlines() if n.endswith('.py'))
    variants = {name: hashlib.sha256() for name in ('committed', 'lf', 'crlf')}
    for name in names:
        body = git(project, 'show', commit + ':' + name)
        lf = body.replace(b'\r\n', b'\n')
        for mode, data in (('committed', body), ('lf', lf), ('crlf', lf.replace(b'\n', b'\r\n'))):
            digest = variants[mode]; encoded = name.encode()
            digest.update(len(encoded).to_bytes(8, 'big')); digest.update(encoded)
            digest.update(len(data).to_bytes(8, 'big')); digest.update(data)
    return len(names), {key: value.hexdigest() for key, value in variants.items()}


def checked_report(gates, name, base):
    record = gates['reports'][name]
    path = Path(record['path'])
    if not path.is_absolute():
        path = base / path
    require(sha(path) == record['sha256'], 'Check report changed: ' + name)
    report = read(path)
    require(report.get('passed') is True, 'Actual check must pass: ' + name)
    return report


def validate_sdk(sdk, project, base):
    url = urlparse(sdk.get('official_source', ''))
    require(url.scheme == 'https' and (url.hostname or '').endswith('.photonengine.com'), 'Official Photon download provenance required')
    require(sdk.get('version') == SDK_VERSION and sdk.get('archive_sha256') == SDK_ARCHIVE_SHA, 'Reviewed official SDK version/archive required')
    archive_path = Path(sdk['archive_path'])
    if not archive_path.is_absolute(): archive_path = base / archive_path
    require(sha(archive_path) == SDK_ARCHIVE_SHA, 'Retained official SDK archive fingerprint differs')
    with zipfile.ZipFile(archive_path) as archive_file:
        for path in sorted((project / 'Assets/Photon').rglob('*')):
            if path.is_file() and path.suffix in ('.cs', '.dll', '.asmdef'):
                name = path.relative_to(project).as_posix()
                try:
                    with archive_file.open(name) as stream:
                        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                except KeyError:
                    raise ValueError('Imported SDK file is absent from the official archive: ' + name) from None
                require(sha(path) == digest, 'Imported SDK bytes differ from the official archive: ' + name)


def validate_backend(metadata, embedded, online, native, target, commit):
    for key in BACKEND_FIELDS:
        require(key in metadata and metadata[key] == embedded.get(key) == online.get('capabilities', {}).get(key)
                == native.get('capabilities', {}).get(key), 'Backend provenance mismatch: ' + key)
    require(metadata['release'] == 'v36.3' and metadata['build_variant'] == VARIANT
            and metadata['source_commit'] == commit and metadata['source_dirty'] is False
            and metadata['recording_schema'] == 'redbool.landmarks/1', 'Expected a clean, matching online-candidate backend')
    platform, architecture = ('Darwin', 'arm64') if target == 'mac' else ('Windows', 'AMD64')
    require(metadata['platform'] == platform and metadata['architecture'] == architecture, 'Backend target mismatch')
    for report in (online, native):
        require(report.get('camera_opened') is False and report.get('game_packets') == 0
                and report.get('capabilities', {}).get('frozen') is True, 'Backend checks must execute frozen binaries without a camera')
    require(metadata.get('optional_cross_refine') == {'bundled': True, 'default_enabled': False}, 'Expected bundled Heavy, source default off')
    for key in ('inference', 'refinement_inference'):
        check = native.get(key, {})
        require(check.get('passed') is True and check.get('frozen') is True and check.get('camera_opened') is False
                and check.get('game_packets') == 0 and check.get('platform') == platform
                and check.get('architecture') == architecture, 'Successful matching native inference required: ' + key)
    require(any(row.get('variant') == 'Heavy' and row.get('mode') == 'IMAGE' and len(row.get('detect_ms', [])) >= 3
                for row in native['refinement_inference'].get('models', [])), 'Explicit Heavy IMAGE inference required')


def validate_game(report, executable, target):
    require(report.get('build_sha256') == sha(executable), 'Native game check executable mismatch')
    require(report.get('platform') == ('darwin' if target == 'mac' else 'win32'), 'Native game check target mismatch')
    results = {row.get('mode'): row for row in report.get('results', [])}
    require(set(results) == {'lan', 'online'}, 'Actual native LAN and online game checks are both required')
    for mode, result in results.items():
        require(result.get('passed') is True and not result.get('failures') and result.get('camera_backend_launch_seen') is False,
                'Actual native game check failed: ' + mode)
        agreement = result.get('snapshot_agreement', {})
        require(agreement.get('matched_snapshots', 0) > 0 and not agreement.get('mismatch_sequences')
                and not agreement.get('trace_parse_errors') and agreement.get('guest_sequence_strictly_increasing') is True,
                'Native snapshot agreement required: ' + mode)
        for slot in ('P1', 'P2'):
            require(result.get('combat_outcomes', {}).get(slot + ':APPLIED', 0) > 0,
                    'Real applied combat ACKs required: ' + mode + ' ' + slot)
    evidence = results['online'].get('actual_cloud_peer_evidence', {})
    require(evidence.get('host') is True and evidence.get('guest') is True, 'Both actual Cloud peers must be observed')


def validate_mac_reference(gates, identities, report, base):
    """A Windows test artifact may explicitly await external Windows gameplay QA.

    Its Mac evidence must cover the same source/SDK/config, not just a similarly
    named app. This never upgrades a Mac run into a Windows gameplay/FPS pass.
    """
    reference = gates.get('native_mac_reference', {})
    player = Path(reference['player']); player = player if player.is_absolute() else base / player
    require(canonical_sha(player_tree(player, 'mac')) == reference.get('player_tree_sha256'), 'Mac reference player changed')
    pres_path = player.parent / 'source-preservation.json'; info_path = player.parent / 'build-info.json'
    require(sha(pres_path) == reference.get('source_preservation_sha256') and sha(info_path) == reference.get('build_info_sha256'),
            'Mac reference build provenance changed')
    pres, info = read(pres_path), read(info_path)
    require(pres.get('passed') is True and pres.get('source_unchanged') is True and pres.get('before') == pres.get('after')
            and pres.get('exit_code') == 0 and pres.get('automatic_restore_performed') is False
            and not pres.get('changed_preexisting_files') and set(EXTRA_ASSETS).issubset(pres['after']['extra_assets']),
            'Mac reference requires successful preserved-source build')
    require(info.get('release') == RELEASE and info.get('git_commit') == identities['unity_commit'] and info.get('runtime_sha256') == identities['unity_runtime_sha256']
            and canonical_sha(pres['after']['files']) == identities['unity_inputs_sha256'],
            'Mac/Windows Unity source, SDK or embedded config differ')
    executable, _, _ = player_parts(player, 'mac')
    validate_game(report, executable, 'mac')


def validate(args):
    gates = read(args.gates); base = args.gates.resolve().parent
    require(gates.get('schema') == SCHEMA and gates.get('release') == RELEASE, 'Explicit OnlinePreview gate manifest required')
    require(not args.output.exists() and not args.output.is_symlink(), 'Preserve previous package; choose a fresh output')
    # An exe is only one file in a Windows input tree. Checking only that file
    # would allow recursive copies into its own _Data folder.
    input_trees = (args.player if args.target == 'mac' else args.player.parent,
                   args.backend, args.unity_project, args.python_project)
    for destination in (args.output, args.archive):
        if destination is None: continue
        for source in input_trees:
            require(destination != source and source not in destination.parents, 'Outputs must be outside every input tree')
    if args.archive:
        require(not args.archive.exists() and not args.archive.is_symlink() and args.archive.suffix.lower() == '.zip',
                'A fresh ZIP path is required')
        require(args.output not in args.archive.parents, 'Archive must be outside the package')
    pres = read(args.player.parent / 'source-preservation.json')
    require(pres.get('passed') is True and pres.get('exit_code') == 0 and pres.get('source_unchanged') is True
            and pres.get('before') == pres.get('after') and not pres.get('changed_preexisting_files')
            and pres.get('automatic_restore_performed') is False, 'Successful unchanged-source Unity build required')
    require(set(EXTRA_ASSETS).issubset(pres['after']['extra_assets']), 'Preserved build must explicitly cover ignored config JSON and .meta')
    identities, current = fingerprint(args.unity_project, args.python_project, args.player, args.backend,
                                      args.target, pres['after']['extra_assets'])
    require(current == pres['after'], 'Unity inputs changed after build; do not restore files to bypass this check')
    require(gates.get('identities') == identities, 'Checks must be bound to these exact source, SDK, config and artifact identities')
    build = read(args.player.parent / 'build-info.json')
    require(build.get('release') == RELEASE and build.get('git_commit') == identities['unity_commit'] and build.get('runtime_sha256') == identities['unity_runtime_sha256']
            and build.get('view_protocol') == 34, 'Wrong Unity build/runtime/protocol')
    require(not git(args.python_project, 'status', '--porcelain').strip(), 'Python candidate worktree must be clean')
    sdk = gates.get('sdk', {})
    validate_sdk(sdk, args.unity_project, base)
    reports = {name: checked_report(gates, name, base) for name in ('cloud', 'native_game', 'protocol', 'anticipation', 'backend_online', 'backend_native')}
    cloud = reports['cloud']
    require(not cloud.get('error') and cloud.get('sdkVersion') == sdk['version'] and cloud.get('probesReceivedByPeer', 0) > 0
            and cloud.get('echoedWithin1000ms', 0) > 0 and cloud.get('checks')
            and all(row.get('passed') is True for row in cloud['checks']), 'Successful actual SDK/Cloud session checks required')
    require(reports['protocol'].get('assertions', 0) >= 121 and reports['anticipation'].get('checks', 0) >= 1467, 'Current native protocol/anticipation checks required')
    executable, _, _ = player_parts(args.player, args.target)
    if args.target == 'windows' and gates.get('windows_game_validation') == 'pending_external_windows_game_validation':
        validate_mac_reference(gates, identities, reports['native_game'], base)
    else:
        validate_game(reports['native_game'], executable, args.target)
        if args.target == 'windows':
            require(gates.get('windows_game_validation') == 'passed_native_windows', 'Explicit Windows game-validation status required')
    metadata = read(args.backend / 'backend-build.json')
    embedded = read(args.backend / '_internal/assets/backend-provenance.json')
    validate_backend(metadata, embedded, reports['backend_online'], reports['backend_native'], args.target, identities['python_commit'])
    count, variants = source_hashes(args.python_project, identities['python_commit'])
    require(metadata['source_file_count'] == count and metadata['source_sha256'] in variants.values(), 'Backend does not contain this committed Python runtime')
    require(metadata.get('models') == MODELS, 'Model manifest must match the stable model fingerprints')
    for name, digest in MODELS.items():
        require(sha(args.backend / '_internal/assets' / name) == digest, 'Model bytes differ: ' + name)
    if args.target == 'windows':
        ci = gates.get('windows_ci', {})
        path = Path(ci['run_path']); path = path if path.is_absolute() else base / path
        require(sha(path) == ci['run_sha256'], 'Windows CI run record changed')
        run = read(path)
        require(run.get('headSha') == identities['python_commit'] and run.get('conclusion') == 'success'
                and run.get('status') == 'completed', 'Successful exact-HEAD Windows CI run required')
        artifact_path = Path(ci['artifact_path']); artifact_path = artifact_path if artifact_path.is_absolute() else base / artifact_path
        require(sha(artifact_path) == ci['artifact_sha256'], 'Windows artifact metadata changed')
        artifact = read(artifact_path)
        require(artifact.get('name') == 'REDBOOLCamera-OnlineCandidate-Windows-x64' and artifact.get('expired') is False
                and artifact.get('workflow_run', {}).get('head_sha') == identities['python_commit']
                and artifact.get('workflow_run', {}).get('id') == run.get('databaseId'), 'Wrong Windows candidate artifact or CI run')
    return gates, identities, metadata


def portable_backend(metadata):
    keys = (*BACKEND_FIELDS, 'platform', 'architecture', 'python', 'mode', 'models', 'optional_cross_refine', 'macos_compatibility')
    return {key: metadata[key] for key in keys if key in metadata}


def instructions(entry, pending_windows=False):
    validation = ('Mac에서는 게임과 온라인 연결 검사를 통과했습니다. 이 Windows판은 실제 게임 실행과 화면 속도를 사용자 PC에서 확인할 시험본입니다.\n'
                  if pending_windows else '이 플랫폼의 게임과 온라인 연결은 카메라 없이 시험 입력으로 확인했습니다.\n')
    return (f'REDBOOL {RELEASE} · 온라인 대전 후보 시험본\n\n'
            f'{entry}를 실행하세요. Unity·Python을 따로 설치할 필요가 없습니다.\n'
            'Windows는 ZIP을 먼저 모두 풀고, 실행 파일과 옆의 폴더를 함께 유지하세요.\n'
            'Mac은 압축을 풀어 나온 앱을 실행하세요.\n\n'
            '서로 같은 OnlinePreview를 사용하고 PvP → 온라인을 선택하세요.\n'
            '한 사람은 방 만들기, 다른 사람은 전달받은 KR-XXXXXX 코드로 참가합니다.\n'
            '각자 보정과 인물 선택을 완료하면 대전을 시작할 수 있습니다.\n'
            '같은 공유기에서 직접 연결하려면 PvP → LAN을 선택하세요. 스토리·수련도 사용할 수 있습니다.\n'
            '처음 카메라 권한을 요청하면 허용하세요. 내 영상은 상대에게 보내지 않습니다.\n'
            '온라인 연결에는 인터넷이 필요하며, 지역과 회선에 따라 반응 시간이 달라집니다.\n'
            '이 파일은 OnlinePreview 후보이며 기존 v36.3 배포본과 구분해 보관하세요.\n'
            + validation +
            '서로 다른 장소의 두 노트북과 실제 카메라를 함께 사용한 성능은 별도 확인이 필요합니다.\n')


def archive(output, destination, target):
    require(not destination.exists(), 'Archive already exists; preserve it')
    require(output not in destination.parents, 'Archive must be outside the package')
    if target == 'mac':
        require(sys.platform == 'darwin', 'Mac archiving requires macOS ditto')
        subprocess.run(['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(output), str(destination)], check=True)
    else:
        with zipfile.ZipFile(destination, 'x', zipfile.ZIP_DEFLATED, allowZip64=True) as package:
            for path in sorted(output.rglob('*')):
                require(not path.is_symlink(), 'Windows package should not contain symlinks')
                package.write(path, path.relative_to(output.parent).as_posix())
    verify_archive_symlinks(output, destination)


def verify_archive_symlinks(output, destination):
    with zipfile.ZipFile(destination) as package:
        for path in output.rglob('*'):
            if path.is_symlink():
                name = path.relative_to(output.parent).as_posix()
                info = package.getinfo(name)
                require(stat.S_ISLNK(info.external_attr >> 16) and package.read(name).decode() == os.readlink(path),
                        'Mac ZIP did not preserve a symlink')


def assemble(args, validated):
    gates, identities, metadata = validated
    _, _, roots = player_parts(args.player, args.target)
    require(not args.output.exists(), 'Fresh output required')
    if args.target == 'mac': require(sys.platform == 'darwin', 'Mac assembly/signing requires macOS')
    args.output.mkdir(parents=True)
    if args.target == 'mac':
        entry = args.output / 'REDBOOL-OnlinePreview.app'
        shutil.copytree(args.player, entry, symlinks=True)
        data = entry / 'Contents/Resources/Data'
    else:
        entry = args.output / args.player.name
        for root in roots:
            if root.is_dir(): shutil.copytree(root, args.output / root.name, symlinks=True)
            else: shutil.copy2(root, args.output / root.name)
        data = args.output / (args.player.stem + '_Data')
    destination = data / 'StreamingAssets/RedboolCamera'
    shutil.copytree(args.backend, destination, symlinks=True)
    portable = portable_backend(metadata)
    (destination / 'backend-build.json').write_text(json.dumps(portable, indent=2) + '\n', encoding='utf-8')
    pending_windows = args.target == 'windows' and gates.get('windows_game_validation') == 'pending_external_windows_game_validation'
    (args.output / '시작 안내.txt').write_text(instructions(entry.name, pending_windows), encoding='utf-8')
    if args.target == 'mac':
        plist_path = entry / 'Contents/Info.plist'
        with plist_path.open('rb') as stream: info = plistlib.load(stream)
        info['CFBundleName'] = '흑운협'
        info['CFBundleDisplayName'] = '흑운협'
        # MediaPipe's bundled arm64 library requires macOS 14. The camera
        # build separately rejects dependencies requiring a newer release.
        info['LSMinimumSystemVersion'] = '14.0'
        info['NSCameraUsageDescription'] = '동작 인식과 내 모습 미리보기에 카메라를 사용합니다. 영상은 상대에게 보내지 않습니다.'
        with plist_path.open('wb') as stream: plistlib.dump(info, stream, sort_keys=False)
        subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(entry)], check=True)
        subprocess.run(['codesign', '--verify', '--deep', '--strict', str(entry)], check=True)
    for name, digest in MODELS.items():
        require(sha(destination / '_internal/assets' / name) == digest, 'Installed model fingerprint mismatch')
    executable_name = 'REDBOOLCamera' if args.target == 'mac' else 'REDBOOLCamera.exe'
    manifest = {'release': RELEASE, 'build_variant': VARIANT, 'view_protocol': 34, 'target': args.target,
                'entry': entry.name, 'identities': identities, 'backend': portable,
                'sdk': {key: gates['sdk'][key] for key in ('version', 'archive_sha256', 'official_source')},
                'backend_executable_sha256': sha(destination / executable_name),
                'gate_manifest_sha256': sha(args.gates),
                'evidence_sha256': {key: row['sha256'] for key, row in gates['reports'].items()},
                'production_ready': False,
                'validation': {'actual_cloud': 'passed', 'native_backend': 'passed',
                               'native_mac_game': 'passed' if args.target == 'mac' or pending_windows else 'not_used',
                               'native_windows_game': 'pending_external_windows_game_validation' if pending_windows
                                  else ('passed' if args.target == 'windows' else 'not_applicable'),
                               'distant_laptops_with_cameras': 'not_verified'},
                'scope': 'Online candidate; actual Cloud/native synthetic checks passed. Not a distant-laptop camera latency certification.',
                'mac_signature': 'local ad-hoc; not Developer ID notarization' if args.target == 'mac' else None}
    (args.output / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if args.archive: archive(args.output, args.archive, args.target)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('fingerprint', 'validate', 'assemble'))
    for name in ('unity-project', 'python-project', 'player', 'backend'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--target', choices=('mac', 'windows'), required=True)
    parser.add_argument('--gates', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--archive', type=Path)
    args = parser.parse_args()
    for name in ('output', 'archive'):
        value = getattr(args, name)
        require(value is None or not value.is_symlink(), 'Output paths must not be symlinks')
    for name in ('unity_project', 'python_project', 'player', 'backend', 'gates', 'output', 'archive'):
        if getattr(args, name) is not None: setattr(args, name, getattr(args, name).resolve())
    if args.action == 'fingerprint':
        require(not args.archive, 'Fingerprint does not archive')
        preservation = args.player.parent / 'source-preservation.json'
        extra = read(preservation)['after']['extra_assets'] if preservation.is_file() else EXTRA_ASSETS
        identities, _ = fingerprint(args.unity_project, args.python_project, args.player, args.backend, args.target, extra)
        print(json.dumps({'release': RELEASE, 'identities': identities}, indent=2)); return
    if not args.gates or not args.output: parser.error('validate/assemble require --gates and --output')
    if args.archive:
        require(args.action == 'assemble' and not args.archive.exists(), 'Only explicit assembly may create a fresh archive')
    validated = validate(args)
    if args.action == 'validate':
        print(json.dumps({'passed': True, 'release': RELEASE, 'copied': False, 'archived': False}, indent=2))
    else:
        manifest = assemble(args, validated)
        print(json.dumps({'passed': True, 'release': manifest['release'], 'entry': manifest['entry'], 'output': str(args.output)}, indent=2))


if __name__ == '__main__':
    main()
