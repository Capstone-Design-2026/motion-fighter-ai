"""Reproducible development ablations; no training, threshold search or phase truth inference."""
import argparse
from collections import Counter, defaultdict
from dataclasses import asdict, replace
import json
from pathlib import Path
import numpy as np
from vision_v2.preprocess import load_trials, selection_rows, sha256
from replay_session import iter_frames
from .types import EngineConfig, PoseObservation
from .core import ActionEngine, ns
from .experimental import AblationConfig, DevelopmentEngine, ChannelSignals, joint_validity, elbow_angle


def write(path,data):path.write_text(json.dumps(data,indent=2,ensure_ascii=False,allow_nan=False))


def configurations(baseline):
    base=asdict(EngineConfig(**baseline))
    out={'A0':EngineConfig(**base)}
    for name,flags in {'A1':{'channel_validity':True},'A2':{'calibrated_neutral_block':True},
                       'A3':{'image_3d_extension':True},'A4':{'channel_validity':True,'calibrated_neutral_block':True,'image_3d_extension':True}}.items():
        out[name]=AblationConfig(**{**base,'version':f'event-engine-0.2-{name}-development',**flags})
    return out


def units(engine):return engine.attack.units if isinstance(engine,DevelopmentEngine) else {'BOTH':engine.attack}


def blockers(engine,obs,probe,validity):
    """Non-mutating actual eligibility ladder + overlapping descriptive flags.

    Downstream failures count only when reached. Not an additive causal attribution.
    """
    output={};cfg=engine.config
    for side in ('LEFT','RIGHT'):
        unit=units(engine).get(side,units(engine).get('BOTH'))
        flags={'global_six_joint_invalid':bool(validity['BLOCK']), 'own_channel_invalid':bool(validity[side]),
               'opposite_only_invalid':not validity[side] and bool(validity['BLOCK']),
               'baseline_absent':unit.baseline is None,
               'trial_start_without_neutral':engine.last_capture is None and unit.baseline is None,
               'capture_gap':engine.last_capture is not None and obs.capture_ns-engine.last_capture>ns(cfg.max_gap_ms)}
        gate=validity[side] if getattr(cfg,'channel_validity',False) else validity['BLOCK']
        primary=None
        if gate:primary='own_channel_invalid' if validity[side] else 'opposite_only_invalid'
        elif flags['capture_gap']:primary='capture_gap_reset'
        elif engine.signals.previous is None or side not in probe:primary='derivative_pair_unavailable'
        else:
            selected={side:probe[side]} if getattr(cfg,'channel_validity',False) else probe
            if set(selected)!=({side} if getattr(cfg,'channel_validity',False) else {'LEFT','RIGHT'}):primary='hidden_opposite_mask'
            else:
                candidate=set(probe)=={'LEFT','RIGHT'} and all(a['position'][1]<=cfg.block_wrist_y_max and a['angle']<cfg.neutral_angle_max_rad for a in probe.values())
                if getattr(cfg,'calibrated_neutral_block',False):
                    anchors=engine.attack.anchors()
                    candidate=set(probe)==set(anchors)=={'LEFT','RIGHT'} and all(anchors[s]['position'][1]-a['position'][1]>=cfg.commit_displacement and a['position'][1]<=cfg.block_wrist_y_max and a['angle']<cfg.neutral_angle_max_rad for s,a in probe.items())
                state=unit.state
                if hasattr(unit,'history'):
                    previous_history=unit.history.copy()
                    while unit.history and obs.capture_ns-unit.history[0][0]>ns(cfg.neutral_dwell_ms):unit.history.popleft()
                    unit.history.append((obs.capture_ns,{k:a['position'].copy() for k,a in selected.items()}))
                    neutral=unit.neutral(selected)
                    unit.history=previous_history
                else:neutral=unit.neutral(selected)
                signals=unit.candidates(selected)[side]
                flags['angle_2d_below_commit_observed']=probe[side]['angle']<cfg.commit_angle_rad
                if state in ('PREPARING','UNAVAILABLE','RETURNING'):
                    if candidate or engine.block.held:primary='block_prevents_ready'
                    elif not neutral:primary='neutral_pose_or_stability_failed'
                    elif unit.neutral_since is not None and obs.capture_ns-unit.neutral_since>=ns(cfg.neutral_dwell_ms) and (unit.last_commit is None or obs.capture_ns-unit.last_commit>=ns(cfg.cooldown_ms)):primary='ready_available'
                    else:primary='neutral_dwell_or_cooldown_pending'
                elif state=='READY':
                    primary='block_prevents_onset' if candidate or engine.block.held else ('onset_insufficient' if not signals['onset_rule'] else 'onset_available')
                    for key in ('displacement_signal','speed_signal','direction_signal','extension_signal'):flags['onset_missing_'+key]=not signals[key]
                elif state=='CANDIDATE':
                    flags['angle_2d_below_at_commit_stage']=probe[side]['angle']<cfg.commit_angle_rad
                    if candidate or engine.block.held or neutral or obs.capture_ns-unit.entered>ns(cfg.candidate_timeout_ms):primary='candidate_cancel_condition'
                    elif not signals['commit_rule']:
                        primary='commit_insufficient'
                        flags['commit_displacement_insufficient']=signals['xy_displacement']<cfg.commit_displacement
                        flags['commit_direction_insufficient']=not signals['direction_signal']
                        flags['commit_extension_change_insufficient']=not signals['extension_signal']
                        angle=probe[side]['angle3d'] if getattr(cfg,'image_3d_extension',False) else probe[side]['angle']
                        flags['commit_angle_insufficient']=angle is None or angle<cfg.commit_angle_rad
                    else:primary='commit_available'
                else:primary='latched_until_return'
        flags[primary]=True
        output[side]={'state_before':unit.state,'primary':primary,'flags':flags}
    return output


