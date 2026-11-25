"""Exercise production live wiring with synthetic camera/models; never open a device."""
import argparse
import contextlib
import io
from collections import Counter
import json
from pathlib import Path
import tempfile
import threading
import time
import types
import unittest
from unittest.mock import patch
import numpy as np
from redbool_demo.landmark_replay import verify_replay
from redbool_demo.landmark_recording import iter_journal
from test_chest_special import pose


class CalibratedCrossScript:
    """Fixture phases follow readiness, never a camera counter or an event.

    The camera writes this pose code into each captured image. Every fake model
    reads that immutable code, even if the script advances before inference.
    """
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.lock = threading.RLock()
        self.phase = 'waiting'
        self.cross_since = self.return_since = None
        self.pause_frame = self.finish_frame = None
        self.started = False
        self.commands = []

    def observe(self, *, calibrated, helper_ready, frame_id):
        with self.lock:
            if self.phase == 'waiting' and self.started and calibrated and helper_ready:
                self.phase = 'cross'
            elif self.phase == 'recalibrating' and calibrated:
                self.phase, self.finish_frame = 'finished', frame_id

    def pose_code(self):
        with self.lock:
            now = self.clock()
            if self.phase == 'cross':
                if self.cross_since is None:
                    self.cross_since = now
                if now - self.cross_since < 1.2:
                    return 1
                self.phase, self.return_since = 'returning', now
            if self.phase == 'returning' and now - self.return_since >= .25:
                self.phase = 'pause_ready'
            return 0

    def command(self, frame_id):
        with self.lock:
            command = None
            if not self.started and frame_id >= 1:
                self.started = True
                command = 'calibrate'
            elif self.phase == 'pause_ready':
                self.phase, self.pause_frame = 'paused', frame_id
                command = 'pause'
            elif self.phase == 'paused' and frame_id >= self.pause_frame + 5:
                self.phase = 'recalibrating'
                command = 'calibrate'
            elif self.phase == 'finished' and frame_id >= self.finish_frame + 20:
                self.phase = 'stopped'
                command = 'stop'
            if command:
                self.commands.append(command)
            return command


