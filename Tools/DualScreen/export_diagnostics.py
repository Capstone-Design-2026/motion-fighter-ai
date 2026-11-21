"""Small support ZIP: no RGB videos, raw landmarks, models or credentials.

Run from a release folder: py -3.11 export_diagnostics.py
Original files are only read. A new timestamped ZIP is created alongside them.
"""
import argparse
import bisect
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
                keep = data.get('type') in ('event', 'personal_range', 'demo_calibrated', 'calibration_feedback', 'identity_reset', 'safety')
            elif kind in ('runtime_clock', 'camera_settings', 'camera_runtime', 'clock_sync', 'late_event_ack'):
                stamp = row.get('timestamp_ns', row.get('ack_receive_ns', 0)); key = None; keep = True
            else:
                continue
            if keep:
                last[kind] = key; times[kind] = stamp
                yield json.dumps(row, ensure_ascii=False, separators=(',', ':')).encode('utf-8') + b'\n'


def source_lines(path):
    with path.open(encoding='utf-8') as stream:
        yield from stream


def practice_windows(trace):
    """Separate people/runs/actions using sender time, not a late ACK's arrival."""
    states=[]; acknowledgements=[]; last_state=None
    for line in source_lines(trace):
        row=json.loads(line)
        if row.get('type')=='send' and row['packet'].get('kind')=='STATE':
            p=row['packet']; state=(bool(p.get('calibrated')),p.get('calibration_generation'))
            if state!=last_state: states.append((p['sent_ns'],*state));last_state=state
        elif row.get('type')=='ack':
            a=row['ack']; action=a.get('tutorial_action'); stamp=a.get('sender_ns')
            if a.get('tutorial_phase') in ('PRACTICE','RETURN') and action and type(stamp) is int:
                acknowledgements.append((stamp,a.get('tutorial_run',''),action))
    states.sort(key=lambda s:s[0]); stamps=[s[0] for s in states]; windows=[]
    for stamp,run,action in sorted(acknowledgements,key=lambda a:a[0]):
        index=bisect.bisect_right(stamps,stamp)-1
        if index<0 or not states[index][1]:continue
        generation=states[index][2];key=(generation,run,action)
        if not windows or windows[-1]['key']!=key:
            windows.append({'key':key,'first_ns':stamp,'last_ns':stamp})
        else:windows[-1]['last_ns']=stamp
    for i,window in enumerate(windows):
        generation,run,action=window.pop('key')
        window.update(window_id=i+1,calibration_generation=generation,tutorial_run=run,action=action,
                      start_ns=max(0,window['first_ns']-2_000_000_000),end_ns=window['last_ns']+2_000_000_000)
    # Padding must not assign one original frame to two different people/actions.
    for a,b in zip(windows,windows[1:]):
        if a['end_ns']>=b['start_ns']:
            boundary=(a['last_ns']+b['first_ns'])//2
            a['end_ns']=boundary;b['start_ns']=boundary+1
    return windows


def practice_window(trace):
    """Compatibility envelope only; export uses the individual windows below."""
    windows=practice_windows(trace)
    return (windows[0]['start_ns'],windows[-1]['end_ns']) if windows else None


