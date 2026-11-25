"""Explicit human-confirmed neutral initialization comparison; never unsupervised accuracy."""
import argparse
from dataclasses import asdict,replace
import json
from pathlib import Path
import numpy as np
from action_engine.ablation import configurations,units
from action_engine.core import ActionEngine,ns
from action_engine.experimental import DevelopmentEngine,ChannelSignals,AblationConfig
from action_engine.types import PoseObservation
from vision_v2.preprocess import sha256
from .schema import mapped_segments,digest
from .evaluate import score,PROTOCOL


def initialize_confirmed(engine,observations,segment):
 if segment['status']!='CONFIRMED' or segment['phase'] not in ('READY','GUARD') or not segment['reviewer'] or not segment['evidence_reference']:raise ValueError('Confirmed human neutral evidence required')
 if not observations:return {'status':'REJECTED','reason':'no observations'}
 now=observations[-1].capture_ns;cfg=engine.config
 if observations[0].capture_ns<segment['start_max_ns'] or now>segment['end_min_ns']:raise ValueError('Outside certain neutral interval')
 if now-observations[0].capture_ns<ns(cfg.neutral_dwell_ms):return {'status':'REJECTED','reason':'insufficient confirmed dwell'}
 if any(b.capture_ns-a.capture_ns>ns(cfg.max_gap_ms) for a,b in zip(observations,observations[1:])):return {'status':'REJECTED','reason':'capture gap'}
 sig=ChannelSignals(AblationConfig(**{**asdict(cfg),'channel_validity':getattr(cfg,'channel_validity',False)}));samples=[]
 for o in observations:samples.append(sig.update(o))
 accepted=[];rejected={}
 with engine.lock:
  for name,unit in units(engine).items():
   sides=('LEFT','RIGHT') if name=='BOTH' else (name,)
   if unit.last_commit is not None:rejected[name]='attack already occurred; no retrospective rearm';continue
   if any(not all(s in a for s in sides) for a in samples[1:]):rejected[name]='invalid required channel';continue
   if len(samples)<2 or not all(s in samples[-1] for s in sides):rejected[name]='missing valid current pair';continue
   baseline={};good=True
   for side in sides:
    values=[a[side] for a in samples[1:]];pos=np.stack([a['position'] for a in values]);center=np.median(pos,axis=0)
    if np.max(np.linalg.norm(pos[:,:2]-center[:2],axis=1))>cfg.neutral_radius or any(a['angle']>cfg.neutral_angle_max_rad for a in values):good=False;break
    baseline[side]={'position':center,'angle':float(np.median([a['angle'] for a in values])),
                    'angle3d':float(np.median([a['angle3d'] for a in values]))}
   if not good:rejected[name]='unstable or not bent neutral geometry';continue
   unit.baseline=baseline;unit.state='READY';unit.side=None;unit.neutral_since=observations[0].capture_ns;unit.entered=now
   if hasattr(unit,'history'):unit.history.clear()
   accepted.append(name)
  engine.control=replace(engine.control,attack_state=engine.attack.state)
 return {'status':'APPLIED' if accepted else 'REJECTED','channels':accepted,'rejected':rejected,'capture_ns':now,
         'reviewer':segment['reviewer'],'evidence_reference':segment['evidence_reference'],'mode':'human_confirmed_neutral'}


def run(directory,annotations,root,output):
 if output.exists() or root.resolve()/'local_data' not in output.resolve().parents:raise ValueError('New local_data output required')
 dataset=json.loads((directory/'dataset.json').read_text());doc=json.loads(annotations.read_text());segments=mapped_segments(doc,dataset)
 hashes=dataset['sources']
 if any(sha256(p)!=h for p,h in hashes.items()):raise ValueError('Source changed')
 configs=configurations(json.loads((root/'local_data/vision_v2/action_engine_20261024/frozen_config.json').read_text()))
 eligible=[s for s in segments if s['status']=='CONFIRMED' and s['phase'] in ('READY','GUARD')]
 output.mkdir(parents=True)
 (output/'protocol.json').write_text(json.dumps({**PROTOCOL,'dataset_sha256':digest(dataset),'annotation_sha256':digest(doc),
  'initialization':'same full input for auto and assisted; inject only at END of confirmed interval, never backdate; geometry safety still required',
  'input_sha256':hashes,'code_sha256':{str(p.resolve()):sha256(p) for p in Path(__file__).parent.glob('*.py')},'configs':{k:asdict(v) for k,v in configs.items()}},indent=2))
 if not eligible:
  (output/'evaluation.json').write_text(json.dumps({'status':'NOT_RUN','reason':'No human-confirmed READY/GUARD intervals; no fabricated assisted result'},indent=2));return
 comparisons=[]
 for trial in dataset['trials']:
  anchors=[s for s in eligible if s['key']==trial['key']]
  if not anchors:continue
  raw=[json.loads(line) for line in (root/'sessions'/trial['session_id']/'frames.jsonl').open()]
  frames=[f for f in raw if trial['frames'][0]['frame_id']<=f['frame_id']<=trial['frames'][-1]['frame_id']]
  manifest=json.loads((root/'sessions'/trial['session_id']/'manifest.json').read_text())
  for name,cfg in configs.items():
   for mode in ('automatic','human_confirmed_neutral'):
    engine=ActionEngine(cfg) if name=='A0' else DevelopmentEngine(cfg);events=[];trace=[];observations=[];applied=set();injections=[]
    for f in frames:
     o=PoseObservation.from_record(f,manifest,epoch=trial['key']);t=o.capture_ns;observations.append(o)
     engine.tick(t);engine.process(o,now_ns=t);events.extend(asdict(e) for e in engine.drain_events(t))
     if mode=='human_confirmed_neutral':
      for i,s in enumerate(anchors):
       if i not in applied and o.frame_id==s['end_min_frame_id']:
        candidates=[v for v in observations if s['start_max_ns']<=v.capture_ns<=t]
        injections.append(initialize_confirmed(engine,candidates,s));applied.add(i)
     trace.append({'capture_ns':t,'frame_id':o.frame_id,'state':asdict(engine.control),'logs':engine.take_logs()})
    result=score(trial,[s for s in segments if s['key']==trial['key']],events,trace)
    comparisons.append({'key':trial['key'],'variant':name,'mode':mode,'events':events,'trace':trace,'injections':injections,'evaluation':result})
 engine_hashes_changed=[p for p,h in hashes.items() if sha256(p)!=h]
 if engine_hashes_changed:raise ValueError('Sources changed during replay')
 (output/'evaluation.json').write_text(json.dumps({'scope':'development; assisted performance is not unsupervised performance','comparisons':comparisons},indent=2))

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',type=Path,required=True);p.add_argument('--annotations',type=Path,required=True);p.add_argument('--root',type=Path,default=Path('.'));p.add_argument('--output',type=Path,required=True);a=p.parse_args();run(a.directory,a.annotations,a.root,a.output)
