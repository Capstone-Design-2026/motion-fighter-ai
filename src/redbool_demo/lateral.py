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
        self.personal_enter = {}
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
        self.calibrate_samples([obs])

    def calibrate_samples(self, observations):
        """Use the same stable window as the personal guard, not its last frame.

        Median centre/width/roll replace only an outlier final estimate.
        Small normal settling keeps the established movement neutral position.
        Manual C constructs a new movement controller, so no prior person leaks.
        """
        rows = []
        for obs in observations:
            if joint_validity(obs, self.cfg)['MOVEMENT']: continue
            p = np.asarray(obs.as_frame()['image_landmarks'], float)
            rows.append(((p[11, 0] + p[12, 0]) / 2,
                         abs(p[11, 0] - p[12, 0]), self._roll(p, obs),
                         1. if p[11, 0] > p[12, 0] else -1.))
        if not rows:
            self.origin = self.width = self.roll_origin = None
            self.reset()
            return
        median = np.median(rows, axis=0)
        last = np.asarray(rows[-1])
        # Do not move a valid neutral anchor by millimetres: lean hysteresis was
        # calibrated against that settled pose. Correct clear single-frame noise.
        limits = np.array([.05 * median[1], .10 * median[1], 2.0])
        mad = np.median(np.abs(np.asarray(rows)[:, :3] - median[:3]), axis=0)
        recent = np.median(np.asarray(rows[-4:-1]), axis=0) if len(rows) > 1 else last
        outlier = bool(np.any((np.abs(last[:3] - median[:3]) > np.maximum(limits, 3 * mad))
            & (np.abs(last[:3] - recent[:3]) > limits)))
        self.calibration_source = 'window_median_outlier' if outlier else 'settled_final_frame'
        self.origin, self.width, self.roll_origin, self.left_sign = map(float, median if outlier else last)
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
            translation = x >= (.12 if holding else self.personal_enter.get(direction, .22))
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
