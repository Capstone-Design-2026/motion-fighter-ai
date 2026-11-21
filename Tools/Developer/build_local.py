"""Build this checkout for this computer. Close Unity Editor before running."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
VERSION = '6000.4.5f1'


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--editor', type=Path); p.add_argument('--check', action='store_true')
    # Play build without the "Development Build" watermark. It goes to its own folder (Build/REDBOOL-release) so the
    # default development build, which the synthetic camera check needs, is left untouched.
    p.add_argument('--release', action='store_true')
    a = p.parse_args()
    editor = a.editor or (Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Unity/Hub/Editor' / VERSION / 'Editor/Unity.exe' if os.name == 'nt' else Path('/Applications/Unity/Hub/Editor') / VERSION / 'Unity.app/Contents/MacOS/Unity')
    if not editor.is_file(): raise SystemExit('Unity ' + VERSION + ' not found; pass --editor PATH')
    output = ROOT / ('Build/REDBOOL-release' if a.release else 'Build/REDBOOL') / ('REDBOOL.exe' if os.name == 'nt' else 'REDBOOL.app')
    log = ROOT / 'Logs/local-build.log'
    args = [str(editor), '-batchmode', '-quit', '-projectPath', str(ROOT), '-executeMethod', 'RedboolLocalDemoBuild.Run', '-logFile', str(log)]
    if a.check: print(json.dumps({'command': args, 'output': str(output)}, ensure_ascii=False)); return
    log.parent.mkdir(parents=True, exist_ok=True)
    from build_stamp import snapshot
    stamp = snapshot()
    env = {**os.environ, 'UNITY_MCP_DISABLE': '1', 'REDBOOL_BUILD_OUTPUT': str(output),
           'REDBOOL_BUILD_TARGET': 'windows' if os.name == 'nt' else 'mac',
           'REDBOOL_BUILD_RELEASE': '1' if a.release else '0'}
    code = subprocess.call(args, cwd=ROOT, env=env)
    if code == 0:
        (output.parent / 'build-info.json').write_text(json.dumps(stamp, ensure_ascii=False, indent=2), encoding='utf-8')
    return code


if __name__ == '__main__': sys.exit(main() or 0)
