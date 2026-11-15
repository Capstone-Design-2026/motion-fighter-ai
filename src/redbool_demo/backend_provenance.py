"""Camera package provenance, without camera, models or network access."""
from functools import lru_cache
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

from .landmark_recording import SCHEMA, VERSION

RELEASE = 'v36.2'
PROVENANCE_FILE = 'backend-provenance.json'


def source_provenance(root):
    """Fingerprint exact relative paths and bytes; a parent repo is not this source repo."""
    root = Path(root).resolve()
    files = sorted((p for p in (root/'src').rglob('*.py') if p.is_file()),
                   key=lambda p: p.relative_to(root).as_posix())
    entry = root/'scripts/camera_backend_entry.py'
    if entry.is_file():
        files.append(entry)
    files.sort(key=lambda p: p.relative_to(root).as_posix())
    if not files:
        raise ValueError('No runtime Python source to fingerprint')
    digest = hashlib.sha256()
    for path in files:
        name = path.relative_to(root).as_posix().encode('utf-8')
        data = path.read_bytes()
        digest.update(len(name).to_bytes(8, 'big')); digest.update(name)
        digest.update(len(data).to_bytes(8, 'big')); digest.update(data)
    commit = None
    dirty = None
    try:
        def git(*args):
            return subprocess.run(['git', '-C', str(root), *args], check=True,
                                  capture_output=True, text=True, timeout=5).stdout.strip()
        if Path(git('rev-parse', '--show-toplevel')).resolve() == root:
            commit = git('rev-parse', 'HEAD')
            dirty = bool(git('status', '--porcelain', '--', 'src', 'scripts/camera_backend_entry.py'))
    except (OSError, subprocess.SubprocessError):
        pass
    return {'release': RELEASE, 'recording_schema': f'{SCHEMA}/{VERSION}',
            'source_commit': commit, 'source_sha256': digest.hexdigest(),
            'source_dirty': dirty, 'source_file_count': len(files)}


@lru_cache(maxsize=1)
def capabilities():
    """Read frozen build identity once; schema comes from the imported journal code."""
    if getattr(sys, 'frozen', False):
        path = Path(sys._MEIPASS)/'assets'/PROVENANCE_FILE
        data = json.loads(path.read_text(encoding='utf-8'))
        if (data.get('release') != RELEASE or
                data.get('recording_schema') != f'{SCHEMA}/{VERSION}' or
                not re.fullmatch('[0-9a-f]{64}', str(data.get('source_sha256', '')))):
            raise ValueError('Bundled camera provenance/schema mismatch')
        commit = data.get('source_commit')
        if commit is not None and not re.fullmatch('[0-9a-f]{40,64}', str(commit)):
            raise ValueError('Invalid bundled source commit')
    else:
        data = source_provenance(Path(__file__).resolve().parents[2])
    return {**data, 'recording_schema': f'{SCHEMA}/{VERSION}',
            'frozen': bool(getattr(sys, 'frozen', False)),
            'camera_opened': False, 'game_packets': 0}
