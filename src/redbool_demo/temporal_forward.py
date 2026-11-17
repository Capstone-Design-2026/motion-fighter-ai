"""Experimental camera-directed onset proof; never commits or rearms a punch.

Three distinct, fresh source samples must show simultaneous image-elbow and
image-3D opening plus monotonic depth approach. Current existing forward routing
still must agree. These monocular estimates are not metric ground truth.
"""
from collections import deque
import math

class TemporalForwardOnset:
    def __init__(self):
        self.samples=deque(maxlen=3)
        self.stamp=None
    def reset(self):
        self.samples.clear(); self.stamp=None
    def observe(self, arm, base, now, *, available, forward, independent, upper_projection=None, guard_origin=False):
        self.stamp=None
        if not available or arm is None or base is None:
            self.reset(); return False
        dx,dy,dz=(float(x-y) for x,y in zip(arm['position'],base['position']))
        a2=float(arm['angle']); a3=arm['angle3d']
        if upper_projection is None:upper_projection=arm.get('upper_projection')
        if upper_projection is None or not math.isfinite(upper_projection) or upper_projection<=0:
            self.reset(); return False
        if a3 is None or not all(math.isfinite(v)for v in (dx,dy,dz,a2,a3)):
            self.reset(); return False
        if self.samples and not 0<now-self.samples[-1][0]<=100_000_000:
            self.reset(); return False
        # This extra onset is confined to a compact forward stroke. Lowering,
        # above-shoulder raises and wide sideways sweeps retain their old routes.
        if not (abs(dx)<=.35 and -.40<dy<=.12 and .15<=a2<=1.5):
            self.reset(); return False
        self.samples.append((now,a2,float(a3),dz,float(upper_projection),bool(guard_origin)))
        if len(self.samples)!=3 or not forward or not independent:return False
        first,middle,last=self.samples
        elapsed=last[0]-first[0]
        if not 40_000_000<=elapsed<=100_000_000:return False
        # Actual earlier bent/guard elbow, then independent opening on both
        # intervals. Depth jitter alone cannot start a candidate.
        if not (first[5] and first[1]<=.60 and first[2]<=min(2.3,base['angle3d']+.20)
                and a2-first[1]>=.15 and a3-first[2]>=.18
                and dz-first[3]<=-.075 and dz<=-.45
                and a3>=2.4 and a3-base['angle3d']>=.30
                and math.hypot(dx,dy)>=.24
                and first[4]-last[4]>=.12 and last[4]<=first[4]*.85):return False
        for previous,current in ((first,middle),(middle,last)):
            if not (current[1]-previous[1]>=.03 and current[2]-previous[2]>=.03
                    and current[3]-previous[3]<=-.02
                    and current[4]-previous[4]<=-.02):return False
        self.stamp=now
        return True
