"""Published model fingerprints also used by scripts/setup_demo_models.py."""
import hashlib
from pathlib import Path

POSE_SHA256 = {
    'Lite': '59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a',
    'Full': '5134a3aad27a58b93da0088d431f366da362b44e3ccfbe3462b3827a839011b1',
    'Heavy': '64437af838a65d18e5ba7a0d39b465540069bc8aae8308de3e318aad31fcbc7b',
}

HEAVY_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/1/pose_landmarker_heavy.task'


def verify_pose_asset(model, variant):
    path = Path(model)
    if variant not in POSE_SHA256 or not path.is_file():
        raise ValueError('Verified '+str(variant)+' pose asset is missing')
    if hashlib.sha256(path.read_bytes()).hexdigest() != POSE_SHA256[variant]:
        raise ValueError('Model SHA does not match the verified '+variant+' asset')
