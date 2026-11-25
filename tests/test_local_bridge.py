import http.client
import json
import os
import subprocess
import sys
import threading
import time
import unittest
from redbool_demo.local_bridge import LocalBridge, ParentProcess, jpeg_encoder


class Parent:
    living = True
    closed = False
    def alive(self):return self.living
    def close(self):self.closed=True


class LocalBridgeTests(unittest.TestCase):
    def setUp(self):
        self.parent=Parent()
        self.bridge=LocalBridge(0,'t'*32,preview_fps=8,parent=self.parent,
                                encoder=lambda f,m,l:b'jpeg:'+bytes([f])).start()

    def tearDown(self):self.bridge.close()

    def request(self,path='/status',method='GET',body=None,token=True,headers=None):
        conn=http.client.HTTPConnection('127.0.0.1',self.bridge.port,timeout=2)
        h={'X-REDBOOL-Token':'t'*32} if token else {}
        h.update(headers or {})
        conn.request(method,path,body=body,headers=h);response=conn.getresponse()
        data=response.read();result=response.status,dict(response.getheaders()),data
        conn.close();return result

    def control(self,command,identity):
        return self.request('/control','POST',json.dumps(dict(command=command,request_id=identity)))

    def wait_until(self,predicate,seconds=2):
        deadline=time.monotonic()+seconds
        while not predicate() and time.monotonic()<deadline:time.sleep(.005)
        self.assertTrue(predicate())

    def test_loopback_token_no_preview_before_camera_and_bounded_body(self):
        self.assertEqual(self.bridge.server.server_address[0],'127.0.0.1')
        self.assertEqual(self.request(token=False)[0],403)
        self.assertEqual(self.request('/preview.jpg')[0],204)
        self.assertEqual(self.request('/control','POST','x'*1025)[0],413)
        self.assertEqual(self.request('/control','POST','{}')[0],400)
        status=json.loads(self.request()[2])
        self.assertFalse(status['camera_connected']);self.assertEqual(status['phase'],'starting')

    def test_control_dedup_latest_command_and_stop_cannot_be_undone(self):
        self.assertEqual(self.control('calibrate','one')[0],202)
        self.assertEqual(self.bridge.take_command(),'calibrate')
        self.assertTrue(json.loads(self.control('calibrate','one')[2])['duplicate'])
        self.assertIsNone(self.bridge.take_command())
        self.control('calibrate','two');self.control('pause','three')
        self.assertEqual(self.bridge.take_command(),'pause')
        self.control('stop','four');self.control('calibrate','five')
        self.assertTrue(self.bridge.stop_requested.is_set())
        self.assertIsNone(self.bridge.take_command())

    def test_slow_encoder_keeps_only_latest_without_blocking_offer(self):
        entered=threading.Event();release=threading.Event();frames=[]
        def encoder(frame,mirror,landmarks):
            frames.append(frame);entered.set();release.wait(2);return b'jpeg:'+bytes([frame])
        self.bridge.encoder=encoder
        self.bridge.offer_preview(1,10)
        self.assertTrue(entered.wait(1))
        start=time.monotonic()
        for i in range(2,50):self.bridge.offer_preview(i,i*10)
        self.assertLess(time.monotonic()-start,.1)
        release.set()
        self.wait_until(lambda:self.bridge.encoded==2)
        self.assertEqual(frames,[1,49]);self.assertEqual(self.bridge.dropped,47)
        code,headers,data=self.request('/preview.jpg')
        self.assertEqual(code,200);self.assertEqual(headers['X-REDBOOL-Sequence'],'2')
        self.assertEqual(data,b'jpeg:1')  # byte49, the latest synthetic frame

    def test_parent_exit_requests_graceful_stop_and_close_joins_workers(self):
        self.parent.living=False
        self.wait_until(self.bridge.stop_requested.is_set)
        self.assertEqual(self.bridge.snapshot()['stop_reason'],'owning_game_exited')
        self.bridge.close()
        self.assertTrue(self.parent.closed)
        self.assertTrue(all(not t.is_alive() for t in self.bridge.threads))

    def test_preview_mirrors_copy_and_does_not_change_inference_pixels(self):
        import cv2 as cv
        import numpy as np
        pixels=np.zeros((480,640,3),np.uint8);pixels[:,:320]=(0,0,240)
        original=pixels.copy()
        data=jpeg_encoder(pixels,True,None)
        view=cv.imdecode(np.frombuffer(data,np.uint8),cv.IMREAD_COLOR)
        self.assertEqual(view.shape,(240,320,3))
        self.assertGreater(int(view[120,280,2]),200)
        self.assertLess(int(view[120,40,2]),20)
        np.testing.assert_array_equal(pixels,original)

    def test_real_parent_probe_does_not_signal_or_kill_parent(self):
        parent=ParentProcess(os.getppid())
        try:self.assertTrue(parent.alive())
        finally:parent.close()

    def test_process_exit_is_detected_using_real_native_handle(self):
        child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)'])
        probe=ParentProcess(child.pid)
        try:
            self.assertTrue(probe.alive())
            child.terminate();child.wait(timeout=5)
            self.assertFalse(probe.alive())
        finally:
            if child.poll() is None:child.kill();child.wait()
            probe.close()


if __name__=='__main__':unittest.main()
