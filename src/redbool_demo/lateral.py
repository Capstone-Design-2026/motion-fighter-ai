"""Seated movement from calibrated translation plus corroborated shoulder roll.

A small shift needs a same-direction roll; raising one shoulder alone is not a
movement command. Thresholds use body width and camera aspect ratio, not pixels.
"""
import math
import numpy as np
from action_engine.experimental import joint_validity


class LateralMovement:
    def __init__(self, cfg):
        self.cfg = cfg
        self.origin = self.width = self.roll_origin = None
        self.left_sign = 1.
        self.reset()

    def reset(self):
        # Keep calibration through tracking loss, but release input immediately.
        self.state = self.candidate = 'NEUTRAL'
        self.since = None
        self.strength = 0.

    @staticmethod
    def _roll(p, obs):
        # Anatomical shoulder order gives the same roll sign after x mirroring.
        return math.degrees(math.atan2((p[11, 1] - p[12, 1]) * obs.height / obs.width,
                                      abs(p[11, 0] - p[12, 0])))

    def calibrate(self, obs):
        p = np.asarray(obs.as_frame()['image_landmarks'], float)
        self.origin = float((p[11, 0] + p[12, 0]) / 2)
        self.width = abs(float(p[11, 0] - p[12, 0]))
        self.left_sign = 1. if p[11, 0] > p[12, 0] else -1.
        self.roll_origin = self._roll(p, obs)
        self.reset()

    def update(self, obs, now):
        if self.origin is None or self.width <= 1e-6 or joint_validity(obs, self.cfg)['MOVEMENT']:
            self.reset()
            return
        p = np.asarray(obs.as_frame()['image_landmarks'], float)
        offset = ((p[11, 0] + p[12, 0]) / 2 - self.origin) / self.width * self.left_sign
        roll = self._roll(p, obs) - self.roll_origin
        candidate = 'NEUTRAL'
        for direction, sign in (('LEFT', 1), ('RIGHT', -1)):
            x, angle = offset * sign, roll * sign
            holding = self.state == direction
            translation = x >= (.12 if holding else .22)
            lean = x >= (.10 if holding else .14) and angle >= (1.5 if holding else 3.)
            if translation or lean:
                candidate = direction
                break
        if candidate != self.candidate:
            self.candidate, self.since = candidate, now
        if self.since is not None and now - self.since >= self.cfg.movement_dwell_ms * 1_000_000:
            self.state = candidate
        if self.state == 'NEUTRAL':
            self.strength = 0.
        else:
            sign = 1 if self.state == 'LEFT' else -1
            # A recognised modest lean must be usable; Unity smooths this input.
            shift_strength = (offset * sign - .10) / .45
            lean_strength = max(0., roll * sign) / 15. if offset * sign >= .10 else 0.
            self.strength = float(np.clip(max(.35, shift_strength, lean_strength), 0., 1.))
