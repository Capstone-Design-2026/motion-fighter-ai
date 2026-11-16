"""Explicitly launched demo: camera + configurable review recording + FSM + UDP v2.

The capture latest-slot avoids latency backlog; skipped camera samples are
counted, never silently represented as recorded frames. The recorder stores
every processed Pose observation. Review mode samples compressed RGB at 15 fps. No camera is opened by --check.
"""
import argparse
import copy
import hashlib
from dataclasses import asdict, replace
from datetime import datetime
import json
import sys
from pathlib import Path
import threading
import time
import uuid
from .six_engine import SixGestureEngine as DemoEngine
from .transport import DemoSender
from realtime_vision.transport import network_policy
from realtime_vision.clock import now_ns, metadata as clock_metadata
from .camera_diagnostics import CaptureCadence, camera_properties
from .camera_profile import eligible as profile_eligible, select_startup_profile, format_name
from .key_input import CalibrationKeyGate
from .performance_policy import InferencePolicy
from .guided import GuidedCheck
from .framing import framing_status
from collections import Counter
from concurrent.futures import ThreadPoolExecutor


def _print_status(*values, **options):
    """A terminal encoding/closed pipe must never interrupt capture cleanup."""
    try:
        print(*values, **options)
    except (UnicodeError, OSError, ValueError):
        try:
            print(*(str(v).encode('ascii','backslashreplace').decode('ascii') for v in values), **options)
        except (UnicodeError, OSError, ValueError):
            pass


