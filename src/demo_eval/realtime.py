"""Isolated webcam instrument. Existing weights only; UDP is opt-in."""
import argparse,json,socket,time,uuid
from collections import deque
from pathlib import Path
import numpy as np
from vision_v2.preprocess import Config,features,resample,aggregate,apply_standardizer,sha256


def common(label):
    if label in ('LEFT_PUNCH','RIGHT_PUNCH','ATTACK'):return 'ATTACK','NEUTRAL'
    if label=='BLOCK':return 'BLOCK','NEUTRAL'
    if label in ('MOVE_LEFT','MOVE_RIGHT'):return 'NONE',label.removeprefix('MOVE_')
    if label in ('NONE','IDLE'):return 'NONE','NEUTRAL'
    return 'UNMAPPED','NEUTRAL'


class Adapter:
    def __init__(self,kind,fold_dir=None):
        import tensorflow as tf
        self.kind=kind;self.buffer=deque();self.result=('NONE','NEUTRAL',0.);self.last=0
        if kind=='v1':
            import run_inference as v1
            self.v1=v1;self.model,self.classes=v1.load_artifacts(v1.TRAINED_MODEL_PATH,v1.LABEL_CLASSES_PATH)
            self.actions=deque(maxlen=v1.STABLE_FRAME_COUNT);self.movement=v1.create_movement_detector()
        else:
            if fold_dir is None:raise ValueError('Explicit V2 fold directory required, no implicit best-fold selection')
            import joblib
            if kind in ('tcn','gru'):from vision_v2 import temporal_models
            self.model=joblib.load(fold_dir/'logistic.joblib') if kind=='logistic' else tf.keras.models.load_model(fold_dir/(kind+'.keras'),compile=False)
            with np.load(fold_dir/'standardizer.npz') as d:self.stats={k:d[k] for k in d.files}
            self.classes=json.loads((fold_dir.parent/'evaluation.json').read_text())['classes']
    def predict(self,landmarks,stamp,width,height):
        if self.kind=='v1':
            if landmarks:
                label,confidence=self.v1.predict_action(self.model,self.classes,landmarks)
                movement=self.movement.update(landmarks,now=stamp/1e9).final_movement
            else:label,confidence,movement='NONE',0.,self.movement.reset().final_movement
            self.actions.append(label);action=self.v1.get_stable_action(self.actions)
            mapped,_=common(action);return mapped,movement,confidence,action
        frame={'capture_return_monotonic_ns':stamp,'pose_success':bool(landmarks),'pose_validity':{'pose_present':bool(landmarks)},
               'image_landmarks':[[p.x,p.y,p.z] for p in landmarks] if landmarks else None,
               'landmark_visibility':{'image':[p.visibility for p in landmarks] if landmarks else None},'_height_over_width':height/width}
        self.buffer.append(frame)
        while self.buffer and stamp-self.buffer[0]['capture_return_monotonic_ns']>3_000_000_000:self.buffer.popleft()
        if len(self.buffer)<2 or stamp-self.buffer[0]['capture_return_monotonic_ns']<2_800_000_000:return 'NONE','NEUTRAL',0.,'WARMUP'
        if stamp-self.last<200_000_000:return *self.result,'HELD_LAST_OUTPUT'
        self.last=stamp;x,mask,ts=features(list(self.buffer),Config());sx,sm,obs,idx,grid=resample(x,mask,ts,Config())
        if self.kind in ('logistic','mlp'):
            v,vm=aggregate(sx,sm);z=apply_standardizer(v[None],vm[None],self.stats)
            probs=self.model.predict_proba(z)[0] if self.kind=='logistic' else self.model(z,training=False).numpy()[0]
        else:
            from vision_v2.temporal_models import prepare_inputs
            z=apply_standardizer(sx[None],sm[None],self.stats)
            probs=self.model(prepare_inputs(z,sm[None],obs[None],grid),training=False).numpy()[0]
        label=self.classes[int(probs.argmax())];action,movement=common(label);self.result=(action,movement,float(probs.max()))
        return *self.result,label


