"""Behavioral tests for real captured replay, safety and actual UDP delivery."""
from dataclasses import asdict,replace
import json
import os
from pathlib import Path
import socket
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from action_engine.types import PoseObservation,ActionEvent
from realtime_vision.transport import ReceiverState
from redbool_demo.engine import DemoEngine
from redbool_demo.transport import DemoSender

SESSION=os.environ.get('REDBOOL_DEMO_SESSION')

@unittest.skipUnless(SESSION,'Set REDBOOL_DEMO_SESSION to the seated development recording')
class CapturedDemoTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(SESSION);cls.manifest=json.loads((p/'manifest.json').read_text())
        cls.frames={f['frame_id']:f for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines())}

    def run_frames(self,ids,calibrate=True,transform=None):
        e=DemoEngine();events=[];states=[];calibrations=[]
        if calibrate:e.request_calibration()
        first=self.frames[ids[0]]['capture_return_monotonic_ns']
        for i,fid in enumerate(ids):
            o=PoseObservation.from_record(self.frames[fid],self.manifest)
            o=replace(o,frame_id=i+1,capture_ns=first+i*33_333_333)
            if transform:o=transform(i,o)
            e.process(o,now_ns=o.capture_ns);events+=e.drain_events(o.capture_ns)
            states.append({k:u.state for k,u in e.attack.units.items()})
            calibrations += [x for x in e.take_logs() if x['type']=='demo_calibrated']
        return e,events,states,calibrations

    def test_continuous_left_right_and_rearm(self):
        e,ev,states,cal=self.run_frames(list(range(8731,9517)))
        self.assertEqual([x.side for x in ev],['LEFT','RIGHT']);self.assertEqual(len(cal),1)
        for x in ev:self.assertTrue(any(s[x.side]=='READY' for s in states[x.source_frame_id:]))

    def test_no_attacks_without_explicit_calibration(self):
        _,ev,_,_=self.run_frames(list(range(8731,9517)),False);self.assertFalse(ev)

    def test_same_hand_can_punch_twice_after_return(self):
        ids=list(range(8755,8811))+list(range(8811,8894))+list(range(8811,8894))
        _,ev,_,_=self.run_frames(ids);self.assertEqual([e.side for e in ev],['LEFT','LEFT'])

    def test_extended_hold_does_not_repeat(self):
        ids=list(range(8755,8836))+[8835]*180
        _,ev,_,_=self.run_frames(ids);self.assertEqual(len(ev),1)

    def test_downward_arm_motion_is_not_punch(self):
        ids=list(range(8755,8811))+list(range(8894,8929))*3
        _,ev,_,_=self.run_frames(ids);self.assertFalse(ev)

    def test_missing_own_wrist_cannot_attack(self):
        def invalid(i,o):
            if i>60:
                v=list(o.visibility);v[15]=0;return replace(o,visibility=tuple(v))
            return o
        _,ev,_,_=self.run_frames(list(range(8755,8894)),transform=invalid);self.assertFalse(ev)

    def test_guard_does_not_become_block(self):
        e,ev,_,_=self.run_frames(list(range(8767,8810))*4)
        self.assertFalse(ev);self.assertFalse(e.control.block)

    def test_stale_frames_cannot_calibrate(self):
        e=DemoEngine();e.request_calibration()
        for fid in range(8767,8810):
            o=PoseObservation.from_record(self.frames[fid],self.manifest)
            e.process(o,now_ns=o.capture_ns+200_000_000)
        self.assertFalse(e.calibrated)

    def test_block_release_and_watchdog(self):
        e,_,_,_=self.run_frames(list(range(8767,8810)))
        base=PoseObservation.from_record(self.frames[8809],self.manifest)
        now=e.last_capture;fid=e.last_frame
        for i in range(15):
            p=[list(v) for v in base.landmarks]
            for j in (15,16):p[j][1]-=.16
            o=replace(base,frame_id=fid+i+1,capture_ns=now+(i+1)*33_333_333,landmarks=tuple(map(tuple,p)))
            e.process(o,now_ns=o.capture_ns)
        self.assertTrue(e.control.block)
        for i in range(15,25):
            o=replace(base,frame_id=fid+i+1,capture_ns=now+(i+1)*33_333_333)
            e.process(o,now_ns=o.capture_ns)
        self.assertFalse(e.control.block)
        e.tick(o.capture_ns+200_000_000)
        self.assertEqual(e.control.movement,'NEUTRAL');self.assertFalse(e.control.block)

    def test_pause_requires_new_calibration(self):
        e,_,_,_=self.run_frames(list(range(8767,8810)))
        self.assertTrue(e.calibrated);e.request_calibration();e.calibrating=False
        self.assertFalse(e.calibrated);self.assertFalse(e.control.block)


