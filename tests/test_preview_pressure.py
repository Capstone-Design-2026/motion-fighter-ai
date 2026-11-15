"""Producer budget tests, with no camera, UDP, HTTP listener or model work."""
import threading
import time
import unittest
from unittest.mock import patch

from redbool_demo.local_bridge import LocalBridge
from redbool_demo.performance_policy import InferencePolicy


class NoSocketServer:
    def __init__(self, address, handler):self.server_address=('127.0.0.1',0)
    def serve_forever(self,**kw):pass
    def shutdown(self):pass
    def server_close(self):pass


class PreviewPressureTests(unittest.TestCase):
    def bridge(self,fps=15,encoder=lambda *args:b'jpeg',clock=time.monotonic):
        with patch('redbool_demo.local_bridge.HTTPServer',NoSocketServer):
            bridge=LocalBridge(0,'t'*32,preview_fps=fps,encoder=encoder,clock=clock)
        self.addCleanup(bridge.close)
        return bridge

    def test_normal_requested_rate_unchanged(self):
        bridge=self.bridge()
        self.assertEqual(bridge.snapshot()['preview_target_fps'],15.)
        self.assertAlmostEqual(bridge.interval,1/15)

    def test_overload_cap_and_lower_original_rate_are_monotone(self):
        for requested,expected in ((15,8),(12,8),(8,8),(5,5),(1,1)):
            with self.subTest(requested=requested):
                bridge=self.bridge(requested)
                self.assertEqual(bridge.limit_preview_fps(8.),expected)
                self.assertEqual(bridge.limit_preview_fps(15.),expected)
                self.assertEqual(bridge.snapshot()['preview_requested_fps'],requested)

    def test_invalid_limits_do_not_change_budget(self):
        bridge=self.bridge()
        for value in (-1,0,16,float('nan'),float('inf'),float('-inf'),None,'8',True):
            with self.subTest(value=value),self.assertRaises(ValueError):
                bridge.limit_preview_fps(value)
            self.assertEqual(bridge.snapshot()['preview_target_fps'],15.)

    def test_c_and_pause_cannot_restore_overload_budget(self):
        bridge=self.bridge();policy=InferencePolicy(True)
        transition=None
        for i in range(10):
            stamp=(i+1)*40_000_000
            transition=policy.observe(auxiliary_ran=True,capture_ns=stamp,
                primary_end_ns=stamp+10_000_000,auxiliary_end_ns=stamp+140_000_000,
                processing_end_ns=stamp+145_000_000)
        self.assertTrue(transition)
        bridge.limit_preview_fps(8.)
        for command in ('calibrate','pause','calibrate'):
            bridge.command(command,command+str(time.monotonic_ns()))
            self.assertEqual(bridge.take_command(),command)
            policy.observe(auxiliary_ran=False,capture_ns=1_000_000_000,
                primary_end_ns=1_010_000_000,auxiliary_end_ns=1_010_000_000,
                processing_end_ns=1_011_000_000)
            self.assertFalse(policy.run_auxiliary(True))
            self.assertEqual(bridge.snapshot()['preview_target_fps'],8.)

    def test_lower_limit_during_previous_wait_is_rechecked_before_encode(self):
        # Deterministic clock/event fixture executes the real preview loop.
        # The cap arrives during an already-scheduled 1/15s wait.
        now=[0.];starts=[]
        bridge=self.bridge(clock=lambda:now[0])
        class Ready:
            def wait(self,timeout):return True
            def clear(self):pass
            def set(self):pass
        class Closed:
            done=False
            waits=0
            def is_set(self):return self.done
            def set(self):self.done=True
            def wait(self,delay):
                self.waits+=1
                if self.waits==2:bridge.limit_preview_fps(8.)
                now[0]+=delay
                return self.done
        bridge.frame_ready=Ready();bridge.closed=Closed()
        def encode(*args):
            starts.append(now[0])
            if len(starts)>=4:bridge.closed.set()
            else:bridge.offer_preview(len(starts),len(starts))
            return b'jpeg'
        bridge.encoder=encode;bridge.offer_preview(0,0)
        bridge._preview_loop()
        self.assertEqual(len(starts),4)
        self.assertAlmostEqual(starts[1],.125)
        self.assertTrue(all(b-a>=.125-1e-12 for a,b in zip(starts,starts[1:])))

    def test_slow_encoder_cannot_block_offer_and_only_latest_is_kept(self):
        entered=threading.Event();release=threading.Event();offered=threading.Event()
        samples=[]
        def encode(frame,*args):
            samples.append(frame)
            if len(samples)==1:entered.set();release.wait(2)
            return b'jpeg'
        bridge=self.bridge(encoder=encode);bridge.limit_preview_fps(8.)
        bridge.threads[1].start();bridge.offer_preview('first',1)
        self.assertTrue(entered.wait(1))
        def producer():
            for i in range(100):bridge.offer_preview(i,100+i)
            offered.set()
        worker=threading.Thread(target=producer);worker.start()
        try:
            self.assertTrue(offered.wait(.5),'Inference-side offer blocked behind encoder')
            self.assertFalse(release.is_set())
            self.assertEqual(samples,['first'])
            self.assertEqual(bridge.pending_frame[0],99)
        finally:
            release.set();worker.join(1)
        deadline=time.monotonic()+1
        while len(samples)<2 and time.monotonic()<deadline:time.sleep(.005)
        self.assertEqual(samples,['first',99])
        self.assertGreaterEqual(bridge.dropped,99)


if __name__=='__main__':unittest.main()
