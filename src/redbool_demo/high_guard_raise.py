"""Allow a fresh upward stroke from held high block, never a mere block release."""

class HighGuardRaise:
    def __init__(self):
        self.refs = {}; self.owned = set(); self.peak = {}; self.last = None
        self.block_seen = None

    def step(self, arms, anchors, stamp, *, valid, blocked, excluded):
        started = set()
        if not valid or set(arms) != {'LEFT','RIGHT'} or set(anchors) != {'LEFT','RIGHT'}:
            self.__init__(); return set(), started
        if self.last is not None and not 0 < stamp-self.last <= 100_000_000:
            self.__init__()
        self.last = stamp
        if blocked: self.block_seen = stamp
        for side, other in [('LEFT','RIGHT'),('RIGHT','LEFT')]:
            a,b=arms[side],arms[other];p=a['position'];d=p-anchors[side]['position']
            geometry=(side not in excluded and d[1] <= -.60 and abs(d[0]) <= .60
                and b['position'][1]-p[1] >= .40 and b['angle'] <= 1.2
                and b['position'][1] <= .35)
            if side in self.owned:
                self.peak[side]=min(self.peak[side],float(p[1]))
                if not geometry:self.owned.remove(side)
            ref=self.refs.get(side)
            recent_block=self.block_seen is not None and stamp-self.block_seen <= 800_000_000
            if (side not in self.owned and geometry and recent_block and ref is not None
                    and stamp-ref[0] <= 1_000_000_000 and ref[1][1]-p[1] >= .35):
                self.owned.add(side);started.add(side);self.peak[side]=float(p[1]);self.refs.pop(side,None)
            # A fresh stable folded return is required before another upward stroke.
            returned=side not in self.peak or p[1]-self.peak[side] >= .30
            if (side not in self.owned and returned and a['angle'] <= .60
                    and a['speed_xy'] <= 1.2 and -.95 <= p[1] <= .20):
                self.refs[side]=(stamp,p.copy())
        return self.owned.copy(),started
