"""Small support ZIP: no RGB videos, raw landmarks, models or credentials.

Run from a release folder: py -3.11 export_diagnostics.py
Original files are only read. A new timestamped ZIP is created alongside them.
"""
import argparse
import datetime
import json
from pathlib import Path
import zipfile


def small_trace(source):
    last = {}; times = {}
    with source.open(encoding='utf-8') as stream:
        for line in stream:
            row = json.loads(line); kind = row.get('type')
            keep = False
            if kind == 'ack':
                data = row['ack']; stamp = row.get('ack_receive_ns', 0)
                key = tuple(data.get(k) for k in ('status', 'tutorial_run', 'tutorial_action', 'tutorial_phase',
                    'tutorial_hits', 'game_can_act', 'battle_preparing', 'game_preparation_locked'))
                keep = bool(data.get('event_id')) or key != last.get(kind) or stamp-times.get(kind, 0) >= 100_000_000
            elif kind == 'send':
                data = row['packet']; stamp = data.get('sent_ns', 0)
                key = tuple(data.get(k) for k in ('kind', 'calibration_generation', 'calibrated', 'tracking',
                    'fsm_state', 'block_active', 'movement', 'personal_status'))
                keep = data.get('kind') != 'STATE' or key != last.get(kind) or stamp-times.get(kind, 0) >= 100_000_000
            elif kind == 'engine':
                data = row.get('log', {}); stamp = data.get('timestamp_ns', 0); key = None
                keep = data.get('type') in ('event', 'personal_range', 'demo_calibrated', 'identity_reset', 'safety')
            else:
                continue
            if keep:
                last[kind] = key; times[kind] = stamp
                yield json.dumps(row, ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n'


def export(root, output=None):
    records = root / 'play_records' if (root / 'play_records').is_dir() else root
    if not records.is_dir(): raise ValueError('play_records folder not found')
    output = output or root / ('REDBOOL_diagnostics_' + datetime.datetime.now().strftime('%Y%m%d_%H%M%S') + '.zip')
    allowed = {'Unity.log', 'summary.json', 'personal_profile.json', 'release.json', 'manifest.json', 'annotations.jsonl'}
    count = 0
    with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(records.rglob('*')):
            if not path.is_file() or path.is_symlink(): continue
            name = str(path.relative_to(records)).replace('\\', '/')
            if path.name in allowed:
                archive.write(path, name); count += 1
            elif path.name == 'trace.jsonl':
                with archive.open(name.replace('trace.jsonl', 'diagnostic_trace.jsonl'), 'w') as target:
                    for row in small_trace(path): target.write(row)
                count += 1
        archive.writestr('READ_ME.txt', 'Diagnostic export only. RGB video and frame landmarks excluded.\nSTATE/ACK sampled at 10Hz plus every state transition and event. Original files unchanged.\n')
    print(f'{output}\n{count} files, {output.stat().st_size / 1024**2:.2f} MiB. Originals unchanged.')
    return output


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, default=Path.cwd())
    p.add_argument('--output', type=Path)
    a = p.parse_args(); export(a.root, a.output)
