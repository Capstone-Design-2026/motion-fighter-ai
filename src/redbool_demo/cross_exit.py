"""Release a fired chest X when the hands separate on the way out of the image."""
from math import hypot, isfinite


class CrossExit:
    def __init__(self):
        self.identity = None
        self.last_stamp = self.crossed = self.split = self.proved = None
        self.side_since = [None, None]

    def reset(self):
        if self.identity is not None:
            self.identity = None
            self.last_stamp = self.crossed = self.split = self.proved = None
            self.side_since[:] = (None, None)

    @staticmethod
    def _arms(obs):
        points, visibility = obs.landmarks, obs.visibility
        if (not obs.pose_present or obs.width <= 0 or obs.height <= 0 or points is None
                or visibility is None or len(points) != 33 or len(visibility) != 33):
            return None

        def visible(i, margin=0):
            point = points[i]
            return (len(point) >= 2 and visibility[i] >= .80
                    and -margin / obs.width <= point[0] <= 1 + margin / obs.width
                    and -margin / obs.height <= point[1] <= 1 + margin / obs.height
                    and isfinite(visibility[i]))

        if not visible(11) or not visible(12):
            return None
        ratio = obs.height / obs.width
        sx = points[11][0] - points[12][0]
        sy = (points[11][1] - points[12][1]) * ratio
        span = hypot(sx, sy)
        if span < .10 or abs(sy) > .4 * span:
            return None
        cx = (points[11][0] + points[12][0]) / 2
        cy = (points[11][1] + points[12][1]) / 2

        def local(i):
            dx = (points[i][0] - cx) / span
            dy = (points[i][1] - cy) * ratio / span
            across = (dx * sx + dy * sy) / span
            down = (dy * abs(sx) - dx * sy * (1 if sx >= 0 else -1)) / span
            return across, down

        arms = []
        for elbow, wrist, index, direction in ((13,15,19,1), (14,16,20,-1)):
            # Elbows on the border are rounded to the nearest pixel. The
            # wrist and index themselves must both be visible inside the image.
            if not visible(wrist) or not visible(index) or not visible(elbow, margin=.5):
                arms.append(None)
                continue
            wx, wy = local(wrist)
            fx, fy = local(index)
            ex, ey = local(elbow)
            if not (.18 < hypot(wx-ex, wy-ey) < 1.5 and .09 < hypot(wx-fx, wy-fy) < .48):
                arms.append(None)
                continue
            arms.append((direction*wx, wy, direction*fx, fy))
        return (cx, cy, span), arms

    def step(self, obs, aux, *, eligible, strict, now):
        if not eligible:
            self.reset()
            return False
        identity = (obs.actor_id, obs.capture_epoch)
        if identity != self.identity:
            self.reset()
            self.identity = identity
        stamp = obs.capture_ns
        if not 0 <= now-stamp <= 120_000_000 or self.last_stamp is not None and stamp <= self.last_stamp:
            self.reset()
            return False
        if self.last_stamp is not None and stamp-self.last_stamp > 75_000_000:
            self.crossed = self.split = None
            self.side_since[:] = (None, None)
        self.last_stamp = stamp
        if self.split is not None and stamp-self.split > 320_000_000:
            self.split = None
            self.side_since[:] = (None, None)
        if self.proved is not None and stamp-self.proved > 5_000_000_000:
            self.proved = None
        if (aux is None or aux is obs or (aux.actor_id,aux.capture_epoch,aux.frame_id,aux.capture_ns,aux.width,
                aux.height,aux.source_mirrored) != (*identity,obs.frame_id,stamp,obs.width,obs.height,obs.source_mirrored)):
            self.side_since[:] = (None, None)
            return False
        # 'strict' is set only when both time-aligned, same-body estimators
        # see the returning X. Historical pose observations never fire it.
        if self.proved is not None and strict and stamp-self.proved >= 250_000_000:
            self.reset()
            return True
        primary, secondary = self._arms(obs), self._arms(aux)
        if primary is None or secondary is None:
            self.side_since[:] = (None, None)
            return False
        c1, a = primary
        c2, b = secondary
        if not .9 <= c1[2]/c2[2] <= 1.1 or hypot(c1[0]-c2[0], c1[1]-c2[1]) > .12*c1[2]:
            self.side_since[:] = (None, None)
            return False
        pairs = [(x,y) if x and y and max(abs(i-j) for i,j in zip(x,y)) <= .22 else None
                 for x,y in zip(a,b)]
        # From a confirmed cross, both tracked wrists must split in opposite
        # directions below the shoulder line, before either leaves the image.
        if all(pairs):
            if all(arm[0] <= .02 and arm[2] < -.15 for pair in pairs for arm in pair):
                self.crossed = stamp
            if (self.crossed is not None and 0 < stamp-self.crossed <= 250_000_000
                    and all(arm[0] >= .09 and arm[1] >= .28 for pair in pairs for arm in pair)
                    and all(left[0]+right[0] >= .28 for left,right in (a,b))):
                self.split = stamp
        if self.split is not None:
            # A clearly visible low hand continues outwards on its anatomical
            # side for several frames. Tracking loss never counts as separation.
            for side, pair in enumerate(pairs):
                if pair and all(wx >= .33 and wy >= .51 and fx >= .20 and fy >= .24 for wx,wy,fx,fy in pair):
                    if self.side_since[side] is None:
                        self.side_since[side] = stamp
                    if stamp-self.side_since[side] >= 90_000_000:
                        self.proved = stamp
                        self.split = None
                        self.side_since[:] = (None, None)
                        break
                else:
                    self.side_since[side] = None
        return False
