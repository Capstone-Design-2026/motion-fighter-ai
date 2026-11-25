"""Observation distributions, not automatic recording acceptance or pose truth."""
import argparse
from collections import Counter
import json
from pathlib import Path
import numpy as np
from action_engine.types import PoseObservation
from action_engine.experimental import AblationConfig, DevelopmentEngine
from event_ground_truth.assisted import initialize_confirmed
from event_ground_truth.schema import mapped_segments, validate

JOINTS = {11: 'L shoulder', 12: 'R shoulder', 13: 'L elbow', 14: 'R elbow', 15: 'L wrist', 16: 'R wrist'}
CHANNELS = {'LEFT': (11,12,13,15), 'RIGHT': (11,12,14,16), 'BOTH': tuple(JOINTS)}


def joint_states(frame, visibility=.5):
    points = frame.get('image_landmarks')
    vis = (frame.get('landmark_visibility') or {}).get('image')
    present = frame.get('pose_validity', {}).get('pose_present', bool(points))
    result = {}
    for j in JOINTS:
        flags = []
        if not present or not frame.get('pose_success', False):
            flags.append('POSE_MISSING')
        elif not points or len(points) <= j or len(points[j]) != 3 or not all(
                isinstance(x, (int,float)) and np.isfinite(x) for x in points[j]):
            flags.append('INVALID_COORDINATE')
        else:
            if not all(0 <= x <= 1 for x in points[j][:2]):
                flags.append('OUTSIDE')
            if not vis or len(vis) <= j or vis[j] is None or not np.isfinite(vis[j]) or vis[j] < visibility:
                flags.append('LOW_VISIBILITY')
        result[j] = flags or ['VALID']
    return result


def load_frames(path, source, session_id):
    """Accept recorder rows or paired-replay wrappers; never nearest-time join."""
    rows = []
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if 'key' in row and row['key'].split('/')[0] != session_id:
            raise ValueError('Pose file belongs to another session')
        f = row.get('frame', row)
        original = source.get(f['frame_id'])
        if original is None or f['capture_return_monotonic_ns'] != original['capture_return_monotonic_ns']:
            raise ValueError('Pose/source frame or timestamp mismatch')
        rows.append(f)
    ids = [r['frame_id'] for r in rows]
    if len(ids) != len(set(ids)) or ids != sorted(ids):
        raise ValueError('Duplicate/unordered pose frame IDs')
    return rows


def stats(values):
    return {'count': len(values), 'p50': float(np.median(values)) if values else None,
            'p95': float(np.percentile(values,95)) if values else None,
            'max': float(max(values)) if values else None}


def summarize(frames, *, visibility=.5, gap_ms=100):
    if not frames:
        raise ValueError('No observations')
    timestamps = [f['capture_return_monotonic_ns'] for f in frames]
    if any(b['frame_id'] <= a['frame_id'] for a,b in zip(frames,frames[1:])):
        raise ValueError('Duplicate/unordered frame IDs')
    if any(b <= a for a,b in zip(timestamps,timestamps[1:])):
        raise ValueError('Nonincreasing capture clock')
    counts = {j:Counter() for j in JOINTS}
    runs = {k:[] for k in [*map(str,JOINTS),*CHANNELS]}
    active = {}; previous = None
    def finish(key):
        if key in active:
            n,lo,hi = active.pop(key)
            runs[key].append({'frames':n,'duration_ms':(hi-lo)/1e6,'start_ns':lo,'end_ns':hi})
    for frame in frames:
        stamp = frame['capture_return_monotonic_ns']; states = joint_states(frame,visibility)
        for j,flags in states.items():counts[j].update(flags)
        good = {str(j):flags==['VALID'] for j,flags in states.items()}
        good.update({k:all(states[j]==['VALID'] for j in ids) for k,ids in CHANNELS.items()})
        broken = previous is not None and (stamp-previous['capture_return_monotonic_ns']>gap_ms*1e6 or frame['frame_id']!=previous['frame_id']+1)
        for k,valid in good.items():
            if broken or not valid:finish(k)
            if valid:
                n,lo,_ = active.get(k,(0,stamp,stamp));active[k]=(n+1,lo,stamp)
        previous=frame
    for key in list(active):finish(key)
    dt=[(b-a)/1e6 for a,b in zip(timestamps,timestamps[1:])]
    return {'frames':len(frames),'visibility_diagnostic_threshold':visibility,'run_gap_ms':gap_ms,
        'acceptance_decision':None,'pose_present_frames':sum(bool(f.get('pose_validity',{}).get('pose_present',f.get('image_landmarks'))) for f in frames),
        'joint_status_counts':{str(j):dict(c) for j,c in counts.items()},
        'joint_valid_rates':{str(j):c['VALID']/len(frames) for j,c in counts.items()},
        'capture_interval_ms':stats(dt),'intervals_above_run_gap':sum(d>gap_ms for d in dt),
        'unmapped_frame_id_gaps':sum(b['frame_id']-a['frame_id']-1 for a,b in zip(frames,frames[1:])),
        'observed_fps':(len(frames)-1)*1e9/(timestamps[-1]-timestamps[0]) if len(frames)>1 else None,
        'valid_runs':runs,'longest_valid_runs':{k:max(v,key=lambda x:x['frames']) if v else None for k,v in runs.items()},
        'notes':'Thresholds describe existing engine compatibility, not validated capture acceptance. Runs require consecutive frame IDs. Camera internal drops cannot be inferred from frame IDs alone.'}


