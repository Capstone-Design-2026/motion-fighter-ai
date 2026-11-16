"""Verify an explicitly labelled frozen online candidate without camera or UDP."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile


def verify(executable, expected_commit):
    executable = Path(executable).resolve()
    if not re.fullmatch(r'[0-9a-f]{40,64}', expected_commit):
        raise ValueError('Expected source commit must be a full hexadecimal commit.')
    metadata = json.loads((executable.parent / 'backend-build.json').read_text(encoding='utf-8'))
    embedded = json.loads((executable.parent / '_internal/assets/backend-provenance.json').read_text(encoding='utf-8'))
    fields = ('release', 'recording_schema', 'source_commit', 'source_sha256',
              'source_dirty', 'source_file_count', 'build_variant')
    for name, data in (('metadata', metadata), ('embedded provenance', embedded)):
        if data.get('release') != 'v36.3' or data.get('build_variant') != 'online-candidate':
            raise ValueError(name + ': not an explicit v36.3 online candidate')
        if data.get('source_commit') != expected_commit or data.get('source_dirty') is not False:
            raise ValueError(name + ': source does not match the clean CI commit')
        if not re.fullmatch(r'[0-9a-f]{64}', str(data.get('source_sha256'))):
            raise ValueError(name + ': invalid source fingerprint')
    if any(metadata.get(key) != embedded.get(key) for key in fields):
        raise ValueError('Embedded provenance and sidecar differ.')
    # --check goes through the actual frozen live parser and returns before
    # creating a camera, bridge, or sender. Synthetic bridge mode alone would
    # ignore unknown flags and therefore would not prove parser compatibility.
    with tempfile.TemporaryDirectory(prefix='redbool-online-check-') as folder:
        result = subprocess.run([str(executable), '--check', '--network-profile', 'online'],
                                cwd=folder, capture_output=True, text=True, encoding='utf-8', timeout=120)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    capabilities = json.loads(result.stdout.strip().splitlines()[-1])
    if any(capabilities.get(key) != metadata.get(key) for key in fields):
        raise ValueError('Executed binary provenance differs from the candidate metadata.')
    if (capabilities.get('frozen') is not True or capabilities.get('camera_opened') is not False
            or capabilities.get('game_packets') != 0):
        raise ValueError('The check did not stay frozen and camera/network free.')
    return {'passed': True, 'scope': 'Frozen online-option parsing and exact candidate provenance only; '
            'not a Relay connection, live camera or Internet latency test.',
            'capabilities': capabilities, 'camera_opened': False, 'game_packets': 0}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--executable', type=Path, required=True)
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = verify(args.executable, args.expected_commit)
    if args.output:
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