def run(args):
    import cv2,mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    from pose_capture import open_camera
    from send_unity import build_payload
    from run_inference import should_send_to_unity
    if args.output.exists():raise ValueError('Output must be new')
    args.output.mkdir(parents=True);run_id=str(uuid.uuid4())
    paths=[args.pose]
    if args.model=='v1':paths += [Path('models/motion_model.keras'),Path('models/label_classes.npy')]
    else:paths+=list(args.fold_dir.glob('*.npz'))+list(args.fold_dir.glob('*.keras'))+list(args.fold_dir.glob('*.joblib'))
    hashes={str(p.resolve()):sha256(p) for p in paths}
    adapter=Adapter(args.model,args.fold_dir)
    config=vars(args).copy();config={k:str(v) if isinstance(v,Path) else v for k,v in config.items()}
    (args.output/'protocol.json').write_text(json.dumps({'run_id':run_id,**config,'input_sha256':hashes,'timebase':'host_monotonic_ns','v2_window_seconds':3,'v2_min_coverage_seconds':2.8,'v2_cadence_seconds':.2,'v2_deployment_status':'experimental distribution shift, not trained for streaming','mini_map_stream':False},indent=2))
    log=(args.output/'events.jsonl').open('x');marks=(args.output/'operator_marks.jsonl').open('x')
    def write(row):row['run_id']=run_id;log.write(json.dumps(row)+'\n');log.flush()
    cap=None;sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);last_action=last_move=None;passive=0.;i=0
    try:
        cap=open_camera(cv2,args.camera)
        options=vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(args.pose),delegate=python.BaseOptions.Delegate.CPU),running_mode=vision.RunningMode.IMAGE,num_poses=1)
        with vision.PoseLandmarker.create_from_options(options) as pose:
            start=time.monotonic()
            while time.monotonic()-start<args.seconds:
                begin=time.monotonic_ns();ok,bgr=cap.read();capture=time.monotonic_ns()
                if not ok:raise RuntimeError('Camera read failed')
                h,w=bgr.shape[:2];t=time.monotonic_ns()
                result=pose.detect(mp.Image(image_format=mp.ImageFormat.SRGB,data=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB)));pose_end=time.monotonic_ns()
                landmarks=result.pose_landmarks[0] if result.pose_landmarks else None
                action,move,confidence,label=adapter.predict(landmarks,capture,w,h)
                now=time.monotonic();sent_action='NONE' if action=='UNMAPPED' or confidence<.7 else action
                if should_send_to_unity(sent_action,move,last_action,last_move,passive,now):
                    payload=build_payload(sent_action,move,confidence,0. if move=='NEUTRAL' else 1.)
                    if args.send_udp:sock.sendto(json.dumps(payload).encode(),(args.host,args.port))
                    write({'type':'send','send_ns':time.monotonic_ns(),'capture_ns':capture,'action':sent_action,'movement':move,'confidence':confidence,'udp_sent':args.send_udp,'source_label':label})
                    last_action,last_move=sent_action,move
                    if sent_action in ('NONE','IDLE'):passive=now
                key=-1
                if not args.headless:
                    cv2.putText(bgr,f'{args.model} {label} => {action}/{move}',(15,30),cv2.FONT_HERSHEY_SIMPLEX,.6,(0,255,0),2)
                    cv2.putText(bgr,'Marks: a attack b block j left l right n NONE; q quit',(15,60),cv2.FONT_HERSHEY_SIMPLEX,.5,(0,255,0),1)
                    cv2.imshow('Demo comparison instrument',bgr);key=cv2.waitKey(1)&255
                    if key in map(ord,'abjln'):
                        marks.write(json.dumps({'run_id':run_id,'host_monotonic_ns':time.monotonic_ns(),'key':chr(key),'status':'NOT_REVIEWED','meaning':'operator mark; verify actual onset separately'})+'\n');marks.flush()
                write({'type':'frame','frame_id':i,'capture_ns':capture,'end_ns':time.monotonic_ns(),'pose_ms':(pose_end-t)/1e6,'loop_ms':(time.monotonic_ns()-begin)/1e6,'pose_missing':not bool(landmarks),'action':sent_action,'movement':move,'source_label':label,'confidence':confidence});i+=1
                if key==ord('q'):break
    except Exception as exc:
        write({'type':'error','error':repr(exc)});raise
    finally:
        if cap is not None:cap.release()
        sock.close();log.close();marks.close()
        if not args.headless:cv2.destroyAllWindows()
        for p,h in hashes.items():
            if sha256(p)!=h:raise ValueError('Model changed: '+p)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--model',choices=['v1','logistic','mlp','tcn','gru'],required=True);p.add_argument('--fold-dir',type=Path);p.add_argument('--pose',type=Path,default=Path('pose_landmarker.task'));p.add_argument('--output',type=Path,required=True);p.add_argument('--camera',type=int,default=0);p.add_argument('--seconds',type=float,default=60);p.add_argument('--headless',action='store_true');p.add_argument('--send-udp',action='store_true');p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5055)
    a=p.parse_args()
    if a.model!='v1' and a.fold_dir is None:p.error('--fold-dir required for V2')
    run(a)
