"""Time-grounded evaluation restricted to explicit confirmed human coverage."""
import argparse
import json
from pathlib import Path
from .schema import mapped_segments,digest
from vision_v2.preprocess import sha256

NS=1_000_000_000
PROTOCOL={'version':'event-eval-1.1','late_threshold_ms':300,'maximum_match_delay_ms':1000,
 'state_hold_limit_ms':150,'scope':'P01 development','matching':'same side, chronological one-to-one; window capped at next same-side onset',
 'uncertainty':'latency interval; boundary-ambiguous detection separate; state scoring uses certain interval core only',
 'accuracy':'not defined; event metrics and duration-based state metrics are distinct'}

def overlap(a,b):return max(0,min(a[1],b[1])-max(a[0],b[0]))
def inside(t,intervals):return any(a<=t<b for a,b in intervals)
def merge(intervals):
 out=[]
 for a,b in sorted(intervals):
  if b<=a:continue
  if out and a<=out[-1][1]:out[-1]=(out[-1][0],max(b,out[-1][1]))
  else:out.append((a,b))
 return out

def score(trial,segments,predictions,trace,protocol=PROTOCOL):
 rows=sorted(trace,key=lambda r:r['capture_ns'])
 if not rows: return {'status':'NOT_EVALUATED','reason':'no engine coverage','attack':None,'block':None,'movement':None}
 lo,hi=rows[0]['capture_ns'],rows[-1]['capture_ns']
 confirmed=[s for s in segments if s['status']=='CONFIRMED' and s['family']!='UNKNOWN']
 frames=trial['frames'];edges=[(int(a['capture_return_monotonic_ns']),int(b['capture_return_monotonic_ns'])) for a,b in zip(frames,frames[1:]) if b['frame_id']==a['frame_id']+1 and 0<int(b['capture_return_monotonic_ns'])-int(a['capture_return_monotonic_ns'])<=protocol['state_hold_limit_ms']*1_000_000]
 # Only video-supported continuous edges, certain annotation cores, and engine range.
 spans=[]
 for s in confirmed:
  for a,b in edges:
   aa=max(a,lo,s['start_max_ns']);bb=min(b,hi,s['end_min_ns'])
   if aa<bb:spans.append((aa,bb,s))
 action_intervals=merge([(a,b) for a,b,s in spans if s['channel']=='action'])
 movement_intervals=merge([(a,b) for a,b,s in spans if s['channel']=='movement'])
 if not action_intervals and not movement_intervals:
  return {'status':'NOT_EVALUATED','reason':'no confirmed human time coverage','attack':None,'block':None,'movement':None,'accuracy':None}
 onset_ranges=[(max(lo,s['start_min_ns']),min(hi,s['start_max_ns'])+1) for s in confirmed if s['phase']=='ATTACK_ONSET' and lo<=s['start_min_ns']<=s['start_max_ns']<=hi]
 event_coverage=merge(action_intervals+onset_ranges)
 events=sorted([e for e in predictions if inside(e['committed_ns'],event_coverage)],key=lambda e:e['committed_ns'])
 truth=sorted([s for s in confirmed if s['phase']=='ATTACK_ONSET' and lo<=s['start_min_ns']<=s['start_max_ns']<=hi],key=lambda s:s['start_min_ns'])
 nonattack=merge([(a,b) for a,b,s in spans if s['channel']=='action' and s['family']!='ATTACK'])
 used=set();matches=[];duplicates=[];late_ns=protocol['late_threshold_ms']*1_000_000;max_ns=protocol['maximum_match_delay_ms']*1_000_000
 for n,s in enumerate(truth):
  next_start=min([v['start_min_ns'] for v in truth[n+1:] if v['side']==s['side']],default=hi+1)
  end=min(s['start_max_ns']+max_ns,next_start-1,hi)
  candidates=[i for i,e in enumerate(events) if i not in used and not inside(e['committed_ns'],nonattack) and e['side']==s['side'] and s['start_min_ns']<=e['committed_ns']<=end]
  match={'event_id':s['event_id'],'side':s['side'],'onset_range_ns':[s['start_min_ns'],s['start_max_ns']]}
  if not candidates:match.update(status='RIGHT_CENSORED' if hi<s['start_max_ns']+max_ns and next_start>hi else 'MISSED',latency_ms=None)
  else:
   i=candidates[0];used.add(i);e=events[i];lat=[(e['committed_ns']-s['start_max_ns'])/1e6,(e['committed_ns']-s['start_min_ns'])/1e6]
   status='BOUNDARY_UNCERTAIN' if lat[0]<0<=lat[1] else ('LATE' if lat[0]>late_ns/1e6 else ('ON_TIME' if lat[1]<=late_ns/1e6 else 'BOUNDARY_UNCERTAIN'))
   match.update(status=status,prediction_id=e['event_id'],engine_ns=e['committed_ns'],latency_ms=lat)
   event_spans=[(a,b) for a,b,phase in spans if phase['event_id']==s['event_id'] and phase['family']=='ATTACK']
   duplicate_indices=[j for j,p in enumerate(events) if j not in used and p['side']==s['side'] and e['committed_ns']<=p['committed_ns']<next_start and inside(p['committed_ns'],event_spans)]
   for j in duplicate_indices:used.add(j);duplicates.append(events[j]['event_id'])
  matches.append(match)
 nonattack=merge([(a,b) for a,b,s in spans if s['channel']=='action' and s['family']!='ATTACK'])
 false_events=[e['event_id'] for e in events if inside(e['committed_ns'],nonattack)]
 wrong_side=[e['event_id'] for i,e in enumerate(events) if i not in used and any(s['side']!=e['side'] and s['start_min_ns']<=e['committed_ns']<=s['start_max_ns']+max_ns for s in truth)]
 by_phase={}
 for phase in sorted({s['phase'] for _,_,s in spans if s['channel']=='action' and s['family']!='ATTACK'}):
  details={}
  for side in sorted({s['side'] for _,_,s in spans if s['phase']==phase}):
   intervals=merge([(a,b) for a,b,s in spans if s['phase']==phase and s['side']==side])
   ids=[e['event_id'] for e in events if inside(e['committed_ns'],intervals)]
   details[side]={'prediction_ids':ids,'count':len(ids),'coverage_seconds':sum(b-a for a,b in intervals)/NS}
  by_phase[phase]=details
 counts={k:sum(m['status']==k for m in matches) for k in ('ON_TIME','LATE','MISSED','BOUNDARY_UNCERTAIN','RIGHT_CENSORED')}
 attack={'ground_truth_events':len(truth),'outcomes':counts,'matches':matches,'duplicate_prediction_ids':duplicates,
         'nonattack_prediction_ids':false_events,'nonattack_by_phase_and_side':by_phase,'wrong_side_prediction_ids':wrong_side,'unmatched_prediction_ids':[e['event_id'] for i,e in enumerate(events) if i not in used],
         'nonattack_coverage_seconds':sum(b-a for a,b in nonattack)/NS,
         'detected_fraction_in_match_window':(len(truth)-counts['MISSED']-counts['RIGHT_CENSORED'])/(len(truth)-counts['RIGHT_CENSORED']) if len(truth)>counts['RIGHT_CENSORED'] else None,
         'on_time_recall':counts['ON_TIME']/(len(truth)-counts['RIGHT_CENSORED']) if len(truth)>counts['RIGHT_CENSORED'] else None,
         'unannotated_predictions':len(predictions)-len(events)} if action_intervals else None
 # Piecewise engine states are bounded by next capture and configured safety TTL.
 states=[(a['capture_ns'],min(b['capture_ns'],a['capture_ns']+protocol['state_hold_limit_ms']*1_000_000),a['state']) for a,b in zip(rows,rows[1:])]
 block_counts={'tp_ns':0,'fp_ns':0,'fn_ns':0,'tn_ns':0};movement={};block_edges=[]
 for a,b,s in spans:
  for c,d,state in states:
   duration=overlap((a,b),(c,d))
   if not duration:continue
   if s['channel']=='action':
    actual=s['phase'] in ('BLOCK_ONSET','BLOCK_HELD');pred=bool(state['block'])
    key='tp_ns' if actual and pred else 'fn_ns' if actual else 'fp_ns' if pred else 'tn_ns'
    block_counts[key]+=duration
   else:
    actual=s['phase'].removeprefix('MOVE_');key=actual+'->'+state['movement'];movement[key]=movement.get(key,0)+duration
 for s in confirmed:
  if s['phase'] not in ('BLOCK_ONSET','BLOCK_RELEASE') or not lo<=s['start_min_ns']<=s['start_max_ns']<=hi:continue
  target=s['phase']=='BLOCK_ONSET';trans=[]
  for prev,row in zip(rows,rows[1:]):
   if row['capture_ns']-prev['capture_ns']<=protocol['state_hold_limit_ms']*1_000_000 and bool(prev['state']['block'])!=target and bool(row['state']['block'])==target and s['start_min_ns']<=row['capture_ns']<=s['start_max_ns']+max_ns:trans.append(row['capture_ns'])
  before=[r for r in rows if r['capture_ns']<=s['start_min_ns']]
  already=bool(before and before[-1]['state']['block']==target)
  block_edges.append({'event_id':s['event_id'],'phase':s['phase'],'status':'DETECTED' if trans else 'ALREADY_TARGET_OR_LEFT_CENSORED' if already else 'RIGHT_CENSORED' if hi<s['start_max_ns']+max_ns else 'MISSING_WITHIN_WINDOW',
                      'latency_ms':[(trans[0]-s['start_max_ns'])/1e6,(trans[0]-s['start_min_ns'])/1e6] if trans else None})
 tp,fp,fn,tn=[block_counts[k] for k in ('tp_ns','fp_ns','fn_ns','tn_ns')]
 block={**{k.replace('_ns','_seconds'):v/NS for k,v in block_counts.items()},'iou':tp/(tp+fp+fn) if tp+fp+fn else None,
        'precision':tp/(tp+fp) if tp+fp else None,'recall':tp/(tp+fn) if tp+fn else None,'entry_release':block_edges} if action_intervals else None
 return {'status':'EVALUATED_CONFIRMED_COVERAGE_ONLY','accuracy':None,'attack':attack,'block':block,
         'movement':{'confusion_seconds':{k:v/NS for k,v in movement.items()},'agreement':sum(v for k,v in movement.items() if k.split('->')[0]==k.split('->')[1])/sum(movement.values()) if movement else None} if movement_intervals else None,
         'action_coverage_seconds':sum(b-a for a,b in action_intervals)/NS,'movement_coverage_seconds':sum(b-a for a,b in movement_intervals)/NS}