class TransportTests(unittest.TestCase):
    def test_dropped_packet_and_ack_retry_deduplicated(self):
        stream='demo-test';state=ReceiverState(stream,'DEV01');stop=threading.Event()
        sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);sock.bind(('127.0.0.1',0));sock.settimeout(.01)
        received=[];errors=[]
        def worker():
            try:
                while not stop.is_set():
                    state.tick(time.monotonic_ns())
                    try:data,peer=sock.recvfrom(8192)
                    except socket.timeout:continue
                    p=json.loads(data)
                    if p['kind']=='ATTACK':
                        received.append(p)
                        if len(received)==1:continue # first datagram lost
                    r=state.accept(p,time.monotonic_ns())
                    if p['kind']=='ATTACK' and len(received)==2:continue # first applied ACK lost
                    # Unity receiver echoes event ID on every ACK, including
                    # duplicate outcomes; the old reference omits it on reject.
                    r.update(version=2,stream_id=stream,event_id=p.get('event_id'),ack_sent_ns=time.monotonic_ns())
                    sock.sendto(json.dumps(r).encode(),peer)
            except BaseException as exc:errors.append(repr(exc))
        t=threading.Thread(target=worker);t.start();sender=DemoSender('127.0.0.1',sock.getsockname()[1],stream,'DEV01')
        try:
            deadline=time.monotonic()+1
            while sender.clock is None and time.monotonic()<deadline:sender.service();time.sleep(.002)
            self.assertIsNotNone(sender.clock)
            now=time.monotonic_ns();sender.offer(ActionEvent('one','DEV01',stream,1,now,now,now+150_000_000,'ATTACK','LEFT',{},'test'))
            deadline=time.monotonic()+.25
            while time.monotonic()<deadline:sender.service();time.sleep(.002)
            self.assertEqual(len(state.attacks),1);self.assertEqual(len(received),3)
            self.assertEqual(sender.delivery['DUPLICATE_EVENT'],1)
            self.assertLessEqual(received[-1]['sent_ns']+received[-1]['ttl_ms']*1_000_000,now+150_000_000)
        finally:stop.set();t.join();sock.close();sender.close()
        self.assertFalse(errors)

    def test_expired_event_never_sent(self):
        s=DemoSender('127.0.0.1',9,'unused','DEV01');now=time.monotonic_ns()
        try:
            s.offer(ActionEvent('old','DEV01','unused',1,now,now,now-1,'ATTACK','LEFT',{},'test'))
            s.service();self.assertFalse(s.events);self.assertEqual(s.delivery['expired_without_ack'],1)
            self.assertEqual(s.counts['sent_ATTACK'],0)
        finally:s.close()


