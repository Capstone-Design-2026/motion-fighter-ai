"""Isolated candidate: independent fresh stationary-X proof at slow cadence.

This does not extend output/event leases or revive the normal gesture candidate.
The shared CrossSpecial latch still owns release and one event per held pose.
"""
import numpy as np
from collections import deque


class StaticCrossProof:
    max_gap_ns = 150_000_000
    max_age_ns = 120_000_000
    history_ns = 270_000_000
    hold_ns = 700_000_000
    pair_credit_ns = 100_000_000
    minimum_samples = 8
    minimum_slow_pairs = 2

    def __init__(self):
        self.identity = None
        self.last_seen = None
        self.last_now = None
        self.capture_gaps = deque(maxlen=20)
        self.reset()

    def reset(self):
        self.previous = None
        self.anchor = None
        self.count = self.slow_pairs = self.confirmed_ns = 0

    @property
    def slow_source(self):
        return len(self.capture_gaps) == 20 and sum(gap > self.pair_credit_ns for gap in self.capture_gaps) >= 8

    def expire(self, now):
        # A watchdog may only remove evidence. It never commits an attack.
        if self.last_now is not None and now < self.last_now:
            self.reset()
        self.last_now = now
        if self.previous is not None and now >= self.previous['stamp'] + self.history_ns:
            self.reset()

    @staticmethod
    def _shape(obs):
        p = np.asarray(obs.landmarks, dtype=float).copy()
        p[:, 1] *= obs.height / obs.width
        center = (p[11, :2] + p[12, :2]) / 2
        axis = p[11, :2] - p[12, :2]
        width = float(np.linalg.norm(axis))
        if not np.isfinite(width) or width < .08:
            return None
        axis /= width
        down = np.array([-axis[1], axis[0]])
        if down[1] < 0:
            down = -down
        points = (p[[11, 12, 13, 14, 15, 16], :2] - center) @ np.stack([axis, down], axis=1) / width
        angles = []
        for s, e, w in ((11, 13, 15), (12, 14, 16)):
            a = p[s, :2] - p[e, :2]
            b = p[w, :2] - p[e, :2]
            denominator = np.linalg.norm(a) * np.linalg.norm(b)
            if denominator < 1e-12:
                return None
            angles.append(float(np.arccos(np.clip(a @ b / denominator, -1., 1.))))
        if not np.isfinite(points).all() or not np.isfinite(angles).all():
            return None
        return dict(center=center, width=width, axis=axis, points=points, angles=np.asarray(angles))

    @staticmethod
    def _stationary(current, previous, anchor):
        return bool(
            .90 <= current['width'] / previous['width'] <= 1.10
            and np.linalg.norm(current['center'] - previous['center']) / previous['width'] <= .10
            and np.arccos(np.clip(current['axis'] @ previous['axis'], -1., 1.)) <= np.deg2rad(7.5)
            and np.max(np.linalg.norm(current['points'] - previous['points'], axis=1)) <= .10
            and np.max(np.linalg.norm(current['points'] - anchor['points'], axis=1)) <= .15
            and np.max(np.abs(current['angles'] - previous['angles'])) <= .20)

    def observe(self, obs, now, *, calibrated, primary_strict, valid, latched, primary_only=True):
        """Return fresh proof once; caller must set the shared normal X latch.

        primary_strict/valid come from this exact current primary observation,
        never a cached/auxiliary skeleton or the game's requested tutorial pose.
        """
        self.expire(now)
        identity = (obs.actor_id, obs.capture_epoch)
        if identity != self.identity:
            self.reset()
            self.identity = identity
            self.last_seen = None
            self.capture_gaps.clear()
        current_seen = (obs.frame_id, obs.capture_ns)
        if (self.last_seen is not None and
                (obs.frame_id <= self.last_seen[0] or obs.capture_ns <= self.last_seen[1])):
            self.reset()
            self.capture_gaps.clear()
            return False
        if self.last_seen is not None:
            self.capture_gaps.append(obs.capture_ns - self.last_seen[1])
        self.last_seen = current_seen
        # Protect the existing same-frame Full fusion path. The extra proof is
        # only for a sustained slow primary-only source, not isolated jitter.
        if not primary_only:
            self.reset()
            self.capture_gaps.clear()
            return False
        if (not calibrated or latched or not valid or not primary_strict
                or not 0 <= now - obs.capture_ns <= self.max_age_ns):
            self.reset()
            return False
        if not self.slow_source:
            self.reset()
            return False
        shape = self._shape(obs)
        if shape is None:
            self.reset()
            return False
        if self.previous is not None:
            dt = obs.capture_ns - self.previous['stamp']
            if (not 0 < dt <= self.max_gap_ns
                    or not self._stationary(shape, self.previous['shape'], self.anchor)):
                self.reset()
            else:
                self.confirmed_ns += min(dt, self.pair_credit_ns)
                self.slow_pairs += int(dt > self.pair_credit_ns)
                self.count += 1
        if self.previous is None:
            self.anchor = shape
            self.count = 1
        self.previous = dict(stamp=obs.capture_ns, shape=shape)
        if (self.count >= self.minimum_samples and self.slow_pairs >= self.minimum_slow_pairs
                and self.confirmed_ns >= self.hold_ns):
            self.reset()
            return True
        return False


class StaticCrossRelease:
    """Release only a static-proof latch from new, visibly separated hands.

    This never fires an event or unlocks a normal/refined-model X latch.
    """
    def __init__(self):
        self.last_now = None
        self.reset()

    def reset(self):
        self.previous = None
        self.count = self.confirmed_ns = 0

    def expire(self, now):
        if self.last_now is not None and now < self.last_now:
            self.reset()
        self.last_now = now
        if self.previous is not None and now >= self.previous['stamp'] + 270_000_000:
            self.reset()

    def observe(self, obs, now, *, eligible, valid, approach, strict):
        self.expire(now)
        if (not eligible or not valid or approach or strict
                or not 0 <= now - obs.capture_ns <= 120_000_000):
            self.reset()
            return False
        shape = StaticCrossProof._shape(obs)
        # Both wrists must currently be visible on their respective own side.
        # A lone raised hand, an inferred wrist or tracking loss is not release.
        if shape is None or not (shape['points'][4, 0] > .22 and shape['points'][5, 0] < -.22):
            self.reset()
            return False
        ident = (obs.actor_id, obs.capture_epoch)
        if self.previous is not None:
            dt = obs.capture_ns - self.previous['stamp']
            if (ident != self.previous['ident'] or obs.frame_id <= self.previous['frame'] or dt <= 0):
                self.reset()
                return False
            if dt > 150_000_000:
                self.reset()
            else:
                self.confirmed_ns += min(dt, 100_000_000)
                self.count += 1
        if self.previous is None:
            self.count = 1
        self.previous = dict(stamp=obs.capture_ns, frame=obs.frame_id, ident=ident)
        if self.count >= 3 and self.confirmed_ns >= 180_000_000:
            self.reset()
            return True
        return False
