"""Explicitly launched demo: camera + configurable review recording + FSM + UDP v2.

The capture latest-slot avoids latency backlog; skipped camera samples are
counted, never silently represented as recorded frames. The recorder stores
every processed Pose observation. Review mode samples compressed RGB at 15 fps. No camera is opened by --check.
"""
import argparse
import hashlib
from dataclasses import asdict, replace
from datetime import datetime
import json
from pathlib import Path
import threading
import time
import uuid
from .six_engine import SixGestureEngine as DemoEngine
from .transport import DemoSender
from realtime_vision.clock import now_ns, metadata as clock_metadata
from .camera_diagnostics import CaptureCadence, camera_properties
from .guided import GuidedCheck
from .framing import framing_status
from collections import Counter
from concurrent.futures import ThreadPoolExecutor


def run(a):
    import numpy as np
    import cv2 as cv
    import mediapipe as mp
    from mediapipe.tasks.python import BaseOptions
    from mediapipe.tasks.python.vision import PoseLandmarker,PoseLandmarkerOptions,RunningMode
    from action_engine.types import PoseObservation
    from continuous_events.protocol import verify_pose_asset
    from continuous_events.viewer import overlay
    from session_recording import SessionRecorder
    from collect_gesture_pilot import PilotVideo,AsyncPilotVideo
    from pose_capture import open_camera
    from realtime_vision.live import Latest,AsyncLog
    recording=getattr(a,'recording','lossless')
    if recording not in ('review','logs','lossless'):raise ValueError('Unknown recording mode')
    preview_fps=getattr(a,'preview_fps',15.)
    if not 1<=preview_fps<=60:raise ValueError('preview_fps must be between 1 and 60')
    preview_interval_ns=int(1e9/preview_fps)
    variant=getattr(a,'pose_variant','Lite')
    verify_pose_asset(a.pose,variant)
    cross_path=getattr(a,"cross_pose",None)
    if cross_path:verify_pose_asset(cross_path,"Full")
    if getattr(a,'guard_model',None):
        from .learned import LearnedGuardEngine
        engine=LearnedGuardEngine(a.guard_model,movement=a.movement,clock=now_ns)
        trained_variant=engine.posture_model.meta['provenance']['pose_variant']
        if trained_variant!=variant:raise ValueError('Guard model and Pose variant do not match')
    else:
        engine=DemoEngine(movement=a.movement,clock=now_ns)
    if getattr(a,'personal_calibration',False):
        if getattr(a,'guard_model',None): raise ValueError('Personal calibration requires v26 rules')
        from .personal import PersonalGestureEngine
        engine=PersonalGestureEngine(movement=a.movement,clock=now_ns)
    if cross_path and getattr(a,"guard_model",None):
        raise ValueError("Auxiliary cross pose is supported by the calibrated engine only")
    if a.check:
        mode='learned guard confirmation' if getattr(a,'guard_model',None) else 'calibrated rules'
        print(f'Demo modules, {variant} asset and {mode} OK. Camera/UDP not opened.');return
    stream=a.stream_id or uuid.uuid4().hex
    out=a.output/(datetime.now().strftime('%Y%m%d_%H%M%S')+'_'+stream[:8])
    out.mkdir(parents=True,exist_ok=False)
    sender=DemoSender(a.host,a.port,stream,a.actor,clock_ns=now_ns)
    log=AsyncLog(out/'trace.jsonl');slot=Latest();stop=threading.Event();errors=[]
    runtime_clock={**clock_metadata(),'origin_ns':now_ns(),'origin_utc':datetime.now().astimezone().isoformat()}
    log.write({'type':'runtime_clock',**runtime_clock})
    acquisition=CaptureCadence();camera_info={}
    counts={'captured':0,'processed':0,'events':0,'left':0,'right':0,'both':0,'preview_frames':0}
    guide=GuidedCheck(movement=a.movement, self_paced=not getattr(a,'guided_timed',False), focus=getattr(a,'guided_focus','all')) if getattr(a,'guided',False) else None
    display={'event':'NONE','event_ns':0};cap=None;pose=None;cross_pose=None;recovery_pose=None;recovery_pool=None;video=None;recorder=None
    def fail(where,exc):
        errors.append({'where':where,'error':repr(exc)});stop.set()
    def capture():
        nonlocal cap
        try:
            cap=open_camera(cv,a.camera,a.backend)
            requested={'width':bool(cap.set(cv.CAP_PROP_FRAME_WIDTH,640)),
                       'height':bool(cap.set(cv.CAP_PROP_FRAME_HEIGHT,480))}
            # Keep acquisition/inference at 30 fps; only the preview is throttled.
            # Backend acceptance does not guarantee delivery: capture timestamps
            # and skipped-sample counts remain the source of truth.
            requested['fps']=bool(cap.set(cv.CAP_PROP_FPS,30))
            camera_info.update(requested={'width':640,'height':480,'fps':30},
                               setting_accepted=requested,initial=camera_properties(cv,cap))
            log.write({'type':'camera_settings','timestamp_ns':now_ns(),**camera_info})
            while not stop.is_set():
                read_start=now_ns();ok,bgr=cap.read();stamp=now_ns()
                if not ok:raise RuntimeError('Camera read failed')
                counts['captured']+=1;slot.put((counts['captured'],stamp,bgr.copy()))
                window=acquisition.observe(stamp,read_start)
                if window is not None:
                    log.write({'type':'camera_runtime',**window,'properties':camera_properties(cv,cap)})
        except BaseException as exc:fail('camera',exc)
        finally:
            if cap is not None:
                camera_info['final']=camera_properties(cv,cap)
                cap.release()
    def publisher():
        try:
            while not stop.wait(.01):
                with engine.lock:
                    engine.tick();events=engine.drain_events();state=engine.control
                    if hasattr(engine,'tutorial_context'):
                        engine.tutorial_context(sender.tutorial_context(),now_ns())
                    stamp=engine.last_capture;logs=engine.take_logs();armed=engine.calibrated
                for item in logs:log.write({'type':'engine','log':item})
                if not armed:sender.events.clear()
                for ev in events:
                    counts['events']+=1;counts[ev.side.lower()]+=1
                    gesture=ev.reason.get('gesture',ev.side+'_PUNCH')
                    counts[gesture]=counts.get(gesture,0)+1
                    display.update(event=gesture,event_ns=ev.committed_ns)
                    sender.offer(ev)
                now=now_ns();fresh=stamp is not None and 0<=now-stamp<150_000_000
                if sender.clock:
                    ttl=max(1,min(150,int((stamp+150_000_000-now)//1_000_000))) if fresh else 150
                    log.write(sender.send('STATE',ttl_ms=ttl,block_active=bool(state.block and fresh),
                        movement=state.movement if fresh else 'NEUTRAL',movement_strength=float(state.movement_strength) if fresh else 0.,
                        tracking=state.tracking if fresh else "STALE",fsm_state=state.attack_state,capture_ns=stamp,
                        calibration_version=1,calibrated=bool(armed),
                        calibration_generation=getattr(engine,'calibration_generation',0),
                        personal_status=engine.trainer.status if hasattr(engine,'trainer') else 'BASELINE V26'))
                # Deliver a fresh BLOCK release before an attack from that frame.
                for item in sender.service():log.write(item)
                if log.error or log.dropped:raise RuntimeError('Diagnostic log failed or overflowed')
        except BaseException as exc:fail('publisher',exc)
        finally:
            if sender.clock:
                log.write(sender.send('STATE',ttl_ms=150,block_active=False,movement='NEUTRAL',movement_strength=0.,tracking='STOPPED',fsm_state='UNAVAILABLE',calibration_version=1,calibrated=False))
            sender.close()
    workers=[threading.Thread(target=capture,name='demo-camera',daemon=True),threading.Thread(target=publisher,name='demo-publisher',daemon=True)]
    print(f'Output: {out}\nUDP destination: {a.host}:{a.port}\nStream: {stream}\nC calibrate | M movement | X pause/reset | Q finish',flush=True)
    started=time.monotonic();last_stamp=None;origin=None;last_ms=-1;pose_ms=0.
    next_preview_ns=0;cross_samples=[];recovery_samples=[];pose_samples=[];age_samples=[];framing_counts=Counter()
    prior_cv_threads=cv.getNumThreads()
    try:
        # OpenCV shares this process with MediaPipe, capture, recording and UDP.
        # Prevent small color/overlay operations from oversubscribing CPU cores.
        cv.setNumThreads(1)
        # Load inference before starting camera to avoid an initialization backlog.
        pose=PoseLandmarker.create_from_options(PoseLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(a.pose),delegate=BaseOptions.Delegate.CPU),
            running_mode=RunningMode.VIDEO,num_poses=1,min_pose_detection_confidence=.5,
            min_pose_presence_confidence=.5,min_tracking_confidence=.5))
        if cross_path:
            cross_pose=PoseLandmarker.create_from_options(PoseLandmarkerOptions(
                base_options=BaseOptions(model_asset_path=str(cross_path),delegate=BaseOptions.Delegate.CPU),
                running_mode=RunningMode.VIDEO,num_poses=1,min_pose_detection_confidence=.5,
                min_pose_presence_confidence=.5,min_tracking_confidence=.5))
        if cross_path and getattr(a,'cross_redetect',True):
            recovery_pose=PoseLandmarker.create_from_options(PoseLandmarkerOptions(
                base_options=BaseOptions(model_asset_path=str(cross_path),delegate=BaseOptions.Delegate.CPU),
                running_mode=RunningMode.IMAGE,num_poses=1,min_pose_detection_confidence=.5,
                min_pose_presence_confidence=.5,min_tracking_confidence=.5))
            recovery_pool=ThreadPoolExecutor(max_workers=1,thread_name_prefix='cross-redetect')
        def redetect(image):
            begin=now_ns();result=recovery_pose.detect(image)
            return result,begin,now_ns()
        for worker in workers:worker.start()
        while not stop.is_set() and time.monotonic()-started<a.seconds:
            item=slot.take()
            if item is None:
                if cv.waitKey(1)&255==ord('q'):break
                stop.wait(.002);continue
            fid,stamp,bgr=item
            if origin is None:origin=stamp
            ms=(stamp-origin)//1_000_000
            if ms<=last_ms:continue
            last_ms=ms;rgb=cv.cvtColor(bgr,cv.COLOR_BGR2RGB)
            if recorder is None:
                recorder=SessionRecorder(out/'sessions',subject_id=a.actor,take_id='seated-live-demo',
                    camera_index=a.camera,requested_resolution=[640,480],pose_model_path=a.pose.resolve(),
                    pose_model_variant=variant,pose_running_mode='VIDEO',mirror_display=a.mirror,
                    gesture_set_version='raised-kicks-v3',recording_config={'source_mirrored':False,
                        'engine_version':engine.config.version,'runtime_clock':runtime_clock,
                        'camera_properties':dict(camera_info),
                        'cross_pose':{'variant':'Full','sha256':hashlib.sha256(cross_path.read_bytes()).hexdigest(),'scope':'same-frame auxiliary X only'} if cross_path else None,
                        'cross_redetect':{'mode':'IMAGE','variant':'Full','scope':'same-frame X recovery only'} if recovery_pose else None,
                        'camera_requested_fps':30,'preview_target_fps':preview_fps,'opencv_threads':1,
                        'guard_model':engine.posture_model.meta if getattr(a,'guard_model',None) else None,
                        'stream_id':stream,'recording_mode':recording,'scope':'full-rate Pose + FSM; optional RGB; not human ground truth',
                        'all_successful_captures_saved':False,'frame_policy':'full-rate pose; review RGB 15fps, lossless RGB full-rate, logs no RGB'})
                recorder.update_manifest(actual_resolution=[bgr.shape[1],bgr.shape[0]],camera_backend=cap.getBackendName())
                if recording!='logs':
                    if recording=='lossless':
                        raw=PilotVideo(cv,recorder,(bgr.shape[1],bgr.shape[0]),30,'ffv1',override_fps=30)
                        raw.metadata.update(recording_scope=['DEMO'],timebase='processed frames; use original capture timestamps')
                    else:
                        from .compact_video import CompactVideo
                        raw=CompactVideo(cv,recorder,(bgr.shape[1],bgr.shape[0]),15)
                    video=AsyncPilotVideo(raw,32)
                    recorder.update_manifest(video=raw.metadata)
            # One immutable RGB sample goes to independent model instances.
            # Await this frame before publishing; no auxiliary backlog or stale reuse.
            image=mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb)
            recovery_future=recovery_pool.submit(redetect,image) if recovery_pool else None
            start=now_ns();result=pose.detect_for_video(image,ms);end=now_ns()
            cross=None;cross_data=None
            if cross_pose is not None:
                cross_start=now_ns()
                cross=cross_pose.detect_for_video(image,ms)
                cross_end=now_ns();cross_samples.append((cross_end-cross_start)/1e6)
                pts=cross.pose_landmarks[0] if cross.pose_landmarks else []
                cross_data={'variant':'Full','start_ns':cross_start,'end_ns':cross_end,
                    'landmarks':[[p.x,p.y,p.z] for p in pts], 'visibility':[p.visibility for p in pts]}
            recovery_data=None
            if recovery_future is not None:
                recovery,begin,finish=recovery_future.result()
                recovery_samples.append((finish-begin)/1e6)
                pts=recovery.pose_landmarks[0] if recovery.pose_landmarks else []
                recovery_data={'variant':'Full','mode':'IMAGE','start_ns':begin,'end_ns':finish,
                    'landmarks':[[p.x,p.y,p.z] for p in pts], 'visibility':[p.visibility for p in pts]}
            frame=recorder.record_frame(capture_return_monotonic_ns=stamp,pose_success=True,
                pose_start_monotonic_ns=start,pose_end_monotonic_ns=end,
                image_landmarks=result.pose_landmarks[0] if result.pose_landmarks else None,
                world_landmarks=result.pose_world_landmarks[0] if result.pose_world_landmarks else None,
                processing_metadata={'camera_frame_id':fid,'stream_id':stream,'cross_pose':cross_data,'cross_recovery':recovery_data})
            obs=PoseObservation.from_record(frame,{'subject_id':a.actor,'session_id':stream,
                'actual_resolution':[bgr.shape[1],bgr.shape[0]],'recording_config':{'source_mirrored':False}})
            aux=None
            if cross_data is not None:
                aux=replace(obs,landmarks=tuple(map(tuple,cross_data['landmarks'])) or None,
                    visibility=tuple(cross_data['visibility']) or None,pose_present=bool(cross_data['landmarks']))
            if recovery_data is not None:
                recovery=replace(obs,landmarks=tuple(map(tuple,recovery_data['landmarks'])) or None,
                    visibility=tuple(recovery_data['visibility']) or None,pose_present=bool(recovery_data['landmarks']))
                aux=(aux,recovery)
            state=engine.process(obs,special_observation=aux) if cross_data is not None else engine.process(obs)
            if video is not None:video.write(bgr,rgb,frame,trial_state='DEMO')
            framing=framing_status(obs,engine.config.visibility);framing_counts[framing['code']]+=1
            counts['processed']+=1;pose_ms=(end-start)/1e6
            engine_end=now_ns();pose_samples.append(pose_ms);age_samples.append((engine_end-stamp)/1e6)
            log.write({'type':'frame','frame_id':frame['frame_id'],'camera_frame_id':fid,'capture_ns':stamp,
                'pose_end_ns':end,'engine_end_ns':engine_end,'state':asdict(state),'framing':framing})
            with engine.lock:
                armed=engine.calibrated;progress=engine.calibration_progress
                arms=' / '.join(k+':'+v.state for k,v in engine.attack.units.items())
                ready=engine.ready
                cross_owned=engine.special.owned;cross_sent=engine.special.latched
                cross_progress=min(1.,engine.special.confirmed_ns/engine.special.hold_ns)
            fps=1e9/(stamp-last_stamp) if last_stamp is not None else 0;last_stamp=stamp
            # Prompts, keys, recording, recognition and UDP keep their full rate.
            guided_prompt=guide.update(frame['frame_id'],stamp,armed,recorder,ready=ready) if guide else None
            preview_now=now_ns()
            if preview_now>=next_preview_ns:
                next_preview_ns=(preview_now+preview_interval_ns if preview_now-next_preview_ns>preview_interval_ns
                    else next_preview_ns+preview_interval_ns)
                view=overlay(bgr,frame,name=f'REDBOOL {variant} VIDEO',mirror=a.mirror)
                panel=view[bgr.shape[0]:];panel[:]=0
                message=display['event'] if now_ns()-display['event_ns']<1_000_000_000 else ''
                mode=('C: CALIBRATE GUARD' if not armed else 'TRACKING LOST' if state.tracking!='VALID'
                    else 'X SENT: OPEN ARMS OR RAISE ONE' if cross_owned and cross_sent
                    else f'X HOLD: {cross_progress:.0%}' if cross_owned
                    else 'BLOCK' if state.block else 'READY' if ready else 'RELEASE LAST POSE / NEXT MOVE')
                if framing['code']!='OK':mode=framing['message']
                hint=getattr(sender,'game_hint',lambda:'GAME UNKNOWN / SPECIAL UNKNOWN')()
                prompt=guided_prompt if guide else hint
                if hasattr(engine,'trainer') and engine.trainer.context: prompt=engine.trainer.status+' | '+engine.trainer.context['action']
                lines=[f"{mode}  {progress:.0%}   {message}",prompt,
                    f"LEFT {counts['left']}  RIGHT {counts['right']}  X {counts['both']}  BLOCK {state.block}  MOVE {state.movement} ({'ON' if engine.movement_enabled else 'OFF'})",
                    f"UDP {'SYNCED' if sender.clock else 'WAITING'}  {sender.last_status}",
                    f"Pose {pose_ms:.1f}ms | {fps:.1f} FPS | capture skipped {slot.dropped} | preview {preview_fps:g}",
                    ('MIRROR VIEW' if a.mirror else 'CAMERA VIEW')+' | C: calibrate | M: move | X: pause | Q: finish']
                for i,line in enumerate(lines):
                    width=cv.getTextSize(line,0,.47,1)[0][0]
                    scale=.47*min(1.,(panel.shape[1]-12)/max(1,width))
                    color=(80,230,160) if armed else (50,200,255)
                    if i==0 and framing['code']!='OK':color=(50,170,255)
                    if guide and i==1:
                        color=(40,190,255) if guide.preparing else (80,255,80) if 'GO: ' in line else (220,220,220)
                    cv.putText(panel,line,(6,22+i*24),0,scale,color,1)
                cv.imshow('REDBOOL DEMO',view)
                counts['preview_frames']+=1
            key=cv.waitKey(1)&255
            if key==ord('q') or (guide and guide.completed):break
            if key in (ord('c'),ord('x')):
                engine.request_calibration()
                if key==ord('x'):engine.calibrating=False
                recorder.record_annotation(label='CALIBRATE' if key==ord('c') else 'PAUSE',annotation_type='marker',event_frame_id=frame['frame_id'],source='human_demo_control')
            if key==ord('m'):engine.toggle_movement()
            if key==ord(' ') and guide:guide.advance(stamp)
            if video is not None:video.check_error()
    except KeyboardInterrupt:pass
    except BaseException as exc:fail('main',exc)
    finally:
        stop.set()
        for worker in workers:
            if worker.ident is not None:worker.join(timeout=3)
        if any(w.is_alive() for w in workers):errors.append({'where':'shutdown','error':'worker did not stop'})
        if workers[1].ident is None:sender.close()
        if recovery_pool is not None:recovery_pool.shutdown(wait=True)
        if recovery_pose is not None:recovery_pose.close()
        if pose is not None:pose.close()
        if cross_pose is not None:cross_pose.close()
        cv.destroyAllWindows()
        for item in engine.take_logs():log.write({'type':'engine','log':item})
        log.close()
        if log.error or log.dropped:errors.append({'where':'log','error':log.error,'dropped':log.dropped})
        print("촬영 종료. 기록을 마무리합니다. 원본 데이터는 삭제하지 않습니다.",flush=True)
        if video is not None:
            try:video.close()
            except BaseException as exc:errors.append({'where':'video','error':repr(exc)})
        if recorder is not None:
            if guide:guide.finish(recorder)
            recorder.finalize(completed=not errors,metadata={'errors':errors,'camera_slot_drops':slot.dropped,
                'video_diagnostics':video.diagnostics() if video else None})
        cv.setNumThreads(prior_cv_threads)
        performance={'camera_requested_fps':30,'preview_target_fps':preview_fps,
            'runtime_clock':runtime_clock,'acquisition':acquisition.summary(),'camera_properties':camera_info,
            'processed_fps':(counts['processed']-1)*1e9/(last_stamp-origin) if last_stamp is not None and last_stamp>origin else 0.,
            'cross_redetect_ms_p50_p95':np.percentile(recovery_samples,[50,95]).tolist() if recovery_samples else [],
            'cross_pose_ms_p50_p95':np.percentile(cross_samples,[50,95]).tolist() if cross_samples else [],
            'pose_ms_p50_p95':np.percentile(pose_samples,[50,95]).tolist() if pose_samples else [],
            'capture_to_engine_ms_p50_p95':np.percentile(age_samples,[50,95]).tolist() if age_samples else [],
            'opencv_threads':1,'frame_policy':'latest camera sample; preview throttling never skips recognition or recording'}
        report={'performance':performance,'framing':dict(framing_counts),'stream_id':stream,'counts':counts,'delivery':dict(sender.delivery),'errors':errors,
            'camera_slot_drops':slot.dropped,'duration_seconds':time.monotonic()-started,
            'recording_mode':recording,'session':str(recorder.directory) if recorder else None,'unity_screen_verified':False,
            'guided_check':{'completed':guide.completed,'interrupted':guide.interrupted,'steps_finished':guide.index,'self_paced':guide.self_paced} if guide else None}
        (out/'summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
    if hasattr(engine,'profile'):
        (out/'personal_profile.json').write_text(json.dumps(engine.profile(),indent=2))
    if errors:raise RuntimeError(errors)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,default=Path('local_data/demo_20261103'))
    p.add_argument('--pose',type=Path)
    p.add_argument('--pose-variant',choices=['Lite','Full'],default='Lite')
    p.add_argument('--cross-pose',type=Path,help='Optional Full asset for same-frame crossed-arm geometry only')
    p.add_argument('--cross-redetect',action=argparse.BooleanOptionalAction,default=True,help='Same-frame independent Full detection for X recovery; requires --cross-pose')
    p.add_argument('--personal-calibration',action='store_true',help='Bounded per-person tutorial ranges; C resets the profile')
    p.add_argument('--guard-model',type=Path,help='Optional learned confirmation for relaxed guard; match Pose variant')
    p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065)
    p.add_argument('--actor',default='DEV01');p.add_argument('--stream-id');p.add_argument('--camera',type=int,default=0)
    p.add_argument('--backend',default='auto');p.add_argument('--seconds',type=float,default=180)
    p.add_argument('--guided',action='store_true',help='Self-paced prompts: SPACE advances after each action/guard; saves instructions, not ground truth')
    p.add_argument('--guided-focus',choices=['all','left-return'],default='all',help='Short five-repeat left raise and return check')
    p.add_argument('--guided-timed',action='store_true',help='Use legacy fixed-duration prompts instead of SPACE')
    p.add_argument('--movement',action='store_true');p.add_argument('--mirror',action=argparse.BooleanOptionalAction,default=True,
        help='Mirror only the preview (default); --no-mirror shows the original camera view. Recognition and recording stay unmirrored.')
    p.add_argument('--preview-fps',type=float,default=15.,help='Camera window refresh only; recognition remains full rate (1-60)')
    p.add_argument('--recording',choices=['review','logs','lossless'],default='review',help='review: compressed 15fps MP4; logs: no video; lossless: full RGB. Pose inference stays 30fps.')
    p.add_argument('--check',action='store_true');a=p.parse_args()
    if a.pose is None:
        a.pose=Path('pose_landmarker.task' if a.pose_variant=='Lite' else 'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task')
    import math
    if not math.isfinite(a.seconds) or a.seconds<=0 or not 1<=a.port<=65535:p.error('Positive seconds and valid UDP port required')
    run(a)

if __name__=='__main__':main()