@unittest.skipUnless(SESSION,'Stored development landmarks required')
class SyntheticLiveTests(unittest.TestCase):
    def test_camera_free_full_live_recording_and_clean_shutdown(self):
        import argparse
        import cv2
        import numpy as np
        import types
        from redbool_demo.live import run
        from mediapipe.tasks.python.vision import PoseLandmarker
        source=[json.loads(x) for x in (Path(SESSION)/'frames.jsonl').read_text().splitlines()][8774]
        points=[types.SimpleNamespace(x=p[0],y=p[1],z=p[2],visibility=source['landmark_visibility']['image'][i],presence=1.) for i,p in enumerate(source['image_landmarks'])]
        class FakeCamera:
            def set(self,*args):pass
            def getBackendName(self):return 'SYNTHETIC_TEST'
            def read(self):time.sleep(.033);return True,np.zeros((480,640,3),np.uint8)
            def release(self):pass
        class FakePose:
            def detect_for_video(self,*args):return types.SimpleNamespace(pose_landmarks=[points],pose_world_landmarks=[])
            def detect(self,*args):return self.detect_for_video(*args)
            def close(self):pass
        shown=[0]
        def imshow(*args):shown[0]+=1
        def key(*args):return ord('q') if shown[0]>=35 else ord('c') if shown[0]==1 else -1
        with tempfile.TemporaryDirectory() as folder:
            args=argparse.Namespace(pose=Path('pose_landmarker.task'),check=False,stream_id=None,output=Path(folder),
                guided=True,movement=False,host='127.0.0.1',port=9,actor='DEV01',camera=0,backend='auto',seconds=6,mirror=True,preview_fps=15.)
            with patch('pose_capture.open_camera',return_value=FakeCamera()),patch.object(PoseLandmarker,'create_from_options',return_value=FakePose()) as factory,patch.object(cv2,'imshow',side_effect=imshow),patch.object(cv2,'waitKey',side_effect=key),patch.object(cv2,'destroyAllWindows'):
                run(args)
            root=next(Path(folder).iterdir());r=json.loads((root/'summary.json').read_text())
            self.assertFalse(r['errors']);self.assertEqual(r['counts']['events'],0)
            self.assertTrue(r['guided_check']['interrupted'])
            self.assertGreaterEqual(r['counts']['processed'],35)
            self.assertGreater(r['counts']['processed'],r['counts']['preview_frames']*1.4)
            self.assertEqual(r['performance']['camera_requested_fps'],30)
            self.assertGreater(r['performance']['processed_fps'],0)
            session=Path(r['session']);m=json.loads((session/'manifest.json').read_text())
            self.assertTrue(m['video']['lossless_verified'])
            self.assertTrue(m['mirror_display'])
            self.assertFalse(m['recording_config']['source_mirrored'])
            self.assertFalse(m['video']['mirror'])
            annotations=[json.loads(x) for x in (session/'annotations.jsonl').read_text().splitlines()]
            self.assertTrue(any(a['source']=='prompt' and a['notes']['ground_truth_status']=='NOT_REVIEWED' for a in annotations))
            self.assertEqual(m['video']['encoded_frame_count'],r['counts']['processed'])
            self.assertEqual(json.loads((session/'summary.json').read_text())['status'],'completed')
            if getattr(args,'cross_pose',None) and getattr(args,'cross_redetect',True):
                self.assertEqual([c.args[0].running_mode.name for c in factory.call_args_list],['VIDEO','VIDEO','IMAGE'])
                self.assertEqual(m['recording_config']['cross_redetect']['mode'],'IMAGE')
                rows=[json.loads(x) for x in (session/'frames.jsonl').read_text().splitlines()]
                # Calibration deliberately uses Lite only. A skipped auxiliary
                # must remain null rather than reusing a previous skeleton.
                metadata=[row['processing_metadata'] for row in rows]
                skipped=[row for row in metadata if row['cross_recovery'] is None]
                recovered=[row for row in metadata if row['cross_recovery'] is not None]
                self.assertTrue(skipped)
                self.assertTrue(all(row['cross_pose'] is None for row in skipped))
                self.assertTrue(recovered)
                self.assertTrue(all(row['cross_recovery']['mode']=='IMAGE' and row['cross_pose']['variant']=='Full'
                                    for row in recovered))
                self.assertTrue(r['performance']['cross_redetect_ms_p50_p95'])


if __name__=='__main__':unittest.main()
