"""Real live publisher with synthetic frames: calibration state travels to Unity."""
import argparse
from collections import Counter
import json
import os
from pathlib import Path
import tempfile
import time
import types
import unittest
from unittest.mock import patch


@unittest.skipUnless(os.environ.get('REDBOOL_DEMO_SESSION'), 'Stored development landmarks required')
class CalibrationWireTests(unittest.TestCase):
    def test_calibration_then_pause_and_stop_reach_state_packets(self):
        import cv2
        import numpy as np
        from mediapipe.tasks.python.vision import PoseLandmarker
        from redbool_demo.live import run
        source=json.loads((Path(os.environ['REDBOOL_DEMO_SESSION'])/'frames.jsonl').read_text().splitlines()[8774])
        points=[types.SimpleNamespace(x=p[0],y=p[1],z=p[2],visibility=source['landmark_visibility']['image'][i],presence=1.) for i,p in enumerate(source['image_landmarks'])]
        class Camera:
            def set(self,*a): pass
            def getBackendName(self): return 'SYNTHETIC_TEST'
            def read(self): time.sleep(.033);return True,np.zeros((480,640,3),np.uint8)
            def release(self): pass
        class Pose:
            def detect_for_video(self,*a): return types.SimpleNamespace(pose_landmarks=[points],pose_world_landmarks=[])
            def close(self): pass
        class Sender:
            clock={'synced':True}
            last_status='TEST'
            def __init__(self): self.events={};self.delivery=Counter();self.packets=[]
            def send(self,kind,**fields):
                p={'kind':kind,**fields};self.packets.append(p);return {'type':'send','packet':p}
            def offer(self,event): self.events[event.event_id]=event
            def service(self): return []
            def close(self): pass
        sender=Sender();shown=[0]
        def show(*a): shown[0]+=1
        def key(*a):
            return ord('q') if shown[0]>=85 else ord('c') if shown[0]==1 else ord('x') if shown[0]==65 else -1
        with tempfile.TemporaryDirectory() as folder:
            args=argparse.Namespace(pose=Path('pose_landmarker.task'),check=False,stream_id=None,output=Path(folder),guided=False,movement=False,host='127.0.0.1',port=9,actor='DEV01',camera=0,backend='auto',seconds=8,mirror=False)
            with patch('pose_capture.open_camera',return_value=Camera()),patch.object(PoseLandmarker,'create_from_options',return_value=Pose()),patch('redbool_demo.live.DemoSender',return_value=sender),patch.object(cv2,'imshow',side_effect=show),patch.object(cv2,'waitKey',side_effect=key),patch.object(cv2,'destroyAllWindows'):
                run(args)
            report=json.loads(next(Path(folder).glob('*/summary.json')).read_text())
            self.assertFalse(report['errors'])
        self.assertTrue(sender.packets)
        self.assertTrue(all(p['calibration_version']==1 for p in sender.packets))
        values=[p['calibrated'] for p in sender.packets]
        self.assertFalse(values[0]);self.assertIn(True,values)
        first_ready=values.index(True)
        self.assertIn(False,values[first_ready:-1], 'X must reach Unity before final shutdown')
        self.assertFalse(values[-1]);self.assertEqual(sender.packets[-1]['tracking'],'STOPPED')
