"""Frame-linked human review contract. No model output is ground truth."""
import hashlib
import json

PHASES={
 'ARM_LOWER':('ARM_MOTION','action'),'ARM_RAISE':('ARM_MOTION','action'),
 'READY':('POSTURE','action'),'GUARD':('POSTURE','action'),'NONE':('NONE','action'),
 'ATTACK_ONSET':('ATTACK','action'),'ATTACK_EXTENDING':('ATTACK','action'),
 'ATTACK_HOLD':('ATTACK','action'),'ATTACK_RETURN':('ATTACK','action'),
 'BLOCK_ONSET':('BLOCK','action'),'BLOCK_HELD':('BLOCK','action'),'BLOCK_RELEASE':('BLOCK','action'),
 'UNKNOWN':('UNKNOWN','action'),'UNCERTAIN':('UNKNOWN','action'),
 'MOVE_LEFT':('MOVEMENT','movement'),'MOVE_RIGHT':('MOVEMENT','movement'),'MOVE_NEUTRAL':('MOVEMENT','movement')}


def digest(data):return hashlib.sha256(json.dumps(data,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def template(dataset):
 return {'schema_version':1,'dataset_sha256':digest(dataset),'source':'HUMAN_VIDEO_REVIEW','trials':[
  {'key':t['key'],'review_status':'NOT_REVIEWED','segments':[],'unobserved_phases':[],'notes':''} for t in dataset['trials']]}


def validate(document,dataset):
 if document.get('schema_version')!=1 or document.get('source')!='HUMAN_VIDEO_REVIEW' or document.get('dataset_sha256')!=digest(dataset):raise ValueError('Wrong schema/source/dataset SHA')
 targets={t['key']:t for t in dataset['trials']};seen=set()
 for trial in document['trials']:
  key=trial['key']
  if key not in targets or key in seen:raise ValueError('Unknown or duplicate Trial')
  seen.add(key);frames=targets[key]['frames'];byid={f['frame_id']:i for i,f in enumerate(frames)}
  if trial['review_status'] not in ('NOT_REVIEWED','PARTIAL','COMPLETE'):raise ValueError('Review status')
  if trial['review_status']=='NOT_REVIEWED' and trial['segments']:raise ValueError('Unreviewed Trial cannot contain asserted annotations')
  occupied={'action':set(),'movement':set()};events={};onsets=set()
  for s in trial['segments']:
   phase=s['phase']
   if phase not in PHASES:raise ValueError('Unknown phase')
   family,channel=PHASES[phase]
   if s['family']!=family or s['channel']!=channel:raise ValueError('Phase/family/channel mismatch')
   if s['status'] not in ('CONFIRMED','UNCERTAIN') or not s['reviewer'].strip() or not s['evidence_reference'].strip():raise ValueError('Human reviewer and evidence reference required')
   if phase in ('UNKNOWN','UNCERTAIN') and s['status']!='UNCERTAIN':raise ValueError('Unknown must be uncertain')
   if s['side'] not in ('LEFT','RIGHT','BOTH','NA'):raise ValueError('Invalid side')
   if family in ('ATTACK','ARM_MOTION') and s['side'] not in ('LEFT','RIGHT'):raise ValueError('Attack/arm motion needs anatomical LEFT or RIGHT')
   if family in ('ATTACK','BLOCK') and not s['event_id'].strip():raise ValueError('Event ID required')
   if s['event_id']:
    identity=(family,s['side'])
    if s['event_id'] in events and events[s['event_id']]!=identity:raise ValueError('Inconsistent event family/side')
    events[s['event_id']]=identity
   if phase.endswith('_ONSET'):
    if s['event_id'] in onsets:raise ValueError('Duplicate onset for one event')
    onsets.add(s['event_id'])
   ids=[s[k] for k in ('start_frame_id','end_frame_id','start_min_frame_id','start_max_frame_id','end_min_frame_id','end_max_frame_id')]
   if any(type(i) is not int or i not in byid for i in ids):raise ValueError('Frame not mapped to original video')
   a,b,amin,amax,bmin,bmax=[byid[i] for i in ids]
   if not amin<=a<=amax or not bmin<=b<=bmax or a>b or amin>bmax:raise ValueError('Invalid interval/uncertainty')
   span=set(range(amin,bmax+1))
   if occupied[channel]&span:raise ValueError('Overlapping annotations/uncertainty in one channel')
   occupied[channel]|=span
   # UI may send only frame IDs. Stored time/index references are computed from
   # authoritative mapping, never trusted from client or replay FPS.
  order={'ATTACK_ONSET':0,'ATTACK_EXTENDING':1,'ATTACK_HOLD':2,'ATTACK_RETURN':3,'BLOCK_ONSET':0,'BLOCK_HELD':1,'BLOCK_RELEASE':2}
  for event_id,(family,_) in events.items():
   if family not in ('ATTACK','BLOCK'):continue
   phases=sorted([s for s in trial['segments'] if s['event_id']==event_id],key=lambda s:byid[s['start_frame_id']])
   ranks=[order[s['phase']] for s in phases]
   if ranks!=sorted(ranks):raise ValueError('Event phases out of temporal order')
  if any(p not in PHASES for p in trial.get('unobserved_phases',[])):raise ValueError('Unknown unobserved phase')
  if set(trial.get('unobserved_phases',[])) & {s['phase'] for s in trial['segments']}:raise ValueError('Phase cannot be both annotated and unobserved')
 if seen!=set(targets):raise ValueError('Missing Trial records')
 return True


def endpoints(segment,trial):
 byid={f['frame_id']:i for i,f in enumerate(trial['frames'])};frames=trial['frames']
 def t(key,next_frame=False):
  i=byid[segment[key]];i=min(i+int(next_frame),len(frames)-1)
  return int(frames[i]['capture_return_monotonic_ns'])
 return {'start_min_ns':t('start_min_frame_id'),'start_max_ns':t('start_max_frame_id'),
         'end_min_ns':t('end_min_frame_id',True),'end_max_ns':t('end_max_frame_id',True)}


def mapped_segments(document,dataset):
 validate(document,dataset);trials={t['key']:t for t in dataset['trials']};out=[]
 for t in document['trials']:
  for s in t['segments']:
   frames={f['frame_id']:f for f in trials[t['key']]['frames']}
   out.append({**s,'key':t['key'],**endpoints(s,trials[t['key']]),
     'start_reference':frames[s['start_frame_id']],'end_reference':frames[s['end_frame_id']]})
 return out
