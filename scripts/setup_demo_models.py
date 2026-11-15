"""Fetch two default pinned assets, and optional Heavy only on request. No camera."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import tempfile
import urllib.request

MODELS = [
    ('pose_landmarker.task', 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task', '59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a'),
    ('local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task', 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/1/pose_landmarker_full.task', '5134a3aad27a58b93da0088d431f366da362b44e3ccfbe3462b3827a839011b1'),
]
HEAVY = ('local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_heavy.task',
    'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/1/pose_landmarker_heavy.task',
    '64437af838a65d18e5ba7a0d39b465540069bc8aae8308de3e318aad31fcbc7b')


def setup(root, source=None, check=False, include_heavy=False):
    for relative, url, digest in MODELS + ([HEAVY] if include_heavy else []):
        target = root / relative
        if target.exists():
            if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
                raise RuntimeError(f'Existing model differs; left unchanged: {target}')
            print('Verified:', target); continue
        if check: raise RuntimeError(f'Missing model: {target}')
        target.parent.mkdir(parents=True, exist_ok=True)
        handle, name = tempfile.mkstemp(prefix='.redbool-model-', dir=target.parent); os.close(handle)
        temp = Path(name)
        try:
            if source: shutil.copyfile(source / relative, temp)
            else:
                with urllib.request.urlopen(url, timeout=60) as response, temp.open('wb') as out:
                    shutil.copyfileobj(response, out)
            if hashlib.sha256(temp.read_bytes()).hexdigest() != digest: raise RuntimeError('Downloaded model SHA mismatch')
            if target.exists(): raise RuntimeError('Model appeared during setup; refusing overwrite')
            temp.replace(target); print('Installed:', target)
        finally:
            if temp.exists(): temp.unlink()


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source', type=Path, help='Optional existing v28/v29 release folder; avoids downloading')
    p.add_argument('--check', action='store_true')
    p.add_argument('--include-heavy', action='store_true',help='Opt-in optional X refinement asset; does not enable live inference')
    a = p.parse_args(); setup(Path(__file__).resolve().parents[1], a.source, a.check, a.include_heavy)
