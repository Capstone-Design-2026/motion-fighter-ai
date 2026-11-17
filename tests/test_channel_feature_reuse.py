import unittest
from dataclasses import replace
from unittest.mock import patch
import threading
from action_engine.types import PoseObservation
from action_engine.experimental import ChannelSignals, AblationConfig, features, joint_validity, _joint_validity, _validity_cache

class ChannelFeatureReuseTests(unittest.TestCase):
    def setUp(self):
        self.cfg = AblationConfig(channel_validity=True)
        p = [[.5,.5,0.] for _ in range(33)]
        for j,xy in {11:(.4,.4),12:(.6,.4),13:(.3,.6),14:(.7,.6),15:(.4,.3),16:(.6,.3)}.items():
            p[j][:2] = xy
        self.a = PoseObservation('P1','session',1,1_000_000_000,tuple(map(tuple,p)),(1.,)*33,640,480)
        self.b = replace(self.a,frame_id=2,capture_ns=1_033_333_333)
        ChannelSignals._feature_cache.last = None
        _validity_cache.entries = ()
    def test_reuse_preserves_separate_channel_history_and_returned_values(self):
        channels = [ChannelSignals(self.cfg) for _ in range(3)]
        for c in channels:c.update(self.a)
        with patch('action_engine.experimental.features',wraps=features) as calls:
            arms=[c.update(self.b) for c in channels]
            self.assertEqual(calls.call_count,1)
        arms[0]['LEFT']['position'][0]=900
        self.assertNotEqual(arms[1]['LEFT']['position'][0],900)
        channels[0].reset()
        self.assertEqual(channels[0].update(self.b),{})
        self.assertIs(channels[1].previous,self.b)
    def test_different_capture_identity_visibility_and_gap_do_not_reuse(self):
        with patch('action_engine.experimental.features',wraps=features) as calls:
            ChannelSignals._features(self.a,self.b,self.cfg)
            ChannelSignals._features(self.a,replace(self.b,capture_epoch='other'),self.cfg)
            ChannelSignals._features(self.a,self.b,replace(self.cfg,visibility=.9))
            ChannelSignals._features(self.a,self.b,replace(self.cfg,max_gap_ms=200))
            self.assertEqual(calls.call_count,4)
    def test_mutable_inputs_never_reused(self):
        b=replace(self.b,landmarks=[list(x) for x in self.b.landmarks])
        with patch('action_engine.experimental.features',wraps=features) as calls:
            ChannelSignals._features(self.a,b,self.cfg)
            b.landmarks[15][0]=.7
            ChannelSignals._features(self.a,b,self.cfg)
            self.assertEqual(calls.call_count,2)
            self.assertIsNone(ChannelSignals._feature_cache.last)
    def test_joint_reasons_are_not_mutable_shared_state_and_config_is_respected(self):
        with patch('action_engine.experimental._joint_validity',wraps=_joint_validity) as calls:
            first=joint_validity(self.a,self.cfg)
            first['LEFT'].append('stale_capture')
            second=joint_validity(self.a,self.cfg)
            self.assertEqual(second['LEFT'],[])
            joint_validity(self.a,replace(self.cfg,visibility=.9))
            self.assertEqual(calls.call_count,2)
        for i in range(10):joint_validity(replace(self.a,frame_id=i),self.cfg)
        self.assertLessEqual(len(_validity_cache.entries),4)
    def test_invalid_joint_reasons_match_uncached_with_nonfinite_and_outside(self):
        for value in [float('nan'),float('inf'),-.1,1.2]:
            pts=[list(v) for v in self.a.landmarks];pts[15][0]=value
            o=replace(self.a,landmarks=tuple(map(tuple,pts)))
            self.assertEqual(joint_validity(o,self.cfg),_joint_validity(o,self.cfg))
            self.assertEqual(joint_validity(o,self.cfg),_joint_validity(o,self.cfg))
    def test_worker_threads_do_not_share_cache(self):
        ChannelSignals._features(self.a,self.b,self.cfg)
        seen=[]
        def other():seen.append(getattr(ChannelSignals._feature_cache,'last',None))
        t=threading.Thread(target=other);t.start();t.join()
        self.assertEqual(seen,[None])

if __name__=='__main__': unittest.main()
