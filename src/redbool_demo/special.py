"""Development chest-X gesture. Geometric rules, not a newly trained classifier."""
import numpy as np
from action_engine.experimental import joint_validity


def chest_cross(obs, cfg):
    if joint_validity(obs,cfg)['BLOCK']: return False,False
    p=np.array(obs.as_frame()['image_landmarks'],dtype=float)
    p[:,1]*=obs.height/obs.width
    axis=p[11,:2]-p[12,:2];width=np.linalg.norm(axis)
    if width<.08: return False,False
    axis/=width;down=np.array([-axis[1],axis[0]])
    if down[1]<0:down=-down
    center=(p[11,:2]+p[12,:2])/2
    def pos(i):
        d=(p[i,:2]-center)/width
        return np.array([d@axis,d@down])
    le,re,lw,rw=map(pos,(13,14,15,16))
    # Anatomical left wrist crosses to the right half and vice versa. Keep the
    # hands below the shoulder line, away from face-height block/raised kicks.
    approach=(lw[0]<-.04 and rw[0]>.04 and all(.04<=w[1]<=.85 for w in (lw,rw)))
    strict=(approach and lw[0]<=-.15 and rw[0]>=.15
            and all(.05<=w[1]<=.60 for w in (lw,rw))
            and abs(lw[1]-rw[1])<=.25 and le[0]>.10 and re[0]<-.10
            and le[1]>lw[1]+.12 and re[1]>rw[1]+.12)
    return bool(approach),bool(strict)


class CrossSpecial:
    hold_ns=700_000_000
    return_ns=180_000_000
    max_gap_ns=100_000_000
    def __init__(self):
        self.armed=False;self.owned=False;self.latched=False
        self.since=None;self.return_since=None;self.previous=None
    def invalidate(self):
        self.armed=False;self.since=self.return_since=self.previous=None
        if self.owned:self.latched=True
    def step(self,stamp,*,calibrated,valid,neutral,approach,strict):
        if not calibrated:
            self.__init__();return False
        if self.previous is not None and not 0<stamp-self.previous<=self.max_gap_ns:
            self.invalidate()
        if not valid:
            self.invalidate();return False
        self.previous=stamp
        # A real neutral return rearms; relaxing the cross alone is insufficient.
        if neutral and not approach:
            if self.return_since is None:self.return_since=stamp
            if stamp-self.return_since>=self.return_ns:
                self.armed=True;self.owned=self.latched=False;self.since=None
        else:self.return_since=None
        if self.armed and approach and not self.owned:
            self.owned=True;self.armed=False;self.latched=False
        if not self.owned or self.latched:return False
        if not strict:
            self.since=None
            return False
        if self.since is None:self.since=stamp
        if stamp-self.since<self.hold_ns:return False
        self.latched=True
        return True
