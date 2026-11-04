"""Five gameplay gestures; left raise is high kick, right raise is low kick.

An opt-in development controller, not a trained or independently validated model.
Vertical actions require both arms visible and the other hand in calibrated guard.
Both-hand raising owns the block channel and cannot produce two kicks.
"""
from dataclasses import asdict, replace
import numpy as np
from action_engine.experimental import ChannelSignals, joint_validity
from action_engine.types import ActionEvent
from .engine import DemoEngine, DemoAttack
from .lateral import LateralMovement


class SixAttack(DemoAttack):
    inhibited = False

    def update(self, arms, now, allow_commit=True, block=False):
        # An upward gesture may first look diagonal in its first two frames.
        # Hand it to the raise channel only while the punch is uncommitted.
        # ACTIVE/EXTENDED/RETURNING still require the normal physical return.
        handoff = False
        if self.state == 'CANDIDATE' and self.baseline:
            for side, arm in arms.items():
                dx, dy = arm['position'][:2] - self.baseline[side]['position'][:2]
                if dy <= -.18 and -dy > abs(dx) * 1.1:
                    self.state = 'READY'
                    self.side = None
                    self.neutral_since = None
                    self.entered = now
                    handoff = True
        commit, signals, reason = super().update(arms, now, allow_commit, block)
        return commit, signals, reason or ('uncommitted_raise_handoff' if handoff else '')

    def neutral(self, arms):
        """One guard definition for punches, raises and the READY indicator.

        Returning fists need not occupy the exact calibration pixel: inward
        horizontal variation is normal. Image-depth elbow estimates fluctuate;
        allow modest variation but cap extension and also check the 2D elbow.
        Allow a relaxed return up to .30 shoulder-width below the anchor.
        Keep vertical position, bent elbow and
        low speed mandatory so lowered/raised/extended holds cannot re-arm.
        The original calibration anchor never drifts toward an action pose.
        """
        if not arms or self.baseline is None:
            return False
        for side, arm in arms.items():
            base = self.baseline[side]
            dx, dy = arm['position'][:2] - base['position'][:2]
            if not (abs(dx) <= .35 and -.30 <= dy <= .30
                    and arm['speed_xy'] <= self.cfg.neutral_speed
                    and arm['angle'] <= 1.5
                    and arm['angle3d'] is not None
                    and arm['angle3d'] <= min(2.5, base['angle3d'] + .40)):
                return False
        return True

    def candidates(self, arms):
        result = super().candidates(arms)
        for side, arm in arms.items():
            delta = arm['position'] - self.baseline[side]['position'] if self.baseline else np.zeros(3)
            vertical = abs(delta[1]) >= .18 and abs(delta[1]) > abs(delta[0]) * 1.1
            if vertical or self.inhibited:
                result[side]['onset_rule'] = result[side]['commit_rule'] = False
        return result


