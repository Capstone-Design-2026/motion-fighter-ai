"""Explicit bilateral guard calibration before A4-derived event recognition.

No labels/timestamps from a replay are available to the recognizer. Calibration
uses only preceding observations. This is a development preset, not a validated
general-purpose punch classifier.
"""
from collections import deque
from dataclasses import replace
import numpy as np
from action_engine.experimental import (AblationConfig, DevelopmentEngine,
    DevelopmentAttack, ChannelSignals, joint_validity)
from action_engine.types import ControlState
from .calibration_feedback import CalibrationFeedback, calibration_reasons


class DemoAttack(DevelopmentAttack):
    def neutral(self, arms):
        if self.baseline is None:
            return False  # Only the explicit calibration controller can seed it.
        return bool(arms) and all(
            a['speed_xy'] <= self.cfg.neutral_speed
            and np.linalg.norm(a['position'][:2]-self.baseline[s]['position'][:2]) <= .25
            and a['angle3d'] is not None
            and a['angle3d'] <= self.baseline[s]['angle3d'] + .15
            for s,a in arms.items())

    def candidates(self, arms):
        signals = super().candidates(arms)
        for side,a in arms.items():
            # A downward arm drop must not be classified as a punch simply
            # because its elbow extends. Keep the frozen extension thresholds.
            base = self.baseline[side] if self.baseline else None
            not_lowering = base is not None and a['position'][1]-base['position'][1] <= .12
            signals[side]['not_lowering'] = bool(not_lowering)
            for key in ('onset_rule','commit_rule'):
                signals[side][key] = bool(signals[side][key] and not_lowering)
        return signals


class DemoEngine(DevelopmentEngine):
    def __init__(self, *, movement=False, **kwargs):
        self.movement_enabled = movement
        self.calibrating = False
        self.calibrated = False
        self.calibration_status = 'PRESS C: HOLD GUARD'
        self.calibration_progress = 0.
        self.calibration_feedback = CalibrationFeedback()
        self.samples = deque()
        self.calibration_observations = deque()
        cfg = AblationConfig(version='seated-demo-20261103-v1', channel_validity=True,
            calibrated_neutral_block=True, image_3d_extension=True)
        super().__init__(cfg, **kwargs)

    def _components(self):
        super()._components()
        self.attack.units = {s:DemoAttack(self.config) for s in ('LEFT','RIGHT')}
        # A stationary guard can span slower camera samples. Keep this bound
        # separate from live attack timing; freshness and pose checks stay strict.
        self.calibration_signals = ChannelSignals(replace(self.config,
            max_gap_ms=max(self.config.max_gap_ms, 200)))
        self.samples.clear()
        self.calibration_observations.clear()
        self.calibrated = False
        self.calibration_progress = 0.
        self.calibration_feedback = CalibrationFeedback()

    def request_calibration(self):
        with self.lock:
            self._components()
            self.outbox.clear()
            self.calibrating = True
            self.calibration_status = 'HOLD BOTH FISTS IN GUARD'
            self.control = ControlState(*(self.identity or ('','')))

    def toggle_movement(self):
        with self.lock:
            self.movement_enabled = not self.movement_enabled
            self.movement.reset()
            self.control = replace(self.control, movement='NEUTRAL', movement_strength=0.)
            return self.movement_enabled

    def process(self, obs, evidence=None, now_ns=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            # Reject duplicates before feeding the calibration history.
            if self.last_capture is not None and (obs.capture_ns<=self.last_capture or obs.frame_id<=self.last_frame):
                return super().process(obs, evidence, now)
            state = super().process(obs, evidence, now)
            if not self.calibrated:
                self.outbox.pending.clear()
                self.block.reset()
                state = replace(state, block=False, movement='NEUTRAL', movement_strength=0., attack_state='PREPARING')
                if self.calibrating and self.identity == (obs.actor_id,obs.capture_epoch):
                    arms = self.calibration_signals.update(obs)
                    joint_errors = joint_validity(obs,self.config)['BLOCK']
                    fresh = 0<=now-obs.capture_ns<=self.config.evidence_age_ms*1_000_000
                    shifted = False
                    valid = (not joint_errors
                        and fresh
                        and set(arms)=={'LEFT','RIGHT'})
                    valid = valid and all(a['speed_xy']<=.8 and a['angle']<1.5
                        and a['angle3d'] is not None and a['angle3d']<2.4
                        and -.8<=a['position'][1]<=.4
                        and np.linalg.norm(a['position'][:2])<.9 for a in arms.values())
                    if not valid:
                        self.samples.clear();self.calibration_observations.clear();self.calibration_progress=0.
                    else:
                        if self.samples and any(np.linalg.norm(a['position'][:2]-old[s]['position'][:2])>.12
                            for _,old in self.samples for s,a in arms.items()):
                            self.samples.clear();self.calibration_observations.clear()
                            shifted = True
                        self.samples.append((obs.capture_ns,arms))
                        self.calibration_observations.append(obs)
                        elapsed=(obs.capture_ns-self.samples[0][0])/1e9
                        self.calibration_progress=min(1.,elapsed/.6,len(self.samples)/12.)
                        if elapsed>=.6 and len(self.samples)>=12:
                            for side,unit in self.attack.units.items():
                                anchor={k:np.median([a[side][k] for _,a in self.samples],axis=0)
                                    for k in ('position','angle','angle3d')}
                                anchor['angle']=float(anchor['angle']);anchor['angle3d']=float(anchor['angle3d'])
                                unit.baseline={side:anchor}
                                unit.state='READY';unit.neutral_since=obs.capture_ns
                            self.calibrated=True;self.calibrating=False
                            self.calibration_status='CALIBRATED'
                            self._log('demo_calibrated',now,source_frame_id=obs.frame_id,
                                samples=len(self.samples),duration_ms=elapsed*1000)
                            state=replace(state,attack_state='READY')
                    diagnostic = self.calibration_feedback.observe(
                        calibration_reasons(arms,joint_errors,fresh,shifted),now,
                        complete=self.calibrated)
                    if diagnostic is not None:
                        self._log('calibration_feedback',now,**diagnostic)
            if not self.movement_enabled:
                state=replace(state,movement='NEUTRAL',movement_strength=0.)
            self.control=state
            return state
