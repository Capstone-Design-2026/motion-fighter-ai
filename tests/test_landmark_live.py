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
        from redbool_demo.live import run
        from mediapipe.tasks.python.vision import PoseLandmarker
        class Camera:
            def set(self,*args):return True
            def get(self,*args):return 30.
            def getBackendName(self):return 'SYNTHETIC_NO_DEVICE'
            def read(self):time.sleep(.018);return True,np.zeros((48,64,3),dtype=np.uint8)
            def release(self):pass
        class Model:
            def __init__(self):self.frames=0
            def detect_for_video(self,*args):
                self.frames+=1;o=pose('guard' if self.frames<60 else 'cross')
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
                if fid>=135:self.stop_requested.set()
                return None
            def close(self):pass
        with tempfile.TemporaryDirectory() as td:
            root=Path(td)/'한글 경로';root.mkdir();lite=root/'lite.task';full=root/'full.task';lite.write_bytes(b'lite');full.write_bytes(b'full')
            bridge=Bridge();args=argparse.Namespace(output=root/'runs',pose=lite,pose_variant='Lite',cross_pose=full,
                cross_redetect=True,guard_model=None,personal_calibration=True,check=False,stream_id='synthetic',
                movement=True,actor='DEV01',host='127.0.0.1',port=9,camera=0,backend='auto',seconds=6,
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
            self.assertGreater(result['events'],0);self.assertEqual(result['calibrations'],2)
            frames=[r for r in iter_journal(output/'recognition') if r['kind']=='process']
            self.assertTrue(all(len(r['frame']['full_image']['world'])==33 for r in frames))
            self.assertTrue(all(len(r['frame']['full_video']['presence'])==33 for r in frames))
            self.assertEqual(bridge.data['recording']['state'],'complete')
            self.assertFalse(any(t.is_alive() and t.name=='landmark-journal' for t in threading.enumerate()))
            self.assertFalse(list(output.rglob('*.part')))
            self.assertFalse(list(output.rglob('frames.jsonl')))
            self.assertFalse(list(output.rglob('*.avi'))+list(output.rglob('*.mp4')))

if __name__=='__main__':unittest.main()
