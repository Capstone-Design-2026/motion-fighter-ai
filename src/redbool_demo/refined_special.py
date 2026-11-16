"""Visible release evidence when an optional source resolved an ambiguous X."""
import numpy as np
from action_engine.experimental import joint_validity


class RefinedRepeatLatch:
    """Remember a delivered refined X without blocking unrelated controls.

    Ordinary input recovery uses the original CrossSpecial lifecycle. This
    independent latch only forbids a second SPECIAL until fresh visible release.
    It never reuses a skeleton or represents a published control lease.
    """
    def __init__(self):
        self.locked = False
        self.previous = self.since = None

    def invalidate(self):
        self.previous = self.since = None

    def observe(self, stamp, *, valid, release):
        if not self.locked or not valid:
            self.invalidate()
            return
        if self.previous is not None and not 0 < stamp-self.previous <= 100_000_000:
            self.invalidate()
        self.previous = stamp
        if not release:
            self.since = None
        elif self.since is None:
            self.since = stamp
        elif stamp-self.since >= 180_000_000:
            self.locked = False
            self.invalidate()


def definite_cross_release(obs, cfg):
    """Require complete arms and a pose clearly outside a compact chest X.

    This does not create, complete, or rearm an attack by itself. The caller
    still applies the existing source freshness, cross veto and release dwell.
    Ordinary v36.2 crosses keep their original release policy.
    """
    if joint_validity(obs, cfg)['BLOCK']:
        return False
    p = np.array(obs.as_frame()['image_landmarks'], dtype=float)
    p[:, 1] *= obs.height / obs.width
    axis = p[11, :2] - p[12, :2]
    width = np.linalg.norm(axis)
    if width < .08:
        return False
    axis /= width
    down = np.array([-axis[1], axis[0]])
    if down[1] < 0:
        down = -down
    center = (p[11, :2] + p[12, :2]) / 2
    hands = []
    for i in (15, 16):
        delta = (p[i, :2] - center) / width
        hands.append((delta @ axis, delta @ down))
    left, right = hands
    return bool((left[0] > .5 and right[0] < -.5)
                or left[1] < -.6 or right[1] < -.6
                or (left[1] > .85 and right[1] > .85))