def fair_budgets(counts,limit):
    budgets=[0]*len(counts);remaining=min(limit,sum(counts))
    while remaining:
        active=[i for i,n in enumerate(counts) if budgets[i]<n]
        share=max(1,remaining//len(active))
        for i in active:
            take=min(share,counts[i]-budgets[i],remaining)
            budgets[i]+=take;remaining-=take
            if not remaining:break
    return budgets


def export_practice(trace,records,archive,limit=8000):
    """Fair contiguous prefix/suffix chunks per tutorial action; never decimate."""
    windows=practice_windows(trace)
    if not windows:return 0
    starts=[w['start_ns'] for w in windows]; candidates=[[] for _ in windows]
    files=sorted((trace.parent/'sessions').glob('*/frames.jsonl'))
    for frames in files:
        for line_index,line in enumerate(source_lines(frames)):
            frame=json.loads(line);stamp=frame['capture_return_monotonic_ns']
            i=bisect.bisect_right(starts,stamp)-1
            if i>=0 and stamp<=windows[i]['end_ns']:
                candidates[i].append((str(frames),line_index,frame['frame_id'],stamp))
    budgets=fair_budgets([len(c) for c in candidates],limit); selected=[]
    for entries,budget in zip(candidates,budgets):
        entries.sort(key=lambda x:x[3])
        if budget>=len(entries):chosen=entries[:]
        else:
            left=(budget+1)//2;right=budget//2
            chosen=entries[:left]+(entries[-right:] if right else [])
        selected.append(chosen)
    wanted={(r[2],r[3]) for group in selected for r in group}; clocks={}
    for line in source_lines(trace):
        row=json.loads(line)
        if row.get('type')=='frame' and (row['frame_id'],row['capture_ns']) in wanted:
            clocks[(row['frame_id'],row['capture_ns'])]={'frame_id':row['frame_id'],
                'capture_ns':row['capture_ns'],'engine_ns':row['state']['timestamp_ns']}
    selected=[[r for r in group if (r[2],r[3]) in clocks] for group in selected]
    trace_name=str(trace.relative_to(records)).replace('\\','/')
    with archive.open(trace_name.replace('trace.jsonl','practice_clock.jsonl'),'w') as target:
        for row in sorted(clocks.values(),key=lambda x:x['capture_ns']):
            target.write((json.dumps(row)+'\n').encode('utf-8'))

    def coverage(i,source=None):
        w=windows[i]; all_rows=[r for r in candidates[i] if source is None or r[0]==source]
        kept=[r for r in selected[i] if source is None or r[0]==source];segments=[]
        for r in kept:
            if not segments or segments[-1]['source']!=r[0] or segments[-1]['end_frame_id']+1!=r[2]:
                segments.append({'source':r[0],'start_frame_id':r[2],'end_frame_id':r[2],
                                 'start_capture_ns':r[3],'end_capture_ns':r[3],'frames':1})
            else:
                segments[-1].update(end_frame_id=r[2],end_capture_ns=r[3],frames=segments[-1]['frames']+1)
        for segment in segments:
            segment['session_frames']=str(Path(segment.pop('source')).relative_to(records)).replace('\\','/')
        return {'window_id':w['window_id'],'calibration_generation':w['calibration_generation'],
                'tutorial_run':w['tutorial_run'],'action':w['action'],'window_ns':[w['start_ns'],w['end_ns']],
                'available_frames':len(all_rows),'frames':len(kept),'truncated':len(kept)<len(all_rows),'segments':segments}

    base={'schema':2,'scope':'partial tutorial poses only; no RGB; not full-play accuracy',
          'selection':'fair budget per calibration/run/action; contiguous prefix and suffix, never every-Nth-frame sampling',
          'max_frames_per_trace':limit,'window_ns':[windows[0]['start_ns'],windows[-1]['end_ns']],
          'replay_note':'Reset motion history at each segment boundary; omitted gaps cannot validate continuity or missed actions.',
          'annotations_note':'Annotations are original and may reference omitted frames; only listed segments are available.'}
    all_windows=[coverage(i) for i in range(len(windows))]
    total=sum(len(g) for g in selected)
    archive.writestr(trace_name.replace('trace.jsonl','practice_coverage.json'),json.dumps({**base,
        'frames':total,'available_frames':sum(map(len,candidates)),'truncated':any(w['truncated'] for w in all_windows),
        'missing_clock_frames':len(wanted)-len(clocks),'windows':all_windows}))
    count=2
    for frames in files:
        source=str(frames);line_indices={r[1] for group in selected for r in group if r[0]==source}
        pose_name=str(frames.relative_to(records)).replace('\\','/').replace('frames.jsonl','practice_frames.jsonl')
        with archive.open(pose_name,'w') as target:
            for line_index,line in enumerate(source_lines(frames)):
                if line_index in line_indices:target.write(line.encode('utf-8'))
        subset=[coverage(i,source) for i in range(len(windows)) if any(r[0]==source for r in candidates[i])]
        archive.writestr(pose_name.replace('practice_frames.jsonl','practice_pose_scope.json'),json.dumps({**base,
            'frames':len(line_indices),'available_frames':sum(w['available_frames'] for w in subset),
            'truncated':any(w['truncated'] for w in subset),'windows':subset}))
        count+=2
    return count


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
                if practice_poses:count+=export_practice(path,records,archive)
        archive.writestr('READ_ME.txt', 'Diagnostic export. RGB video excluded. '+('Optional partial tutorial landmarks included; see practice_pose_scope.json.\n' if practice_poses else 'Raw frame landmarks excluded.\n')+'STATE/ACK sampled at 10Hz plus every state transition and event. Original files unchanged.\n')
    print(f'{output}\n{count} files, {output.stat().st_size / 1024**2:.2f} MiB. Originals unchanged.')
    return output


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, default=Path.cwd())
    p.add_argument('--output', type=Path)
    p.add_argument('--practice-poses',action='store_true',help='Include only tutorial landmarks for personal-calibration analysis, no video')
    a = p.parse_args(); export(a.root, a.output, a.practice_poses)
