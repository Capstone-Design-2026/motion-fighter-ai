"""Read-only probability adapters over existing V1/V2 assets and preprocessing."""
from collections import deque
from types import SimpleNamespace
import time
import numpy as np
from demo_eval.realtime import Adapter
from vision_v2.preprocess import Config,features,resample,aggregate,apply_standardizer
from .types import ActionEvidence

class FrozenEvidenceAdapter:
    def __init__(self,kind,fold_dir=None):
        self.kind=kind;self.loaded=Adapter(kind,fold_dir);self.identity=None;self.frames=deque();self.last_prediction=None;self.last_capture=None
    def predict(self,obs,produced_ns=None):
        ident=(obs.actor_id,obs.capture_epoch)
        if self.identity!=ident:
            self.identity=ident;self.frames.clear();self.last_prediction=None;self.last_capture=None
        if self.last_capture is not None and obs.capture_ns<=self.last_capture:return None
        if self.last_capture is not None and obs.capture_ns-self.last_capture>100_000_000:
            self.frames.clear();self.last_prediction=None
        self.last_capture=obs.capture_ns
        if not obs.pose_present or obs.landmarks is None or obs.visibility is None or obs.width<=0 or obs.height<=0:
            self.frames.clear();self.last_prediction=None;return None
        frame=obs.as_frame()
        if not np.isfinite(frame['image_landmarks']).all() or not np.isfinite(obs.visibility).all():return None
        if self.kind=='v1':
            lm=[SimpleNamespace(x=p[0],y=p[1],z=p[2],visibility=v) for p,v in zip(frame['image_landmarks'],obs.visibility)]
            x=self.loaded.v1.normalize_landmarks(lm)[None]
            prob=self.loaded.model(x,training=False).numpy()[0]
        else:
            self.frames.append(frame)
            while self.frames and obs.capture_ns-self.frames[0]['capture_return_monotonic_ns']>3_000_000_000:self.frames.popleft()
            if len(self.frames)<2 or obs.capture_ns-self.frames[0]['capture_return_monotonic_ns']<2_800_000_000:return None
            if self.last_prediction is not None and obs.capture_ns-self.last_prediction<200_000_000:return None
            x,m,t=features(list(self.frames),Config());sx,sm,observed,_,grid=resample(x,m,t,Config())
            if self.kind in ('logistic','mlp'):
                v,vm=aggregate(sx,sm);z=apply_standardizer(v[None],vm[None],self.loaded.stats)
                prob=self.loaded.model.predict_proba(z)[0] if self.kind=='logistic' else self.loaded.model(z,training=False).numpy()[0]
            else:
                from vision_v2.temporal_models import prepare_inputs
                z=apply_standardizer(sx[None],sm[None],self.loaded.stats)
                prob=self.loaded.model(prepare_inputs(z,sm[None],observed[None],grid),training=False).numpy()[0]
        self.last_prediction=obs.capture_ns
        return ActionEvidence(*ident,obs.frame_id,obs.capture_ns,time.monotonic_ns() if produced_ns is None else produced_ns,
                              self.kind,{str(c):float(p) for c,p in zip(self.loaded.classes,prob)})
