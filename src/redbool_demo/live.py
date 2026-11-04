"""Explicitly launched demo: camera + lossless processed RGB + FSM + UDP v2.

The capture latest-slot avoids latency backlog; skipped camera samples are
counted, never silently represented as recorded frames. The recorder stores
every processed RGB/Pose pair. No camera is opened by --check.
"""
import argparse
from dataclasses import asdict
from datetime import datetime
import json
from pathlib import Path
import threading
import time
import uuid
from .six_engine import SixGestureEngine as DemoEngine
from .transport import DemoSender
from .guided import GuidedCheck


def run(a):
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
    verify_pose_asset(a.pose,'Lite')
    if a.check:
        print('Demo modules and Lite asset OK. Camera/UDP not opened.');return
    stream=a.stream_id or uuid.uuid4().hex
    out=a.output/(datetime.now().strftime('%Y%m%d_%H%M%S')+'_'+stream[:8])
    out.mkdir(parents=True,exist_ok=False)
    engine=DemoEngine(movement=a.movement)
    sender=DemoSender(a.host,a.port,stream,a.actor)
    log=AsyncLog(out/'trace.jsonl');slot=Latest();stop=threading.Event();errors=[]
    counts={'captured':0,'processed':0,'events':0,'left':0,'right':0}
    guide=GuidedCheck(movement=a.movement, self_paced=not getattr(a,'guided_timed',False)) if getattr(a,'guided',False) else None
    display={'event':'NONE','event_ns':0};cap=None;pose=None;video=None;recorder=None
    def fail(where,exc):
        errors.append({'where':where,'error':repr(exc)});stop.set()
    def capture():
        nonlocal cap
        try:
            cap=open_camera(cv,a.camera,a.backend)
            cap.set(cv.CAP_PROP_FRAME_WIDTH,640);cap.set(cv.CAP_PROP_FRAME_HEIGHT,480)
            while not stop.is_set():
                ok,bgr=cap.read();stamp=time.monotonic_ns()
                if not ok:raise RuntimeError('Camera read failed')
                counts['captured']+=1;slot.put((counts['captured'],stamp,bgr.copy()))
        except BaseException as exc:fail('camera',exc)
        finally:
            if cap is not None:cap.release()
    def publisher():
        try:
            while not stop.wait(.01):
                with engine.lock:
                    engine.tick();events=engine.drain_events();state=engine.control
                    stamp=engine.last_capture;logs=engine.take_logs();armed=engine.calibrated
                for item in logs:log.write({'type':'engine','log':item})
                if not armed:sender.events.clear()
                for ev in events:
                    counts['events']+=1;counts[ev.side.lower()]+=1
                    gesture=ev.reason.get('gesture',ev.side+'_PUNCH')
                    counts[gesture]=counts.get(gesture,0)+1
                    display.update(event=gesture,event_ns=ev.committed_ns)
                    sender.offer(ev)
                now=time.monotonic_ns();fresh=stamp is not None and 0<=now-stamp<150_000_000
                if sender.clock:
                    ttl=max(1,min(150,int((stamp+150_000_000-now)//1_000_000))) if fresh else 150
                    log.write(sender.send('STATE',ttl_ms=ttl,block_active=bool(state.block and fresh),
                        movement=state.movement if fresh else 'NEUTRAL',movement_strength=float(state.movement_strength) if fresh else 0.,
                        tracking=state.tracking,fsm_state=state.attack_state,capture_ns=stamp))
                # Deliver a fresh BLOCK release before an attack from that frame.
                for item in sender.service():log.write(item)
                if log.error or log.dropped:raise RuntimeError('Diagnostic log failed or overflowed')
        except BaseException as exc:fail('publisher',exc)
        finally:
            if sender.clock:
                log.write(sender.send('STATE',ttl_ms=150,block_active=False,movement='NEUTRAL',movement_strength=0.,tracking='STOPPED',fsm_state='UNAVAILABLE'))
            sender.close()
    workers=[threading.Thread(target=capture,name='demo-camera',daemon=True),threading.Thread(target=publisher,name='demo-publisher',daemon=True)]
    print(f'Output: {out}\nUDP destination: {a.host}:{a.port}\nStream: {stream}\nC calibrate | M movement | X pause/reset | Q finish',flush=True)
    started=time.monotonic();last_stamp=None;origin=None;last_ms=-1;pose_ms=0.
    try:
        # Load inference before starting camera to avoid an initialization backlog.
        pose=PoseLandmarker.create_from_options(PoseLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(a.pose),delegate=BaseOptions.Delegate.CPU),
            running_mode=RunningMode.VIDEO,num_poses=1,min_pose_detection_confidence=.5,
            min_pose_presence_confidence=.5,min_tracking_confidence=.5))
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
                    pose_model_variant='Lite',pose_running_mode='VIDEO',mirror_display=a.mirror,
                    gesture_set_version='raised-kicks-v3',recording_config={'source_mirrored':False,
                        'engine_version':engine.config.version,
                        'stream_id':stream,'scope':'processed RGB + Pose + FSM; not human ground truth',
                        'all_successful_captures_saved':False,'frame_policy':'latest camera slot; all processed frames saved'})
                recorder.update_manifest(actual_resolution=[bgr.shape[1],bgr.shape[0]],camera_backend=cap.getBackendName())
                raw=PilotVideo(cv,recorder,(bgr.shape[1],bgr.shape[0]),30,'ffv1',override_fps=30)
                raw.metadata.update(recording_scope=['DEMO'],timebase='processed frames; use original capture timestamps')
                video=AsyncPilotVideo(raw,32)
                recorder.update_manifest(video=raw.metadata)
            start=time.monotonic_ns();result=pose.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB,data=rgb),ms);end=time.monotonic_ns()
            frame=recorder.record_frame(capture_return_monotonic_ns=stamp,pose_success=True,
                pose_start_monotonic_ns=start,pose_end_monotonic_ns=end,
                image_landmarks=result.pose_landmarks[0] if result.pose_landmarks else None,
                world_landmarks=result.pose_world_landmarks[0] if result.pose_world_landmarks else None,
                processing_metadata={'camera_frame_id':fid,'stream_id':stream})
            obs=PoseObservation.from_record(frame,{'subject_id':a.actor,'session_id':stream,
                'actual_resolution':[bgr.shape[1],bgr.shape[0]],'recording_config':{'source_mirrored':False}})
            state=engine.process(obs);video.write(bgr,rgb,frame,trial_state='DEMO')
            counts['processed']+=1;pose_ms=(end-start)/1e6
            log.write({'type':'frame','frame_id':frame['frame_id'],'camera_frame_id':fid,'capture_ns':stamp,
                'pose_end_ns':end,'engine_end_ns':time.monotonic_ns(),'state':asdict(state)})
            view=overlay(bgr,frame,name='REDBOOL Lite VIDEO',mirror=a.mirror)
            panel=view[bgr.shape[0]:];panel[:]=0
            with engine.lock:
                armed=engine.calibrated;progress=engine.calibration_progress
                arms=' / '.join(k+':'+v.state for k,v in engine.attack.units.items())
                ready=engine.ready
            fps=1e9/(stamp-last_stamp) if last_stamp is not None else 0;last_stamp=stamp
            message=display['event'] if time.monotonic_ns()-display['event_ns']<1_000_000_000 else ''
            mode=('C: CALIBRATE GUARD' if not armed else 'TRACKING LOST' if state.tracking!='VALID'
                else 'BLOCK' if state.block else 'READY' if ready else 'RETURN TO GUARD')
            prompt=guide.update(frame['frame_id'],stamp,armed,recorder) if guide else arms
            lines=[f"{mode}  {progress:.0%}   {message}",prompt,
                f"LEFT {counts['left']}  RIGHT {counts['right']}  BLOCK {state.block}  MOVE {state.movement} ({'ON' if engine.movement_enabled else 'OFF'})",
                f"UDP {'SYNCED' if sender.clock else 'WAITING'}  {sender.last_status}",
                f"Pose {pose_ms:.1f}ms | {fps:.1f} FPS | capture skipped {slot.dropped}",
                'C: hold guard 2 sec | SPACE: next prompt | M: move | X: pause | Q: finish']
            for i,line in enumerate(lines):
                width=cv.getTextSize(line,0,.47,1)[0][0]
                scale=.47*min(1.,(panel.shape[1]-12)/max(1,width))
                cv.putText(panel,line,(6,22+i*24),0,scale,(80,230,160) if armed else (50,200,255),1)
            cv.imshow('REDBOOL DEMO',view);key=cv.waitKey(1)&255
            if key==ord('q') or (guide and guide.completed):break
            if key in (ord('c'),ord('x')):
                engine.request_calibration()
                if key==ord('x'):engine.calibrating=False
                recorder.record_annotation(label='CALIBRATE' if key==ord('c') else 'PAUSE',annotation_type='marker',event_frame_id=frame['frame_id'],source='human_demo_control')
            if key==ord('m'):engine.toggle_movement()
            if key==ord(' ') and guide:guide.advance(stamp)
            video.check_error()
    except KeyboardInterrupt:pass
    except BaseException as exc:fail('main',exc)
    finally:
        stop.set()
        for worker in workers:
            if worker.ident is not None:worker.join(timeout=3)
        if any(w.is_alive() for w in workers):errors.append({'where':'shutdown','error':'worker did not stop'})
        if workers[1].ident is None:sender.close()
        if pose is not None:pose.close()
        cv.destroyAllWindows()
        for item in engine.take_logs():log.write({'type':'engine','log':item})
        log.close()
        if log.error or log.dropped:errors.append({'where':'log','error':log.error,'dropped':log.dropped})
        if video is not None:
            try:video.close()
            except BaseException as exc:errors.append({'where':'video','error':repr(exc)})
        if recorder is not None:
            if guide:guide.finish(recorder)
            recorder.finalize(completed=not errors,metadata={'errors':errors,'camera_slot_drops':slot.dropped,
                'video_diagnostics':video.diagnostics() if video else None})
        report={'stream_id':stream,'counts':counts,'delivery':dict(sender.delivery),'errors':errors,
            'camera_slot_drops':slot.dropped,'duration_seconds':time.monotonic()-started,
            'session':str(recorder.directory) if recorder else None,'unity_screen_verified':False,
            'guided_check':{'completed':guide.completed,'interrupted':guide.interrupted,'steps_finished':guide.index,'self_paced':guide.self_paced} if guide else None}
        (out/'summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
    if errors:raise RuntimeError(errors)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,default=Path('local_data/demo_20261103'))
    p.add_argument('--pose',type=Path,default=Path('pose_landmarker.task'))
    p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065)
    p.add_argument('--actor',default='DEV01');p.add_argument('--stream-id');p.add_argument('--camera',type=int,default=0)
    p.add_argument('--backend',default='auto');p.add_argument('--seconds',type=float,default=180)
    p.add_argument('--guided',action='store_true',help='Self-paced prompts: SPACE advances after each action/guard; saves instructions, not ground truth')
    p.add_argument('--guided-timed',action='store_true',help='Use legacy fixed-duration prompts instead of SPACE')
    p.add_argument('--movement',action='store_true');p.add_argument('--mirror',action='store_true')
    p.add_argument('--check',action='store_true');a=p.parse_args()
    import math
    if not math.isfinite(a.seconds) or a.seconds<=0 or not 1<=a.port<=65535:p.error('Positive seconds and valid UDP port required')
    run(a)

if __name__=='__main__':main()
