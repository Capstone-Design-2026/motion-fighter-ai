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

    def _check_live(self, overload):
        from redbool_demo.live import run
        from mediapipe.tasks.python.vision import PoseLandmarker
        models=[]
        class Camera:
            frame=0
            def set(self,*args):return True
            def get(self,*args):return 30.
            def getBackendName(self):return 'SYNTHETIC_NO_DEVICE'
            def read(self):
                time.sleep(.018);self.frame+=1
                bgr=np.zeros((48,64,3),dtype=np.uint8)
                bgr[0,0,0]=self.frame%256;bgr[0,0,1]=self.frame//256
                return True,bgr
            def release(self):pass
        class Model:
            def __init__(self):self.calls=[];self.index=len(models);models.append(self)
            def detect_for_video(self,image,*args):
                # Independent models may run at different rates. Their fake
                # observations must describe this captured image, not callcount.
                if overload and self.index:time.sleep(.145)
                rgb=image.numpy_view();sample=int(rgb[0,0,2])+256*int(rgb[0,0,1])
                self.calls.append(sample);o=pose('cross' if 60<=sample<110 else 'guard')
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
            def __init__(self):self.stop_requested=threading.Event();self.data=dict(preview_encoded=0);self.done=set()
            def update(self,**kw):self.data.update(kw)
            def offer_preview(self,*args,**kw):self.data['preview_encoded']+=1
            def snapshot(self):return dict(self.data)
            def take_command(self):
                fid=self.data.get('frame_id',0)
                for target,cmd in [(1,'calibrate'),(120,'pause'),(125,'calibrate')]:
                    if fid>=target and target not in self.done:self.done.add(target);return cmd
                if fid>=180:self.stop_requested.set()
                return None
            def close(self):pass
        with tempfile.TemporaryDirectory() as td:
            root=Path(td)/'한글 경로';root.mkdir();lite=root/'lite.task';full=root/'full.task';lite.write_bytes(b'lite');full.write_bytes(b'full')
            bridge=Bridge();args=argparse.Namespace(output=root/'runs',pose=lite,pose_variant='Lite',cross_pose=full,
                cross_redetect=True,guard_model=None,personal_calibration=True,check=False,stream_id='synthetic',
                movement=True,actor='DEV01',host='127.0.0.1',port=9,camera=0,backend='auto',seconds=8,
                guided=False,recording='diagnostics',preview_fps=8,headless=True,bridge_instance=bridge,mirror=True)
            with patch('continuous_events.protocol.verify_pose_asset'),patch('pose_capture.open_camera',return_value=Camera()),\
                 patch.object(PoseLandmarker,'create_from_options',side_effect=lambda *a,**kw:Model()),\
                 patch('redbool_demo.live.DemoSender',Sender), \
                 contextlib.redirect_stdout(io.TextIOWrapper(io.BytesIO(),encoding='cp1252')):
                run(args)
            output=next((root/'runs').iterdir());summary=json.loads((output/'summary.json').read_text())
            self.assertFalse(summary['errors']);self.assertEqual(summary['recording']['state'],'complete')
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
            self.assertEqual(len(models),3,'All models are created once and reused')
            self.assertEqual(models[0].calls,camera_ids)
            auxiliary_ids=[r['frame']['camera_frame_id'] for r in frames if r['has_auxiliary']]
            self.assertEqual(models[1].calls,auxiliary_ids)
            self.assertEqual(models[2].calls,auxiliary_ids)
            self.assertEqual(summary['performance']['auxiliary_frames'],len(auxiliary_ids))
            self.assertEqual(summary['performance']['auxiliary_calibration_skipped_frames']+
                summary['performance']['auxiliary_overload_skipped_frames'],len(frames)-len(auxiliary_ids))
            self.assertEqual(summary['performance']['inference_runtime']['overload_latched'],overload)
            self.assertEqual(bridge.data['inference_mode'],'lite_overload' if overload else 'full')
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