class LandmarkLiveTests(unittest.TestCase):
    def test_terminal_unicode_and_broken_pipe_are_nonfatal(self):
        from redbool_demo.live import _print_status
        encoded=io.BytesIO();console=io.TextIOWrapper(encoded,encoding='cp1252')
        with contextlib.redirect_stdout(console):_print_status('한글 경로',flush=True)
        self.assertIn(b'\\u',encoded.getvalue())
        class Broken:
            def write(self,*args):raise BrokenPipeError('terminal closed')
            def flush(self):raise BrokenPipeError('terminal closed')
        with contextlib.redirect_stdout(Broken()):_print_status('종료',flush=True)
        closed=io.StringIO();closed.close()
        with contextlib.redirect_stdout(closed):_print_status('already closed',flush=True)

    def test_headless_all_models_calibration_controls_and_full_rate_journal(self):
        self._check_live(overload=False)

    def test_sustained_auxiliary_overload_stops_full_calls_and_replays_exactly(self):
        self._check_live(overload=True)

    def test_opt_in_heavy_same_frame_journal_replays_without_changing_capture(self):
        # Test scheduling only: shorten qualification/pending gates so this
        # existing synthetic controlled-pose fixture exercises the optional slot.
        with patch('redbool_demo.heavy_auxiliary.BoundedHeavyAuxiliary.WINDOW',2), \
             patch('redbool_demo.heavy_auxiliary.BoundedHeavyAuxiliary.PENDING_NS',0):
            self._check_live(overload=False,refine=True)

    def test_opt_in_heavy_waits_for_calibration_after_delayed_control(self):
        # Capture continues during this delay. The old capture-60..109 script
        # started X before guard calibration and produced no event on both the
        # original and optimized engine. No production timing limit is relaxed.
        with patch('redbool_demo.heavy_auxiliary.BoundedHeavyAuxiliary.WINDOW',2), \
             patch('redbool_demo.heavy_auxiliary.BoundedHeavyAuxiliary.PENDING_NS',0):
            self._check_live(overload=False,refine=True,calibration_delay=.65)

    def test_startup_probe_frames_never_enter_engine_or_journal(self):
        self._check_live(overload=False,profiling=True)

    def test_cancel_during_startup_probe_releases_owned_camera_without_observations(self):
        self._check_live(overload=False,profiling=True,cancel_profile=True)

    def _check_live(self, overload, profiling=False, cancel_profile=False,refine=False,
                    calibration_delay=0.):
        from redbool_demo.live import run
        from redbool_demo.camera_profile import select_startup_profile
        from redbool_demo.engine import DemoEngine
        from redbool_demo.heavy_auxiliary import BoundedHeavyAuxiliary
        from mediapipe.tasks.python.vision import PoseLandmarker
        models=[]; helpers=[]; script=CalibratedCrossScript() if refine else None
        original_calibrate=DemoEngine.request_calibration;calibration_calls=0
        def calibrate(engine):
            nonlocal calibration_calls
            result=original_calibrate(engine);calibration_calls+=1
            if calibration_calls==1 and calibration_delay:time.sleep(calibration_delay)
            return result
        def helper(*args,**kwargs):
            value=BoundedHeavyAuxiliary(*args,**kwargs);helpers.append(value);return value
        class Camera:
            frame=0
            def __init__(self):self.pose_codes={}
            def set(self,*args):return True
            def get(self,*args):return 30.
            def getBackendName(self):return 'DSHOW' if profiling else 'SYNTHETIC_NO_DEVICE'
            def read(self):
                time.sleep(.018);self.frame+=1
                if cancel_profile and self.frame>=4:bridge.stop_requested.set()
                bgr=np.zeros((480,640,3) if profiling else (48,64,3),dtype=np.uint8)
                bgr[0,0,0]=self.frame%256;bgr[0,0,1]=self.frame//256
                code=script.pose_code() if script else int(60<=self.frame<110)
                self.pose_codes[self.frame]=code;bgr[0,1,0]=code
                return True,bgr
            def release(self):self.released=True
        class Model:
            def __init__(self):self.calls=[];self.pose_calls=[];self.index=len(models);models.append(self)
            def detect_for_video(self,image,*args):
                # Independent models may run at different rates. Their fake
                # observations must describe this captured image, not callcount.
                if overload and self.index:time.sleep(.145)
                rgb=image.numpy_view();sample=int(rgb[0,0,2])+256*int(rgb[0,0,1])
                code=int(rgb[0,1,2]);self.calls.append(sample);self.pose_calls.append((sample,code))
                o=pose('cross' if code else 'guard')
                points=[types.SimpleNamespace(x=p[0],y=p[1],z=p[2],visibility=.99,presence=.98) for p in o.landmarks]
                return types.SimpleNamespace(pose_landmarks=[points],pose_world_landmarks=[points])
            def detect(self,*args):return self.detect_for_video(*args)
            def close(self):pass
        class Sender:
            def __init__(self,*a,**kw):
                self.clock=None;self.events={};self.delivery=Counter();self.last_status='TEST';self.closed=False
            def tutorial_context(self):return None
            def service(self):return []
            def offer(self,event):self.delivery['offered']+=1
            def close(self):self.closed=True
        class Bridge:
            def __init__(self):self.stop_requested=threading.Event();self.data=dict(preview_encoded=0,preview_target_fps=15.);self.done=set();self.history=[];self.preview_limits=[]
            def update(self,**kw):
                self.data.update(kw);self.history.append(dict(kw))
                if script and 'calibrated' in kw:
                    script.observe(calibrated=kw['calibrated'],frame_id=kw['frame_id'],
                        helper_ready=bool(helpers and helpers[0].snapshot()['ready']))
            def limit_preview_fps(self,fps):
                self.preview_limits.append(fps)
                self.data['preview_target_fps']=min(self.data['preview_target_fps'],fps)
                return self.data['preview_target_fps']
            def offer_preview(self,*args,**kw):self.data['preview_encoded']+=1
            def snapshot(self):return dict(self.data)
            def take_command(self):
                fid=self.data.get('frame_id',0)
                if script:
                    command=script.command(fid)
                    if command=='stop':self.stop_requested.set();return None
                    return command
                for target,cmd in [(1,'calibrate'),(120,'pause'),(125,'calibrate')]:
                    if fid>=target and target not in self.done:self.done.add(target);return cmd
                if fid>=180:self.stop_requested.set()
                return None
            def close(self):pass
        with tempfile.TemporaryDirectory() as td:
            root=Path(td)/'한글 경로';root.mkdir();lite=root/'lite.task';full=root/'full.task';lite.write_bytes(b'lite');full.write_bytes(b'full')
            bridge=Bridge();camera=Camera();args=argparse.Namespace(output=root/'runs',pose=lite,pose_variant='Lite',cross_pose=full,
                cross_refine_pose=full if refine else None,
                cross_redetect=True,guard_model=None,personal_calibration=True,check=False,stream_id='synthetic',
                movement=True,actor='DEV01',host='127.0.0.1',port=9,camera=0,backend='auto',seconds=8,
                guided=False,recording='diagnostics',preview_fps=15,headless=True,bridge_instance=bridge,mirror=True)
            with patch('continuous_events.protocol.verify_pose_asset'),patch('pose_capture.open_camera',return_value=camera),\
                 patch('redbool_demo.backend_assets.verify_pose_asset'),\
                 patch.object(DemoEngine,'request_calibration',calibrate), \
                 patch('redbool_demo.heavy_auxiliary.BoundedHeavyAuxiliary',side_effect=helper), \
                 patch.object(PoseLandmarker,'create_from_options',side_effect=lambda *a,**kw:Model()),\
                 patch('redbool_demo.live.DemoSender',Sender), \
                 patch('redbool_demo.live.profile_eligible',return_value=profiling), \
                 patch('redbool_demo.live.select_startup_profile',side_effect=lambda *a,**kw:select_startup_profile(*a,**kw,system='Windows')), \
                 contextlib.redirect_stdout(io.TextIOWrapper(io.BytesIO(),encoding='cp1252')):
                run(args)
            output=next((root/'runs').iterdir());summary=json.loads((output/'summary.json').read_text())
            self.assertFalse(summary['errors']);self.assertEqual(summary['recording']['state'],'complete')
            self.assertTrue(camera.released)
            if cancel_profile:
                self.assertEqual(summary['counts']['processed'],0)
                self.assertEqual(summary['counts']['captured'],0)
                self.assertTrue(all(not m.calls for m in models))
                self.assertFalse(any(r['kind']=='process' for r in iter_journal(output/'recognition')))
                self.assertFalse(bridge.data['camera_connected'])
                self.assertFalse(any(t.is_alive() and t.name=='demo-camera' for t in threading.enumerate()))
                return
            result=verify_replay(output/'recognition')
            self.assertTrue(result['passed'],result['mismatches'][:1])
            self.assertEqual(result['frames'],summary['counts']['processed'])
            self.assertEqual(result['events'],summary['counts']['events'])
            if not overload:self.assertGreater(result['events'],0)
            self.assertEqual(result['calibrations'],2)
            rows=list(iter_journal(output/'recognition'))
            frames=[r for r in rows if r['kind']=='process']
            camera_ids=[r['frame']['camera_frame_id'] for r in frames]
            self.assertTrue(all(isinstance(fid,int) and 0<fid<=summary['counts']['captured'] for fid in camera_ids))
            self.assertTrue(all(after>before for before,after in zip(camera_ids,camera_ids[1:])))
            transitions=[json.loads(line) for line in (output/'trace.jsonl').read_text().splitlines()
                if json.loads(line)['type']=='inference_policy']
            self.assertEqual(len(transitions),1 if overload else 0)
            fallback_after=transitions[0]['frame_id'] if transitions else float('inf')
            was_calibrated=False;phases=[]
            for row in rows:
                if row['kind']=='process':
                    self.assertEqual(row['has_auxiliary'],was_calibrated and row['observation'][2]<=fallback_after,
                        'Auxiliary inference follows calibration and the measured overload latch')
                    phases.append(row['has_auxiliary'])
                    if row['has_auxiliary']:
                        self.assertEqual(len(row['frame']['full_image']['world']),33)
                        self.assertEqual(len(row['frame']['full_video']['presence']),33)
                    else:
                        self.assertIsNone(row['frame']['full_image'])
                        self.assertIsNone(row['frame']['full_video'])
                if 'expected' in row:was_calibrated=row['expected']['calibrated']
            runs=[v for i,v in enumerate(phases) if i==0 or phases[i-1]!=v]
            self.assertEqual(runs,[False,True,False] if overload else [False,True,False,True],
                'Recalibration retains an overload latch, otherwise it reenables auxiliaries')
            self.assertEqual(len(models),4 if refine else 3,'All models are created once and reused')
            if refine:
                self.assertEqual(script.commands,['calibrate','pause','calibrate','stop'])
                cross=[row for row in frames if camera.pose_codes[row['frame']['camera_frame_id']]]
                self.assertTrue(cross)
                self.assertTrue(all(row['expected']['calibrated'] for row in cross),
                    'Synthetic X starts only after actual calibration and helper readiness')
                self.assertGreaterEqual(cross[-1]['observation'][3]-cross[0]['observation'][3],
                    1_000_000_000,'The fixed X fixture must provide more than the unchanged700ms hold')
                heavy=[row for row in frames if row['frame'].get('heavy_image') is not None]
                self.assertGreater(len(heavy),0)
                self.assertTrue(any(row['frame'].get('heavy_image') is None for row in frames))
                for row in heavy:
                    data=row['frame']['heavy_image']
                    self.assertEqual(data['source']['frame_id'],row['observation'][2])
                    self.assertEqual(data['source']['capture_ns'],row['observation'][3])
                    self.assertLessEqual(data['end_ns'],row['clocks'][0])
                    self.assertLessEqual(data['end_ns']-row['observation'][3],120_000_000)
                self.assertFalse(summary['performance']['optional_refine']['worker_alive'])
            else:self.assertTrue(all('heavy_image' not in row['frame'] for row in frames))
            for model in models:
                self.assertTrue(all(code==camera.pose_codes[sample]
                    for sample,code in model.pose_calls if sample),
                    'Every model reads the original captured pose code, not mutable fixture state')
            probe_reads=summary['performance']['camera_properties'].get('startup_profile',{}).get('probe_reads',0)
            self.assertEqual(probe_reads,14 if profiling else 0)
            if profiling:
                optimizing=[h for h in bridge.history if h.get('phase')=='optimizing_camera']
                self.assertEqual(len(optimizing),1);self.assertFalse(optimizing[0]['camera_connected'])
                self.assertTrue(any(h.get('camera_connected') is True for h in bridge.history))
                self.assertEqual(summary['performance']['camera_properties']['startup_profile']['probe_frames_published'],0)
                self.assertTrue(all(c>probe_reads for c in models[0].calls))
            self.assertEqual(models[0].calls,[c+probe_reads for c in camera_ids])
            auxiliary_ids=[r['frame']['camera_frame_id'] for r in frames if r['has_auxiliary']]
            self.assertEqual(models[1].calls,[c+probe_reads for c in auxiliary_ids])
            self.assertEqual(models[2].calls,[c+probe_reads for c in auxiliary_ids])
            self.assertEqual(summary['performance']['auxiliary_frames'],len(auxiliary_ids))
            self.assertEqual(summary['performance']['auxiliary_calibration_skipped_frames']+
                summary['performance']['auxiliary_overload_skipped_frames'],len(frames)-len(auxiliary_ids))
            self.assertEqual(summary['performance']['inference_runtime']['overload_latched'],overload)
            self.assertEqual(bridge.data['inference_mode'],'lite_overload' if overload else 'full')
            self.assertEqual(bridge.preview_limits,[8.] if overload else [])
            self.assertEqual(summary['performance']['preview_requested_fps'],15)
            self.assertEqual(summary['performance']['preview_target_fps'],8. if overload else 15.)
            if overload:
                self.assertEqual(len(auxiliary_ids),10)
                self.assertGreater(summary['performance']['auxiliary_overload_skipped_frames'],0)
                self.assertEqual(transitions[0]['bad_frames'],10)
                self.assertGreater(len(camera_ids),len(auxiliary_ids))
            self.assertEqual(rows[0]['inference_policy'],summary['performance']['inference_policy'])
            self.assertEqual(bridge.data['recording']['state'],'complete')
            self.assertFalse(any(t.is_alive() and t.name=='landmark-journal' for t in threading.enumerate()))
            self.assertFalse(list(output.rglob('*.part')))
            self.assertFalse(list(output.rglob('frames.jsonl')))
            self.assertFalse(list(output.rglob('*.avi'))+list(output.rglob('*.mp4')))

if __name__=='__main__':unittest.main()
