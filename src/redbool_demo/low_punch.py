"""Observe an advancing low fist before it leaves the camera image."""
from collections import deque
from math import hypot, isfinite


class LowPunchProjection:
    def __init__(self):
        self.tracks = [deque(maxlen=16), deque(maxlen=16)]

    def observe(self, obs, arms, eligible):
        points, visibility = obs.landmarks, obs.visibility
        if (not any(eligible) or points is None or visibility is None or len(points) < 23
                or len(visibility) < 23 or obs.width <= 0 or obs.height <= 0):
            for history in self.tracks:
                history.clear()
            return None

        stamp = obs.capture_ns
        candidates = []
        for i, side in enumerate(('LEFT', 'RIGHT')):
            history = self.tracks[i]
            if not eligible[i] or arms[side]['angle3d'] is None:
                history.clear()
                continue
            if history and not 0 < stamp - history[-1][0] <= 85_000_000:
                history.clear()
            while history and stamp - history[0][0] > 220_000_000:
                history.popleft()
            arm = arms[side]
            # Keep only references to immutable capture data during normal play.
            # Project recent joints only if the wrist is actively descending.
            history.append((stamp, obs, arm['angle'], arm['angle3d']))
            if (len(history) >= 4 and arm['angle'] >= .35 and arm['velocity'][1] >= .35
                    and self._qualifies(history, i)):
                candidates.append(side)
        return candidates[0] if len(candidates) == 1 else None

    @staticmethod
    def _geometry(sample, i):
        _, obs, _, _ = sample
        p, v = obs.landmarks, obs.visibility
        sh, elbow, wrist = p[11 + i], p[13 + i], p[15 + i]
        if wrist[1] > .92 or not all(v[j + i] >= .7 for j in (11, 13, 15, 17, 19, 21)):
            return None
        aspect = obs.height / obs.width
        span = hypot(p[11][0] - p[12][0], (p[11][1] - p[12][1]) * aspect)
        if not isfinite(span) or span < .075:
            return None
        sx, sy = 1 / span, aspect / span
        return (wrist[0] - sh[0]) * sx, (wrist[1] - sh[1]) * sy, (elbow[1] - sh[1]) * sy, sx, sy

    @staticmethod
    def _spread(obs, i, sx, sy):
        p = obs.landmarks
        wx, wy = p[15+i][:2]
        lengths = [hypot((p[j+i][0] - wx) * sx, (p[j+i][1] - wy) * sy) for j in (17, 19, 21)]
        return sorted(lengths)[1]

    def _qualifies(self, history, i):
        latest = history[-1]
        geometry = self._geometry(latest, i)
        if geometry is None:
            return False
        x, y, elbow, sx, sy = geometry
        if y - elbow >= -.10:
            return False
        # The elbow lifts as the wrist descends, and both elbow angles open.
        # A relaxed whole arm normally drops together. Finger spread supplies
        # an additional projection check only after the stronger joint evidence.
        stamp, obs, angle, angle3d = latest
        previous_y, previous_elbow = y, elbow
        for count, anchor in enumerate(reversed(history), start=1):
            if count == 1:
                continue
            geometry = self._geometry(anchor, i)
            if geometry is None:
                break
            x0, y0, elbow0, sx0, sy0 = geometry
            if previous_y < y0 - .014 or previous_elbow > elbow0 + .012:
                break
            previous_y, previous_elbow = y0, elbow0
            first, source, angle0, angle3d0 = anchor
            if (count >= 4 and stamp - first >= 100_000_000
                    and .12 <= y - y0 <= .55 and -.24 <= elbow - elbow0 <= -.10
                    and abs(x - x0) <= .18 and .7 <= elbow0 <= 1.15 and y0 - elbow0 <= -.40
                    and angle0 < .41 and angle - angle0 >= .25 and angle3d - angle3d0 >= .11
                    and self._spread(obs, i, sx, sy) <= .72 * self._spread(source, i, sx0, sy0)):
                return True
        return False
