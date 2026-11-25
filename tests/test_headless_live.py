"""Run the real live loop with synthetic capture/model, no physical camera."""
import argparse
from collections import Counter
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import threading
import time
import types
import unittest
from unittest.mock import patch


class HeadlessLiveTests(unittest.TestCase):
    def test_local_controls_no_native_windows_no_saved_poses_clean_stop(self):
        import cv2
        import numpy as np
        from mediapipe.tasks.python.vision import PoseLandmarker
        from redbool_demo.local_bridge import LocalBridge
        from redbool_demo.live import run
        points=[types.SimpleNamespace(x=.5,y=.5,z=0.,visibility=1.,presence=1.) for _ in range(33)]
        for j,(x,y) in {11:(.4,.5),12:(.6,.5),13:(.35,.68),14:(.65,.68),15:(.42,.4),16:(.58,.4)}.items():
            points[j].x=x;points[j].y=y
        class Camera:
            released=False
            def set(self,*a):return True
            def getBackendName(self):return 'SYNTHETIC_TEST'
            def read(self):time.sleep(.033);return True,np.zeros((480,640,3),np.uint8)
            def release(self):self.released=True
        class Pose:
            def detect_for_video(self,*a):return types.SimpleNamespace(pose_landmarks=[points],pose_world_landmarks=[])
            def close(self):pass
        class Sender:
            clock={'synced':True};last_status='TEST'
            def __init__(self):self.events={};self.delivery=Counter();self.packets=[]
            def send(self,kind,**fields):
                p={'kind':kind,'sent_ns':time.monotonic_ns(),**fields};self.packets.append(p)
                return {'type':'send','packet':p}
            def offer(self,event):self.events[event.event_id]=event
            def service(self):return []
            def tutorial_context(self):return None
            def close(self):pass
        sender=Sender();camera=Camera();bridges=[];controller_errors=[]
        def bridge(*a,**kw):
            result=LocalBridge(*a,**kw);bridges.append(result);return result
        def control():
            try:
                deadline=time.monotonic()+8
                while (not bridges or bridges[0].snapshot().get('frame_id',0)<2) and time.monotonic()<deadline:time.sleep(.01)
                self.assertTrue(bridges)
                bridges[0].command('calibrate','test-c')
                while bridges[0].snapshot().get('frame_id',0)<20 and time.monotonic()<deadline:time.sleep(.01)
                bridges[0].command('stop','test-q')
            except BaseException as exc:controller_errors.append(exc)
        with tempfile.TemporaryDirectory() as folder:
            model=Path(folder)/'unused.task';model.write_bytes(b'asset verified separately')
            output=Path(folder)/'records'
            args=argparse.Namespace(pose=model,check=False,stream_id=None,output=output,guided=False,
                movement=True,host='127.0.0.1',port=9,actor='P1',camera=0,backend='auto',seconds=8,
                mirror=True,preview_fps=8.,headless=True,bridge_port=0,bridge_token='t'*32,
                parent_pid=os.getppid(),recording='diagnostics',personal_calibration=True)
            worker=threading.Thread(target=control);worker.start()
            with patch('pose_capture.open_camera',return_value=camera), \
                 patch('continuous_events.protocol.verify_pose_asset'), \
                 patch.object(PoseLandmarker,'create_from_options',return_value=Pose()), \
                 patch('redbool_demo.live.DemoSender',return_value=sender), \
                 patch('redbool_demo.local_bridge.LocalBridge',side_effect=bridge), \
                 patch.object(cv2,'imshow',side_effect=AssertionError('headless imshow')), \
                 patch.object(cv2,'waitKey',side_effect=AssertionError('headless waitKey')), \
                 patch.object(cv2,'destroyAllWindows',side_effect=AssertionError('headless GUI cleanup')), \
                 contextlib.redirect_stdout(io.StringIO()):
                run(args)
            worker.join(2)
            self.assertFalse(worker.is_alive());self.assertFalse(controller_errors)
            report=json.loads(next(output.glob('*/summary.json')).read_text())
            self.assertFalse(report['errors']);self.assertGreaterEqual(report['counts']['processed'],20)
            self.assertTrue(report['performance']['headless']);self.assertTrue(camera.released)
            session=Path(report['session']);self.assertFalse((session/'frames.jsonl').exists())
            annotations=[json.loads(line) for line in (session/'annotations.jsonl').read_text().splitlines()]
            self.assertTrue(any(r['label']=='CALIBRATE' and r['source']=='unity_game_control' for r in annotations))
            self.assertFalse(any(session.glob('*.avi')));self.assertFalse(any(session.glob('*.mp4')))
            self.assertFalse(sender.packets[-1]['calibrated'])
            self.assertEqual(sender.packets[-1]['tracking'],'STOPPED')
            self.assertTrue(all(not t.is_alive() for t in bridges[0].threads))


if __name__=='__main__':unittest.main()
