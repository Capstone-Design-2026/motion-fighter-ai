"""Observe an advancing low fist before it leaves the camera image."""
from collections import deque
from math import hypot, isfinite
from typing import NamedTuple


class _Forearm(NamedTuple):
    wrist_x: float
    wrist_y: float
    elbow_y: float
    elbow_x: float
    length: float
    depth_from_elbow: float
    depth_from_shoulder: float


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
            # Project recent joints only for a moving, low wrist.
            history.append((stamp, obs, arm['angle'], arm['angle3d']))
            descending = (len(history) >= 4 and arm['angle'] >= .35 and arm['velocity'][1] >= .35
                          and self._qualifies(history, i))
            forward = (not descending and len(history) >= 5 and arm['angle'] <= .70
                       and abs(arm['velocity'][0]) >= 1 and abs(arm['velocity'][1]) < 2
                       and self._foreshortened(history, i))
            if descending or forward:
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

    def _forearm(self, sample, i):
        geometry = self._geometry(sample, i)
        if geometry is None:
            return None
        x, y, elbow_y, sx, _ = geometry
        points = sample[1].landmarks
        shoulder, elbow, wrist = (points[j + i] for j in (11, 13, 15))
        outward = 1 if shoulder[0] > points[12 - i][0] else -1
        x *= outward
        elbow_x = (elbow[0] - shoulder[0]) * sx * outward
        # Image-landmark depth decreases towards the camera. Compare the wrist
        # to both proximal joints to suppress sideways guard repositioning.
        return _Forearm(x, y, elbow_y, elbow_x, hypot(x - elbow_x, y - elbow_y),
                        (elbow[2] - wrist[2]) * sx, (shoulder[2] - wrist[2]) * sx)

    def _foreshortened(self, history, i):
        end = self._forearm(history[-1], i)
        if end is None or not (.20 <= end.wrist_x <= .65 and .14 <= end.wrist_y <= .48
                and .45 <= end.elbow_y <= .90 and .28 <= end.elbow_y - end.wrist_y <= .64
                and 0 < end.elbow_x - end.wrist_x < .40 and end.length <= .60):
            return False
        previous = self._forearm(history[-2], i)
        if (previous is None or abs(previous.wrist_y - end.wrist_y) > .10
                or abs(previous.elbow_y - end.elbow_y) > .12 or previous.length > .65
                or end.wrist_x < previous.wrist_x + .035):
            return False
        # A single depth jump or an elbow pulling back behind a stationary fist
        # is insufficient. Demand sustained forward change on both references.
        first, second = (self._forearm(history[k], i) for k in (-4, -3))
        if first is None or second is None:
            return False
        if min(end.depth_from_elbow, previous.depth_from_elbow) < max(
                first.depth_from_elbow, second.depth_from_elbow) + .10:
            return False
        old_shoulder_depth = (first.depth_from_shoulder + second.depth_from_shoulder) / 2
        if min(end.depth_from_shoulder, previous.depth_from_shoulder) < old_shoulder_depth + .10:
            return False

        for sample in reversed(history):
            age = history[-1][0] - sample[0]
            if age < 100_000_000:
                continue
            start = self._forearm(sample, i)
            if start is None:
                break
            if (sample[2] <= .40 and abs(start.wrist_x) <= .14 and .10 <= start.wrist_y <= .48
                    and .70 <= start.elbow_y <= 1.15 and abs(end.wrist_y - start.wrist_y) <= .13
                    and .12 <= start.elbow_y - end.elbow_y <= .40 and end.wrist_x - start.wrist_x >= .22
                    and start.length >= .68 and end.length <= .70 * start.length
                    and previous.length <= .82 * start.length and abs(end.elbow_x - start.elbow_x) < .24):
                return True
        return False
