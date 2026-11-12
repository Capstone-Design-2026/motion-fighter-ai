"""Session-local, bounded tutorial calibration. The prompt alone is never a label.

Two consistent complete motions can tune travel thresholds, not pose quality,
cross ownership, event lifetime, cooldown, damage or the physical return gates.
Profiles are diagnostic JSON; they are never silently loaded for the next person.
"""
from dataclasses import replace
import math
import numpy as np
from action_engine.experimental import ChannelSignals, joint_validity
from .six_engine import SixGestureEngine

ACTIONS = ('JAB', 'STRAIGHT', 'HIGH_KICK', 'LOW_KICK', 'BLOCK',
           'MOVE_LEFT', 'MOVE_RIGHT', 'SPECIAL')


class PersonalTrainer:
    def __init__(self):
        self.context = None
        self.identity = None
        self.samples = {}
        self.cycle = None
        self.rest_since = None
        self.armed = False
        self.last_ns = None
        self.status = 'BASELINE V26'

    def reset_motion(self):
        self.cycle = None
        self.rest_since = None
        self.armed = False

    def set_context(self, context, now):
        # Context expires with ACK freshness; keyboard demonstrations cannot train.
        valid = (isinstance(context, dict) and context.get('action') in ACTIONS
                 and context.get('phase') in ('PRACTICE', 'RETURN')
                 and isinstance(context.get('run'), str) and 0 < len(context['run']) <= 100
                 and 0 <= now - context.get('sent_ns', -10**20) <= 500_000_000)
        new = (context['run'], context['action']) if valid else None
        old = (self.context['run'], self.context['action']) if self.context else None
        if new != old: self.reset_motion()
        self.context = dict(context) if valid else None

    def observe(self, row, now):
        """row is already body-normalized and independently geometry-validated."""
        if (self.last_ns is not None and not 0 < now - self.last_ns <= 100_000_000):
            self.reset_motion()
        self.last_ns = now
        if (not self.context or now - self.context['sent_ns'] > 500_000_000
                or not row or not row['valid']):
            self.reset_motion(); return None
        action = self.context['action']
        if row['rest']:
            if self.rest_since is None: self.rest_since = now
            if now - self.rest_since < 150_000_000: return None
            self.armed = True
            if self.cycle is not None:
                cycle = self.cycle; self.cycle = None
                duration = (cycle['last'] - cycle['start']) / 1e9
                if cycle['frames'] < 3 or not .06 <= duration <= 4.: return None
                rows = self.samples.setdefault(action, [])
                rows.append(cycle['peak']); del rows[:-6]
                self.status = f'{action}: {len(rows)} CYCLES'
                if len(rows) >= 2:
                    pair = rows[-2:]
                    # No threshold fitting to an outlier or two unlike motions.
                    if min(pair) > 0 and max(pair) / min(pair) <= 1.4:
                        self.status = f'{action}: PERSONAL RANGE READY'
                        return action, min(pair)
            return None
        self.rest_since = None
        if self.cycle is not None and now - self.cycle['start'] > 4_000_000_000:
            self.reset_motion(); return None
        if not row['target']:
            # Transition frames are allowed, but a conflicting gesture cancels.
            if row.get('conflict'): self.reset_motion()
            return None
        if self.cycle is None:
            if not self.armed or self.context['phase'] != 'PRACTICE': return None
            self.cycle = {'start': now, 'last': now, 'frames': 0, 'peak': 0.}
            self.armed = False
        self.cycle['frames'] += 1; self.cycle['last'] = now
        self.cycle['peak'] = max(self.cycle['peak'], float(row['amount']))
        return None