def validate_manual_annotations(data,trial_bounds):
    for row in data['annotations']:
        key=(row['session_id'],row['trial_id'])
        if key not in trial_bounds:raise ValueError('Unknown annotation Trial')
        if row['status']=='NOT_REVIEWED':
            if any(row.get(k) is not None for k in ('onset_capture_ns','peak_capture_ns','return_capture_ns')):raise ValueError('Unreviewed phases must be null')
            continue
        if row['status'] not in ('CONFIRMED','UNCERTAIN') or not row.get('reviewer') or not row.get('evidence_reference'):raise ValueError('Human reviewer and evidence required')
        values=[row.get(k) for k in ('onset_capture_ns','peak_capture_ns','return_capture_ns')]
        values=[v for v in values if v is not None];lo,hi=trial_bounds[key]
        if any(type(v) is not int or not lo<=v<=hi for v in values) or values!=sorted(values):raise ValueError('Invalid phase timestamps')


def run(root,output):
    root=root.resolve();output=output.resolve()
    if output.exists() or root/'local_data' not in output.parents:raise ValueError('New local_data output required')
    baseline=root/'local_data/vision_v2/action_engine_20261024'
    configs=configurations(json.loads((baseline/'frozen_config.json').read_text()))
    rows=[r for r in selection_rows(root/'local_data/vision_v2/selection_20261021') if r['subject']=='P01' and r['status']=='USE']
    input_paths=list((root/'local_data/vision_v2/selection_20261021').glob('*'))+[baseline/'frozen_config.json',baseline/'validated_coordinates/results.json']
    for sid in {r['session'] for r in rows}:input_paths += [p for p in (root/'sessions'/sid).iterdir() if p.is_file()]
    hashes={str(p):sha256(p) for p in input_paths if p.is_file()}
    code={str(p.resolve()):sha256(p) for p in Path(__file__).parent.glob('*.py')}
    code.update({str(root/p):sha256(root/p) for p in ('src/vision_v2/preprocess.py','src/movement.py','src/replay_session.py')})
    output.mkdir(parents=True)
    # Locked before outputs: isolated A1/A2/A3 vs A0, A4 combination, both contexts.
    write(output/'protocol.json',{'scope':'P01 development only, NOT independent generalization evaluation','configs':{k:asdict(v) for k,v in configs.items()},
          'comparisons':'A1/A2/A3 each change one axis from A0; A4 combines all. No threshold optimization.',
          'contexts':['recording_only','countdown_plus_recording'],'input_sha256':hashes,'code_sha256':code,
          'phase_truth':None,'world_geometry':'descriptive audit only; no physical ground truth',
          'neutral_policy':'No COUNTDOWN truth assumption. No forced READY; automatic geometric stability is only a neutral hypothesis.'})
    for name,cfg in configs.items():
        (output/name).mkdir();write(output/name/'config.json',asdict(cfg))
        write(output/name/'protocol.json',{'config_version':cfg.version,'input_sha256':hashes,'code_sha256':code,'scope':'development','delta':{k:v for k,v in asdict(cfg).items() if v!=asdict(configs['A0']).get(k)},'accuracy':None})
    sessions={}
    for sid in {r['session'] for r in rows}:
        directory=root/'sessions'/sid
        sessions[sid]=([json.loads(line) for line in (directory/'annotations.jsonl').read_text().splitlines()],list(iter_frames(directory,allow_truncated_tail=False)))
    prepared=[];manual=[];geometry=[]
    for row,manifest,ann,trial,frames in load_trials(root/'sessions',rows):
        annotations,allframes=sessions[row['session']]
        countdown=[a['event_frame_id'] for a in annotations if a.get('notes',{}).get('trial_id')==row['trial'] and a.get('notes',{}).get('event')=='countdown_start']
        start=max([i for i in countdown if i is not None and i<=ann['start_frame_id']],default=ann['start_frame_id'])
        combined=[f for f in allframes if start<=f['frame_id']<=ann['end_frame_id']]
        prepared.append((row,manifest,frames,combined))
        video=root/'sessions'/row['session']/manifest.get('video',{}).get('path','__missing__')
        sidecar=video.parent/'video_frames.jsonl'
        manual.append({'subject_id':row['subject'],'session_id':row['session'],'trial_id':row['trial'],'original_label':row['label'],
                       'status':'NOT_REVIEWED','reviewer':'','evidence_reference':'','onset_capture_ns':None,'peak_capture_ns':None,'return_capture_ns':None,
                       'video_path':str(video) if video.is_file() else None,'video_frame_mapping':str(sidecar) if sidecar.is_file() else None,
                       'min_capture_ns':combined[0]['capture_return_monotonic_ns'],'max_capture_ns':combined[-1]['capture_return_monotonic_ns']})
        signal=ChannelSignals(replace(configs['A1'],channel_validity=True));samples=defaultdict(list)
        for f in frames:
            obs=PoseObservation.from_record(f,manifest);arms=signal.update(obs)
            for side,a in arms.items():
                ids=(11,13,15) if side=='LEFT' else (12,14,16)
                w=f.get('world_landmarks');wv=(f.get('landmark_visibility') or {}).get('world')
                world=elbow_angle(w,ids) if w is not None and wv is not None and all(np.isfinite(wv[j]) and wv[j]>=configs['A0'].visibility for j in ids) else None
                samples[side].append({'capture_ns':obs.capture_ns,'xy':a['position'][:2].tolist(),'z':float(a['position'][2]),'xy_speed':a['speed_xy'],
                                      'z_rate':float(a['velocity'][2]),'angle2d':a['angle'],'angle3d':a['angle3d'],'world_angle':world})
        for side,values in samples.items():
            origin=np.array(values[0]['xy']);za=values[0]['z'];a2=values[0]['angle2d'];a3=values[0]['angle3d']
            result={'session_id':row['session'],'trial_id':row['trial'],'label':row['label'],'side':side,'samples':len(values),'reference':'first own-channel-valid pair; NOT neutral truth'}
            for key in ('xy_speed','z_rate','angle2d','angle3d','world_angle'):
                vals=[v[key] for v in values if v[key] is not None];result[key+'_p95']=float(np.percentile(vals,95)) if vals else None
            result['xy_displacement_p95']=float(np.percentile([np.linalg.norm(np.array(v['xy'])-origin) for v in values],95))
            result['image_z_delta_p05']=float(np.percentile([v['z']-za for v in values],5))
            for key,base in [('angle2d',a2),('angle3d',a3)]:result[key+'_extension_p95']=float(np.percentile([v[key]-base for v in values if v[key] is not None],95)) if base is not None else None
            rates=[v['z_rate'] for v in values];result['z_rate_sign_reversals']=sum(x*y<0 for x,y in zip(rates,rates[1:]));result['raw_samples']=values
            geometry.append(result)
    write(output/'manual_onset_annotations.json',{'timebase':'original capture monotonic ns; use video_frames.jsonl, not nominal video FPS','annotations':manual})
    write(output/'geometry.json',geometry)
    allresults=[]
    for context in ('recording_only','countdown_plus_recording'):
        for name,cfg in configs.items():
            directory=output/name/context;directory.mkdir()
            results=[]
            with (directory/'trace.jsonl').open('x') as trace:
                for row,manifest,recording,combined in prepared:
                    frames=recording if context=='recording_only' else combined
                    engine=ActionEngine(cfg) if name=='A0' else DevelopmentEngine(cfg)
                    probe=ChannelSignals(replace(configs['A1'],channel_validity=True))
                    counts={s:Counter() for s in ('LEFT','RIGHT')};first={s:{} for s in counts};primary={s:Counter() for s in counts}
                    ev=[];warm_ev=[];ready=set();candidates=Counter();block_ns=0;prev_t=None;prev_block=False;invalid=Counter();resets=Counter();transitions=[];unexpected=[];neutral_at_start={};first_ready={}
                    for f in frames:
                        obs=PoseObservation.from_record(f,manifest,epoch=row['session']+'/'+row['trial']);t=obs.capture_ns
                        inrecord=f['frame_id']>=recording[0]['frame_id'];validity=joint_validity(obs,cfg);arms=probe.update(obs)
                        diag=blockers(engine,obs,arms,validity)
                        if f['frame_id']==recording[0]['frame_id']:
                            neutral_at_start={k:{'baseline_acquired':u.baseline is not None,'state':u.state} for k,u in units(engine).items()}
                        if inrecord:
                            for side,r in diag.items():
                                primary[side][r['primary']]+=1
                                for reason,active in r['flags'].items():
                                    if active:
                                        counts[side][reason]+=1;first[side].setdefault(reason,{'frame_id':obs.frame_id,'capture_ns':t})
                            for channel,causes in validity.items():
                                for cause in causes:invalid[channel+':'+cause]+=1
                            if prev_t is not None and prev_block:block_ns+=min(t-prev_t,ns(cfg.safety_timeout_ms))
                        engine.tick(t);engine.process(obs,now_ns=t)
                        emitted=engine.drain_events(t);(ev if inrecord else warm_ev).extend(asdict(e) for e in emitted)
                        logs=engine.take_logs()
                        for log in logs:
                            if inrecord and log['type']=='safety':resets[log['reason']]+=1
                            if inrecord and log['type']=='channel_invalid':resets['channel_invalid:'+log['channel']]+=1
                            if inrecord and log['type']=='observation' and not log.get('valid',True):resets['global_invalid']+=1
                            if inrecord and log['type']=='transition':
                                transitions.append(log)
                                before=log['before'];after=log['after']
                                if isinstance(after,(list,tuple)):before,after=before[0],after[0]
                                if after=='CANDIDATE':candidates[log.get('channel','BOTH')]+=1
                                permitted={'UNAVAILABLE':{'PREPARING','READY'},'PREPARING':{'READY'},'READY':{'CANDIDATE'},'CANDIDATE':{'PREPARING','ACTIVE'},'ACTIVE':{'RETURNING','EXTENDED','UNAVAILABLE'},'EXTENDED':{'RETURNING','UNAVAILABLE'},'RETURNING':{'READY'}}
                                if after!=before and after not in permitted.get(before,set()):unexpected.append(log)
                        if inrecord:
                            ready.update(k for k,u in units(engine).items() if u.state=='READY')
                            for k,u in units(engine).items():
                                if u.state=='READY':first_ready.setdefault(k,{'frame_id':obs.frame_id,'capture_ns':t})
                        trace.write(json.dumps({'session_id':row['session'],'trial_id':row['trial'],'label':row['label'],'frame_id':obs.frame_id,'capture_ns':t,
                                               'recording':inrecord,'diagnostic':diag,'state':asdict(engine.control),'logs':logs},allow_nan=False)+'\n')
                        prev_t=t;prev_block=engine.control.block
                    engine.tick(t+ns(cfg.safety_timeout_ms))
                    assert not engine.control.block and engine.control.movement=='NEUTRAL'
                    results.append({'subject_id':row['subject'],'session_id':row['session'],'trial_id':row['trial'],'label':row['label'],'recording_frames':len(recording),
                                    'warmup_frames':len(frames)-len(recording),'recording_start_capture_ns':recording[0]['capture_return_monotonic_ns'],'recording_end_capture_ns':recording[-1]['capture_return_monotonic_ns'],
                                    'neutral_at_recording_start':neutral_at_start,'first_ready':first_ready,'event_count':len(ev),'events':ev,'warmup_events':warm_ev,'block_held_seconds':block_ns/1e9,
                                    'ready_channels':sorted(ready),'candidate_entries':dict(candidates),'blockers':{s:dict(c) for s,c in counts.items()},'first_blockers':first,
                                    'primary_blockers':{s:dict(c) for s,c in primary.items()},'invalid_reasons':dict(invalid),'resets':dict(resets),
                                    'transitions':transitions,'unexpected_transitions':unexpected,'safety_after_end':asdict(engine.control)})
            result={'variant':name,'context':context,'scope':'P01 development','trials':results,'accuracy':None,'onset_latency':None}
            write(directory/'results.json',result);allresults.append(result)
            print(name,context,'events',sum(r['event_count'] for r in results),'ready',sum(bool(r['ready_channels']) for r in results),flush=True)
    old=json.loads((baseline/'validated_coordinates/results.json').read_text())['trial_results']
    a0=allresults[0]['trials'];oldmap={(r['session_id'],r['trial_id']):r for r in old}
    reproduced=all(r['event_count']==oldmap[(r['session_id'],r['trial_id'])]['event_count'] for r in a0)
    changed=[p for p,h in hashes.items() if sha256(p)!=h]
    write(output/'verification.json',{'input_files':len(hashes),'changed':changed,'A0_event_counts_reproduced':reproduced})
    if changed or not reproduced:raise ValueError('Integrity or baseline mismatch')
    write(output/'comparison.json',allresults)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path('.'));p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();run(a.root,a.output)