class SixGestureEngine(DemoEngine):
    def _components(self):
        super()._components()
        self.attack.units = {s: SixAttack(self.config) for s in ('LEFT', 'RIGHT')}
        self.movement = LateralMovement(self.config)
        self.vertical_signals = ChannelSignals(self.config)
        self.vertical_state = 'DISARMED'
        self.vertical_candidate = None
        self.vertical_since = self.return_since = None

    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.config = replace(self.config, version='seated-lean-guard-20261104-v6')
        self._components()

    @property
    def ready(self):
        return (self.calibrated and self.vertical_state == 'READY'
                and all(u.state == 'READY' for u in self.attack.units.values()))

    def _publish_state(self, state):
        # AttackGroup's READY can mean only one arm is ready. Unity's tutorial
        # and the live display must not mistake that for full guard recovery.
        if self.ready:
            phase = 'READY'
        elif self.vertical_state == 'LATCHED':
            phase = 'RETURNING'
        elif self.vertical_state == 'PENDING':
            phase = 'CANDIDATE'
        else:
            states = {u.state for u in self.attack.units.values()}
            phase = next((s for s in ('ACTIVE', 'EXTENDED', 'CANDIDATE',
                'RETURNING', 'UNAVAILABLE', 'PREPARING') if s in states), 'PREPARING')
        self.control = replace(state, attack_state=phase)
        return self.control

    def _safe(self, now, reason):
        super()._safe(now, reason)
        self.vertical_signals.reset()
        self.vertical_state = 'DISARMED'
        self.vertical_candidate = self.vertical_since = self.return_since = None

    def process(self, obs, evidence=None, now_ns=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            ident = (obs.actor_id, obs.capture_epoch)
            if ident in self.retired or (ident == self.identity and self.last_capture is not None
                    and (obs.capture_ns <= self.last_capture or obs.frame_id <= self.last_frame)):
                return super().process(obs, evidence, now)
            ready_before = all(u.state == 'READY' for u in self.attack.units.values())
            calibrated_before = self.calibrated
            for unit in self.attack.units.values():
                unit.inhibited = self.vertical_state in ('PENDING', 'LATCHED')
            state = super().process(obs, evidence, now)
            if self.calibrated and not calibrated_before:
                self.movement.calibrate(obs)
            arms = self.vertical_signals.update(obs)
            fresh = 0 <= now - obs.capture_ns <= self.config.evidence_age_ms * 1_000_000
            validity = joint_validity(obs, self.config)
            if not fresh or validity['MOVEMENT']:
                # Release a held direction immediately when shoulder evidence
                # disappears; do not carry it until the shared safety timeout.
                self.movement.reset()
                state = replace(state, movement='NEUTRAL', movement_strength=0.)
            valid = fresh and not validity['BLOCK'] and set(arms) == {'LEFT', 'RIGHT'}
            if not self.calibrated or not valid:
                self.vertical_state = 'DISARMED'
                self.vertical_candidate = self.vertical_since = self.return_since = None
                return self._publish_state(state)
            anchors = self.attack.anchors()
            delta = {s: a['position'] - anchors[s]['position'] for s, a in arms.items()}
            neutral = all(self.attack.units[s].neutral({s: a}) for s, a in arms.items())
            # Re-arm only after a real return, including after invalid tracking.
            if neutral:
                if self.return_since is None:
                    self.return_since = now
                if now - self.return_since >= 180_000_000 or not calibrated_before:
                    self.vertical_state = 'READY'
                    self.vertical_candidate = self.vertical_since = None
            else:
                self.return_since = None
            bilateral = all(delta[s][1] <= -.32 for s in arms)
            if state.block or bilateral:
                self.vertical_state = 'LATCHED'
                self.vertical_candidate = self.vertical_since = None
            candidate = None
            if not bilateral and not state.block:
                for side, other in (('LEFT', 'RIGHT'), ('RIGHT', 'LEFT')):
                    dx, dy = delta[side][:2]
                    other_dx, other_dy = delta[other][:2]
                    other_guard = abs(other_dx) <= .35 and -.30 <= other_dy <= .30
                    vertical = abs(dx) <= .35 and abs(dy) > abs(dx) * 1.2
                    if other_guard and vertical:
                        if dy <= -.40:
                            candidate = side + '_RAISE'
            if self.vertical_state == 'READY' and candidate and ready_before:
                self.vertical_state = 'PENDING'
                self.vertical_candidate, self.vertical_since = candidate, now
            elif self.vertical_state == 'PENDING':
                if candidate != self.vertical_candidate:
                    # Do not turn an incomplete block or cancelled raise into a punch.
                    self.vertical_state = 'LATCHED'
                    self.vertical_candidate = self.vertical_since = None
                elif now - self.vertical_since >= 180_000_000:
                    self.sequence += 1
                    side = candidate.split('_')[0]
                    event = ActionEvent(f'{self.run_id}:{self.sequence}', *ident,
                        obs.frame_id, obs.capture_ns, now, now + 150_000_000,
                        'ATTACK', side, {'gesture': candidate, 'method': 'calibrated_vertical_dwell',
                        'game_mapping': 'LOW_KICK' if candidate == 'RIGHT_RAISE' else 'HIGH_KICK'}, self.config.version)
                    self.outbox.offer(event)
                    self._log('event', now, event=asdict(event))
                    self.vertical_state = 'LATCHED'
            # Explicit gesture names travel independently of the anatomical side.
            for key, event in list(self.outbox.pending.items()):
                if 'gesture' not in event.reason:
                    self.outbox.pending[key] = replace(event, reason={**event.reason, 'gesture': event.side + '_PUNCH'})
            return self._publish_state(state)
