"""Launch a locally built game from this Git checkout. No release ZIP required."""
import argparse
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/DualScreen'))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['host', 'join', 'story'])
    p.add_argument('--host'); p.add_argument('--check', action='store_true')
    p.add_argument('--binary', type=Path)
    a = p.parse_args()
    binary = a.binary or ROOT / 'Build/REDBOOL' / ('REDBOOL.exe' if os.name == 'nt' else 'REDBOOL.app/Contents/MacOS/Vision Striker')
    if not binary.is_file(): raise SystemExit('Build first: python Tools/Developer/build_local.py')
    host = a.host
    if a.mode == 'join' and not a.check:
        if not host:
            from lan_discovery import discover
            peers = discover(); host = peers[0] if len(peers) == 1 else input('Host IPv4: ').strip()
        import ipaddress
        host = str(ipaddress.IPv4Address(host))
        print('Joining:', host)
    args = [str(binary), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
    if a.mode == 'host': args += ['--redbool-pvp', '--redbool-require-view']
    if a.mode == 'join': args += ['--redbool-pvp-client', host or 'HOST_IPV4']
    if a.check: print(json.dumps(args, ensure_ascii=False)); return
    record = ROOT / 'Logs/PvP' / datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f'); record.mkdir(parents=True)
    print('Game log:', record)
    return subprocess.call(args + ['-logFile', str(record / 'Unity.log')], cwd=ROOT)


if __name__ == '__main__': sys.exit(main() or 0)
