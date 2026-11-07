"""One raised arm visibly releases an X without forcing the other into guard."""
import json,os,time,unittest
from pathlib import Path
from dataclasses import replace
from unittest.mock import patch
import numpy as np
from test_chest_special import pose
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import cross_geometry
from redbool_demo.transport import DemoSender
from realtime_vision.transport import Sender

class OneArmReleaseTests(unittest.TestCase):
    def test_cross_raise_cross_with_other_hand_still_crossed(self):
        for side,idx,x in [('LEFT',15,.60),('RIGHT',16,.40)]:
            with self.subTest(side=side):
                e=SixGestureEngine();e.request_calibration();events=[];i=0
                def feed(n,kind='guard',raise_arm=False):
                    nonlocal i
                    for _ in range(n):
                        i+=1;o=pose(kind,i,i*33_333_333)
                        if raise_arm:
                            p=np.array(o.landmarks);p[idx]=[x,.05,0];o=replace(o,landmarks=tuple(map(tuple,p)))
                        e.process(o,now_ns=o.capture_ns);events.extend(e.drain_events(o.capture_ns));e.take_logs()
                feed(55);feed(45,'cross');feed(55,'cross',True);feed(45,'cross');feed(90,'cross')
                self.assertEqual([v.reason['gesture'] for v in events],['SPECIAL',side+'_RAISE','SPECIAL'])
    def test_cross_held_and_missing_hand_are_not_release(self):
        o=pose('cross');cfg=SixGestureEngine().config
        self.assertFalse(cross_geometry(o,cfg)[2])
        v=list(o.visibility);v[15]=0
        self.assertEqual(cross_geometry(replace(o,visibility=tuple(v)),cfg),(False,False,False))

class GameFeedbackTests(unittest.TestCase):
    def test_fresh_charge_and_busy_hint_expires_and_old_ack_cannot_replace(self):
        s=DemoSender('127.0.0.1',9,'feedback','DEV01')
        try:
            now=time.monotonic_ns()
            def ack(seq,stamp,can=True,charge=True):
                return {'type':'ack','ack':{'status':'APPLIED','sequence':seq,'sender_ns':stamp,'game_can_act':can,'special_ready':charge}}
            with patch.object(Sender,'service',return_value=[ack(3,now,False,False)]):s.service()
            self.assertEqual(s.game_hint(now),'GAME BUSY / SPECIAL NEEDS CHARGE')
            with patch.object(Sender,'service',return_value=[ack(2,now),ack(4,now-1_000_000_000)]):s.service()
            self.assertEqual(s.game_hint(now),'GAME BUSY / SPECIAL NEEDS CHARGE')
            self.assertIn('UNKNOWN',s.game_hint(now+600_000_000))
            fresh=time.monotonic_ns()
            with patch.object(Sender,'service',return_value=[ack(5,fresh)]):s.service()
            self.assertEqual(s.game_hint(fresh),'GAME READY / SPECIAL READY')
        finally:s.close()

@unittest.skipUnless(os.environ.get('REDBOOL_CHAIN_SESSION'),'Reviewed 23:39 capture required')
class RecordedChainTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_CHAIN_SESSION']);m=json.loads((p/'manifest.json').read_text())
        marks={a['event_frame_id'] for a in map(json.loads,(p/'annotations.jsonl').read_text().splitlines()) if a['label']=='CALIBRATE'}
        e=SixGestureEngine();cls.events=[];t0=None
        for line in (p/'frames.jsonl').read_text().splitlines():
            o=PoseObservation.from_record(json.loads(line),m);t0=t0 or o.capture_ns
            e.process(o,now_ns=o.capture_ns)
            cls.events.extend(((x.source_capture_ns-t0)/1e9,x.reason['gesture']) for x in e.drain_events(o.capture_ns));e.take_logs()
            if o.frame_id in marks:e.request_calibration()
    def test_first_five_cross_attempts_and_following_raises(self):
        for lo,hi in [(138.4,141),(142.5,145),(146.3,147.7),(150.8,154.3),(155.4,158.4)]:
            self.assertEqual([g for t,g in self.events if lo<=t<=hi],['SPECIAL'])
        for lo,hi,expected in [(141.1,142.2,'RIGHT_RAISE'),(145.2,146.1,'RIGHT_RAISE'),
                               (148.0,148.8,'LEFT_RAISE'),(154.5,155.3,'RIGHT_RAISE'),(158.6,159.4,'RIGHT_RAISE')]:
            self.assertEqual([g for t,g in self.events if lo<=t<=hi],[expected])
    def test_missing_late_cross_to_left_raise_is_recovered_without_duplicates(self):
        for lo,hi in [(231.4,232.2),(233.8,234.5),(236.3,237.1)]:
            self.assertEqual([g for t,g in self.events if lo<=t<=hi],['LEFT_RAISE'])
    def test_earlier_three_action_chain_and_rest_preserved(self):
        self.assertEqual([g for t,g in self.events if 110<=t<=113.5],['LEFT_RAISE','RIGHT_RAISE','LEFT_PUNCH'])
        self.assertFalse([(t,g) for t,g in self.events if 193<=t<=202])

if __name__=='__main__':unittest.main()
