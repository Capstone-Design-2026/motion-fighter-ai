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
            elif kind in ('runtime_clock', 'camera_settings', 'camera_runtime', 'clock_sync'):
                stamp = row.get('timestamp_ns', 0); key = None; keep = True
            else:
                continue
            if keep:
                last[kind] = key; times[kind] = stamp
                yield json.dumps(row, ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n'


def practice_window(trace):
    """Local sender clock, from calibrated tutorial input through completion."""
    start = end = None
    calibrated = False
    for line in trace.open(encoding='utf-8'):
        row=json.loads(line)
        if row.get('type')=='send' and row['packet'].get('kind')=='STATE':
            calibrated=bool(row['packet'].get('calibrated'))
        elif row.get('type')=='ack' and calibrated:
            a=row['ack']
            if a.get('tutorial_phase') in ('PRACTICE','RETURN'):
                now=row['ack_receive_ns'];start=now if start is None else start;end=now
    return (start-2_000_000_000,end+2_000_000_000) if start is not None else None


def export(root, output=None, practice_poses=False):
    records = root / 'play_records' if (root / 'play_records').is_dir() else root
    if not records.is_dir(): raise ValueError('play_records folder not found')
    output = output or root / ('REDBOOL_diagnostics_' + datetime.datetime.now().strftime('%Y%m%d_%H%M%S') + '.zip')
    allowed = {'Unity.log', 'summary.json', 'personal_profile.json', 'release.json', 'manifest.json', 'annotations.jsonl', 'build-info.json'}
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
                # Optional small, full-rate tutorial observations. No RGB, and
                # explicitly marked partial: these cannot imply full-play accuracy.
                window=practice_window(path) if practice_poses else None
                if window:
                    with archive.open(name.replace('trace.jsonl','practice_clock.jsonl'),'w') as clocks:
                        for line in path.open(encoding='utf-8'):
                            row=json.loads(line)
                            if row.get('type')=='frame' and window[0]<=row['capture_ns']<=window[1]:
                                clocks.write((json.dumps({'frame_id':row['frame_id'],'capture_ns':row['capture_ns'],
                                    'engine_ns':row['state']['timestamp_ns']})+'\n').encode('utf-8'))
                    count+=1
                    for frames in sorted((path.parent/'sessions').glob('*/frames.jsonl')):
                        pose_name=str(frames.relative_to(records)).replace('\\','/').replace('frames.jsonl','practice_frames.jsonl')
                        selected=0;truncated=False
                        with archive.open(pose_name,'w') as target:
                            for line in frames.open(encoding='utf-8'):
                                frame=json.loads(line)
                                if window[0]<=frame['capture_return_monotonic_ns']<=window[1]:
                                    if selected>=8000:truncated=True;break
                                    target.write(line.encode('utf-8'));selected+=1
                        archive.writestr(pose_name.replace('practice_frames.jsonl','practice_pose_scope.json'),
                            json.dumps({'scope':'partial tutorial poses only; no RGB','frames':selected,'truncated':truncated,'window_ns':window}))
                        count+=2
        archive.writestr('READ_ME.txt', 'Diagnostic export. RGB video excluded. '+('Optional partial tutorial landmarks included; see practice_pose_scope.json.\n' if practice_poses else 'Raw frame landmarks excluded.\n')+'STATE/ACK sampled at 10Hz plus every state transition and event. Original files unchanged.\n')
    print(f'{output}\n{count} files, {output.stat().st_size / 1024**2:.2f} MiB. Originals unchanged.')
    return output


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, default=Path.cwd())
    p.add_argument('--output', type=Path)
    p.add_argument('--practice-poses',action='store_true',help='Include only tutorial landmarks for personal-calibration analysis, no video')
    a = p.parse_args(); export(a.root, a.output, a.practice_poses)