def audit(session, output, pose_files=(), dataset_path=None, annotation_path=None):
    if output.exists():raise ValueError('New output required')
    manifest=json.loads((session/'manifest.json').read_text())
    original=[json.loads(s) for s in (session/'frames.jsonl').read_text().splitlines()]
    source={f['frame_id']:f for f in original}
    variants={'stored':original}
    for name,path in pose_files:
        if name in variants:raise ValueError('Duplicate variant')
        variants[name]=load_frames(path,source,manifest['session_id'])
    common=set.intersection(*(set(f['frame_id'] for f in fs) for fs in variants.values()))
    if not common:raise ValueError('No common mapped observations')
    result={'scope':'observation quality, no model accuracy or automatic ground truth',
            'per_variant':{},'common_frame_ids':sorted(common),'common_support':{},
            'guard_initialization':{'status':'NOT_EVALUATED','reason':'No confirmed human GUARD annotation supplied'},
            'capture_failures':sum(json.loads(s).get('event_type')=='capture_failure' for s in (session/'diagnostics.jsonl').read_text().splitlines())}
    for name,fs in variants.items():
        result['per_variant'][name]=summarize(fs)
        result['common_support'][name]=summarize([f for f in fs if f['frame_id'] in common])
    if (dataset_path is None)!=(annotation_path is None):raise ValueError('Dataset and annotation required together')
    if dataset_path is not None:
        dataset=json.loads(dataset_path.read_text());doc=json.loads(annotation_path.read_text());validate(doc,dataset)
        checks=[]
        for s in mapped_segments(doc,dataset):
            if not s['key'].startswith(manifest['session_id']+'/') or s['phase'] not in ('GUARD','READY') or s['status']!='CONFIRMED':continue
            expected={f['frame_id'] for f in dataset['trials'][next(i for i,t in enumerate(dataset['trials']) if t['key']==s['key'])]['frames'] if s['start_max_frame_id']<=f['frame_id']<=s['end_min_frame_id']}
            for name,fs in variants.items():
                selected=[f for f in fs if s['start_max_frame_id']<=f['frame_id']<=s['end_min_frame_id']]
                if not expected.issubset({f['frame_id'] for f in selected}):
                    verdict={'status':'REJECTED','reason':'missing mapped GUARD observations'}
                else:
                    engine=DevelopmentEngine(AblationConfig(channel_validity=True))
                    verdict=initialize_confirmed(engine,[PoseObservation.from_record(f,manifest) for f in selected],s)
                checks.append({'key':s['key'],'variant':name,'result':verdict})
        result['guard_initialization']={'status':'CHECKED' if checks else 'NOT_EVALUATED','checks':checks,
            'meaning':'Existing assisted geometry requirements only; not human pose correctness or real-time engine performance'}
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,indent=2,allow_nan=False))
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--session',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--pose',nargs=2,action='append',default=[],metavar=('NAME','FRAMES'));p.add_argument('--dataset',type=Path);p.add_argument('--annotations',type=Path)
    a=p.parse_args();audit(a.session,a.output,a.pose,a.dataset,a.annotations)