def run(a):
    import numpy as np
    import cv2 as cv
    import mediapipe as mp
    from mediapipe.tasks.python import BaseOptions
    from mediapipe.tasks.python.vision import PoseLandmarker,PoseLandmarkerOptions,RunningMode
    from action_engine.types import PoseObservation
    from continuous_events.protocol import verify_pose_asset
    if getattr(sys,'frozen',False):
        from .backend_assets import verify_pose_asset
    from continuous_events.viewer import overlay
    from session_recording import SessionRecorder
    from collect_gesture_pilot import PilotVideo,AsyncPilotVideo
    from pose_capture import open_camera
    from realtime_vision.live import Latest,AsyncLog
    headless=getattr(a,'headless',False)
    bridge=getattr(a,'bridge_instance',None)
    recording=getattr(a,'recording','lossless')
    if recording not in ('review','logs','lossless','diagnostics'):raise ValueError('Unknown recording mode')
    preview_fps=getattr(a,'preview_fps',15.)
    if not 1<=preview_fps<=60:raise ValueError('preview_fps must be between 1 and 60')
    if headless and preview_fps>15:raise ValueError('Headless preview_fps must not exceed15')
    preview_interval_ns=int(1e9/preview_fps)
    journal=None; journal_clock=None
    engine_clock=now_ns
    if recording=='diagnostics':
        from .landmark_recording import LandmarkJournal,ObservedClock,record_operation
        journal_clock=ObservedClock(now_ns);engine_clock=journal_clock
    variant=getattr(a,'pose_variant','Lite')
    verify_pose_asset(a.pose,variant)
    cross_path=getattr(a,"cross_pose",None)
    if cross_path:verify_pose_asset(cross_path,"Full")
    refine_path=getattr(a,'cross_refine_pose',None)
    refine_metadata=None
    if refine_path:
        from .backend_assets import verify_pose_asset as verify_optional,POSE_SHA256,HEAVY_MODEL_URL
        verify_optional(refine_path,'Heavy')
        if not cross_path or not getattr(a,'cross_redetect',True) or getattr(a,'guard_model',None):
            raise ValueError('Optional Heavy requires the existing Full VIDEO/IMAGE calibrated-rule path')
        refine_metadata=dict(variant='Heavy',mode='IMAGE',sha256=POSE_SHA256['Heavy'],
            source_url=HEAVY_MODEL_URL,default_enabled=False,scope='optional bounded same-frame X refinement')
    inference_runtime=InferencePolicy(bool(cross_path))
    inference_policy=inference_runtime.description()
    if getattr(a,'guard_model',None):
        from .learned import LearnedGuardEngine
        engine=LearnedGuardEngine(a.guard_model,movement=a.movement,clock=engine_clock)
        trained_variant=engine.posture_model.meta['provenance']['pose_variant']
        if trained_variant!=variant:raise ValueError('Guard model and Pose variant do not match')
    else:
        engine=DemoEngine(movement=a.movement,clock=engine_clock)
    if getattr(a,'personal_calibration',False):
        if getattr(a,'guard_model',None): raise ValueError('Personal calibration requires v26 rules')
        from .personal import PersonalGestureEngine
        engine=PersonalGestureEngine(movement=a.movement,clock=engine_clock)
    if cross_path and getattr(a,"guard_model",None):
        raise ValueError("Auxiliary cross pose is supported by the calibrated engine only")
    if a.check:
        mode='learned guard confirmation' if getattr(a,'guard_model',None) else 'calibrated rules'
        _print_status(f'Demo modules, {variant} asset and {mode} OK. Camera/UDP not opened.')
        if refine_metadata:_print_status(json.dumps({'verified_optional_refine':refine_metadata}))
        return
    stream=a.stream_id or uuid.uuid4().hex
    out=a.output/(datetime.now().strftime('%Y%m%d_%H%M%S')+'_'+stream[:8])
    out.mkdir(parents=True,exist_ok=False)
    network_profile=getattr(a,'network_profile','lan')
    sender=DemoSender(a.host,a.port,stream,a.actor,clock_ns=now_ns,network_profile=network_profile)
    log=AsyncLog(out/'trace.jsonl')
    if recording=='diagnostics':
        from .diagnostics_recording import DiagnosticsRecorder,DiagnosticLog
        SessionRecorder=DiagnosticsRecorder
        log=DiagnosticLog(log)
    slot=Latest();stop=threading.Event();errors=[]
    runtime_clock={**clock_metadata(),'origin_ns':now_ns(),'origin_utc':datetime.now().astimezone().isoformat()}
    log.write({'type':'runtime_clock',**runtime_clock})
    log.write({'type':'network_profile',**network_policy(network_profile)})
    if recording=='diagnostics':
        from .backend_provenance import capabilities
        capability=capabilities()
        journal=LandmarkJournal(out/'recognition')
        journal.offer('header',schema='redbool.engine-operations',version=1,
            engine=type(engine).__name__,engine_module=type(engine).__module__,
            run_id=engine.run_id,config=asdict(engine.config),movement=engine.movement_enabled,
            actor_id=a.actor,capture_epoch=stream,runtime_clock=runtime_clock,
            primary_model=dict(variant=variant,sha256=hashlib.sha256(a.pose.read_bytes()).hexdigest()),
            auxiliary_model=dict(variant='Full',sha256=hashlib.sha256(cross_path.read_bytes()).hexdigest()) if cross_path else None,
            optional_refine_model=refine_metadata,
            source_mirrored=False,raw_rgb_saved=False,inference_policy=inference_policy,
            implementation={k:capability[k] for k in ('release','recording_schema','source_commit',
                'source_sha256','source_dirty','source_file_count')})
    acquisition=CaptureCadence();camera_info={};calibration_keys=CalibrationKeyGate()
    counts={'captured':0,'processed':0,'events':0,'left':0,'right':0,'both':0,'preview_frames':0}
    guide=GuidedCheck(movement=a.movement, self_paced=not getattr(a,'guided_timed',False), focus=getattr(a,'guided_focus','all')) if getattr(a,'guided',False) else None
    display={'event':'NONE','event_ns':0};cap=None;pose=None;cross_pose=None;recovery_pose=None;recovery_pool=None;video=None;recorder=None
    refine=None;refine_pending_since=None
    def fail(where,exc):
        errors.append({'where':where,'error':repr(exc)});stop.set()
        if bridge:bridge.update(phase='error',error=str(exc))
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
            if profile_eligible(cap):
                if bridge:bridge.update(camera_connected=False,phase='optimizing_camera')
                profile=select_startup_profile(cv,cap,stop,clock_ns=now_ns)
                camera_info['startup_profile']=profile
                if stop.is_set():return
                camera_info['selected']=camera_properties(cv,cap)
                log.write({'type':'camera_profile','timestamp_ns':now_ns(),**profile})
            else:profile=None
            selected=camera_info.get('selected',camera_info['initial'])
            camera_profile=str(selected.get('backend') or 'UNKNOWN')+'/'+format_name(selected.get('fourcc'))
            log.write({'type':'camera_settings','timestamp_ns':now_ns(),**camera_info})
            if bridge:bridge.update(camera_connected=True,phase='waiting_calibration',
                camera_profile=camera_profile,camera_slow=bool(profile and profile['camera_slow']),
                acquisition_fps=profile['acquisition_fps'] if profile else None)
            while not stop.is_set():
                read_start=now_ns();ok,bgr=cap.read();stamp=now_ns()
                if not ok:raise RuntimeError('Camera read failed')
                counts['captured']+=1;slot.put((counts['captured'],stamp,bgr.copy()))
                window=acquisition.observe(stamp,read_start)
                if window is not None:
                    log.write({'type':'camera_runtime',**window,'properties':camera_properties(cv,cap)})
                    if bridge:bridge.update(acquisition_fps=window['measured_fps'],
                        camera_slow=window['measured_fps']<15,camera_profile=camera_profile)
        except BaseException as exc:fail('camera',exc)
        finally:
            if cap is not None:
                camera_info['final']=camera_properties(cv,cap)
                cap.release()
    def publisher():
        try:
            while not stop.wait(.01):
                if bridge and bridge.stop_requested.is_set():stop.set();break
                with engine.lock:
                    if journal:
                        # Keep original tick/drain/context order and each actual
                        # clock read. No inference, expiry or context is replayed live.
                        context_payload={}
                        def publish_operation():
                            engine.tick();result=engine.drain_events()
                            if hasattr(engine,'tutorial_context'):
                                context=sender.tutorial_context();context_ns=now_ns()
                                context_payload.update(context=context,context_ns=context_ns)
                                engine.tutorial_context(context,context_ns)
                            return result
                        events=record_operation(journal,journal_clock,engine,'publisher',
                            publish_operation,context=context_payload)
                        state=engine.control
                    else:
                        engine.tick();events=engine.drain_events();state=engine.control
                        if hasattr(engine,'tutorial_context'):
                            engine.tutorial_context(sender.tutorial_context(),now_ns())
                    stamp=engine.last_capture;logs=engine.take_logs();armed=engine.calibrated
                    calibration_generation=getattr(engine,'calibration_generation',0)
                for item in logs:log.write({'type':'engine','log':item})
                if not armed:sender.events.clear()
                for ev in events:
                    counts['events']+=1;counts[ev.side.lower()]+=1
                    gesture=ev.reason.get('gesture',ev.side+'_PUNCH')
                    counts[gesture]=counts.get(gesture,0)+1
                    display.update(event=gesture,event_ns=ev.committed_ns)
                    if network_profile=='online':sender.offer(ev,calibration_generation=calibration_generation)
                    else:sender.offer(ev)
                now=now_ns();fresh=stamp is not None and 0<=now-stamp<150_000_000
                if sender.clock:
                    ttl=max(1,min(150,int((stamp+150_000_000-now)//1_000_000))) if fresh else 150
                    log.write(sender.send('STATE',ttl_ms=ttl,block_active=bool(state.block and fresh),
                        movement=state.movement if fresh else 'NEUTRAL',movement_strength=float(state.movement_strength) if fresh else 0.,
                        tracking=state.tracking if fresh else "STALE",fsm_state=state.attack_state,capture_ns=stamp,
                        calibration_version=1,calibrated=bool(armed),
                        calibration_generation=calibration_generation if network_profile=='online' else getattr(engine,'calibration_generation',0),
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
    _print_status(f'Output: {out}\nUDP destination: {a.host}:{a.port}\nStream: {stream}\nC calibrate | M movement | X pause/reset | Q finish',flush=True)
    started=time.monotonic();last_stamp=None;origin=None;last_ms=-1;pose_ms=0.
    next_preview_ns=0;cross_samples=[];recovery_samples=[];pose_samples=[];age_samples=[];framing_counts=Counter()
    auxiliary_calibration_skipped_frames=0;auxiliary_overload_skipped_frames=0
    prior_cv_threads=cv.getNumThreads()
    try:
        if headless and bridge is None:
            from .local_bridge import LocalBridge,jpeg_encoder
            bridge=LocalBridge(a.bridge_port,a.bridge_token,preview_fps=preview_fps,
                parent_pid=getattr(a,'parent_pid',None),encoder=jpeg_encoder).start()
        if bridge:bridge.update(stream_id=stream,output=str(out.resolve()))
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
        if refine_path:
            from .heavy_auxiliary import BoundedHeavyAuxiliary,FrameKey,refinement_owned
            def load_refine():
                return PoseLandmarker.create_from_options(PoseLandmarkerOptions(
                    base_options=BaseOptions(model_asset_path=str(refine_path),delegate=BaseOptions.Delegate.CPU),
                    running_mode=RunningMode.IMAGE,num_poses=1,min_pose_detection_confidence=.5,
                    min_pose_presence_confidence=.5,min_tracking_confidence=.5))
            def warm_refine(model):
                model.detect(mp.Image(image_format=mp.ImageFormat.SRGB,data=np.zeros((256,256,3),np.uint8)))
            refine=BoundedHeavyAuxiliary(load_refine,lambda model,image:model.detect(image),
                warm=warm_refine,clock_ns=now_ns)
        if bridge and bridge.stop_requested.is_set():stop.set()
        for worker in workers:
            if not stop.is_set():worker.start()
        while not stop.is_set() and time.monotonic()-started<a.seconds:
            if bridge and bridge.stop_requested.is_set():break
            item=slot.take()
            if item is None:
                if not headless and cv.waitKey(1)&255==ord('q'):break
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
                        'cross_refine':refine_metadata,
                        'camera_requested_fps':30,'preview_target_fps':preview_fps,'opencv_threads':1,
                        'inference_policy':inference_policy,
                        'guard_model':engine.posture_model.meta if getattr(a,'guard_model',None) else None,
                        'stream_id':stream,'recording_mode':recording,'scope':'full-rate Pose + FSM; optional RGB; not human ground truth',
                        'all_successful_captures_saved':False,'frame_policy':'full-rate pose; review RGB 15fps, lossless RGB full-rate, logs no RGB'})
                recorder.update_manifest(actual_resolution=[bgr.shape[1],bgr.shape[0]],camera_backend=cap.getBackendName())
                if journal:
                    journal.offer('session',manifest=copy.deepcopy(recorder.manifest))
                if recording not in ('logs','diagnostics'):
                    if recording=='lossless':
                        raw=PilotVideo(cv,recorder,(bgr.shape[1],bgr.shape[0]),30,'ffv1',override_fps=30)
                        raw.metadata.update(recording_scope=['DEMO'],timebase='processed frames; use original capture timestamps')
                    else:
                        from .compact_video import CompactVideo
                        raw=CompactVideo(cv,recorder,(bgr.shape[1],bgr.shape[0]),15)
                    video=AsyncPilotVideo(raw,32)
                    recorder.update_manifest(video=raw.metadata)
            # Guard calibration consumes the primary observation only; no X
            # action can fire yet. Keep its critical path free of auxiliary
            # models. Configured auxiliaries resume after calibration unless
            # sustained overload latched them off; never reuse old observations.
            with engine.lock:calibrated_at_frame_start=engine.calibrated
            run_auxiliary=inference_runtime.run_auxiliary(calibrated_at_frame_start)
            if cross_pose is not None and not run_auxiliary:
                if inference_runtime.lite_only:auxiliary_overload_skipped_frames+=1
                else:auxiliary_calibration_skipped_frames+=1
            image=mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb)
            refine_ticket=None;refine_submitted=False
            if refine is not None:
                with engine.lock:
                    owned=refinement_owned(owned=engine.special.owned,latched=engine.special.latched,
                        refinement_latched=engine.cross_refinement_latched)
                    latched=engine.special.latched or engine.cross_refinement_latched
                    if not owned:refine_pending_since=None
                    elif refine_pending_since is None:refine_pending_since=engine.last_capture
                    generation=getattr(engine,'calibration_generation',0)
                key=FrameKey(a.actor,stream,counts['processed']+1,stamp,
                    bgr.shape[1],bgr.shape[0],False,generation)
                refine_ticket=refine.plan(key,calibrated=calibrated_at_frame_start,
                    owned=owned,latched=latched,pending_since_ns=refine_pending_since)
                if refine_ticket is not None:refine_submitted=refine.submit(refine_ticket,image)
            recovery_future=recovery_pool.submit(redetect,image) if recovery_pool and run_auxiliary else None
            start=now_ns();result=pose.detect_for_video(image,ms);end=now_ns()
            cross=None;cross_data=None
            if cross_pose is not None and run_auxiliary:
                cross_start=now_ns()
                cross=cross_pose.detect_for_video(image,ms)
                cross_end=now_ns();cross_samples.append((cross_end-cross_start)/1e6)
                pts=cross.pose_landmarks[0] if cross.pose_landmarks else []
                cross_data={'variant':'Full','start_ns':cross_start,'end_ns':cross_end,
                    'landmarks':[[p.x,p.y,p.z] for p in pts], 'visibility':[p.visibility for p in pts],
                    'presence':[getattr(p,'presence',None) for p in pts]}
                if journal:
                    world=cross.pose_world_landmarks[0] if cross.pose_world_landmarks else []
                    cross_data['world']=[[p.x,p.y,p.z,getattr(p,'visibility',None),getattr(p,'presence',None)] for p in world]
            recovery_data=None
            if recovery_future is not None:
                recovery,begin,finish=recovery_future.result()
                recovery_samples.append((finish-begin)/1e6)
                pts=recovery.pose_landmarks[0] if recovery.pose_landmarks else []
                recovery_data={'variant':'Full','mode':'IMAGE','start_ns':begin,'end_ns':finish,
                    'landmarks':[[p.x,p.y,p.z] for p in pts], 'visibility':[p.visibility for p in pts],
                    'presence':[getattr(p,'presence',None) for p in pts]}
                if journal:
                    world=recovery.pose_world_landmarks[0] if recovery.pose_world_landmarks else []
                    recovery_data['world']=[[p.x,p.y,p.z,getattr(p,'visibility',None),getattr(p,'presence',None)] for p in world]
            refine_data=None
            if refine_submitted:
                refined=refine.result(refine_ticket)
                if refined is not None:
                    pts=refined.value.pose_landmarks[0] if refined.value.pose_landmarks else []
                    world=refined.value.pose_world_landmarks[0] if refined.value.pose_world_landmarks else []
                    refine_data={**refine_metadata,'start_ns':refined.started_ns,'end_ns':refined.finished_ns,
                        'source':asdict(refined.key),'worker_generation':refined.generation,
                        'landmarks':[[p.x,p.y,p.z] for p in pts],'visibility':[p.visibility for p in pts],
                        'presence':[getattr(p,'presence',None) for p in pts],
                        'world':[[p.x,p.y,p.z,getattr(p,'visibility',None),getattr(p,'presence',None)] for p in world]}
            frame=recorder.record_frame(capture_return_monotonic_ns=stamp,pose_success=True,
                pose_start_monotonic_ns=start,pose_end_monotonic_ns=end,
                image_landmarks=result.pose_landmarks[0] if result.pose_landmarks else None,
                world_landmarks=result.pose_world_landmarks[0] if result.pose_world_landmarks else None,
                processing_metadata={'camera_frame_id':fid,'stream_id':stream,'cross_pose':cross_data,'cross_recovery':recovery_data,
                    **({'cross_refine':refine_data} if refine is not None else {})})
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
            if refine_data is not None:
                expected=(obs.actor_id,obs.capture_epoch,obs.frame_id,obs.capture_ns,obs.width,obs.height,obs.source_mirrored)
                actual=tuple(refine_data['source'][k] for k in ('actor_id','capture_epoch','frame_id','capture_ns','width','height','source_mirrored'))
                if expected!=actual:raise RuntimeError('Optional Heavy frame identity mismatch')
                heavy=replace(obs,landmarks=tuple(map(tuple,refine_data['landmarks'])) or None,
                    visibility=tuple(refine_data['visibility']) or None,pose_present=bool(refine_data['landmarks']))
                aux=(*aux,heavy) if isinstance(aux,tuple) else (aux,None,heavy)
            if journal:
                state=record_operation(journal,journal_clock,engine,'process',
                    lambda: engine.process(obs,special_observation=aux) if cross_data is not None else engine.process(obs),
                    frame=frame,observation=obs,has_auxiliary=cross_data is not None,
                    raw_models={'cross_pose':cross_data,'cross_recovery':recovery_data,
                        **({'cross_refine':refine_data} if refine is not None else {})})
            else:
                state=engine.process(obs,special_observation=aux) if cross_data is not None else engine.process(obs)
            if video is not None:video.write(bgr,rgb,frame,trial_state='DEMO')
            framing=framing_status(obs,engine.config.visibility);framing_counts[framing['code']]+=1
            counts['processed']+=1;pose_ms=(end-start)/1e6
            engine_end=now_ns();pose_samples.append(pose_ms);age_samples.append((engine_end-stamp)/1e6)
            auxiliary_end=max([end]+[m['end_ns'] for m in (cross_data,recovery_data) if m is not None])
            transition=inference_runtime.observe(auxiliary_ran=run_auxiliary,
                capture_ns=stamp,primary_end_ns=end,auxiliary_end_ns=auxiliary_end,processing_end_ns=engine_end)
            if transition:
                if bridge and inference_runtime.lite_only:
                    bridge.limit_preview_fps(8.)
                log.write({'type':'inference_policy','frame_id':frame['frame_id'],
                    'capture_ns':stamp,'timestamp_ns':engine_end,**transition})
            if refine is not None:
                refine.observe_performance(capture_ns=stamp,primary_end_ns=end,processing_end_ns=engine_end,
                    calibrated=engine.calibrated,overload=inference_runtime.lite_only)
            log.write({'type':'frame','frame_id':frame['frame_id'],'camera_frame_id':fid,'capture_ns':stamp,
                'pose_end_ns':end,'engine_end_ns':engine_end,'state':asdict(state),'framing':framing})
            with engine.lock:
                armed=engine.calibrated;progress=engine.calibration_progress
                calibrating=engine.calibrating
                calibration_hint=engine.calibration_feedback.message
                calibration_reason=engine.calibration_feedback.reason
                arms=' / '.join(k+':'+v.state for k,v in engine.attack.units.items())
                ready=engine.ready
                cross_owned=engine.special.owned;cross_sent=engine.special.latched
                cross_progress=min(1.,engine.special.confirmed_ns/engine.special.hold_ns)
            fps=1e9/(stamp-last_stamp) if last_stamp is not None else 0;last_stamp=stamp
            # Prompts, keys, recording, recognition and UDP keep their full rate.
            guided_prompt=guide.update(frame['frame_id'],stamp,armed,recorder,ready=ready) if guide else None
            preview_now=now_ns()
            if bridge:
                bridge.update(phase='calibrating' if calibrating else 'ready' if armed else 'waiting_calibration',
                    calibrated=armed,calibrating=calibrating,calibration_progress=progress,
                    calibration_hint=calibration_hint,calibration_reason=calibration_reason,
                    tracking=state.tracking,framing=framing,framing_code=framing['code'],framing_message=framing['message'],
                    frame_id=frame['frame_id'],capture_ns=stamp,ready=ready,block=state.block,
                    movement=state.movement,event=display['event'],pose_ms=pose_ms,capture_fps=fps,
                    special_progress=cross_progress,special_owned=cross_owned,special_sent=cross_sent,
                    personal_status=engine.trainer.status if hasattr(engine,'trainer') else 'BASELINE V26',
                    udp_synced=bool(sender.clock),last_status=sender.last_status,
                    recording=journal.health() if journal else None,
                    inference_mode=inference_runtime.snapshot(armed)['inference_mode'],
                    inference_warning=inference_runtime.snapshot(armed)['inference_warning'])
                landmarks=tuple((p.x,p.y,p.visibility) for p in result.pose_landmarks[0]) if result.pose_landmarks else None
                bridge.offer_preview(bgr,stamp,mirror=a.mirror,landmarks=landmarks)
            if not headless and preview_now>=next_preview_ns:
                next_preview_ns=(preview_now+preview_interval_ns if preview_now-next_preview_ns>preview_interval_ns
                    else next_preview_ns+preview_interval_ns)
                view=overlay(bgr,frame,name=f'REDBOOL {variant} VIDEO',mirror=a.mirror)
                panel=view[bgr.shape[0]:];panel[:]=0
                message=display['event'] if now_ns()-display['event_ns']<1_000_000_000 else ''
                mode=('HOLD GUARD - CALIBRATING' if calibrating
                    else 'C: START CALIBRATION' if not armed else 'TRACKING LOST' if state.tracking!='VALID'
                    else 'X SENT: OPEN ARMS OR RAISE ONE' if cross_owned and cross_sent
                    else f'X HOLD: {cross_progress:.0%}' if cross_owned
                    else 'BLOCK' if state.block else 'READY' if ready else 'RELEASE LAST POSE / NEXT MOVE')
                if not calibrating and framing['code']!='OK':mode=framing['message']
                hint=getattr(sender,'game_hint',lambda:'GAME UNKNOWN / SPECIAL UNKNOWN')()
                prompt=guided_prompt if guide else hint
                if hasattr(engine,'trainer') and engine.trainer.context: prompt=engine.trainer.status+' | '+engine.trainer.context['action']
                if calibrating:prompt=calibration_hint
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
            command=bridge.take_command() if bridge else None
            key=({'calibrate':ord('c'),'pause':ord('x')}.get(command,-1) if headless else cv.waitKey(1)&255)
            if key==ord('q') or (guide and guide.completed):break
            if (key==ord('c') and calibration_keys.accept(now_ns())) or key==ord('x'):
                if journal:
                    def reset_operation():
                        engine.request_calibration()
                        if key==ord('x'):engine.calibrating=False
                    record_operation(journal,journal_clock,engine,
                        'calibrate' if key==ord('c') else 'pause',reset_operation,
                        after_frame_id=frame['frame_id'])
                else:
                    engine.request_calibration()
                    if key==ord('x'):engine.calibrating=False
                if refine is not None:
                    refine.reset('calibrate' if key==ord('c') else 'pause');refine_pending_since=None
                recorder.record_annotation(label='CALIBRATE' if key==ord('c') else 'PAUSE',annotation_type='marker',event_frame_id=frame['frame_id'],source='unity_game_control' if headless else 'human_demo_control')
            if key==ord('m'):
                if journal:record_operation(journal,journal_clock,engine,'movement',engine.toggle_movement)
                else:engine.toggle_movement()
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
        if refine is not None:refine.close()
        if recovery_pool is not None:recovery_pool.shutdown(wait=True)
        if recovery_pose is not None:recovery_pose.close()
        if pose is not None:pose.close()
        if cross_pose is not None:cross_pose.close()
        if not headless:cv.destroyAllWindows()
        for item in engine.take_logs():log.write({'type':'engine','log':item})
        log.close()
        if log.error or log.dropped:errors.append({'where':'log','error':log.error,'dropped':log.dropped})
        _print_status("촬영 종료. 기록을 마무리합니다. 원본 데이터는 삭제하지 않습니다.",flush=True)
        if video is not None:
            try:video.close()
            except BaseException as exc:errors.append({'where':'video','error':repr(exc)})
        recording_health=journal.close(completed=not errors) if journal else None
        if recorder is not None:
            if guide:guide.finish(recorder)
            recorder.finalize(completed=not errors,metadata={'errors':errors,'camera_slot_drops':slot.dropped,
                'video_diagnostics':video.diagnostics() if video else None,'recording':recording_health})
        cv.setNumThreads(prior_cv_threads)
        if bridge:
            bridge.update(phase='error' if errors else 'stopped',camera_connected=False,recording=recording_health)
            counts['preview_frames']=bridge.snapshot()['preview_encoded']
            bridge.close()
        performance={'camera_requested_fps':30,'preview_requested_fps':preview_fps,
            'preview_target_fps':bridge.snapshot()['preview_target_fps'] if bridge else preview_fps,
            'headless':headless,'local_preview':bridge.snapshot() if bridge else None,
            'inference_policy':inference_policy,'auxiliary_frames':len(cross_samples),
            'auxiliary_calibration_skipped_frames':auxiliary_calibration_skipped_frames,
            'auxiliary_overload_skipped_frames':auxiliary_overload_skipped_frames,
            'inference_runtime':inference_runtime.snapshot(engine.calibrated),
            'optional_refine':refine.snapshot() if refine is not None else None,
            'runtime_clock':runtime_clock,'acquisition':acquisition.summary(),'camera_properties':camera_info,
            'processed_fps':(counts['processed']-1)*1e9/(last_stamp-origin) if last_stamp is not None and last_stamp>origin else 0.,
            'cross_redetect_ms_p50_p95':np.percentile(recovery_samples,[50,95]).tolist() if recovery_samples else [],
            'cross_pose_ms_p50_p95':np.percentile(cross_samples,[50,95]).tolist() if cross_samples else [],
            'pose_ms_p50_p95':np.percentile(pose_samples,[50,95]).tolist() if pose_samples else [],
            'capture_to_engine_ms_p50_p95':np.percentile(age_samples,[50,95]).tolist() if age_samples else [],
            'opencv_threads':1,'frame_policy':'latest camera sample; preview throttling never skips recognition or recording'}
        report={'performance':performance,'framing':dict(framing_counts),'stream_id':stream,'counts':counts,'delivery':dict(sender.delivery),'errors':errors,
            'transport':sender.transport_diagnostics() if network_profile=='online' else network_policy(network_profile),
            'camera_slot_drops':slot.dropped,'duration_seconds':time.monotonic()-started,
            'recording_mode':recording,'recording':recording_health,'session':str(recorder.directory) if recorder else None,'unity_screen_verified':False,
            'guided_check':{'completed':guide.completed,'interrupted':guide.interrupted,'steps_finished':guide.index,'self_paced':guide.self_paced} if guide else None}
        (out/'summary.json').write_text(json.dumps(report,indent=2));_print_status(json.dumps(report,indent=2))
    if hasattr(engine,'profile'):
        (out/'personal_profile.json').write_text(json.dumps(engine.profile(),indent=2))
    if errors:raise RuntimeError(errors)


def main(argv=None,bridge_instance=None):
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,default=Path('local_data/demo_20261103'))
    p.add_argument('--pose',type=Path)
    p.add_argument('--pose-variant',choices=['Lite','Full'],default='Lite')
    p.add_argument('--cross-pose',type=Path,help='Optional Full asset for same-frame crossed-arm geometry only')
    p.add_argument('--cross-refine-pose',type=Path,help='Opt-in verified Heavy for bounded same-frame X refinement; default off')
    p.add_argument('--cross-redetect',action=argparse.BooleanOptionalAction,default=True,help='Same-frame independent Full detection for X recovery; requires --cross-pose')
    p.add_argument('--personal-calibration',action='store_true',help='Bounded per-person tutorial ranges; C resets the profile')
    p.add_argument('--guard-model',type=Path,help='Optional learned confirmation for relaxed guard; match Pose variant')
    p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065)
    p.add_argument('--network-profile',choices=['lan','online'],default='lan',
        help='Transport clock policy only; online permits higher uncertainty without extending input/event freshness')
    p.add_argument('--actor',default='DEV01');p.add_argument('--stream-id');p.add_argument('--camera',type=int,default=0)
    p.add_argument('--backend',default='auto');p.add_argument('--seconds',type=float,default=180)
    p.add_argument('--guided',action='store_true',help='Self-paced prompts: SPACE advances after each action/guard; saves instructions, not ground truth')
    p.add_argument('--guided-focus',choices=['all','left-return'],default='all',help='Short five-repeat left raise and return check')
    p.add_argument('--guided-timed',action='store_true',help='Use legacy fixed-duration prompts instead of SPACE')
    p.add_argument('--movement',action='store_true');p.add_argument('--mirror',action=argparse.BooleanOptionalAction,default=True,
        help='Mirror only the preview (default); --no-mirror shows the original camera view. Recognition and recording stay unmirrored.')
    p.add_argument('--preview-fps',type=float,default=15.,help='Camera window refresh only; recognition remains full rate (1-60)')
    p.add_argument('--recording',choices=['review','logs','lossless','diagnostics'],default='review',help='review: compressed 15fps MP4; logs: saved poses only; diagnostics: full-rate compressed landmarks and events, no RGB; lossless: full RGB. Inference stays full rate.')
    p.add_argument('--headless',action='store_true',help='Unity-owned loopback controls and preview; no OpenCV window')
    p.add_argument('--bridge-port',type=int,default=0)
    p.add_argument('--bridge-token',default='')
    p.add_argument('--parent-pid',type=int,help='Stop when the owning Unity game process exits')
    p.add_argument('--check',action='store_true');a=p.parse_args(argv)
    a.bridge_instance=bridge_instance
    if a.headless and not a.check:
        if not 1<=a.bridge_port<=65535 or len(a.bridge_token)<16 or not a.parent_pid:
            p.error('Headless launch requires bridge-port, bridge-token and owning parent-pid')
    if a.pose is None:
        a.pose=Path('pose_landmarker.task' if a.pose_variant=='Lite' else 'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task')
    import math
    if not math.isfinite(a.seconds) or a.seconds<=0 or not 1<=a.port<=65535:p.error('Positive seconds and valid UDP port required')
    run(a)

if __name__=='__main__':main()
