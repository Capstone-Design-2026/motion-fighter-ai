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
from .high_guard_raise import HighGuardRaise
from .auxiliary_pose import same_body


class SixBlock(RelativeBlock):
    forward_sides = frozenset()
    def update_relative(self, arms, anchors, now):
        if self.forward_sides:
            self.reset()
            return False
        candidate = super().update_relative(arms, anchors, now)
        # A valid released pose must reach Unity without the old 120ms tail.
        if not candidate: self.reset()
        return candidate


class SixAttack(DemoAttack):
    opposite_guard = False
    early_candidate = False
    unreturned_punch = False
    block_return = False
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

    @classmethod
    def supporting_guard(cls, arm, base):
        """The other hand may keep a folded high block during a body punch.

        This is context for extension evidence, not a READY/rearm condition.
        High holds must remain compact, bent and slow; extended raises do not
        qualify. Ordinary guard keeps its existing bounds.
        """
        ordinary = (cls.guard_position(arm, base)
            and arm['angle'] <= min(1.2, max(.6, base['angle'] + .15)) and arm['speed_xy'] <= 2.0)
        dx, dy = arm['position'][:2] - base['position'][:2]
        high = (abs(dx) <= .45 and -1.3 <= dy < -.30
            and arm['angle'] <= 1.2 and arm['speed_xy'] <= 1.2
            and arm['angle3d'] is not None
            and arm['angle3d'] <= min(2.55, base['angle3d'] + .60))
        return bool(ordinary or high)

    def update(self, arms, now, allow_commit=True, block=False):
        if block: self.folded_low_block_until = now + 1_200_000_000
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
        # A physically retracted hand can recover during a held block.
        # Only the return/preparation branch bypasses the block gate; READY
        # and CANDIDATE still cannot start or commit a blocked attack.
        # A weak onset gets one bounded observation phase. If full onset
        # arrives, start the existing confirmation deadline once, rather than
        # timing out precisely when a slower punch becomes well evidenced.
        if self.state == 'CANDIDATE' and self.early_candidate:
            if any(r['strict_onset_rule'] for r in self.candidates(arms).values()):
                self.entered = now
                self.early_candidate = False
        previous_state = self.state
        recovering = self.state in ('PREPARING', 'RETURNING', 'UNAVAILABLE')
        self.block_return = block and recovering
        try:
            commit, signals, reason = super().update(arms, now, allow_commit,
                block and not recovering)
        finally:
            self.block_return = False
        if previous_state == 'READY' and self.state == 'CANDIDATE':
            self.early_candidate = not signals[self.side]['strict_onset_rule']
        elif self.state != 'CANDIDATE':self.early_candidate = False
        if commit is not None:self.unreturned_punch = True
        elif self.state == 'READY':self.unreturned_punch = False
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
            # Bilateral block can be higher than the ordinary return envelope.
            # Require a compact, stable, bent hand; never accept a straight hold.
            if self.block_return and (abs(dx)<=.45 and -1.3<=dy<=0
                    and arm['angle']<=1.2 and arm['speed_xy']<=1.2
                    and arm['angle3d'] is not None
                    and arm['angle3d']<=min(2.55,base['angle3d']+.60)):
                continue
            # A compact, personally calibrated folded guard may still settle
            # after block release. Keep the full dwell and spatial/elbow proof;
            # avoid resetting that dwell on modest projected wrist jitter.
            compact_return = (self.state == 'PREPARING' and not self.unreturned_punch
                and self.now < getattr(self, 'folded_low_block_until', 0)
                and abs(dx)<=.30 and -.20<=dy<=.40
                and arm['position'][1]<=.45 and arm['angle']<=.50
                and arm['angle3d'] is not None
                and arm['angle3d']<=min(2.3,base['angle3d']+.15))
            return_speed = 2.0 if compact_return else 1.2
            if not (abs(dx)<=.45 and (-.30<=dy<=.65 or (-.65<=dy<-.30 and arm['angle']<=.6 and arm['angle3d'] is not None and arm['angle3d']<=min(2.2,base['angle3d']+.25))) and arm['position'][1]<=.45
                    and arm['speed_xy']<=return_speed and arm['angle']<=1.8
                    and arm['angle3d'] is not None
                    and arm['angle3d']<=min(2.6,base['angle3d']+.60)):
                return False
        return True

    def candidates(self, arms):
        result = super().candidates(arms)
        for side, arm in arms.items():
            delta = arm['position'] - self.baseline[side]['position'] if self.baseline else np.zeros(3)
            vertical = abs(delta[1]) >= .18 and abs(delta[1]) > abs(delta[0]) * 1.1
            row = result[side]
            # A side/diagonal punch can have a clearly extended image elbow
            # while monocular depth folds the reconstructed elbow backwards.
            # Accept independent 2D extension only with substantial horizontal
            # travel, calibrated opening, outward motion and a non-lowering fist.
            planar = bool(self.baseline and arm['angle'] >= 2.2
                and arm['angle'] - self.baseline[side]['angle'] >= .7
                and row['xy_displacement'] >= .50 and row['vertical_fraction'] <= .65
                and row['outward'] and row['not_lowering'])
            # A modestly lower punch is not a relaxed arm drop: require
            # horizontal travel, bounded depth and independently bent-to-open
            # elbow evidence, with the fist still above the lower torso.
            low_forward = bool(self.baseline and .12 < delta[1] <= .30
                and abs(delta[0]) >= .45 and (delta[2] <= .10 or (arm['angle']>=1.2 and arm['angle']-self.baseline[side]['angle']>=.7))
                and arm['position'][1] <= .45 and arm['angle'] >= .6
                and arm['angle3d'] is not None and arm['angle3d'] >= self.cfg.commit_angle_rad
                and row['extension_signal'] and row['vertical_fraction'] <= .65
                and row['outward'])
            compact_low = bool(self.baseline and .12 < delta[1] <= .30
                and abs(delta[0]) >= .30 and delta[2] <= -.10
                and arm['position'][1] <= .45 and arm['angle'] >= .30
                and arm['angle3d'] is not None and arm['angle3d'] >= 2.4
                and arm['angle3d']-self.baseline[side]['angle3d'] >= .20
                and row['vertical_fraction'] <= .50 and row['outward'])
            # A lower body punch needs a visible extension plus the opposite
            # hand's stable guard. Relaxing either arm or lowering both hands
            # cannot use this route. Keep lateral travel mandatory.
            body_extension = bool(self.baseline and self.opposite_guard
                and .12<delta[1]<=.55 and abs(delta[0])>=.45
                and arm['position'][1]<=.8 and arm['angle']>=.8
                and arm['angle']-self.baseline[side]['angle']>=.5
                and arm['angle3d'] is not None and arm['angle3d']>=2.3
                and row['extension_signal'] and row['vertical_fraction']<=.65 and row['outward'])
            body_planar = bool(self.baseline and self.opposite_guard
                and .12<delta[1]<=.9 and abs(delta[0])>=.45
                and arm['position'][1]<=1.0 and arm['angle']>=2.2
                and arm['angle']-self.baseline[side]['angle']>=.7
                and row['vertical_fraction']<=.8 and row['outward'])
            row['body_extension'] = body_extension
            row['body_planar_extension'] = body_planar
            low_forward = low_forward or compact_low or body_extension or body_planar
            row['low_forward_extension'] = low_forward
            if low_forward:
                row['not_lowering'] = True
                row['onset_rule'] = bool(row['displacement_signal'] and row['speed_signal'])
                row['commit_rule'] = row['xy_displacement'] >= self.cfg.commit_displacement
            row['planar_extension'] = planar
            if planar or body_planar:
                row['onset_rule'] = bool(row['onset_rule'] or row['speed_signal'])
                row['commit_rule'] = True
            # A camera-directed short punch can retain a folded 2D elbow and
            # reverse its projected wrist before monocular depth settles. Only
            # complete an already observed onset, with the other fist guarding;
            # never start an attack or rearm an extended hold from this route.
            foreshortened_low = bool(self.baseline and self.state == 'CANDIDATE'
                and self.now >= getattr(self, 'folded_low_block_until', 0)
                and self.now - self.entered >= 80_000_000
                and self.opposite_guard and getattr(self, "independent_elbow_opening", False) and .12 < delta[1] <= .45
                and abs(delta[0]) >= .30 and abs(delta[2]) <= .75
                and arm['position'][1] <= .50 and arm['angle'] <= .65
                and arm['angle3d'] is not None and arm['angle3d'] >= 2.2
                and arm['angle3d'] - self.baseline[side]['angle3d'] >= .15
                and row['xy_displacement'] >= .38 and row['vertical_fraction'] <= .78)
            row['foreshortened_low_commit'] = foreshortened_low
            if foreshortened_low: row['commit_rule'] = True
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
            # Begin observing an extension before its brief peak. Committing
            # still needs the full existing geometry on a later frame; this
            # avoids requiring two peak frames from a short 30fps punch.
            early_extension = bool(self.baseline and arm['angle3d'] is not None
                and arm['angle3d'] >= 2.2
                and arm['angle3d']-self.baseline[side]['angle3d'] >= .05
                and arm['angle'] >= .15 and row['displacement_signal']
                and row['speed_signal'] and row['outward']
                and row['vertical_fraction'] <= .65
                and (row['not_lowering'] or (.12<delta[1]<=.30 and abs(delta[0])>=.25)
                    or (self.opposite_guard and .30<delta[1]<=.55 and abs(delta[0])>=.30)))
            body_windup = bool(self.baseline and self.opposite_guard
                and .12<delta[1]<=.9 and abs(delta[0])>=.30
                and arm['position'][1]<=1.0 and arm['angle']>=.25
                and arm['angle']-self.baseline[side]['angle']>=.15
                and row['vertical_fraction']<=.8 and row['speed_signal'] and row['outward'])
            early_extension = early_extension or body_windup
            row['body_windup'] = body_windup
            row['strict_onset_rule'] = row['onset_rule']
            row['early_extension_onset'] = early_extension
            row['onset_rule'] = bool(row['onset_rule'] or early_extension)
            if (vertical and not self.forward_hint and not (body_windup or body_planar or body_extension)) or self.inhibited:
                result[side]['onset_rule'] = result[side]['commit_rule'] = result[side]['strict_onset_rule'] = False
        return result