def evaluate(directory,annotations,run,output):
 if output.exists():raise ValueError('New output required')
 dataset=json.loads((directory/'dataset.json').read_text());doc=json.loads(annotations.read_text());segments=mapped_segments(doc,dataset)
 original=json.loads((run/'protocol.json').read_text())
 for path,h in dataset['sources'].items():
  if original['input_sha256'].get(path)!=h or sha256(path)!=h:raise ValueError('Replay/dataset source mismatch')
 outputs=[]
 for context in ('recording_only','countdown_plus_recording'):
  for name in ('A0','A1','A2','A3','A4'):
   loc=run/name/context;results=json.loads((loc/'results.json').read_text());preds={r['session_id']+'/'+r['trial_id']:r['events']+r.get('warmup_events',[]) for r in results['trials']}
   traces={t['key']:[] for t in dataset['trials']}
   for line in (loc/'trace.jsonl').open():
    row=json.loads(line);key=row['session_id']+'/'+row['trial_id']
    if key in traces:traces[key].append(row)
   values={t['key']:score(t,[s for s in segments if s['key']==t['key']],preds[t['key']],traces[t['key']]) for t in dataset['trials']}
   outputs.append({'variant':name,'context':context,'neutral_initialization':'automatic','trials':values})
 output.mkdir(parents=True)
 (output/'protocol.json').write_text(json.dumps({**PROTOCOL,'dataset_sha256':digest(dataset),'annotation_sha256':digest(doc),'source_run':str(run.resolve()),'input_sha256':dataset['sources'],'code_sha256':{str(p.resolve()):sha256(p) for p in Path(__file__).parent.glob('*.py')},'replay_sha256':{str(p.resolve()):sha256(p) for p in run.rglob('*') if p.is_file() and p.name in ('protocol.json','results.json','trace.jsonl')}},indent=2))
 (output/'evaluation.json').write_text(json.dumps({'scope':'development only','comparisons':outputs,'human_initialized_evaluation':{'status':'NOT_RUN','reason':'Requires confirmed READY/GUARD interval and explicit assisted replay; automatic replay cannot be relabeled as assisted'}},indent=2))
 print(json.dumps({'comparisons':len(outputs),'evaluated_trial_comparisons':sum(v['status'].startswith('EVALUATED') for o in outputs for v in o['trials'].values())}))

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',type=Path,required=True);p.add_argument('--annotations',type=Path,required=True);p.add_argument('--run',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();evaluate(a.directory,a.annotations,a.run,a.output)
