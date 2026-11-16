"""Six gameplay gestures including a held chest-X special; left raise is high kick, right raise is low kick.

An opt-in development controller, not a trained or independently validated model.
Vertical actions require both arms visible; per-hand release permits natural chaining.
Both-hand raising owns the block channel and cannot produce two kicks.
"""
from dataclasses import asdict, replace
import numpy as np
from action_engine.experimental import ChannelSignals, joint_validity, RelativeBlock
from action_engine.types import ActionEvent
from action_engine.core import ActionEngine
from .engine import DemoEngine, DemoAttack
from .lateral import LateralMovement
from .special import CrossSpecial, cross_geometry, extended_cross_arm
from .refined_special import definite_cross_release, RefinedRepeatLatch
from .high_guard_raise import HighGuardRaise
from .auxiliary_pose import same_body
from .static_special import StaticCrossProof, StaticCrossRelease
from .temporal_forward import TemporalForwardOnset


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

    def __init__(self, cfg):
        super().__init__(cfg)
        self.projected_body_samples = []
        self.centered_depth_samples = []
        self.centered_depth_committed = False
        self.temporal_forward = TemporalForwardOnset()
        self.temporal_forward_pending = False
        self.upper_arm_projection = None

    def invalidate(self, now):
        super().invalidate(now)
        self.projected_body_samples.clear()
        self.centered_depth_samples.clear()
        self.temporal_forward.reset()
        self.temporal_forward_pending = False
        self.upper_arm_projection = None

    def _centered_depth_evidence(self, side, arm):
        """Completion evidence for an already observed camera-directed punch.

        A forward fist may reverse in the image while its elbow remains open.
        Depth alone cannot acquire a punch: the unchanged onset and forward
        arbitration must already agree, with the other fist guarding. Keep the
        projected elbow, calibrated opening and independently moving arm proof.
        """
        if (not self.baseline or self.inhibited or not self.forward_hint
                or not self.opposite_guard
                or not getattr(self, 'independent_elbow_opening', False)):
            return False
        base = self.baseline[side]
        dx, dy, dz = arm['position'] - base['position']
        # The established folded-upward region belongs to raise arbitration.
        # This extra completion must not steal an ambiguous lifted forearm.
        return bool(.10 <= abs(dx) <= .35 and -.40 < dy <= .12 and dz <= -.45
            and np.linalg.norm([dx, dy]) >= .24
            and arm['angle'] >= .4 and arm['angle3d'] is not None
            and arm['angle3d'] >= 2.4 and arm['angle3d'] - base['angle3d'] >= .30)

    def _centered_depth_confirmed(self, side, arm):
        if self.state != 'CANDIDATE' or not self._centered_depth_evidence(side, arm):
            return False
        depth = float(arm['position'][2] - self.baseline[side]['position'][2])
        if not self.centered_depth_samples:
            return False
        stamp, _, angle = self.centered_depth_samples[0]
        return (20_000_000 <= self.now-stamp <= 100_000_000
            and depth <= min(d for _, d, _ in self.centered_depth_samples)
            and arm['angle3d'] >= angle - .10)

    def _remember_centered_depth(self, arms, signals, now, previous_state, block):
        # Observe once per update, never per candidates() query. Invalidated,
        # reserved, returned and committed poses cannot retain completion proof.
        if block or self.state != 'CANDIDATE':
            self.centered_depth_samples.clear()
            return
        seed = previous_state == 'READY' and signals[self.side]['strict_onset_rule']
        if not seed and (not self.centered_depth_samples
                or not 0 <= now-self.centered_depth_samples[0][0] <= 100_000_000):
            self.centered_depth_samples.clear()
            return
        for side, arm in arms.items():
            if self._centered_depth_evidence(side, arm):
                depth = float(arm['position'][2] - self.baseline[side]['position'][2])
                if self.centered_depth_samples and (depth > min(d for _, d, _ in self.centered_depth_samples)
                        or arm['angle3d'] < self.centered_depth_samples[0][2] - .10):
                    self.centered_depth_samples.clear()
                    return
                if not self.centered_depth_samples or self.centered_depth_samples[-1][0] != now:
                    self.centered_depth_samples.append((now,
                        depth,
                        float(arm['angle3d'])))
                return
        self.centered_depth_samples.clear()

    @staticmethod
    def _projected_body_evidence(arm, base, rule):
        delta = arm['position'] - base['position']
        return bool(.12 < delta[1] <= .55 and abs(delta[0]) >= .65
            and arm['position'][1] <= .55 and arm['angle'] >= 1.8
            and arm['angle'] - base['angle'] >= 1.5
            and rule['vertical_fraction'] <= .60 and rule['outward'])

    def _remember_projected_body(self, arms, signals, now, previous_state, block):
        # Record once after processing an observation. candidates() is queried
        # more than once in an update; those queries must not invent extra votes.
        if (previous_state != 'CANDIDATE' or self.state != 'CANDIDATE'
                or block or self.inhibited or not self.opposite_guard):
            self.projected_body_samples.clear()
            return
        self.projected_body_samples = [t for t in self.projected_body_samples
            if 0 <= now-t <= 120_000_000]
        if any(self._projected_body_evidence(a, self.baseline[s], signals[s])
               for s, a in arms.items()):
            if not self.projected_body_samples or self.projected_body_samples[-1] != now:
                self.projected_body_samples.append(now)

    @staticmethod
    def guard_position(arm, base, *, allow_calibrated_floor=False):
        dx, dy = arm['position'][:2] - base['position'][:2]
        # Accommodate eye-height calibration followed by a relaxed chin guard.
        # The absolute shoulder-relative height prevents drift into hands-down rest.
        # Calibrated low guards can re-arm, but must not broaden the other-hand
        # support proof used by extra body-punch routes without reviewed evidence.
        floor = max(.20, min(.45, float(base['position'][1]) + .08)) if allow_calibrated_floor else .20
        return abs(dx) <= .35 and -.30 <= dy <= .45 and arm['position'][1] <= floor

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
        side = next(iter(arms), None)
        temporal_available = self.state == 'READY' and not block and not self.inhibited and self.opposite_guard
        self.temporal_forward.observe(arms.get(side), self.baseline.get(side) if self.baseline else None, now,
            available=temporal_available,
            forward=self.forward_hint, independent=getattr(self, 'independent_elbow_opening', False),
            upper_projection=self.upper_arm_projection, guard_origin=temporal_available and self.guard_neutral(arms))
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
        self._remember_projected_body(arms, signals, now, previous_state, block)
        self._remember_centered_depth(arms, signals, now, previous_state, block)
        if commit is not None and (signals[commit].get('centered_depth_confirmation') or self.temporal_forward_pending):
            self.centered_depth_committed = True
        elif self.state == 'READY':
            self.centered_depth_committed = False
        if previous_state == 'READY' and self.state == 'CANDIDATE':
            self.temporal_forward_pending = bool(signals[self.side].get('temporal_forward_onset'))
            self.early_candidate = not signals[self.side]['strict_onset_rule']
        elif self.state != 'CANDIDATE':
            self.early_candidate = False
            self.temporal_forward_pending = False
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
            # A confirmed forward extension projects close to guard. Do not
            # cancel that existing onset as neutral merely because XY is small.
            if self._centered_depth_confirmed(side, arm):
                return False
            dx, dy = arm['position'][:2] - base['position'][:2]
            if self.learned_guard_required and dy > .30 and self.guard_probability < .80:
                return False
            if not (self.guard_position(arm, base, allow_calibrated_floor=True)
                    and arm['speed_xy'] <= self.cfg.neutral_speed
                    and arm['angle'] <= 1.5
                    and arm['angle3d'] is not None
                    and arm['angle3d'] <= min(2.5, base['angle3d'] + .40)):
                return False
        return True

    def neutral(self, arms):
        # Only the new completion route needs this extra return proof. Its
        # foreshortened extended fist must not rearm as a compact image guard.
        # Keep the latch across tracking loss until actual elbow/depth return.
        if self.centered_depth_committed and self.baseline:
            for side, arm in arms.items():
                base = self.baseline[side]
                if (arm['angle3d'] is not None and arm['angle3d'] >= 2.3
                        and arm['angle3d'] - base['angle3d'] >= .20
                        and arm['position'][2] - base['position'][2] <= -.30):
                    return False
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
            # A front-directed body punch can fold its monocular depth while
            # the image elbow opens clearly. Finish only an existing onset with
            # two separate substantial openings, a guarding opposite hand and
            # large horizontal travel. Never start/rearm from this route.
            projected_body = bool(self.baseline and self.state == 'CANDIDATE'
                and not self.inhibited and self.opposite_guard
                and self._projected_body_evidence(arm, self.baseline[side], row)
                and arm['angle'] >= 2.05
                and any(30_000_000 <= self.now-t <= 120_000_000
                        for t in self.projected_body_samples))
            if projected_body:
                body_planar = True
                row['projected_body_confirmation'] = True
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
            if (self.state == 'READY' and not self.inhibited and self.forward_hint
                    and self.temporal_forward.stamp == self.now and not row['onset_rule']):
                row['onset_rule'] = True
                row['temporal_forward_onset'] = True
            centered_depth = self._centered_depth_confirmed(side, arm)
            row['centered_depth_confirmation'] = centered_depth
            if centered_depth:
                row['commit_rule'] = True
        return result


