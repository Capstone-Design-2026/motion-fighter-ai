"""Explicit offline training CLI; requires reviewed dataset manifest and scikit-learn."""
from pathlib import Path
import argparse,json,hashlib,time,warnings
from collections import Counter
import numpy as np
import joblib
from sklearn.preprocessing import StandardScaler
from sklearn.linear_model import LogisticRegression
from sklearn.neural_network import MLPClassifier
from sklearn.ensemble import ExtraTreesClassifier
from sklearn.metrics import accuracy_score,f1_score,classification_report,confusion_matrix
from action_engine.types import PoseObservation
from redbool_demo.learned import CausalFeatures,CLASSES,export_model,PostureModel
parser=argparse.ArgumentParser(description="Train on reviewed cores, with session-separated evaluation; never auto-label prompts")
parser.add_argument('--labels',type=Path,required=True)
parser.add_argument('--protocol',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--pose-cache',type=Path)
parser.add_argument('--variants',nargs='+',choices=['Lite','Full'],default=['Lite'])
args=parser.parse_args();P=args.labels.resolve().parent;O=args.output
labels=json.loads(args.labels.read_text());cfg=json.loads(args.protocol.read_text())
assert cfg['classes']==list(CLASSES)
assert len({d['run'] for d in labels['sessions']})==len(labels['sessions'])
assert {d['run'] for d in labels['sessions'] if d['split']=='train'}==set(cfg['training_sessions'])
assert [d['run'] for d in labels['sessions'] if d['split']=='validation']==[cfg['validation_session']]
assert [d['run'] for d in labels['sessions'] if d['split']=='test']==[cfg['test_session']]
for d in labels['sessions']:
    source=Path(d['session']);source=source if source.is_absolute() else P/source
    d['session']=str(source)
    if not d.get('source_hashes'):raise ValueError('Source hashes are required')
    for name,digest in d['source_hashes'].items():
        if hashlib.sha256((source/name).read_bytes()).hexdigest()!=digest:raise ValueError('Source changed: '+str(source/name))
if 'Full' in args.variants and args.pose_cache is None:parser.error('--pose-cache required for Full')
O.mkdir(parents=True,exist_ok=False)
(O/'protocol.json').write_bytes(args.protocol.read_bytes())
(O/'labels.json').write_text(json.dumps(labels,indent=2))
def make_dataset(variant):
 sessions=[]
 for d in labels['sessions']:
  saved=O/(d['run']+'_'+variant+'_features.npz')
  if saved.exists():
   sessions.append({**dict(np.load(saved)), 'desc':d});continue
  s=Path(d['session']);m=json.loads((s/'manifest.json').read_text());frames=list(map(json.loads,(s/'frames.jsonl').read_text().splitlines()))
  cache=(args.pose_cache or P/'pose')/(d['run']+'_Full.npz')
  if variant=='Full':
   z=dict(np.load(cache));assert np.array_equal(z['frame_ids'],[x['frame_id'] for x in frames])
   for i,f in enumerate(frames):
    found=bool(np.isfinite(z['landmarks'][i]).all());f['image_landmarks']=z['landmarks'][i].tolist() if found else None
    f['landmark_visibility']['image']=z['visibility'][i].tolist() if found else None;f['pose_validity']['pose_present']=found
  fe=CausalFeatures();xs=[];ids=[];stamps=[];valid=[]
  for f in frames:
   o=PoseObservation.from_record(f,m);x=fe.update(o)
   xs.append(x if x is not None else np.zeros(128));valid.append(x is not None);ids.append(o.frame_id);stamps.append(o.capture_ns)
  X=np.array(xs,dtype=np.float32);target=np.full(len(X),-1);groups=np.full(len(X),-1)
  for j,a in enumerate(d['cores']):
   sel=(np.array(ids)>=a['start'])&(np.array(ids)<=a['end'])&np.array(valid)
   target[sel]=CLASSES.index(a['label']);groups[sel]=j
  result={'X':X,'target':target,'groups':groups,'ids':np.array(ids),'stamps':np.array(stamps),'valid':np.array(valid),'desc':d}
  np.savez_compressed(O/(d['run']+'_'+variant+'_features.npz'),**{k:v for k,v in result.items() if k!='desc'})
  sessions.append(result)
 return sessions
allresults={}
for variant in args.variants:
 data=make_dataset(variant);train=[];ys=[];weights=[]
 for d in [item for item in data if item['desc']['split']=='train']:
  for g in sorted(set(d['groups'])-{-1}):
   ix=np.flatnonzero(d['groups']==g)[::3]
   train.append(d['X'][ix]);ys.append(d['target'][ix]);weights.extend([1/len(ix)]*len(ix))
 X=np.concatenate(train).astype(np.float64);y=np.concatenate(ys);w=np.array(weights);w*=len(w)/w.sum()
 scale=StandardScaler().fit(X);xs=scale.transform(X);results={}
 counts=Counter(y);balanced=w*np.array([len(y)/(len(CLASSES)*counts[v]) for v in y])
 for kind,settings in cfg['models'].items():
  model={'logistic':LogisticRegression,'mlp':MLPClassifier,'extra_trees':ExtraTreesClassifier}[kind](random_state=cfg['seed'],**{k:tuple(v) if k=='hidden_layer_sizes' else v for k,v in settings.items()})
  t=time.perf_counter()
  with warnings.catch_warnings(record=True) as ws:
   warnings.simplefilter('always');model.fit(xs,y,sample_weight=balanced if kind=='mlp' else w)
  fit_seconds=time.perf_counter()-t
  artifact=O/f'{variant}_{kind}.joblib';joblib.dump({'model':model,'scaler':scale,'classes':CLASSES,'training_sessions':cfg['training_sessions']},artifact)
  if kind!='extra_trees':
   path=O/f'{variant}_{kind}.npz';export_model(path,model,scale,CLASSES,{'protocol_sha256':hashlib.sha256(args.protocol.read_bytes()).hexdigest(),'training_sessions':cfg['training_sessions'],'pose_variant':variant})
   reloaded=PostureModel(path);assert np.max(np.abs(reloaded.predict_proba(X[:50])-model.predict_proba(xs[:50])))<1e-6
  row={'fit_seconds':fit_seconds,'warnings':[str(z.message) for z in ws],'training_samples':len(y),'training_class_counts':{CLASSES[k]:v for k,v in counts.items()},'eval':{}}
  for d in data:
   prob=model.predict_proba(scale.transform(d['X'].astype(np.float64)));pred=prob.argmax(axis=1);ix=np.flatnonzero(d['target']>=0)[::3]
   metric={'accuracy':float(accuracy_score(d['target'][ix],pred[ix])),'macro_f1':float(f1_score(d['target'][ix],pred[ix],labels=range(7),average='macro',zero_division=0)),
    'confusion':confusion_matrix(d['target'][ix],pred[ix],labels=range(7)).tolist(),'classification':classification_report(d['target'][ix],pred[ix],labels=range(7),target_names=CLASSES,zero_division=0,output_dict=True),'labeled_frames':len(ix),'note':'Correlated posture-core frames; not independent events or general accuracy.'}
   row['eval'][d['desc']['run']]=metric
   np.savez_compressed(O/f"{d['desc']['run']}_{variant}_{kind}_predictions.npz",probability=prob,ids=d['ids'],stamps=d['stamps'],valid=d['valid'])
  costs=[]
  for _ in range(100):
   t=time.perf_counter_ns();model.predict_proba(scale.transform(X[:1]));costs.append((time.perf_counter_ns()-t)/1e6)
  row['inference_ms_median']=float(np.median(costs));row['inference_ms_p95']=float(np.percentile(costs,95));results[kind]=row
  print(variant,kind,'trained',len(y),'val',row['eval'][cfg['validation_session']]['macro_f1'],'test',row['eval'][cfg['test_session']]['macro_f1'],flush=True)
 allresults[variant]=results
 (O/'results.json').write_text(json.dumps(allresults,indent=2))
for d in labels['sessions']:
 assert all(hashlib.sha256((Path(d['session'])/n).read_bytes()).hexdigest()==h for n,h in d['source_hashes'].items())
print('DONE source hashes unchanged',flush=True)
