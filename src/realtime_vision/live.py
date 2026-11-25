"""Latest-frame webcam -> unchanged frozen FSM -> UDP v2. Experimental only."""
import argparse
from dataclasses import asdict
import json
from pathlib import Path
import queue
import threading
import time
import traceback
from action_engine.ablation import configurations, units
from action_engine.core import ActionEngine
from action_engine.experimental import DevelopmentEngine
from action_engine.types import PoseObservation
from continuous_events.protocol import verify_pose_asset
from vision_v2.preprocess import sha256
from .transport import Sender


class Latest:
    def __init__(self):self.lock=threading.Lock();self.value=None;self.version=0;self.consumed=0;self.dropped=0
    def put(self,value):
        with self.lock:
            if self.version>self.consumed:self.dropped+=1
            self.version+=1;self.value=value
    def take(self):
        with self.lock:
            if self.version==self.consumed:return None
            self.consumed=self.version;return self.value


class AsyncLog:
    def __init__(self,path):
        self.queue=queue.Queue(2048);self.stop=threading.Event();self.dropped=0;self.error=None
        def worker():
            try:
                with path.open('x') as f:
                    while not self.stop.is_set() or not self.queue.empty():
                        try:row=self.queue.get(timeout=.05)
                        except queue.Empty:continue
                        f.write(json.dumps(row,allow_nan=False)+'\n')
            except Exception as exc:self.error=repr(exc)
        self.thread=threading.Thread(target=worker,daemon=True);self.thread.start()
    def write(self,row):
        try:self.queue.put_nowait(row)
        except queue.Full:self.dropped+=1
    def close(self):self.stop.set();self.thread.join(timeout=3)


class Recognizer:
    """Prediction is evidence only; it cannot bypass the frozen coordinate FSM."""
    def __init__(self,kind,fold=None):
        self.kind=kind;self.adapter=None
        if kind!='coordinates':
            if kind!='v1' and fold is None:raise ValueError('Explicit LOSO fold required; not an all-data deployment model')
            from action_engine.adapters import FrozenEvidenceAdapter
            self.adapter=FrozenEvidenceAdapter(kind,fold)
    def predict(self,obs):return self.adapter.predict(obs) if self.adapter else None


