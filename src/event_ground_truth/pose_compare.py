"""Frozen paired Pose IMAGE/VIDEO replay on human-reviewed original RGB frames."""
import argparse
from dataclasses import asdict
import hashlib
import json
from pathlib import Path
import platform
import time
import numpy as np
from action_engine.ablation import configurations
from action_engine.core import ActionEngine
from action_engine.experimental import DevelopmentEngine
from action_engine.types import PoseObservation
from vision_v2.preprocess import sha256
from .schema import digest,mapped_segments
from .evaluate import score,PROTOCOL


def ns_to_ms(stamp,origin):return (int(stamp)-int(origin))//1_000_000

def record_pose(result,mapping):
 present=bool(result.pose_landmarks)
 def coords(points):return [[float(p.x),float(p.y),float(p.z)] for p in points]
 return {'frame_id':mapping['frame_id'],'capture_return_monotonic_ns':int(mapping['capture_return_monotonic_ns']),
         'pose_success':present,'pose_validity':{'pose_present':present},
         'image_landmarks':coords(result.pose_landmarks[0]) if present else None,
         'world_landmarks':coords(result.pose_world_landmarks[0]) if result.pose_world_landmarks else None,
         'landmark_visibility':{'image':[float(p.visibility) for p in result.pose_landmarks[0]] if present else None,
                                'world':[float(p.visibility) for p in result.pose_world_landmarks[0]] if result.pose_world_landmarks else None}}


