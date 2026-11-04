"""Causal local posture classifier. Prediction is evidence, never an event alone."""
from collections import deque
from pathlib import Path
import json
import numpy as np
from vision_v2.preprocess import Config, features

# Body-relative arms, 2D elbow angles, causal velocity and shoulder/elbow/wrist visibility.
SELECT = np.array(list(range(14)) + list(range(16,28)) + list(range(30,36)))
CLASSES = ('GUARD','NONE','LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE','BLOCK')


class CausalFeatures:
    def __init__(self): self.reset()
    def reset(self):
        self.previous=None; self.history=deque(); self.identity=None
    def update(self, obs):
        ident=(obs.actor_id,obs.capture_epoch)
        if ident!=self.identity:
            self.reset();self.identity=ident
        if self.previous is not None and obs.capture_ns<=self.previous.capture_ns:
            return None
        if self.previous is None or obs.capture_ns-self.previous.capture_ns>100_000_000:
            self.history.clear();self.previous=obs;return None
        x,mask,_=features([self.previous.as_frame(),obs.as_frame()],Config(max_gap_seconds=.1))
        self.previous=obs
        v=x[-1,SELECT];m=mask[-1,SELECT]
        self.history.append((obs.capture_ns,v,m))
        while self.history and obs.capture_ns-self.history[0][0]>400_000_000:self.history.popleft()
        if len(self.history)<3:return None
        a=np.stack([h[1] for h in self.history]);valid=np.stack([h[2] for h in self.history])
        count=valid.sum(axis=0);mean=np.divide((a*valid).sum(axis=0),count,out=np.zeros(len(SELECT)),where=count>0)
        variance=np.divide((((a-mean)**2)*valid).sum(axis=0),count,out=np.zeros(len(SELECT)),where=count>0)
        return np.concatenate([v,mean,np.sqrt(variance),valid.mean(axis=0)]).astype(np.float32)


class PostureModel:
    """Load only data (NPZ), never executable pickle, for logistic/MLP inference."""
    def __init__(self,path):
        with np.load(Path(path),allow_pickle=False) as data:
            self.meta=json.loads(str(data['metadata']))
            self.mean=data['mean'].copy();self.scale=data['scale'].copy()
            self.weights=[data[f'w{i}'].copy() for i in range(self.meta['layers'])]
            self.biases=[data[f'b{i}'].copy() for i in range(self.meta['layers'])]
        if (self.meta['feature_version']!='causal-arms-400ms-v1'
                or self.mean.shape!=(128,) or self.scale.shape!=(128,)
                or not np.isfinite(self.mean).all() or not np.isfinite(self.scale).all()
                or np.any(self.scale<=0) or self.meta['classes']!=list(CLASSES)
                or not self.weights):
            raise ValueError('Unsupported or invalid posture model')
        width=128
        for w,b in zip(self.weights,self.biases):
            if (w.ndim!=2 or w.shape[0]!=width or b.shape!=(w.shape[1],)
                    or not np.isfinite(w).all() or not np.isfinite(b).all()):
                raise ValueError('Invalid posture model layer')
            width=w.shape[1]
        if width!=len(CLASSES):raise ValueError('Invalid posture model output')
        self.classes=self.meta['classes']
    def predict_proba(self, vector):
        x=(np.asarray(vector)-self.mean)/self.scale
        for i,(w,b) in enumerate(zip(self.weights,self.biases)):
            x=x@w+b
            if i<len(self.weights)-1:x=np.maximum(0,x)
        x=x-np.max(x,axis=-1,keepdims=True);p=np.exp(x)
        return p/p.sum(axis=-1,keepdims=True)


def export_model(path, model, scaler, classes, provenance):
    if hasattr(model,'coefs_'):weights=model.coefs_;biases=model.intercepts_
    else:weights=[model.coef_.T];biases=[model.intercept_]
    metadata={'feature_version':'causal-arms-400ms-v1','classes':list(classes),'layers':len(weights),'provenance':provenance}
    values={'metadata':json.dumps(metadata),'mean':scaler.mean_,'scale':scaler.scale_}
    for i,(w,b) in enumerate(zip(weights,biases)):values[f'w{i}']=w;values[f'b{i}']=b
    np.savez_compressed(path,**values)


from .six_engine import SixGestureEngine
from action_engine.experimental import joint_validity


class LearnedGuardEngine(SixGestureEngine):
    """Optional classifier confirmation for the newly expanded guard band.

    Existing strict guard is preserved. The model cannot bypass wrist visibility,
    bent elbows, speed, position, dwell, calibration or held-action protection.
    """
    def __init__(self, model_path, **kwargs):
        self.posture_model=PostureModel(model_path)
        self.guard_column=self.posture_model.classes.index('GUARD')
        super().__init__(**kwargs)
    def _components(self):
        super()._components()
        self.posture_features=CausalFeatures()
        self.posture_probability=None
        for unit in self.attack.units.values():unit.learned_guard_required=True
    def process(self, obs, evidence=None, now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns
            vector=self.posture_features.update(obs)
            fresh=0<=now-obs.capture_ns<=self.config.evidence_age_ms*1_000_000
            valid=fresh and not joint_validity(obs,self.config)['BLOCK']
            self.posture_probability=self.posture_model.predict_proba(vector) if vector is not None and valid else None
            probability=float(self.posture_probability[self.guard_column]) if self.posture_probability is not None else 0.
            for unit in self.attack.units.values():unit.guard_probability=probability
            return super().process(obs,evidence,now)
