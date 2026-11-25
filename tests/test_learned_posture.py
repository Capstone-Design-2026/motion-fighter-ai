import json,os,tempfile,unittest
from pathlib import Path
from dataclasses import replace
import numpy as np
from redbool_demo.learned import CausalFeatures,PostureModel,LearnedGuardEngine,CLASSES
from redbool_demo.guided import GuidedCheck
from action_engine.types import PoseObservation

class PortableModelTests(unittest.TestCase):
    def test_portable_softmax_matches_direct_calculation(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'model.npz';rng=np.random.default_rng(41)
            w=rng.normal(size=(128,7));b=rng.normal(size=7);mean=rng.normal(size=128);scale=np.ones(128)*2
            np.savez(p,metadata=json.dumps({'feature_version':'causal-arms-400ms-v1','layers':1,'classes':list(CLASSES)}),mean=mean,scale=scale,w0=w,b0=b)
            x=rng.normal(size=(4,128));z=(x-mean)/scale@w+b;expected=np.exp(z-z.max(axis=1,keepdims=True));expected/=expected.sum(axis=1,keepdims=True)
            np.testing.assert_allclose(PostureModel(p).predict_proba(x),expected,rtol=1e-12)
    def test_nonfinite_model_is_rejected_before_inference(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'bad.npz'
            np.savez(p,metadata=json.dumps({'feature_version':'causal-arms-400ms-v1','layers':1,'classes':list(CLASSES)}),mean=np.zeros(128),scale=np.full(128,np.nan),w0=np.zeros((128,7)),b0=np.zeros(7))
            with self.assertRaises(ValueError):PostureModel(p)
    def test_short_guide_contains_five_real_repeats_and_rest(self):
        g=GuidedCheck(movement=True,self_paced=True,focus='left-return')
        self.assertEqual(len(g.steps),13)
        self.assertEqual(sum(label=='LEFT_RAISE' for label,_ in g.steps),5)
        self.assertEqual(g.steps[-2][0],'NONE')

@unittest.skipUnless(os.environ.get('REDBOOL_CHIN_SESSION'),'recorded observations required')
class CausalFeatureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_CHIN_SESSION']);m=json.loads((p/'manifest.json').read_text())
        cls.obs=[PoseObservation.from_record(json.loads(line),m) for line in (p/'frames.jsonl').read_text().splitlines()[1279:1340]]
    def test_high_model_confidence_cannot_override_missing_joints(self):
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/'guard.npz';bias=np.zeros(7);bias[0]=100
            np.savez(p,metadata=json.dumps({'feature_version':'causal-arms-400ms-v1','layers':1,'classes':list(CLASSES)}),mean=np.zeros(128),scale=np.ones(128),w0=np.zeros((128,7)),b0=bias)
            engine=LearnedGuardEngine(p,movement=True);engine.request_calibration()
            for o in self.obs[:-1]:engine.process(o,now_ns=o.capture_ns);engine.drain_events(o.capture_ns)
            self.assertTrue(engine.calibrated)
            bad=replace(self.obs[-1],visibility=tuple(0. for _ in range(33)))
            state=engine.process(bad,now_ns=bad.capture_ns)
            self.assertFalse(engine.ready);self.assertFalse(state.block);self.assertEqual(state.movement,'NEUTRAL')
            self.assertFalse(engine.drain_events(bad.capture_ns))
    def test_prefix_outputs_do_not_change_with_future_input(self):
        def run(observations):
            f=CausalFeatures();return [f.update(o) for o in observations]
        short=run(self.obs[:20]);long=run(self.obs)
        for a,b in zip(short,long):
            if a is None:self.assertIsNone(b)
            else:np.testing.assert_array_equal(a,b)
        self.assertTrue(np.isfinite(short[-1]).all())
    def test_duplicate_gap_and_new_actor_cannot_reuse_history(self):
        f=CausalFeatures()
        for o in self.obs[:15]:f.update(o)
        self.assertIsNone(f.update(self.obs[14]))
        o=replace(self.obs[15],capture_ns=self.obs[14].capture_ns+200_000_000)
        self.assertIsNone(f.update(o));self.assertEqual(len(f.history),0)
        self.assertIsNone(f.update(replace(o,actor_id='other')));self.assertEqual(len(f.history),0)

@unittest.skipUnless(os.environ.get('REDBOOL_GUARD_SESSION'),'14:04 capture required')
class ReadyDisplayTests(unittest.TestCase):
    def test_removed_lower_gesture_is_not_displayed_as_ready(self):
        from redbool_demo.six_engine import SixGestureEngine
        p=Path(os.environ['REDBOOL_GUARD_SESSION']);m=json.loads((p/'manifest.json').read_text())
        markers={a['event_frame_id'] for a in map(json.loads,(p/'annotations.jsonl').read_text().splitlines()) if a['label']=='CALIBRATE'}
        e=SixGestureEngine()
        for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines()):
            o=PoseObservation.from_record(f,m);state=e.process(o,now_ns=o.capture_ns);events=e.drain_events(o.capture_ns);e.take_logs()
            if any(lo<=o.frame_id<=hi for lo,hi in [(2119,2134),(3374,3394),(4644,4664)]):
                self.assertFalse(e.ready);self.assertNotEqual(state.attack_state,'READY');self.assertFalse(events)
            if o.frame_id in markers:e.request_calibration()