class SixGestureEngine(DemoEngine):
    def _components(self):
        super()._components()
        self.attack.units = {s: SixAttack(self.config) for s in ('LEFT', 'RIGHT')}
        self.movement = LateralMovement(self.config)
        self.special = CrossSpecial()
        self.route_signals = ChannelSignals(self.config)
        self.forward_sides = set()
        self.forward_proof = {}
        self.compact_forward_proof = {}
        self.occluded_guard_sides = set()
        self.occlusion_until = {}
        self.block = SixBlock(self.config)
        self.high_raise = HighGuardRaise()
        self.high_raise_sides = set()
        self.special_inhibited = False
        self.vertical_signals = ChannelSignals(self.config)
        self.vertical_state = 'DISARMED'
        self.guard_held = False
        self.vertical_candidate = None
        self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
        self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
        self.raise_peak = dict.fromkeys(('LEFT','RIGHT'), None)
        self.vertical_since = self.return_since = None

    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.config = replace(self.config, version='seated-block-preparation-20261111-v25')
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
        self.high_raise = HighGuardRaise(); self.high_raise_sides = set()
        self.route_signals.reset();self.forward_sides=set();self.forward_proof={};self.compact_forward_proof={};self.occluded_guard_sides=set();self.occlusion_until={}
        self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
        self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
        self.raise_peak = dict.fromkeys(('LEFT','RIGHT'), None)
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
            self.block.forward_sides=self.forward_sides | self.high_raise_sides
            for side, unit in self.attack.units.items():
                unit.forward_hint = side in self.forward_sides
                unit.inhibited = self.special_inhibited or side in self.high_raise_sides or side in self.occluded_guard_sides or (not self.raise_armed[side] and side not in self.forward_sides) or (
                    self.vertical_state == 'PENDING' and self.vertical_candidate == side + '_RAISE')
            state = super().process(obs, evidence, now)
            if self.calibrated and not calibrated_before:
                self.movement.calibrate_samples(self.calibration_observations)
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
                self.raise_peak = dict.fromkeys(('LEFT','RIGHT'), None)
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
            bilateral = not (self.forward_sides or self.high_raise_sides) and all(delta[s][1] <= -.32 for s in arms)
            for side in arms:
                dy=float(delta[side][1])
                peak=self.raise_peak[side]
                self.raise_peak[side]=dy if self.raise_armed[side] or peak is None else min(peak,dy)
                # A visibly folded arm returned >=.30 shoulder-width from its
                # own raised peak can rearm quickly at a comfortable high guard.
                # Keeping one hand raised after block is still not a new kick.
                compact_return=(dy>-.35 and self.raise_peak[side]<=dy-.30
                    and arms[side]['angle']<=.6)
                if (dy > -.20 or compact_return) and not state.block and not bilateral:
                    if self.raise_release[side] is None:self.raise_release[side] = now
                    dwell=30_000_000 if compact_return else 120_000_000
                    if now-self.raise_release[side] >= dwell or not calibrated_before:
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
                    other_guard = (side in self.high_raise_sides or other_dy > -.30 and arms[other]['position'][1] <= 1.0
                                   and arms[other]['angle'] <= 2.7)
                    vertical = abs(dx) <= (.60 if side in self.high_raise_sides else .35) and abs(dy) > abs(dx) * 1.2
                    # A still-extended punch sweeping back toward the camera is
                    # not a new raise just because its projected wrist is high.
                    punch_return = self.attack.units[side].state in ('ACTIVE','EXTENDED','RETURNING')
                    new_raise_motion = not punch_return or arms[side]['velocity'][1] < -.25
                    if other_guard and vertical and new_raise_motion and side not in self.forward_sides:
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

    def process(self, obs, evidence=None, now_ns=None, special_observation=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            ident = (obs.actor_id, obs.capture_epoch)
            if ident in self.retired or (ident == self.identity and self.last_capture is not None
                    and (obs.capture_ns <= self.last_capture or obs.frame_id <= self.last_frame)):
                return self._process_actions(obs, evidence, now)
            fresh = 0 <= now - obs.capture_ns <= self.config.evidence_age_ms * 1_000_000
            valid = fresh and not joint_validity(obs, self.config)['BLOCK']
            approach, strict, separated = cross_geometry(obs, self.config) if valid else (False, False, False)
            special_valid = valid
            extended = extended_cross_arm(obs, self.config) if valid else False
            # Auxiliary Full landmarks may resolve crossed-arm occlusion which
            # Lite confidently reconstructs as uncrossed. Never use a stale or
            # different frame/person for this channel, and never alter punch,
            # raise or movement coordinates with the auxiliary skeleton.
            aux = special_observation
            if (self.calibrated and aux is not None
                    and (aux.actor_id, aux.capture_epoch, aux.frame_id, aux.capture_ns)
                    == (obs.actor_id, obs.capture_epoch, obs.frame_id, obs.capture_ns)
                    and (aux.width, aux.height, aux.source_mirrored) == (obs.width, obs.height, obs.source_mirrored)
                    and fresh and same_body(obs, aux, self.config) and not joint_validity(aux, self.config)['BLOCK']):
                ap, st, sep = cross_geometry(aux, self.config)
                approach = approach or ap
                strict = strict or st
                # Release a cross when either valid model sees a definite open
                # pose and neither model still supplies actual cross geometry.
                separated = (separated or sep) and not approach
                extended = extended and extended_cross_arm(aux, self.config)
                special_valid = True
            # Reject a transient X candidate once an arm clearly extends. A
            # fired X keeps its normal release latch; tracking loss is not release.
            if special_valid and extended and self.special.owned and not self.special.latched:
                self.special.owned=False;self.special.reset_dwell();self.special.return_since=None
            routing=self.route_signals.update(obs)
            anchors=self.attack.anchors();self.forward_sides=set();self.occluded_guard_sides=set()
            for unit in self.attack.units.values():unit.opposite_guard=False
            if self.calibrated and valid and set(routing)=={'LEFT','RIGHT'}:
                points=np.asarray(obs.landmarks,float).copy();points[:,1]*=obs.height/obs.width
                span=np.linalg.norm(points[11,:2]-points[12,:2])
                hands_overlap=np.linalg.norm(points[15,:2]-points[16,:2]) < .35*span
                for side,other in (('LEFT','RIGHT'),('RIGHT','LEFT')):
                    arm=routing[side];base=anchors[side];d=arm['position']-base['position']
                    other_d=routing[other]['position']-anchors[other]['position']
                    self.attack.units[side].opposite_guard=(
                        self.attack.units[other].supporting_guard(routing[other],anchors[other]))
                    other_arm = routing[other]
                    # Shared depth changes while leaning are not a short punch.
                    # This extra completion route needs asymmetric elbow opening.
                    self.attack.units[side].independent_elbow_opening = bool(
                        arm['angle3d'] is not None and other_arm['angle3d'] is not None
                        and arm['angle3d'] - base['angle3d']
                        >= other_arm['angle3d'] - anchors[other]['angle3d'] + .10)
                    # A held punch crossing the guarding fist can swap wrist
                    # estimates. Temporarily suppress only the uncommitted hand,
                    # retaining the ongoing punch's physical-return lifecycle.
                    if (hands_overlap and self.attack.units[other].state in ('ACTIVE','EXTENDED','RETURNING')
                        and self.attack.units[side].state in ('READY','CANDIDATE','PREPARING')):
                        self.occlusion_until[side]=obs.capture_ns+200_000_000
                    if obs.capture_ns < self.occlusion_until.get(side,0):
                        self.occluded_guard_sides.add(side)
                    # When a cross-body fist occludes the guarding hand, depth
                    # estimates can make both elbows appear to extend. A tightly
                    # folded image elbow is not a second punch alongside the
                    # visibly extending opposite arm.
                    if (arm['angle']<.6 and routing[other]['angle']>=1.8
                        and routing[other]['angle3d'] is not None and routing[other]['angle3d']>=2.5):
                        self.occluded_guard_sides.add(side)
                    # A high-guard punch can travel sideways in the image while
                    # monocular depth points either way. Require independent elbow
                    # opening and lateral travel, with the opposite arm folded.
                    lateral_extension = (arm['angle3d'] is not None and arm['angle3d']>=2.3
                        and arm['angle3d']-base['angle3d']>=.15 and arm['angle']>=.9
                        and abs(d[0])>=.45 and d[1]<=.12
                        and abs(d[1])<=abs(d[0])*1.8 and other_d[1]>-.55
                        and routing[other]['angle']<=.9)
                    if lateral_extension:self.forward_sides.add(side)
                    strong_extension = (arm['angle3d'] is not None and arm['angle3d']>=2.6
                        and arm['angle3d']-base['angle3d']>=.5 and arm['angle']>=.75)
                    # A high camera-directed fist projects upward like a raise.
                    # Extend only the strong straight-elbow/depth route, not the
                    # general raise threshold or weak folded-elbow evidence.
                    if (arm['angle3d'] is not None and arm['angle3d']>=2.5 and arm['angle3d']-base['angle3d']>=.25
                        and d[2]<=(-.4 if strong_extension else -.45) and d[1]<=.12
                        and arm['position'][1]>=(-1.3 if strong_extension else -.95)
                        and arm['angle']>=.6 and other_d[1]>-.55 and routing[other]['angle']<=.9):
                        self.forward_sides.add(side)
                        if strong_extension: self.forward_proof[side]=obs.capture_ns
                    # A compact camera-directed punch can retain a folded image
                    # elbow. Require depth, calibrated 3D opening, lateral travel
                    # and the other hand's compact guard before overriding raise.
                    compact_route_allowed = (self.attack.units[side].state in ('READY','CANDIDATE')
                        or self.attack.units[side].unreturned_punch)
                    compact_forward = (compact_route_allowed and arm['angle3d'] is not None and arm['angle3d']>=2.4
                        and arm['angle3d']-base['angle3d']>=.30 and arm['angle']>=.4
                        and abs(d[0])>=.10 and -.8<d[1]<=.12 and d[2]<=-.4
                        and arm['velocity'][1]>=-.25
                        and self.attack.units[other].guard_position(routing[other],anchors[other])
                        and routing[other]['angle']<=.6)
                    if compact_forward:
                        self.forward_sides.add(side);self.compact_forward_proof[side]=obs.capture_ns
                    projected_extension = (arm['angle3d'] is not None and arm['angle3d']>=2.5
                        and arm['angle3d']-base['angle3d']>=.25 and arm['angle']>=1.0
                        and abs(d[0])>=.30 and d[2]<=-.35 and d[1]<=.12
                        and arm['position'][1]>=-1.3 and other_d[1]>-.55
                        and routing[other]['angle']<=.9)
                    if projected_extension:
                        self.forward_sides.add(side);self.forward_proof[side]=obs.capture_ns
                    # Keep a proven extended punch route through <=100ms of
                    # depth jitter, only while current valid joints still extend.
                    # This is routing continuity, never an attack/guard queue.
                    proof=self.forward_proof.get(side)
                    if (proof is not None and 0<=obs.capture_ns-proof<=100_000_000
                        and arm['angle3d'] is not None and arm['angle3d']>=2.4
                        and arm['angle']>=.6 and d[1]<=.12 and arm['position'][1]>=-1.3
                        and other_d[1]>-.55 and routing[other]['angle']<=.9):
                        self.forward_sides.add(side)
                    elif side not in self.forward_sides:
                        self.forward_proof.pop(side,None)
                    compact_proof=self.compact_forward_proof.get(side)
                    if (compact_route_allowed and compact_proof is not None and 0<=obs.capture_ns-compact_proof<=100_000_000
                        and arm['angle3d'] is not None and arm['angle3d']>=2.4
                        and arm['angle']>=.4 and abs(d[0])>=.10 and -.8<d[1]<=.12 and d[2]<=-.2
                        and self.attack.units[other].guard_position(routing[other],anchors[other])
                        and routing[other]['angle']<=.6):
                        self.forward_sides.add(side)
                    elif not compact_forward:
                        self.compact_forward_proof.pop(side,None)
            else:
                self.forward_proof.clear();self.compact_forward_proof.clear()
            self.special_inhibited = self.special.owned or (not self.special.latched and approach)
            self.high_raise_sides, high_started = self.high_raise.step(routing, anchors, obs.capture_ns,
                valid=valid and self.calibrated, blocked=self.control.block,
                excluded=self.forward_sides | {s for s,u in self.attack.units.items() if u.unreturned_punch})
            if high_started:
                for side in high_started:self.raise_armed[side] = True
                if self.vertical_state in ('DISARMED','LATCHED'):self.vertical_state = 'READY'
            state = self._process_actions(obs, evidence, now)
            fired = self.special.step(obs.capture_ns, calibrated=self.calibrated, valid=special_valid,
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
                # A confirmed bilateral X takes ownership from prior punches.
                for unit in self.attack.units.values():unit.unreturned_punch=False
                self.sequence += 1
                event = ActionEvent(f'{self.run_id}:{self.sequence}', *ident,
                    obs.frame_id, obs.capture_ns, now, now + 150_000_000, 'ATTACK', 'BOTH',
                    {'gesture':'SPECIAL','method':'chest_cross_dwell','hold_ms':700}, self.config.version)
                self.outbox.offer(event)
                self._log('event', now, event=asdict(event))
            self.control = state
            return state
