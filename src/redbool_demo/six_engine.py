"""Six gameplay gestures including a held chest-X special; left raise is high kick, right raise is low kick.

An opt-in development controller, not a trained or independently validated model.
Vertical actions require both arms visible; per-hand release permits natural chaining.
Both-hand raising owns the block channel and cannot produce two kicks.
"""
from dataclasses import asdict, replace
import numpy as np
from action_engine.experimental import ChannelSignals, joint_validity, RelativeBlock
from action_engine.types import ActionEvent
from .engine import DemoEngine, DemoAttack
from .lateral import LateralMovement
from .special import CrossSpecial, cross_geometry, extended_cross_arm


class SixBlock(RelativeBlock):
    forward_sides = frozenset()
    def update_relative(self, arms, anchors, now):
        if self.forward_sides:
            self.reset()
            return False
        return super().update_relative(arms, anchors, now)


class SixAttack(DemoAttack):
    forward_hint = False
    inhibited = False
    learned_guard_required = False
    guard_probability = 0.

    @staticmethod
    def guard_position(arm, base):
        dx, dy = arm['position'][:2] - base['position'][:2]
        # Accommodate eye-height calibration followed by a relaxed chin guard.
        # The absolute shoulder-relative height prevents drift into hands-down rest.
        return abs(dx) <= .35 and -.30 <= dy <= .45 and arm['position'][1] <= .20

    def update(self, arms, now, allow_commit=True, block=False):
        # An upward gesture may first look diagonal in its first two frames.
        # Hand it to the raise channel only while the punch is uncommitted.
        # ACTIVE/EXTENDED/RETURNING still require the normal physical return.
        handoff = False
        if self.state == 'CANDIDATE' and self.baseline and not self.forward_hint:
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

    def guard_neutral(self, arms):
        """One guard definition for punches, raises and the READY indicator.

        Returning fists need not occupy the exact calibration pixel: inward
        horizontal variation is normal. Image-depth elbow estimates fluctuate;
        allow modest variation but cap extension and also check the 2D elbow.
        Allow a relaxed return within .45 shoulder-width of the anchor,
        with an additional absolute shoulder-height bound.
        Keep vertical position, bent elbow and
        low speed mandatory so lowered/raised/extended holds cannot re-arm.
        The original calibration anchor never drifts toward an action pose.
        """
        if not arms or self.baseline is None:
            return False
        for side, arm in arms.items():
            base = self.baseline[side]
            dx, dy = arm['position'][:2] - base['position'][:2]
            if self.learned_guard_required and dy > .30 and self.guard_probability < .80:
                return False
            if not (self.guard_position(arm, base)
                    and arm['speed_xy'] <= self.cfg.neutral_speed
                    and arm['angle'] <= 1.5
                    and arm['angle3d'] is not None
                    and arm['angle3d'] <= min(2.5, base['angle3d'] + .40)):
                return False
        return True

    def neutral(self, arms):
        # Each punching hand can retract to a comfortable bent position below
        # its original guard. The other hand does not have to return with it.
        if self.state not in ('PREPARING','RETURNING','UNAVAILABLE'):
            return self.guard_neutral(arms)
        if not arms or self.baseline is None:return False
        for side, arm in arms.items():
            base=self.baseline[side];dx,dy=arm['position'][:2]-base['position'][:2]
            if not (abs(dx)<=.45 and -.30<=dy<=.65 and arm['position'][1]<=.45
                    and arm['speed_xy']<=1.2 and arm['angle']<=1.8
                    and arm['angle3d'] is not None
                    and arm['angle3d']<=min(2.6,base['angle3d']+.60)):
                return False
        return True

    def candidates(self, arms):
        result = super().candidates(arms)
        for side, arm in arms.items():
            delta = arm['position'] - self.baseline[side]['position'] if self.baseline else np.zeros(3)
            vertical = abs(delta[1]) >= .18 and abs(delta[1]) > abs(delta[0]) * 1.1
            if self.forward_hint:
                # Camera-directed extension can project mostly vertically. Keep
                # displacement, speed, calibrated extension and outward motion.
                row=result[side]
                row['forward_extension']=True
                row['onset_rule']=bool(row['baseline_valid'] and row['displacement_signal']
                    and row['speed_signal'] and row['outward'] and row['extension_signal'] and row['not_lowering'])
                row['commit_rule']=bool(row['baseline_valid'] and row['xy_displacement']>=self.cfg.commit_displacement
                    and row['outward'] and row['extension_signal'] and row['not_lowering']
                    and arm['angle3d']>=self.cfg.commit_angle_rad)
            if (vertical and not self.forward_hint) or self.inhibited:
                result[side]['onset_rule'] = result[side]['commit_rule'] = False
        return result


