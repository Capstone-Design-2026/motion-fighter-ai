"""Small build provenance; does not include machine paths, recordings or credentials."""
import hashlib
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def runtime_hash():
    digest = hashlib.sha256()
    for folder in ('Assets/Scripts', 'Assets/RedboolVision'):
        for path in sorted((ROOT / folder).rglob('*.cs')):
            if 'Editor' in path.relative_to(ROOT).parts:
                continue
            digest.update(path.relative_to(ROOT).as_posix().encode())
            digest.update(b'\0' + path.read_bytes() + b'\0')
    return digest.hexdigest()


def snapshot():
    def git(*args):
        try:
            return subprocess.check_output(['git', *args], cwd=ROOT, text=True, stderr=subprocess.DEVNULL).strip()
        except (OSError, subprocess.CalledProcessError):
            return 'unavailable'
    return {'release': 'OnlinePreview', 'recognition_baseline': 'v36.3', 'view_protocol': 34, 'unity_editor': '6000.4.5f1',
            'git_commit': git('rev-parse', 'HEAD'),
            'runtime_sha256': runtime_hash(),
            'worktree_changes': git('status', '--porcelain', '--untracked-files=no')}