class PersonalGestureEngine(SixGestureEngine):
    """v26 with opt-in tutorial adaptation; no context means identical inference."""
    def _components(self):
        super()._components()
        self.trainer = PersonalTrainer()
        self.personal_signals = ChannelSignals(self.config)
        self.personal_changes = {}
        self.personal_raise = {}
        self.calibration_generation = getattr(self, 'calibration_generation', 0) + 1

    def tutorial_context(self, context, now):
        with self.lock: self.trainer.set_context(context, now)

    def profile(self):
        with self.lock:
            return {'schema': 1, 'baseline': 'v26', 'identity': self.identity,
                    'calibration_generation': self.calibration_generation,
                    'samples': {k:list(v) for k,v in self.trainer.samples.items()}, 'changes': dict(self.personal_changes),
                    'status': self.trainer.status}

    def _measurement(self, obs, state, arms):
        target = self.trainer.context['action'] if self.trainer.context else None
        anchors = self.attack.anchors()
        if (not self.calibrated or state.tracking != 'VALID' or set(arms) != {'LEFT','RIGHT'}
                or set(anchors) != {'LEFT','RIGHT'} or joint_validity(obs,self.config)['BLOCK']): return None
        d = {s:a['position']-anchors[s]['position'] for s,a in arms.items()}
        guard = all(self.attack.units[s].guard_neutral({s:a}) for s,a in arms.items())
        rest = guard and state.movement == 'NEUTRAL' and not self.special.owned and not state.block
        result = dict(valid=True,rest=rest,target=False,amount=0.,conflict=False)
        if target in ('JAB','STRAIGHT'):
            side = 'LEFT' if target == 'JAB' else 'RIGHT'; other = 'RIGHT' if side == 'LEFT' else 'LEFT'
            a=arms[side]; dx,dy,dz=d[side]; travel=float(np.linalg.norm(d[side][:2]))
            extension = a['angle3d'] is not None and a['angle3d'] >= 2.3 and a['angle3d']-anchors[side]['angle3d'] >= .25
            result.update(target=bool(extension and travel>=.27 and abs(dy)<=max(.12,abs(dx)*.65)
                and dy<=.12 and not self.special.owned and not state.block
                and self.attack.units[other].supporting_guard(arms[other],anchors[other])), amount=travel,
                conflict=bool(self.special.owned or state.block or dy<-.35))
        elif target in ('HIGH_KICK','LOW_KICK'):
            side='LEFT' if target=='HIGH_KICK' else 'RIGHT';other='RIGHT' if side=='LEFT' else 'LEFT'
            dx,dy,_=d[side]
            result.update(target=bool(dy<=-.34 and abs(dx)<=.30 and -dy>abs(dx)*1.5
                and d[other][1]>-.20 and not self.special.owned and not state.block
                and side not in self.forward_sides),amount=float(-dy),
                conflict=bool(self.special.owned or state.block or side in self.forward_sides))
        elif target=='BLOCK':
            amount=min(float(-d[s][1]) for s in arms)
            result.update(target=bool(amount>=.28 and not self.special.owned
                and all(a['angle']<1.5 and abs(d[s][0])<.35 for s,a in arms.items())),amount=amount,
                conflict=bool(self.special.owned))
        elif target in ('MOVE_LEFT','MOVE_RIGHT') and self.movement.origin is not None:
            p=np.asarray(obs.as_frame()['image_landmarks']);sign=1 if target=='MOVE_LEFT' else -1
            offset=((p[11,0]+p[12,0])/2-self.movement.origin)/self.movement.width*self.movement.left_sign*sign
            result.update(target=bool(offset>=.17 and guard),amount=float(offset),rest=bool(guard and abs(offset)<.08),conflict=bool(offset<-.15))
        elif target=='SPECIAL':
            # Measure successful v26 X holds only. Do not weaken cross geometry.
            result.update(target=bool(self.special.latched),amount=1.,conflict=False)
        return result

    def _apply_personal(self, action, amount):
        if not math.isfinite(amount): return
        # Only tune complete repeated cycles at rest. All safety checks stay v26.
        if action in ('JAB','STRAIGHT'):
            side='LEFT' if action=='JAB' else 'RIGHT'; unit=self.attack.units[side]
            value=float(np.clip(amount*.88,.28,.32))
            unit.cfg=replace(unit.cfg,commit_displacement=value)
        elif action in ('HIGH_KICK','LOW_KICK'):
            side='LEFT' if action=='HIGH_KICK' else 'RIGHT'
            value=float(np.clip(amount*.90,.36,.40));self.personal_raise[side]=value
        elif action=='BLOCK':
            value=float(np.clip(amount*.90,.29,.32));self.block.cfg=replace(self.block.cfg,commit_displacement=value)
        elif action in ('MOVE_LEFT','MOVE_RIGHT'):
            value=float(np.clip(amount*.90,.18,.22))
            self.movement.personal_enter[action.removeprefix('MOVE_')]=value
        else: value='v26_cross_confirmed'
        self.personal_changes[action]=value

    def process(self, obs, evidence=None, now_ns=None, special_observation=None):
        with self.lock:
            previous=self.last_capture
            state=super().process(obs,evidence,now_ns,special_observation)
            if previous is not None and obs.capture_ns<=previous: return state
            now=self.clock() if now_ns is None else now_ns
            if not 0<=now-obs.capture_ns<=self.config.evidence_age_ms*1_000_000: return state
            if self.trainer.context is None:
                self.personal_signals.reset();return state
            arms=self.personal_signals.update(obs)
            result=self.trainer.observe(self._measurement(obs,state,arms),obs.capture_ns)
            if result:
                self._apply_personal(*result)
                self._log('personal_range',now,action=result[0],profile=self.profile())
            return state