class SixGestureEngine(DemoEngine):
    def _components(self):
        super()._components()
        self.attack.units = {s: SixAttack(self.config) for s in ('LEFT', 'RIGHT')}
        self.movement = LateralMovement(self.config)
        self.special = CrossSpecial()
        self.route_signals = ChannelSignals(self.config)
        self.forward_sides = set()
        self.occluded_guard_sides = set()
        self.block = SixBlock(self.config)
        self.special_inhibited = False
        self.vertical_signals = ChannelSignals(self.config)
        self.vertical_state = 'DISARMED'
        self.guard_held = False
        self.vertical_candidate = None
        self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
        self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
        self.vertical_since = self.return_since = None

    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.config = replace(self.config, version='seated-directed-punch-20261108-v12')
        self._components()

    @property
    def ready(self):
        return (self.calibrated and not self.special.owned and self.guard_held and self.vertical_state == 'READY'
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
        self.special.invalidate()
        self.route_signals.reset();self.forward_sides=set();self.occluded_guard_sides=set()
        self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
        self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
        self.vertical_signals.reset()
        self.guard_held = False
        self.vertical_state = 'DISARMED'
        self.vertical_candidate = self.vertical_since = self.return_since = None

    def _process_actions(self, obs, evidence=None, now_ns=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            ident = (obs.actor_id, obs.capture_epoch)
            if ident in self.retired or (ident == self.identity and self.last_capture is not None
                    and (obs.capture_ns <= self.last_capture or obs.frame_id <= self.last_frame)):
                return super().process(obs, evidence, now)
            calibrated_before = self.calibrated
            if self.vertical_state=='PENDING' and self.vertical_candidate and self.vertical_candidate.split('_')[0] in self.forward_sides:
                self.vertical_state='READY';self.vertical_candidate=self.vertical_since=None
            self.block.forward_sides=self.forward_sides
            for side, unit in self.attack.units.items():
                unit.forward_hint = side in self.forward_sides
                unit.inhibited = self.special_inhibited or side in self.occluded_guard_sides or not self.raise_armed[side] or (
                    self.vertical_state == 'PENDING' and self.vertical_candidate == side + '_RAISE')
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
                self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
                self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
                self.guard_held = False
                self.vertical_state = 'DISARMED'
                self.vertical_candidate = self.vertical_since = self.return_since = None
                return self._publish_state(state)
            anchors = self.attack.anchors()
            delta = {s: a['position'] - anchors[s]['position'] for s, a in arms.items()}
            neutral = all(self.attack.units[s].guard_neutral({s: a}) for s, a in arms.items())
            self.guard_held = neutral
            # A raise is released by lowering that arm, not by a bilateral guard.
            # Keep separate latches so a held block cannot become a one-arm kick.
            bilateral = not self.forward_sides and all(delta[s][1] <= -.32 for s in arms)
            for side in arms:
                if delta[side][1] > -.20 and not state.block and not bilateral:
                    if self.raise_release[side] is None:self.raise_release[side] = now
                    if now-self.raise_release[side] >= 120_000_000 or not calibrated_before:
                        self.raise_armed[side] = True
                else:self.raise_release[side] = None
            if state.block or bilateral:
                self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
                self.vertical_state = 'LATCHED'
                self.vertical_candidate = self.vertical_since = None
            elif self.vertical_state in ('DISARMED','LATCHED') and any(self.raise_armed.values()):
                self.vertical_state = 'READY'
            candidate = None
            if not bilateral and not state.block:
                for side, other in (('LEFT', 'RIGHT'), ('RIGHT', 'LEFT')):
                    dx, dy = delta[side][:2]
                    other_dx, other_dy = delta[other][:2]
                    other_guard = (other_dy > -.30 and arms[other]['position'][1] <= 1.0
                                   and arms[other]['angle'] <= 2.7)
                    vertical = abs(dx) <= .35 and abs(dy) > abs(dx) * 1.2
                    if other_guard and vertical and side not in self.forward_sides:
                        if dy <= -.40:
                            candidate = side + '_RAISE'
            if (self.vertical_state == 'READY' and candidate
                    and self.raise_armed[candidate.split('_')[0]] and not self.special_inhibited):
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
                    self.raise_armed[side] = False
                    self.raise_release[side] = None
                    self.vertical_state = 'LATCHED'
            # Explicit gesture names travel independently of the anatomical side.
            for key, event in list(self.outbox.pending.items()):
                if 'gesture' not in event.reason:
                    self.outbox.pending[key] = replace(event, reason={**event.reason, 'gesture': event.side + '_PUNCH'})
            return self._publish_state(state)

    def process(self, obs, evidence=None, now_ns=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            ident = (obs.actor_id, obs.capture_epoch)
            if ident in self.retired or (ident == self.identity and self.last_capture is not None
                    and (obs.capture_ns <= self.last_capture or obs.frame_id <= self.last_frame)):
                return self._process_actions(obs, evidence, now)
            fresh = 0 <= now - obs.capture_ns <= self.config.evidence_age_ms * 1_000_000
            valid = fresh and not joint_validity(obs, self.config)['BLOCK']
            approach, strict, separated = cross_geometry(obs, self.config) if valid else (False, False, False)
            # Reject a transient X candidate once an arm clearly extends. A
            # fired X keeps its normal release latch; tracking loss is not release.
            if valid and extended_cross_arm(obs,self.config) and self.special.owned and not self.special.latched:
                self.special.owned=False;self.special.reset_dwell();self.special.return_since=None
            routing=self.route_signals.update(obs)
            anchors=self.attack.anchors();self.forward_sides=set();self.occluded_guard_sides=set()
            if self.calibrated and valid and set(routing)=={'LEFT','RIGHT'}:
                for side,other in (('LEFT','RIGHT'),('RIGHT','LEFT')):
                    arm=routing[side];base=anchors[side];d=arm['position']-base['position']
                    other_d=routing[other]['position']-anchors[other]['position']
                    # When a cross-body fist occludes the guarding hand, depth
                    # estimates can make both elbows appear to extend. A tightly
                    # folded image elbow is not a second punch alongside the
                    # visibly extending opposite arm.
                    if (arm['angle']<.6 and routing[other]['angle']>=1.8
                        and routing[other]['angle3d'] is not None and routing[other]['angle3d']>=2.5):
                        self.occluded_guard_sides.add(side)
                    if (arm['angle3d'] is not None and arm['angle3d']>=2.5 and arm['angle3d']-base['angle3d']>=.25
                        and d[2]<=-.45 and d[1]<=.12 and arm['position'][1]>=-.95
                        and arm['angle']>=.6 and other_d[1]>-.55 and routing[other]['angle']<=.9):
                        self.forward_sides.add(side)
            self.special_inhibited = self.special.owned or (not self.special.latched and approach)
            state = self._process_actions(obs, evidence, now)
            fired = self.special.step(obs.capture_ns, calibrated=self.calibrated, valid=valid,
                neutral=separated, approach=approach, strict=strict)
            if self.special.owned or self.special_inhibited:
                # Reserve this bilateral gesture, including its return. Never
                # publish a block/kick/punch from the same cross observation.
                self.outbox.pending = {k:e for k,e in self.outbox.pending.items()
                    if e.reason.get('gesture') == 'SPECIAL' or e.source_frame_id != obs.frame_id}
                self.block.reset()
                state = replace(state, block=False, movement='NEUTRAL', movement_strength=0.,
                    attack_state='RETURNING' if self.special.latched else 'SPECIAL_CANDIDATE')
            if fired:
                self.sequence += 1
                event = ActionEvent(f'{self.run_id}:{self.sequence}', *ident,
                    obs.frame_id, obs.capture_ns, now, now + 150_000_000, 'ATTACK', 'BOTH',
                    {'gesture':'SPECIAL','method':'chest_cross_dwell','hold_ms':700}, self.config.version)
                self.outbox.offer(event)
                self._log('event', now, event=asdict(event))
            self.control = state
            return state