def run(root,output,annotation,repeats=2):
 import cv2,mediapipe as mp
 from mediapipe.tasks import python
 from mediapipe.tasks.python import vision
 root=root.resolve();output=output.resolve()
 if output.exists() or root/'local_data' not in output.parents:raise ValueError('New local output required')
 dataset_path=root/'local_data/vision_v2/event_ground_truth_20261025/review/dataset.json'
 dataset=json.loads(dataset_path.read_text());doc=json.loads(annotation.read_text());segments=mapped_segments(doc,dataset)
 reviewed={s['key'] for s in segments if s['status']=='CONFIRMED'}
 trials=[t for t in dataset['trials'] if t['key'] in reviewed]
 if len(trials)!=5:raise ValueError('Expected reviewed first five Trials')
 models={'lite':root/'pose_landmarker.task','full':root/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task'}
 cfgpath=root/'local_data/vision_v2/action_engine_20261024/frozen_config.json';configs=configurations(json.loads(cfgpath.read_text()))
 hashes={**dataset['sources'],**{str(p.resolve()):sha256(p) for p in [*models.values(),annotation,dataset_path,cfgpath]}}
 if any(sha256(p)!=h for p,h in hashes.items()):raise ValueError('Input integrity failed')
 schedule=[(rep,mode,model) for rep in range(repeats) for mode in ('IMAGE','VIDEO') for model in (('lite','full') if (rep+(mode=='VIDEO'))%2==0 else ('full','lite'))]
 output.mkdir(parents=True)
 protocol={'version':'first5-pose-paired-1','annotation_revision':str(annotation.resolve()),'annotation_sha256':digest(doc),
   'input_sha256':hashes,'configs':{k:asdict(v) for k,v in configs.items()},'schedule':schedule,'mediapipe':mp.__version__,'opencv':cv2.__version__,'numpy':np.__version__,'platform':platform.platform(),
   'delegate':'CPU','num_poses':1,'min_pose_detection_confidence':.5,'min_pose_presence_confidence':.5,'min_tracking_confidence':.5,'engine_visibility':.5,
   'video_timestamp':'floor((original_capture_ns - first_trial_capture_ns)/1e6); collision rejects, original ns preserved',
   'tracking':'fresh landmarker per Trial; COUNTDOWN included; no concatenation across Trial gaps',
   'warmup':'5 first-frame calls on disposable instance per condition; fresh per-Trial tracking afterward; cold first frames included',
   'resolution':'original 640x480 verified per manifest; no resizing/no source mirroring',
   'timing':'perf_counter_ns; single-engine pipeline=read/decode+RGB+pose+pack+one engine, excludes disk output/hash/quality/evaluation; wall FPS includes five-engine audit and output. Not live camera/game FPS.',
   'event_time':'logical original capture ns, not measured capture-to-UDP latency','scope':'five P01 development Trials, not generalization',
   'evaluation':PROTOCOL,'code_sha256':{str(p.resolve()):sha256(p) for folder in ['src/event_ground_truth','src/action_engine'] for p in (root/folder).glob('*.py')}}
 (output/'protocol.json').write_text(json.dumps(protocol,indent=2));summaries=[]
 for rep,mode,name in schedule:
  label=f'{mode.lower()}_{name}_r{rep}';folder=output/label;folder.mkdir();all_perf=[];evaluations=[];wall_start=time.perf_counter()
  options=vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(models[name]),delegate=python.BaseOptions.Delegate.CPU),running_mode=getattr(vision.RunningMode,mode),num_poses=1,min_pose_detection_confidence=.5,min_pose_presence_confidence=.5,min_tracking_confidence=.5)
  cap=cv2.VideoCapture(trials[0]['video_path']);cap.set(cv2.CAP_PROP_POS_FRAMES,trials[0]['frames'][0]['video_frame_index']);ok,bgr=cap.read();cap.release()
  if not ok:raise ValueError('Warmup decode')
  image=mp.Image(image_format=mp.ImageFormat.SRGB,data=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB))
  with vision.PoseLandmarker.create_from_options(options) as warm:
   for i in range(5):warm.detect(image) if mode=='IMAGE' else warm.detect_for_video(image,i*40)
  with (folder/'frames.jsonl').open('x') as frame_file,(folder/'engine_trace.jsonl').open('x') as trace_file:
   for trial in trials:
    manifest=json.loads((root/'sessions'/trial['session_id']/'manifest.json').read_text())
    if manifest['recording_config']['source_mirrored']:raise ValueError('This fixed protocol expects unmirrored source')
    engines={k:ActionEngine(c) if k=='A0' else DevelopmentEngine(c) for k,c in configs.items()};events={k:[] for k in engines};trace={k:[] for k in engines}
    cap=cv2.VideoCapture(trial['video_path']);cap.set(cv2.CAP_PROP_POS_FRAMES,trial['frames'][0]['video_frame_index'])
    previous_index=trial['frames'][0]['video_frame_index']-1;last_ms=-1;origin=int(trial['frames'][0]['capture_return_monotonic_ns'])
    with vision.PoseLandmarker.create_from_options(options) as pose:
     for n,mapping in enumerate(trial['frames']):
      if mapping['video_frame_index']!=previous_index+1:raise ValueError('Nonconsecutive mapped video Trial')
      previous_index=mapping['video_frame_index'];begin=time.perf_counter_ns();ok,bgr=cap.read();decoded=time.perf_counter_ns()
      if not ok or [bgr.shape[1],bgr.shape[0]]!=manifest['actual_resolution']:raise ValueError('Decode/resolution mismatch')
      rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB);image=mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb);converted=time.perf_counter_ns()
      ms=ns_to_ms(mapping['capture_return_monotonic_ns'],origin)
      if ms<=last_ms:raise ValueError('Timestamp collision/reversal')
      last_ms=ms
      detected=pose.detect(image) if mode=='IMAGE' else pose.detect_for_video(image,ms);inferred=time.perf_counter_ns()
      frame=record_pose(detected,mapping);obs=PoseObservation.from_record(frame,manifest,epoch=trial['key']);packed=time.perf_counter_ns()
      costs={}
      for key,engine in engines.items():
       t0=time.perf_counter_ns();engine.tick(obs.capture_ns);engine.process(obs,now_ns=obs.capture_ns);emitted=[asdict(e) for e in engine.drain_events(obs.capture_ns)];logs=engine.take_logs();t1=time.perf_counter_ns()
       costs[key]=(t1-t0)/1e6;events[key].extend(emitted)
       entry={'key':trial['key'],'variant':key,'capture_ns':obs.capture_ns,'frame_id':obs.frame_id,'state':asdict(engine.control),'logs':logs};trace[key].append(entry);trace_file.write(json.dumps(entry,allow_nan=False)+'\n')
      if hashlib.sha256(rgb.tobytes()).hexdigest()!=mapping['source_rgb_sha256']:raise ValueError('Original RGB SHA mismatch')
      perf={'decode_ms':(decoded-begin)/1e6,'rgb_ms':(converted-decoded)/1e6,'inference_ms':(inferred-converted)/1e6,'pack_ms':(packed-inferred)/1e6,'engine_ms':costs,
            'single_engine_pipeline_ms':{k:(packed-begin)/1e6+v for k,v in costs.items()},'first_trial_frame':n==0}
      all_perf.append(perf);frame_file.write(json.dumps({'key':trial['key'],'video_frame_index':mapping['video_frame_index'],'pose_timestamp_ms':ms,'frame':frame,'perf':perf},allow_nan=False)+'\n')
    cap.release()
    for key in engines:evaluations.append({'key':trial['key'],'variant':key,'events':events[key],'evaluation':score(trial,[s for s in segments if s['key']==trial['key']],events[key],trace[key])})
  elapsed=time.perf_counter()-wall_start
  summary={'condition':label,'model':name,'mode':mode,'repeat':rep,'frames':len(all_perf),'wall_seconds_including_audit':elapsed,'audit_wall_fps':len(all_perf)/elapsed}
  for field in ('decode_ms','rgb_ms','inference_ms','pack_ms'):
   vals=[p[field] for p in all_perf];summary[field]={'p50':float(np.median(vals)),'p95':float(np.percentile(vals,95))}
  summary['single_engine_pipeline']={k:{'p50_ms':float(np.median([p['single_engine_pipeline_ms'][k] for p in all_perf])),
    'p95_ms':float(np.percentile([p['single_engine_pipeline_ms'][k] for p in all_perf],95)),
    'throughput_fps':1000/float(np.mean([p['single_engine_pipeline_ms'][k] for p in all_perf]))} for k in engines}
  (folder/'evaluation.json').write_text(json.dumps(evaluations,indent=2));(folder/'timing.json').write_text(json.dumps(summary,indent=2));summaries.append(summary)
  print(label,summary['inference_ms'],summary['single_engine_pipeline']['A0'],flush=True)
 (output/'timing.json').write_text(json.dumps(summaries,indent=2))
 changed=[p for p,h in hashes.items() if sha256(p)!=h];(output/'integrity.json').write_text(json.dumps({'changed':changed,'verified_frames_per_condition':len(all_perf)},indent=2))
 if changed:raise ValueError('Input changed')

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path('.'));p.add_argument('--output',type=Path,required=True);p.add_argument('--annotation',type=Path,required=True);p.add_argument('--repeats',type=int,default=2);a=p.parse_args();run(a.root,a.output,a.annotation,a.repeats)
