"""Bilateral cross geometry and dwell; development rules, not a trained classifier."""
import numpy as np
from action_engine.experimental import joint_validity, elbow_angle


def extended_cross_arm(obs, cfg):
    """An extending forearm is incompatible with the compact X pose.

    Check actual elbow geometry rather than anatomical left/right wrist travel:
    a single cross-body punch can pass through the two-dimensional X region.
    """
    if joint_validity(obs,cfg)['BLOCK']:return False
    p=np.array(obs.as_frame()['image_landmarks'],dtype=float);p[:,1]*=obs.height/obs.width
    flat=p.copy();flat[:,2]=0
    for ids in ((11,13,15),(12,14,16)):
        a3,a2=elbow_angle(p,ids),elbow_angle(flat,ids)
        if a3 is not None and (a3>=2.65 or (a3>=2.5 and a2 is not None and a2>=1.8)):
            return True
    return False


def cross_geometry(obs, cfg):
    """Return early ownership, confirmed X, and visibly separated hands.

    Shoulder-relative coordinates tolerate chest/chin-height X poses. A cross
    still needs both hands near the midline and below-eye height, with bent
    forearms. Merely lifting both hands into face-height block is not an X.
    """
    if joint_validity(obs,cfg)['BLOCK']: return False,False,False
    p=np.array(obs.as_frame()['image_landmarks'],dtype=float)
    p[:,1]*=obs.height/obs.width
    axis=p[11,:2]-p[12,:2];width=np.linalg.norm(axis)
    if width<.08: return False,False,False
    axis/=width;down=np.array([-axis[1],axis[0]])
    if down[1]<0:down=-down
    center=(p[11,:2]+p[12,:2])/2
    def pos(i):
        d=(p[i,:2]-center)/width
        return np.array([d@axis,d@down])
    le,re,lw,rw=map(pos,(13,14,15,16))
    extended=extended_cross_arm(obs,cfg)
    bent=(le[0]>.10 and re[0]<-.10 and le[1]>lw[1]+.12 and re[1]>rw[1]+.12)
    height=all(-.45<=w[1]<=.70 for w in (lw,rw))
    # Reserve a bilateral inward movement before either ordinary punch commits.
    approach=(not extended and bent and height and -.65<lw[0]<.22 and -.22<rw[0]<.65
              and rw[0]-lw[0]>-.15)
    strict=(approach and lw[0]<=.12 and rw[0]>=-.12
            and rw[0]-lw[0]>=.04 and abs(lw[1]-rw[1])<=.40)
    # A clear one-arm raise also breaks the cross, even if the other hand
    # remains at the opposite shoulder. Do not require both hands in guard.
    separated=((lw[0]>.22 and rw[0]<-.22)
               or lw[1]<-.60
               or rw[1]<-.60
               or all(w[1]>.85 for w in (lw,rw)) or extended)
    return bool(approach),bool(strict),bool(separated)


def chest_cross(obs,cfg):
    return cross_geometry(obs,cfg)[:2]


class CrossSpecial:
    hold_ns=700_000_000
    return_ns=180_000_000
    max_gap_ns=100_000_000
    geometry_gap_ns=80_000_000
    def __init__(self):
        self.armed=False;self.owned=False;self.latched=False
        self.since=None;self.return_since=None;self.previous=None
        self.confirmed_ns=0;self.last_strict=None
    def reset_dwell(self):
        self.since=None;self.confirmed_ns=0;self.last_strict=None
    def invalidate(self):
        self.armed=False;self.return_since=self.previous=None
        self.reset_dwell()
        # A lost observation cannot join two hold intervals. If already fired,
        # retain the latch; otherwise a fresh full valid hold can still finish.
    def step(self,stamp,*,calibrated,valid,neutral,approach,strict):
        if not calibrated:
            self.__init__();return False
        previous=self.previous
        if previous is not None and not 0<stamp-previous<=self.max_gap_ns:
            self.invalidate();previous=None
        if not valid:
            self.invalidate();return False
        self.previous=stamp
        # Here neutral means visibly uncrossed hands, not the calibrated guard.
        if neutral and not approach:
            if self.return_since is None:self.return_since=stamp
            if stamp-self.return_since>=self.return_ns:
                self.armed=True;self.owned=self.latched=False;self.reset_dwell()
        else:self.return_since=None
        # Calibration, not an extra uncross pose, authorizes the first X.
        # Returning from a keyboard/brief tracking gap may start a fresh full
        # dwell. An already-fired held X retains its latch across that gap.
        if approach and not self.owned and not self.latched:
            self.owned=True;self.armed=False;self.latched=False
        if not self.owned or self.latched:return False
        if not strict:
            if self.last_strict is None or stamp-self.last_strict>self.geometry_gap_ns:
                self.reset_dwell()
            return False
        if self.since is None:self.since=stamp
        # Count only adjacent confirmed intervals; brief geometry jitter neither
        # adds hold time nor lets an arbitrarily fragmented pose accumulate.
        if self.last_strict is not None and previous==self.last_strict:
            self.confirmed_ns+=stamp-previous
        self.last_strict=stamp
        if self.confirmed_ns<self.hold_ns:return False
        self.latched=True
        return True