class SixGestureEngine(DemoEngine):
    def _components(self):
        super()._components()
        self._continuity_pending = False
        self._continuity_until = None
        self._continuity_processing = False
        self.attack.units = {s: SixAttack(self.config) for s in ('LEFT', 'RIGHT')}
        self.movement = LateralMovement(self.config)
        self.special = CrossSpecial()
        self.cross_refinement_used = False
        self.refinement_repeat = RefinedRepeatLatch()
        self.static_special = StaticCrossProof()
        self.static_release = StaticCrossRelease()
        self.static_latched = False
        self.cross_extension_since = None
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
        self.config = replace(self.config, version='seated-cross-recovery-20261111-v26+field-v32')
        self._components()

    @property
    def cross_refinement_latched(self):
        return self.refinement_repeat.locked

    @property
    def ready(self):
        return (not self._continuity_pending and self.calibrated and not self.special.owned and self.guard_held and self.vertical_state == 'READY'
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
        # Output liveness and the interval between captured observations are
        # different clocks. A fresh result can arrive after its predecessor's
        # output lease expired, despite a continuous <=100ms capture pair.
        # Keep only private state for the latest possible eligible successor;
        # never retain a published direction/block or pending attack.
        deadline = (self.last_capture + int((self.config.max_gap_ms
            + self.config.evidence_age_ms) * 1_000_000)) if self.last_capture is not None else None
        if (reason == 'observation_timeout' and self.calibrated
                and self.last_capture == self.safety.last_valid_ns
                and deadline is not None and now < deadline):
            self._continuity_pending = True
            self._continuity_until = deadline
            self.outbox.pending.clear()
            self.control = replace(self.control, timestamp_ns=now, tracking='LOST',
                block=False, movement='NEUTRAL', movement_strength=0., attack_state='UNAVAILABLE')
            self._log('safety', now, reason=reason, private_continuity_until_ns=deadline,
                state=asdict(self.control))
            return
        self._continuity_pending = False
        self._continuity_until = None
        self._continuity_processing = False
        super()._safe(now, reason)
        self.special.invalidate()
        self.cross_refinement_used = False
        self.refinement_repeat.invalidate()
        self.cross_extension_since = None
        self.high_raise = HighGuardRaise(); self.high_raise_sides = set()
        self.route_signals.reset();self.forward_sides=set();self.forward_proof={};self.compact_forward_proof={};self.occluded_guard_sides=set();self.occlusion_until={}
        self.raise_armed = dict.fromkeys(('LEFT','RIGHT'), False)
        self.raise_release = dict.fromkeys(('LEFT','RIGHT'), None)
        self.raise_peak = dict.fromkeys(('LEFT','RIGHT'), None)
        self.vertical_signals.reset()
        self.guard_held = False
        self.vertical_state = 'DISARMED'
        self.vertical_candidate = self.vertical_since = self.return_since = None

    def tick(self, now_ns=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            self.static_special.expire(now)
            self.static_release.expire(now)
            # Also expire while output is already LOST; ActionEngine's normal
            # watchdog deliberately avoids repeatedly invalidating that state.
            if self._continuity_pending and now >= self._continuity_until:
                self._safe(now, 'recognition_continuity_timeout')
            state = ActionEngine.tick(self, now)
            # The block output was already released above. Keep its private
            # dwell only for the same bounded, subsequently validated pair.
            if (not self._continuity_pending and not self._continuity_processing
                    and (self.last_block_valid is None
                         or now-self.last_block_valid >= self.config.safety_timeout_ms*1_000_000)):
                self.block.reset()
                if self.control.block:
                    self.control = replace(state, timestamp_ns=now, block=False)
                    self._log('block_timeout', now)
            return self.control

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
                        if dy <= -getattr(self, 'personal_raise', {}).get(side, .40):
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
            ordered = (ident not in self.retired and (ident != self.identity
                or self.last_capture is None
                or obs.capture_ns > self.last_capture and obs.frame_id > self.last_frame))
            if ordered:
                if self._continuity_pending or self.safety.expired(now):
                    self.tick(now)
                if self._continuity_pending:
                    eligible = (ident == self.identity
                        and 0 < obs.capture_ns-self.last_capture <= self.config.max_gap_ms*1_000_000
                        and 0 <= now-obs.capture_ns <= self.config.evidence_age_ms*1_000_000
                        and not joint_validity(obs, self.config)['BLOCK'])
                    if eligible:
                        self._continuity_pending = False
                        self._continuity_until = None
                        self._continuity_processing = True
                    else:
                        self._safe(now, 'recognition_continuity_rejected')
            try:
                return self._process_observation(obs, evidence, now, special_observation)
            finally:
                self._continuity_processing = False

    def _process_observation(self, obs, evidence=None, now_ns=None, special_observation=None):
        with self.lock:
            now = self.clock() if now_ns is None else now_ns
            ident = (obs.actor_id, obs.capture_epoch)
            if ident in self.retired or (ident == self.identity and self.last_capture is not None
                    and (obs.capture_ns <= self.last_capture or obs.frame_id <= self.last_frame)):
                self.static_special.reset()
                self.static_release.reset()
                return self._process_actions(obs, evidence, now)
            fresh = 0 <= now - obs.capture_ns <= self.config.evidence_age_ms * 1_000_000
            valid = fresh and not joint_validity(obs, self.config)['BLOCK']
            approach, strict, separated = cross_geometry(obs, self.config) if valid else (False, False, False)
            primary_strict = strict
            primary_approach = approach
            special_valid = valid
            extended = extended_cross_arm(obs, self.config) if valid else False
            open_votes = int(separated and not extended)
            model_count = int(valid)
            baseline_strict = strict
            refinement_contributed = False
            refinement_release = False
            needs_refined_release = self.cross_refinement_latched
            baseline_separated = None
            refinement_approach = False
            definite_release = needs_refined_release and fresh and definite_cross_release(obs, self.config)
            # Auxiliary Full landmarks may resolve crossed-arm occlusion which
            # Lite confidently reconstructs as uncrossed. Never use a stale or
            # different frame/person for this channel, and never alter punch,
            # raise or movement coordinates with the auxiliary skeleton.
            auxiliaries = special_observation if isinstance(special_observation, (tuple, list)) else (special_observation,)
            for aux_index, aux in enumerate(auxiliaries):
                if aux_index == 2:
                    baseline_separated = ((open_votes >= 2 or extended) and not approach
                        if model_count >= 2 else separated)
                # A refinement never acquires a new gesture on its own.
                if aux_index >= 2 and not (self.special.owned or self.cross_refinement_latched):
                    continue
                if (self.calibrated and aux is not None
                        and (aux.actor_id, aux.capture_epoch, aux.frame_id, aux.capture_ns)
                        == (obs.actor_id, obs.capture_epoch, obs.frame_id, obs.capture_ns)
                        and (aux.width, aux.height, aux.source_mirrored) == (obs.width, obs.height, obs.source_mirrored)
                        and fresh and same_body(obs, aux, self.config) and not joint_validity(aux, self.config)['BLOCK']):
                    ap, st, sep = cross_geometry(aux, self.config)
                    if aux_index < 2:
                        baseline_strict = baseline_strict or st
                        if needs_refined_release:
                            definite_release = definite_release or definite_cross_release(aux, self.config)
                    else:
                        refinement_approach = refinement_approach or ap
                        refinement_contributed = refinement_contributed or (st and not baseline_strict)
                        refinement_release = refinement_release or (sep and not ap)
                    approach = approach or ap
                    strict = strict or st
                    # Release a cross when either valid model sees a definite open
                    # pose and neither model still supplies actual cross geometry.
                    separated = (separated or sep) and not approach
                    aux_extended = extended_cross_arm(aux, self.config)
                    open_votes += int(sep and not aux_extended)
                    model_count += 1
                    extended = extended and aux_extended
                    special_valid = True
            if len(auxiliaries) >= 2 and model_count >= 2:
                separated = (open_votes >= 2 or extended) and not approach
            # A refinement supplies positive X evidence, never an extra vote
            # to release the original control lifecycle earlier than before.
            if baseline_separated is not None:
                separated = baseline_separated and not refinement_approach
            # During an owned X, one briefly visible skeleton must not convert
            # occlusion jitter into a punch. Multi-model extension clears at
            # once; lone-model extension must persist for 120 ms. No extra
            # SPECIAL evidence is inferred from this suppression interval.
            if (len(auxiliaries) >= 2 and self.special.owned and not self.special.latched
                    and extended and model_count < 2):
                if self.cross_extension_since is None:self.cross_extension_since = obs.capture_ns
                extended = obs.capture_ns - self.cross_extension_since >= 120_000_000
                separated = separated and extended
            else:self.cross_extension_since = None
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
                    shoulder_id, elbow_id = (11, 13) if side == 'LEFT' else (12, 14)
                    self.attack.units[side].upper_arm_projection = (float(np.linalg.norm(points[elbow_id,:2]-points[shoulder_id,:2])/span) if span>1e-6 else None)
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
                    # A bent forearm lifted above its own shoulder is a raise,
                    # even when monocular depth spuriously straightens the elbow.
                    # Only arbitrate an uncommitted, predominantly upward stroke;
                    # never relabel an already extended punch during its return.
                    shoulder, elbow, wrist = (11, 13, 15) if side == 'LEFT' else (12, 14, 16)
                    folded_upward = (not self.attack.units[side].unreturned_punch
                        and d[1] <= -.40 and abs(d[0]) <= .35
                        and abs(d[1]) > abs(d[0]) * 1.2
                        and arm['angle3d'] is not None and arm['angle3d'] < 2.6
                        and arm['angle'] <= 1.6 and span > 1e-6
                        # A camera-directed upper arm is foreshortened; keep
                        # that forward route unless the elbow visibly lifts
                        # out to the side as well as up.
                        and abs(points[elbow,0] - points[shoulder,0]) / span >= .40
                        and (points[elbow,1] - points[shoulder,1]) / span <= .20
                        and (points[elbow,1] - points[wrist,1]) / span >= .50)
                    if folded_upward:
                        self.forward_sides.discard(side)
                        self.forward_proof.pop(side, None)
                        self.compact_forward_proof.pop(side, None)
                    # Personal speed helps a new punch begin, but must not let
                    # slow depth jitter steal an already observed upward stroke.
                    # Ordinary slow punches and baseline-speed takeovers retain
                    # their existing routing; no timing/geometry bound is lowered.
                    if (self.vertical_state == 'PENDING'
                            and self.vertical_candidate == side + '_RAISE'
                            and self.attack.units[side].cfg.onset_speed < self.config.onset_speed
                            and arm['speed_xy'] < self.config.onset_speed
                            and not self.attack.units[side].unreturned_punch):
                        self.forward_sides.discard(side)
                        self.forward_proof.pop(side, None)
                        self.compact_forward_proof.pop(side, None)
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
            self.refinement_repeat.observe(obs.capture_ns,
                valid=special_valid and self.calibrated and state.tracking == 'VALID',
                release=not approach and (refinement_release or definite_release))
            fired = self.special.step(obs.capture_ns, calibrated=self.calibrated, valid=special_valid,
                neutral=separated, approach=approach, strict=strict)
            if self.special.last_strict is None:
                self.cross_refinement_used = False
            if refinement_contributed and self.special.last_strict == obs.capture_ns:
                self.cross_refinement_used = True
            if fired and self.refinement_repeat.locked:
                # Suppress repeats regardless of which model now sees the same X.
                fired = False
            elif fired and self.cross_refinement_used:
                self.refinement_repeat.locked = True
                self.refinement_repeat.invalidate()
            if fired or not self.special.latched:
                self.static_latched = False
                self.static_release.reset()
            stationary_fired = self.static_special.observe(obs, now, calibrated=self.calibrated,
                primary_strict=primary_strict, valid=valid and state.tracking == 'VALID',
                latched=self.special.latched or self.cross_refinement_latched,
                primary_only=special_observation is None)
            if stationary_fired:
                # New current proof, not restoration of the expired normal
                # candidate. One shared latch; only this slow-source path may
                # use the paired fresh bilateral release proof below.
                self.special.owned = self.special.latched = True
                self.special.armed = False
                self.special.reset_dwell()
                self.static_latched = True
                self.static_release.reset()
                fired = True
            elif self.static_release.observe(obs, now,
                    eligible=(self.static_latched and self.special.latched and self.calibrated
                        and special_observation is None and self.static_special.slow_source),
                    valid=valid and state.tracking == 'VALID', approach=primary_approach, strict=primary_strict):
                # Release is fresh bilateral evidence, not a queued attack.
                # Normal/Heavy X latches never enter this extra release path.
                self.special.owned = self.special.latched = False
                self.special.armed = True
                self.special.return_since = None
                self.special.reset_dwell()
                self.static_latched = False
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
                    {'gesture':'SPECIAL','method':'stationary_cross_dwell' if stationary_fired else 'chest_cross_dwell',
                     'hold_ms':700}, self.config.version)
                self.outbox.offer(event)
                self._log('event', now, event=asdict(event))
            self.control = state
            return state