def run(a):
    import cv2 as cv
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    from pose_capture import open_camera
    from continuous_events.viewer import overlay
    from continuous_events.quality import joint_states
    if a.output.exists():raise ValueError('New local output required')
    root=Path(__file__).resolve().parents[2]
    if root/'local_data' not in a.output.resolve().parents:raise ValueError('Output must be ignored local_data')
    verify_pose_asset(a.pose,a.variant)
    base=root/'local_data/vision_v2/action_engine_20261024/frozen_config.json'
    cfg=configurations(json.loads(base.read_text()))[a.engine]
    recognizer=Recognizer(a.recognizer,a.fold_dir)
    engine=ActionEngine(cfg) if a.engine=='A0' else DevelopmentEngine(cfg)
    a.output.mkdir(parents=True)
    assets=[base,a.pose]
    if a.fold_dir:assets+=list(a.fold_dir.glob('*.npz'))+list(a.fold_dir.glob('*.keras'))+list(a.fold_dir.glob('*.joblib'))
    if a.recognizer=='v1':assets += [root/'models/motion_model.keras',root/'models/label_classes.npy']
    hashes={str(p.resolve()):sha256(p) for p in assets}
    protocol={'args':{k:str(v) if isinstance(v,Path) else v for k,v in vars(a).items()},'engine_config':asdict(cfg),'input_sha256':hashes,
        'recognizer_role':'display/evidence only; frozen model_gate unchanged','v2_fold_status':'experimental LOSO fold, NOT all-data trained live deployment',
        'camera_clock':'host monotonic read-return, NOT exposure','capture_epoch':a.stream_id,
        'frame_policy':'single latest slot, actual IDs/timestamps retained; skipped intervals are not interpolated',
        'recording':'no raw video/encoding in live path; bounded asynchronous JSONL logs; GUI separate main thread',
        'code_sha256':{str(p):sha256(p) for folder in ['src/realtime_vision','src/action_engine'] for p in (root/folder).glob('*.py')}}
    (a.output/'protocol.json').write_text(json.dumps(protocol,indent=2))
    log=AsyncLog(a.output/'trace.jsonl');capture=Latest();display=Latest();stop=threading.Event();errors=[]
    counts={'captured':0,'processed':0,'capture_failures':0,'events_emitted':0,'events_not_sent':0};latest={'frame':None,'prediction':None,'stages':None,'last_event':None}
    def fail(where,exc):errors.append({'where':where,'error':repr(exc)});log.write({'type':'error','where':where,'error':repr(exc)});stop.set()
    def camera_worker():
        cap=None
        try:
            cap=open_camera(cv,a.camera);cap.set(cv.CAP_PROP_FRAME_WIDTH,a.width);cap.set(cv.CAP_PROP_FRAME_HEIGHT,a.height)
            while not stop.is_set():
                read_start=time.monotonic_ns();ok,bgr=cap.read();stamp=time.monotonic_ns()
                if not ok:
                    counts['capture_failures']+=1;raise RuntimeError('Camera read failed; restart requires a new stream/epoch')
                counts['captured']+=1;capture.put((counts['captured'],read_start,stamp,bgr.copy()))
        except BaseException as exc:fail('camera',exc)
        finally:
            if cap is not None:cap.release()
    def inference_worker():
        try:
            opts=vision.PoseLandmarkerOptions(base_options=python.BaseOptions(model_asset_path=str(a.pose),delegate=python.BaseOptions.Delegate.CPU),
                running_mode=getattr(vision.RunningMode,a.mode),num_poses=1,min_pose_detection_confidence=.5,min_pose_presence_confidence=.5,min_tracking_confidence=.5)
            origin=None;last_ms=-1
            with vision.PoseLandmarker.create_from_options(opts) as pose:
                while not stop.is_set():
                    item=capture.take()
                    if item is None:stop.wait(.001);continue
                    fid,read_start,stamp,bgr=item
                    if origin is None:origin=stamp
                    ms=(stamp-origin)//1_000_000
                    if ms<=last_ms:log.write({'type':'frame_rejected','frame_id':fid,'reason':'VIDEO ms collision'});continue
                    last_ms=ms;rgb=cv.cvtColor(bgr,cv.COLOR_BGR2RGB);image=mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb)
                    pose_start=time.monotonic_ns();result=pose.detect(image) if a.mode=='IMAGE' else pose.detect_for_video(image,ms);pose_end=time.monotonic_ns()
                    from event_ground_truth.pose_compare import record_pose
                    frame=record_pose(result,{'frame_id':fid,'capture_return_monotonic_ns':stamp})
                    manifest={'subject_id':a.actor,'session_id':a.stream_id,'actual_resolution':[bgr.shape[1],bgr.shape[0]],'recording_config':{'source_mirrored':False}}
                    obs=PoseObservation.from_record(frame,manifest)
                    evidence=recognizer.predict(obs);decision=time.monotonic_ns()
                    engine.process(obs,evidence)
                    stages={'frame_id':fid,'read_start_ns':read_start,'capture_ns':stamp,'pose_start_ns':pose_start,'pose_end_ns':pose_end,'recognition_ns':decision,'engine_end_ns':time.monotonic_ns()}
                    prediction=asdict(evidence) if evidence else None
                    with engine.lock:
                        latest.update(frame=frame,prediction=prediction,stages=stages)
                        state=asdict(engine.control)
                    counts['processed']+=1;log.write({'type':'frame',**stages,'pose_present':obs.pose_present,'joint_quality':joint_states(frame),'prediction':prediction,'state':state})
                    display.put((bgr,frame,prediction,stages))
        except BaseException as exc:fail('inference',exc)
    sender=Sender(a.host,a.port,a.stream_id,a.actor)
    def publish_worker():
        try:
            while not stop.wait(.02):
                for row in sender.service():log.write(row)
                with engine.lock:
                    engine.tick();events=engine.drain_events();state=engine.control;last_capture=engine.last_capture
                    rearm={k:v.state for k,v in units(engine).items()};stages=latest['stages']
                    logs=engine.take_logs()
                for item in logs:log.write({'type':'engine','log':item})
                now=time.monotonic_ns()
                for e in events:
                    counts['events_emitted']+=1;latest['last_event']=asdict(e)
                    remaining=min(150,(e.expires_ns-now)//1_000_000)
                    if sender.clock and remaining>0:
                        log.write(sender.send('ATTACK',event_id=e.event_id,side=e.side,ttl_ms=int(remaining),commit_ns=e.committed_ns,
                            capture_ns=e.source_capture_ns,source_frame_id=e.source_frame_id,fsm_state=state.attack_state,confidence=None))
                    else:counts['events_not_sent']+=1;log.write({'type':'event_not_sent','event':asdict(e),'reason':'clock_not_ready_or_expired'})
                fresh=last_capture is not None and 0<=now-last_capture<cfg.safety_timeout_ms*1_000_000
                remaining=max(1,min(150,int((last_capture+cfg.safety_timeout_ms*1_000_000-now)//1_000_000))) if fresh else 150
                if sender.clock:
                    log.write(sender.send('STATE',ttl_ms=remaining,block_active=bool(state.block and fresh),
                        movement=state.movement if fresh else 'NEUTRAL',movement_strength=float(state.movement_strength) if fresh else 0.,
                        tracking=state.tracking,fsm_state=state.attack_state,rearm=rearm,capture_ns=last_capture,stages=stages))
        except BaseException as exc:fail('publisher_watchdog',exc)
    threads=[threading.Thread(target=fn,name=name,daemon=True) for fn,name in [(camera_worker,'capture'),(inference_worker,'recognition'),(publish_worker,'publisher-watchdog')]]
    for t in threads:t.start()
    start=time.monotonic();shown=0
    try:
        while not stop.is_set() and time.monotonic()-start<a.seconds:
            item=display.take()
            if item and not a.headless:
                bgr,frame,prediction,stages=item;view=overlay(bgr,frame,name=a.variant+' '+a.mode)
                with engine.lock:state=asdict(engine.control);event=latest['last_event']
                label=max(prediction['probabilities'],key=prediction['probabilities'].get) if prediction else 'NO MODEL PREDICTION'
                lines=[f'{a.engine} {state["attack_state"]} tracking={state["tracking"]}',
                    f'Prediction: {label} (not an event)',f'BLOCK={state["block"]} MOVE={state["movement"]}',
                    f'Last committed event: {event["side"]+" "+event["event_id"] if event else "NONE"}',
                    f'Pose {(stages["pose_end_ns"]-stages["pose_start_ns"])/1e6:.1f}ms dropped={capture.dropped} sync={bool(sender.clock)}',
                    'Q stop | experimental FSM: known punch misses / block confusion']
                for i,s in enumerate(lines):cv.putText(view,s,(5,20+i*20),0,.43,(255,255,255),1)
                cv.imshow('REDBOOL UDP v2 development',view);shown+=1
            if not a.headless and cv.waitKey(1)&255==ord('q'):break
            stop.wait(.005)
    except KeyboardInterrupt:pass
    finally:
        stop.set()
        for t in threads:t.join(timeout=2)
        for item in engine.take_logs():log.write({'type':'engine','log':item})
        if sender.clock:log.write(sender.send('STATE',ttl_ms=150,block_active=False,movement='NEUTRAL',movement_strength=0.,tracking='STOPPED',fsm_state='UNAVAILABLE'))
        sender.close();cv.destroyAllWindows();log.close()
        changed=[p for p,h in hashes.items() if sha256(p)!=h]
        (a.output/'summary.json').write_text(json.dumps({'scope':'live development; no ground-truth accuracy','counts':counts,'camera_slot_drops':capture.dropped,
            'display_slot_drops':display.dropped,'log_dropped':log.dropped,'log_error':log.error,'udp':dict(sender.counts),'errors':errors,
            'threads_still_alive':[t.name for t in threads if t.is_alive()],'gui_frames':shown,'runtime_seconds':time.monotonic()-start,
            'changed':changed,'unity_integration_verified':False},indent=2))
    if errors:raise RuntimeError(errors)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--stream-id',required=True);p.add_argument('--actor',default='DEV01')
    p.add_argument('--engine',choices=['A0','A1','A2','A3','A4'],default='A0');p.add_argument('--recognizer',choices=['coordinates','v1','logistic','mlp','tcn','gru'],default='coordinates');p.add_argument('--fold-dir',type=Path)
    p.add_argument('--pose',type=Path,default=Path('pose_landmarker.task'));p.add_argument('--variant',choices=['Lite','Full'],default='Lite');p.add_argument('--mode',choices=['IMAGE','VIDEO'],default='IMAGE')
    p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065);p.add_argument('--seconds',type=float,default=15)
    p.add_argument('--headless',action='store_true');p.add_argument('--camera',type=int,default=0);p.add_argument('--width',type=int,default=640);p.add_argument('--height',type=int,default=480)
    run(p.parse_args())
