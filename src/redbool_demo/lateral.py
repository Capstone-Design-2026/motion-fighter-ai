"""Seated lateral shoulder-centre input, calibrated with the player's guard."""
import numpy as np
from action_engine.experimental import joint_validity

class LateralMovement:
    def __init__(self, cfg):
        self.cfg=cfg;self.origin=None;self.width=None;self.left_sign=1.
        self.reset()

    def reset(self):
        # Loss of tracking releases control without redefining the centre.
        self.state=self.candidate='NEUTRAL';self.since=None;self.strength=0.

    def calibrate(self, obs):
        p=np.asarray(obs.as_frame()['image_landmarks'],float)
        self.origin=float((p[11,0]+p[12,0])/2)
        self.width=abs(float(p[11,0]-p[12,0]))
        self.left_sign=1. if p[11,0]>p[12,0] else -1.
        self.reset()

    def update(self, obs, now):
        if self.origin is None or self.width<=1e-6 or joint_validity(obs,self.cfg)['MOVEMENT']:
            self.reset();return
        p=np.asarray(obs.as_frame()['image_landmarks'],float)
        offset=((p[11,0]+p[12,0])/2-self.origin)/self.width*self.left_sign
        threshold=.12 if self.state!='NEUTRAL' else .22
        candidate='LEFT' if offset>=threshold else 'RIGHT' if offset<=-threshold else 'NEUTRAL'
        if candidate!=self.candidate:self.candidate=candidate;self.since=now
        if self.since is not None and now-self.since>=120_000_000:self.state=candidate
        self.strength=float(np.clip((abs(offset)-.10)/.45,0.,1.)) if self.state!='NEUTRAL' else 0.
