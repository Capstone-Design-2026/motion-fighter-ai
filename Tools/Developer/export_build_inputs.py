"""Export an auditable Unity source overlay; never resets or commits the worktree.

Only Assets, Packages and ProjectSettings are build inputs. The ZIP contains
changed tracked inputs plus six explicitly required untracked Rql files, not a
copy of the project. See BUILD_INPUTS_README.md for restore and limitations.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import zipfile

SCHEMA = 'redbool.build_inputs/1'
ROOTS = ('Assets', 'Packages', 'ProjectSettings')
RQL = {f'Assets/Plugins/NuGet/Rql.{name}.dll{suffix}'
       for name in ('Contracts', 'Execution', 'Parser') for suffix in ('', '.meta')}
SKIP_DIRS = {'.git', '__pycache__', 'Library', 'Temp', 'Logs', 'obj',
             'CameraDiagnostics', 'DiagnosticsExports', 'sessions', 'local_data',
             'recordings', 'play_records'}
SECRET_SUFFIXES = {'.pem', '.key', '.pfx', '.p12', '.keystore', '.jks'}
MAX_MANIFEST = 32 * 1024 * 1024
MAX_FILE = 2 * 1024 * 1024 * 1024
MAX_ARCHIVE = 16 * 1024 * 1024 * 1024
CREDENTIAL = re.compile(rb'(?im)^[ \t]*["\']?(?:password|[^\r\n]*(?:Password|AccessToken|RefreshToken)|access_token|refresh_token|client_secret|private_key)["\']?[ \t]*[:=][ \t]*([^\r\n]+)')


def git(repo, *args):
    return subprocess.check_output(['git', *args], cwd=repo, stderr=subprocess.PIPE)


def names(repo, *args):
    return {os.fsdecode(x) for x in git(repo, *args).split(b'\0') if x}


def safe_name(name):
    p = PurePosixPath(name)
    if not isinstance(name, str) or not name or '\\' in name or '\0' in name or p.is_absolute() or '..' in p.parts or p.as_posix() != name:
        raise ValueError('Unsafe input path')
    return p


def exclusion(name):
    p = safe_name(name)
    if p.parts[0] not in ROOTS:
        return 'outside_build_inputs'
    if any(x in SKIP_DIRS for x in p.parts) or p.name in ('.DS_Store', 'Thumbs.db') or p.suffix in ('.pyc', '.pyo'):
        return 'cache_or_recording'
    low = p.name.lower()
    if low == '.env' or low.startswith('.env.') or 'credentials' in low or low in ('secrets.json', 'secrets.yaml', 'secrets.yml') or p.suffix.lower() in SECRET_SUFFIXES:
        return 'credential_file'
    return None


def plain_path(root, name):
    p = safe_name(name)
    cursor = root
    for part in p.parts:
        cursor = cursor / part
        if cursor.is_symlink():
            raise ValueError('Linked input is not exported/restored: ' + name)
    return cursor


def fingerprint(path):
    before = path.stat()
    if not stat.S_ISREG(before.st_mode):
        raise ValueError('Build inputs must be regular files')
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns, before.st_ino) != (after.st_size, after.st_mtime_ns, after.st_ino):
        raise ValueError('Input changed during hashing: ' + path.name)
    return {'sha256': digest.hexdigest(), 'bytes': after.st_size}


def tree_hash(files):
    digest = hashlib.sha256()
    for name in sorted(files):
        digest.update(name.encode('utf-8', 'surrogateescape') + b'\0')
        digest.update(files[name]['sha256'].encode('ascii') + b'\0')
    return digest.hexdigest()


def runtime_hash(repo, files):
    digest = hashlib.sha256()
    for folder in ('Assets/Scripts/', 'Assets/RedboolVision/'):
        for name in sorted(x for x in files if x.startswith(folder) and x.endswith('.cs') and 'Editor' not in PurePosixPath(x).parts):
            digest.update(name.encode() + b'\0')
            with plain_path(repo, name).open('rb') as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(block)
            digest.update(b'\0')
    return digest.hexdigest()


def credential_check(path):
    # Binary plugins/audio/meshes are not decoded. Reject recognizable nonempty
    # credential fields in small config/text inputs; never print matched values.
    if path.suffix.lower() not in ('.asset', '.json', '.yaml', '.yml', '.xml', '.config', '.txt'):
        return
    if path.stat().st_size > 16 * 1024 * 1024:
        return
    data = path.read_bytes()
    if b'-----BEGIN ' in data and b'PRIVATE KEY-----' in data:
        raise ValueError('Private key content refused in ' + path.name)
    for match in CREDENTIAL.finditer(data):
        value = match.group(1).strip().strip(b'\'"')
        if value and value.lower() not in (b'null', b'none', b'{}', b'[]'):
            raise ValueError('Nonempty credential field refused in ' + path.name)


def snapshot(repo):
    commit = git(repo, 'rev-parse', 'HEAD').decode().strip()
    dirty = names(repo, 'diff', '--name-only', '--no-renames', '-z', 'HEAD')
    tracked = names(repo, 'ls-files', '-z', '--', *ROOTS)
    untracked = names(repo, 'ls-files', '--others', '--exclude-standard', '-z', '--', *ROOTS)
    unexpected = sorted(p for p in untracked if not exclusion(p) and p not in RQL)
    if unexpected:
        raise ValueError('Commit or review unexpected untracked build inputs first: ' + ', '.join(unexpected))
    files, excluded, deleted = {}, {}, []
    for name in sorted(tracked | (untracked & RQL)):
        reason = exclusion(name)
        if reason:
            excluded[name] = reason
            continue
        path = plain_path(repo, name)
        if not path.exists():
            if name not in dirty:
                raise ValueError('Unexpected missing tracked input: ' + name)
            continue
        files[name] = fingerprint(path)
    overlay = []
    for name in sorted(dirty | (untracked & RQL)):
        reason = exclusion(name)
        if reason:
            excluded[name] = reason
            continue
        path = plain_path(repo, name)
        if not path.exists():
            deleted.append(name)
        else:
            credential_check(path)
            if name not in files:
                raise ValueError('Changed input missing from tracked manifest: ' + name)
            overlay.append(name)
    return {'schema': SCHEMA, 'git_commit': commit, 'build_roots': list(ROOTS),
            'content_tree_sha256': tree_hash(files), 'assets_tree_sha256': tree_hash({p: v for p, v in files.items() if p.startswith('Assets/')}),
            'runtime_csharp_sha256': runtime_hash(repo, files), 'files': files,
            'overlay_files': overlay, 'deleted_files': deleted,
            'dirty_tracked_files': sorted(dirty),
            'included_untracked_files': sorted(untracked & RQL),
            'excluded_files': excluded,
            'scope': 'Exact included source bytes, not byte-identical executable builds. No user configuration, RGB/video, credentials or caches. Requires this commit and resolved Git LFS assets.'}


def export(repo, output):
    repo = repo.resolve(); output = output.resolve()
    if output.exists():
        raise FileExistsError('Existing archive is never overwritten')
    if output.is_relative_to(repo):
        raise ValueError('Write the archive outside the source checkout')
    manifest = snapshot(repo)
    status = git(repo, 'status', '--porcelain', '--untracked-files=no')
    output.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix=output.name + '.', suffix='.part', dir=output.parent)
    os.close(fd); temp = Path(temp_name)
    try:
        with zipfile.ZipFile(temp, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=1, allowZip64=True) as archive:
            for name in manifest['overlay_files']:
                path = plain_path(repo, name)
                if fingerprint(path) != manifest['files'][name]:
                    raise ValueError('Input changed before archive copy: ' + name)
                archive.write(path, 'overlay/' + name)
            archive.writestr('manifest.json', json.dumps(manifest, ensure_ascii=True, indent=2) + '\n')
        verify_archive(temp)
        if git(repo, 'rev-parse', 'HEAD').decode().strip() != manifest['git_commit'] or git(repo, 'status', '--porcelain', '--untracked-files=no') != status:
            raise ValueError('Source revision/status changed during export')
        # The archive contains copies only; verify all recorded inputs once more
        # so an editor/import cannot silently change an otherwise clean asset.
        verify_tree(repo, manifest)
        with output.open('xb') as destination, temp.open('rb') as source:
            shutil.copyfileobj(source, destination, 1024 * 1024)
        return manifest
    finally:
        temp.unlink(missing_ok=True)


def read_manifest(archive):
    entries = archive.infolist()
    if len(entries) > 100000 or len({x.filename for x in entries}) != len(entries):
        raise ValueError('Duplicate/excessive ZIP entries')
    if sum(x.file_size for x in entries) > MAX_ARCHIVE:
        raise ValueError('Archive exceeds source overlay limit')
    for entry in entries:
        safe_name(entry.filename)
        if entry.is_dir() or stat.S_ISLNK(entry.external_attr >> 16) or entry.file_size > MAX_FILE:
            raise ValueError('Unsupported archive entry')
    item = archive.getinfo('manifest.json')
    if item.file_size > MAX_MANIFEST:
        raise ValueError('Manifest too large')
    manifest = json.loads(archive.read(item))
    if manifest.get('schema') != SCHEMA or not re.fullmatch('[a-f0-9]{40}', manifest.get('git_commit', '')) or manifest.get('build_roots') != list(ROOTS):
        raise ValueError('Unsupported manifest')
    files = manifest['files']
    for name, record in files.items():
        if exclusion(name) or not re.fullmatch('[a-f0-9]{64}', record.get('sha256', '')) or not isinstance(record.get('bytes'), int) or not 0 <= record['bytes'] <= MAX_FILE:
            raise ValueError('Invalid input manifest record')
    overlay, deleted = manifest['overlay_files'], manifest['deleted_files']
    if len(set(overlay)) != len(overlay) or len(set(deleted)) != len(deleted) or set(overlay) & set(deleted):
        raise ValueError('Invalid changed input list')
    if any(name not in files for name in overlay) or any(exclusion(name) or name in files for name in deleted):
        raise ValueError('Invalid overlay/delete input')
    if set(x.filename for x in entries) != {'manifest.json'} | {'overlay/' + x for x in overlay}:
        raise ValueError('ZIP entries differ from manifest')
    if tree_hash(files) != manifest['content_tree_sha256'] or tree_hash({p: v for p, v in files.items() if p.startswith('Assets/')}) != manifest['assets_tree_sha256']:
        raise ValueError('Manifest tree hash mismatch')
    return manifest


def verify_archive(path):
    with zipfile.ZipFile(path) as archive:
        manifest = read_manifest(archive)
        for name in manifest['overlay_files']:
            digest = hashlib.sha256(); size = 0
            with archive.open('overlay/' + name) as source:
                for block in iter(lambda: source.read(1024 * 1024), b''):
                    digest.update(block); size += len(block)
            if {'sha256': digest.hexdigest(), 'bytes': size} != manifest['files'][name]:
                raise ValueError('Archived content hash mismatch: ' + name)
    return manifest


def verify_tree(repo, manifest):
    if git(repo, 'rev-parse', 'HEAD').decode().strip() != manifest['git_commit']:
        raise ValueError('Checkout commit does not match manifest')
    missing, mismatched = [], []
    for name, expected in manifest['files'].items():
        path = plain_path(repo, name)
        if not path.is_file():
            missing.append(name)
        elif fingerprint(path) != expected:
            mismatched.append(name)
    resurrected = [x for x in manifest['deleted_files'] if plain_path(repo, x).exists()]
    unexpected = sorted(x for x in names(repo, 'ls-files', '--others', '--exclude-standard', '-z', '--', *ROOTS)
                        if not exclusion(x) and x not in manifest['files'])
    if missing or mismatched or resurrected or unexpected:
        raise ValueError(json.dumps({'missing': missing, 'hash_mismatch': mismatched, 'should_be_deleted': resurrected, 'unexpected_untracked': unexpected}))
    if runtime_hash(repo, manifest['files']) != manifest['runtime_csharp_sha256']:
        raise ValueError('Runtime source aggregate mismatch')
    return True


def restore(repo, path):
    repo = repo.resolve(); manifest = verify_archive(path)
    if git(repo, 'rev-parse', 'HEAD').decode().strip() != manifest['git_commit']:
        raise ValueError('Restore requires the exact recorded commit')
    if git(repo, 'status', '--porcelain', '--untracked-files=no'):
        raise ValueError('Restore requires a separate clean checkout; existing edits are never overwritten')
    tracked = names(repo, 'ls-files', '-z')
    unexpected = sorted(x for x in names(repo, 'ls-files', '--others', '--exclude-standard', '-z', '--', *ROOTS)
                        if not exclusion(x) and x not in manifest['files'])
    if unexpected:
        raise ValueError('Unexpected untracked build inputs; use a separate clean checkout: ' + ', '.join(unexpected))
    for name in manifest['overlay_files'] + manifest['deleted_files']:
        target = plain_path(repo, name)
        if target.exists() and name not in tracked:
            raise ValueError('Untracked file would be overwritten: ' + name)
    # Preflight untouched files before any mutation. Git LFS pointers and CRLF
    # conversion fail here with explicit names rather than a false success.
    changed = set(manifest['overlay_files']) | set(manifest['deleted_files'])
    for name, expected in manifest['files'].items():
        if name not in changed:
            target = plain_path(repo, name)
            if not target.is_file() or fingerprint(target) != expected:
                raise ValueError('Base input differs; resolve LFS/line endings first: ' + name)
    with zipfile.ZipFile(path) as archive:
        for name in manifest['overlay_files']:
            target = plain_path(repo, name); target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open('overlay/' + name) as source, target.open('wb') as destination:
                shutil.copyfileobj(source, destination, 1024 * 1024)
        for name in manifest['deleted_files']:
            plain_path(repo, name).unlink(missing_ok=True)
    verify_tree(repo, manifest)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    commands = parser.add_subparsers(dest='command', required=True)
    command = commands.add_parser('export'); command.add_argument('--output', type=Path, required=True)
    for mode in ('verify-archive', 'verify-tree', 'restore'):
        command = commands.add_parser(mode); command.add_argument('archive', type=Path)
    args = parser.parse_args()
    if args.command == 'export':
        manifest = export(args.repo, args.output)
    elif args.command == 'restore':
        manifest = restore(args.repo, args.archive)
    else:
        manifest = verify_archive(args.archive)
        if args.command == 'verify-tree':
            verify_tree(args.repo, manifest)
    print(json.dumps({'passed': True, 'git_commit': manifest['git_commit'], 'content_tree_sha256': manifest['content_tree_sha256'],
                      'tracked_and_required_inputs': len(manifest['files']), 'overlay_files': len(manifest['overlay_files']),
                      'deleted_files': len(manifest['deleted_files']), 'excluded_files': manifest['excluded_files']}, indent=2))


if __name__ == '__main__':
    main()
