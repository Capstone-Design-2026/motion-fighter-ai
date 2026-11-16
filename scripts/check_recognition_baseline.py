#!/usr/bin/env python3
"""Detect changes to reviewed recognition inputs; never measure recognition quality.

No imports from src, camera access, UDP, model loading, or automatic baseline update.
Only CRLF is normalized to LF. New src/*.py paths require explicit review; tests,
docs, caches, and files outside src are outside that inventory.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys

SCHEMA = 'redbool.recognition-baseline/1'
ALGORITHM = 'sha256-crlf-to-lf-v1'


def digest(data, normalize=True):
    return hashlib.sha256(data.replace(b'\r\n', b'\n') if normalize else data).hexdigest()


def group_digest(rows):
    return digest(''.join(f'{path}\0{sha}\n' for path, sha in sorted(rows.items())).encode(), False)


def checked_path(root, name):
    if not isinstance(name, str) or not name or '\\' in name or ':' in name:
        raise ValueError('Invalid relative path')
    rel = PurePosixPath(name)
    if rel.is_absolute() or '..' in rel.parts or rel.as_posix() != name:
        raise ValueError('Path must be canonical and relative: ' + name)
    path = root
    for part in rel.parts:
        path = path / part
        if path.is_symlink():
            raise ValueError('Symlinks are not baseline inputs: ' + name)
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError('Path escapes root: ' + name)
    return path


def valid_hash(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None


def check(repo, manifest, *, check_models=False, model_root=None):
    repo = Path(repo).resolve()
    errors = []
    if not isinstance(manifest, dict):
        raise ValueError('Recognition baseline must be a JSON object')
    if manifest.get('schema') != SCHEMA or manifest.get('source_hash_algorithm') != ALGORITHM:
        raise ValueError('Unsupported recognition baseline schema or hash algorithm')
    inventory = manifest.get('runtime_python_paths')
    if not isinstance(inventory, list) or not inventory or len(set(inventory)) != len(inventory):
        raise ValueError('Missing or duplicate runtime Python inventory')
    for name in inventory:
        if not isinstance(name, str) or not name.startswith('src/') or not name.endswith('.py'):
            raise ValueError('Inventory must contain explicit src/*.py paths')
        checked_path(repo, name)
    actual_inventory = {p.relative_to(repo).as_posix() for p in (repo / 'src').rglob('*.py')}
    for name in sorted(actual_inventory - set(inventory)):
        errors.append({'kind': 'unreviewed_runtime_file', 'path': name})
    for name in sorted(set(inventory) - actual_inventory):
        errors.append({'kind': 'missing_runtime_file', 'path': name})
    groups = manifest.get('groups')
    if not isinstance(groups, dict) or not groups or 'recognition' not in groups:
        raise ValueError('Missing recognition group')
    checked = set()
    checked_groups = {}
    for group, spec in groups.items():
        files = spec.get('files')
        if not isinstance(files, dict) or not files:
            raise ValueError('Empty protected group: ' + group)
        if spec.get('sha256') != group_digest(files):
            raise ValueError('Invalid group fingerprint: ' + group)
        actual = {}
        for name, expected in files.items():
            if name not in inventory or not valid_hash(expected):
                raise ValueError('Protected input is not in inventory or hash invalid: ' + name)
            if name in checked:
                raise ValueError('Protected input appears in multiple groups: ' + name)
            checked.add(name)
            path = checked_path(repo, name)
            if not path.is_file():
                errors.append({'kind': 'missing_protected_file', 'group': group, 'path': name})
                continue
            actual[name] = digest(path.read_bytes())
            if actual[name] != expected:
                errors.append({'kind': 'source_changed', 'group': group, 'path': name,
                               'expected': expected, 'actual': actual[name]})
        checked_groups[group] = group_digest(actual)
    dependency = manifest.get('dependency_contract')
    if not isinstance(dependency, dict) or not isinstance(dependency.get('files'), dict):
        raise ValueError('Missing pinned backend dependency contract')
    deps = dependency['files']
    if set(deps) != {'requirements-backend-build.txt'} or not all(valid_hash(v) for v in deps.values()):
        raise ValueError('Only requirements-backend-build.txt is the pinned dependency contract')
    if dependency.get('sha256') != group_digest(deps):
        raise ValueError('Invalid backend dependency fingerprint')
    for name, expected in deps.items():
        path = checked_path(repo, name)
        if not path.is_file():
            errors.append({'kind': 'missing_dependency_contract', 'path': name})
        elif digest(path.read_bytes()) != expected:
            errors.append({'kind': 'dependency_contract_changed', 'path': name})
    model_status = 'not_checked'
    if check_models:
        models = manifest.get('models')
        if not isinstance(models, dict) or not models:
            raise ValueError('Native model verification requested without model hashes')
        model_root = Path(model_root).resolve() if model_root else repo
        for name, expected in models.items():
            if not valid_hash(expected):
                raise ValueError('Invalid model hash: ' + name)
            path = checked_path(model_root, name)
            if not path.is_file():
                errors.append({'kind': 'missing_model', 'path': name})
            elif digest(path.read_bytes(), False) != expected:
                errors.append({'kind': 'model_changed', 'path': name})
        model_status = 'failed' if any(e['kind'].endswith('model') or e['kind'] == 'model_changed' for e in errors) else 'verified'
    return {'passed': not errors, 'scope': 'Reviewed source change detection only; not accuracy, latency, or live-play validation.',
            'baseline_commit': manifest.get('baseline_commit'), 'protected_files': len(checked),
            'runtime_inventory_files': len(inventory), 'group_sha256': checked_groups,
            'dependency_contract_sha256': dependency['sha256'],
            'models': model_status, 'errors': errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--check-models', action='store_true')
    parser.add_argument('--model-root', type=Path)
    args = parser.parse_args()
    try:
        manifest_path = args.manifest or args.repo / 'config/recognition_baseline.json'
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        result = check(args.repo, manifest, check_models=args.check_models, model_root=args.model_root)
    except (OSError, ValueError, TypeError) as exc:
        result = {'passed': False, 'scope': 'Source baseline validation', 'errors': [str(exc)]}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
