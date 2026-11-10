import unittest
from dataclasses import replace
import numpy as np
from action_engine.types import EngineConfig,PoseObservation
from redbool_demo.special import CrossSpecial,chest_cross
from redbool_demo.six_engine import SixGestureEngine


def pose(kind='guard',frame=1,stamp=0):
    p=np.zeros((33,3));p[:,0]=.5;p[:,1]=.5
    p[11]=(.65,.30,0);p[12]=(.35,.30,0)
    p[13]=(.65,.60,0);p[14]=(.35,.60,0)
    p[15]=(.60,.30,0);p[16]=(.40,.30,0)
    p[23]=(.62,.78,0);p[24]=(.38,.78,0)
    if kind=='cross':p[15]=(.42,.40,0);p[16]=(.58,.40,0)
    if kind=='block':p[15]=(.60,.10,0);p[16]=(.40,.10,0)
    if kind=='left_raise':p[15]=(.60,.08,0)
    return PoseObservation('DEV01','test',frame,stamp,tuple(map(tuple,p)),(1.,)*33,640,480)


class CrossGeometryTests(unittest.TestCase):
    def test_chest_cross_is_distinct_from_guard_block_and_one_raise(self):
        self.assertEqual(chest_cross(pose('cross'),EngineConfig()),(True,True))
        for kind in ('guard','block','left_raise'):
            self.assertEqual(chest_cross(pose(kind),EngineConfig()),(False,False))
    def test_mirror_and_missing_wrist(self):
        o=pose('cross');p=np.array(o.landmarks);p[:,0]=1-p[:,0]
        self.assertEqual(chest_cross(replace(o,landmarks=tuple(map(tuple,p)),source_mirrored=True),EngineConfig()),(True,True))
        vis=list(o.visibility);vis[15]=.1
        self.assertEqual(chest_cross(replace(o,visibility=tuple(vis)),EngineConfig()),(False,False))
    def test_above_eye_height_cross_does_not_trigger(self):
        o=pose('cross');p=np.array(o.landmarks);p[[15,16],1]=.05
        self.assertEqual(chest_cross(replace(o,landmarks=tuple(map(tuple,p))),EngineConfig()),(False,False))


class CrossLifecycleTests(unittest.TestCase):
    def setUp(self): self.s=CrossSpecial();self.t=0
    def frames(self,n,neutral=False,cross=False,valid=True,calibrated=True):
        events=0
        for _ in range(n):
            self.t+=33_333_333
            events+=self.s.step(self.t,calibrated=calibrated,valid=valid,neutral=neutral,approach=cross,strict=cross)
        return events
    def test_hold_once_then_rearm_only_after_neutral_return(self):
        self.frames(10,neutral=True)
        self.assertEqual(self.frames(20,cross=True),0)
        self.assertEqual(self.frames(100,cross=True),1)
        self.assertEqual(self.frames(30),0)
        self.assertEqual(self.frames(30,cross=True),0)
        self.frames(10,neutral=True)
        self.assertEqual(self.frames(30,cross=True),1)
    def test_calibrated_first_cross_needs_full_hold_but_not_extra_guard(self):
        self.assertEqual(self.frames(60,cross=True,calibrated=False),0)
        self.assertEqual(self.frames(20,cross=True),0)
        self.assertEqual(self.frames(10,cross=True),1)
        self.assertEqual(self.frames(60,cross=True),0)
    def test_tracking_gap_requires_a_whole_new_hold(self):
        self.frames(10,neutral=True);self.frames(10,cross=True)
        self.t+=200_000_000
        self.assertEqual(self.frames(20,cross=True),0)
        self.assertEqual(self.frames(10,cross=True),1)
        self.assertEqual(self.frames(60,cross=True),0)
        self.frames(10,neutral=True)
        self.assertEqual(self.frames(30,cross=True),1)
    def test_occlusion_cannot_finish_a_pending_hold(self):
        self.frames(10,neutral=True);self.frames(10,cross=True)
        self.assertEqual(self.frames(60,cross=True,valid=False),0)
        self.assertEqual(self.frames(20,cross=True),0)
        self.assertEqual(self.frames(10,cross=True),1)
        self.assertEqual(self.frames(60,cross=True),0)
    def test_recalibration_clears_candidate(self):
        self.frames(10,neutral=True);self.frames(10,cross=True)
        self.frames(1,calibrated=False)
        self.assertEqual(self.frames(20,cross=True),0)
        self.assertEqual(self.frames(10,cross=True),1)


class IntegratedCrossTests(unittest.TestCase):
    def test_real_engine_cross_has_no_punch_kick_or_block_and_returns(self):
        engine=SixGestureEngine(movement=True);engine.request_calibration()
        events=[];states=[];i=0
        def frames(n,kind):
            nonlocal i
            for _ in range(n):
                i+=1;o=pose(kind,i,i*33_333_333)
                states.append(engine.process(o,now_ns=o.capture_ns))
                events.extend(engine.drain_events(o.capture_ns));engine.take_logs()
        frames(50,'guard');self.assertTrue(engine.calibrated);self.assertTrue(engine.ready)
        frames(90,'cross')
        self.assertEqual([e.reason['gesture'] for e in events],['SPECIAL'])
        self.assertEqual(events[0].side,'BOTH')
        self.assertEqual(events[0].expires_ns-events[0].committed_ns,150_000_000)
        self.assertFalse(any(s.block for s in states[50:]));self.assertFalse(engine.ready)
        frames(30,'guard');self.assertTrue(engine.ready)
        frames(30,'cross')
        self.assertEqual([e.reason['gesture'] for e in events],['SPECIAL','SPECIAL'])


class SpecialDeliveryTests(unittest.TestCase):
    def test_rejected_special_is_terminal_not_queued_until_charge(self):
        from unittest.mock import patch
        import time
        from action_engine.types import ActionEvent
        from redbool_demo.transport import DemoSender
        for status in ('NOT_CHARGED','NO_SPECIAL','GAME_BUSY','INPUT_LOCKED'):
            with self.subTest(status=status):
                sender=DemoSender('127.0.0.1',9,'special-test','DEV01')
                try:
                    now=time.monotonic_ns()
                    sender.offer(ActionEvent('cross','DEV01','special-test',1,now,now,now+150_000_000,'ATTACK','BOTH',{'gesture':'SPECIAL'},'test'))
                    with patch('realtime_vision.transport.Sender.service',return_value=[{'type':'ack','ack':{'event_id':'cross','status':status}}]):
                        sender.service()
                    self.assertFalse(sender.events)
                    self.assertEqual(sender.delivery[status],1)
                    self.assertEqual(sender.delivery['transmissions'],0)
                finally:sender.close()
