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
DEFAULT_RANGES = dict(JAB=.32,STRAIGHT=.32,HIGH_KICK=.40,LOW_KICK=.40,
                     BLOCK=.32,MOVE_LEFT=.22,MOVE_RIGHT=.22,JAB_SPEED=.8,STRAIGHT_SPEED=.8,
                     HIGH_KICK_DWELL_MS=180.,LOW_KICK_DWELL_MS=180.)


class PersonalTrainer:
    def __init__(self):
        self.context = None
        self.pending_context = None
        self.drain_until = None
        self.identity = None
        self.samples = {}
        self.cycle_details = {}
        self.cycle = None
        self.rest_since = None
        self.last_rest_ns = None
        self.shared_rest = False
        self.armed = False
        self.last_ns = None
        self.status = 'BASELINE V26'

    def reset_motion(self):
        self.cycle = None
        self.rest_since = None
        self.last_rest_ns = None
        self.shared_rest = False
        self.armed = False

    def set_context(self, context, now):
        # Context expires with ACK freshness; keyboard demonstrations cannot train.
        valid = (isinstance(context, dict) and context.get('action') in ACTIONS
                 and context.get('phase') in ('PRACTICE', 'RETURN')
                 and isinstance(context.get('run'), str) and 0 < len(context['run']) <= 100
                 and 0 <= now - context.get('sent_ns', -10**20) <= 500_000_000)
        fresh_complete = (isinstance(context, dict) and context.get('phase') == 'COMPLETE'
            and self.context and context.get('run') == self.context['run']
            and 0 <= now-context.get('sent_ns', -10**20) <= 500_000_000)
        new = (context['run'], context['action']) if valid else None
        old = (self.context['run'], self.context['action']) if self.context else None
        # The UI may advance immediately after the second accepted event. Finish
        # its already-started measured return before switching targets. Never
        # invent a cycle from a prompt or carry one across recalibration/run IDs.
        same_run = self.context and (valid or fresh_complete) and context['run'] == self.context['run']
        if new != old and same_run and self.cycle is not None:
            if self.drain_until is None: self.drain_until = now + 2_000_000_000
            if now < self.drain_until:
                self.pending_context = dict(context) if valid else None
                self.context = {**self.context, 'phase': 'RETURN', 'sent_ns': context['sent_ns']}
                return
        self.drain_until = None; self.pending_context = None
        if new != old:
            # A prompt change must not erase the guard that was just measured
            # while completing the previous step. Carry only common guarded
            # rest from the same run, never a target hold or an X-only release.
            carry = (same_run and valid and context['phase'] == 'PRACTICE'
                and self.shared_rest and self.last_rest_ns is not None
                and 0 <= now-self.last_rest_ns <= 100_000_000)
            rest = (self.rest_since,self.last_rest_ns,self.armed)
            self.reset_motion()
            if carry:
                self.rest_since,self.last_rest_ns,self.armed = rest
                self.shared_rest = True
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
            self.last_rest_ns = now
            self.shared_rest = bool(row.get('shared_rest'))
            if self.rest_since is None: self.rest_since = now
            if now - self.rest_since < 150_000_000: return None
            self.armed = True
            if self.cycle is not None:
                cycle = self.cycle; self.cycle = None
                duration = (cycle['last'] - cycle['start']) / 1e9
                if cycle['frames'] < 3 or cycle['targets'] < 2 or not .06 <= duration <= 4.: return None
                rows = self.samples.setdefault(action, [])
                rows.append(cycle['peak']); del rows[:-6]
                details=self.cycle_details.setdefault(action,[])
                details.append({'peak':cycle['peak'],'speed':cycle['speed'],'duration':duration,
                                'confirmed':bool(cycle.get('confirmed'))})
                del details[:-6]
                self.status = f'{action}: {len(rows)} CYCLES'
                if len(rows) >= 2:
                    pair = rows[-2:]
                    # No threshold fitting to an outlier or two unlike motions.
                    if min(pair) > 0 and max(pair) / min(pair) <= 1.4:
                        self.status = f'{action}: PERSONAL RANGE READY'
                        return action, min(pair)
            return None
        self.rest_since = None
        self.last_rest_ns = None
        self.shared_rest = False
        if self.cycle is not None and now - self.cycle['start'] > 4_000_000_000:
            self.reset_motion(); return None
        # The game acknowledges a punch before its measured peak. Preserve a
        # geometry-proven onset from PRACTICE when that ACK changes to RETURN.
        # A prompt or a RETURN-only pose still cannot start a training cycle.
        if (self.cycle is None and self.context['phase'] == 'PRACTICE'
                and row.get('verified_commit') and row['target'] and not row.get('conflict')):
            # A same-frame v26 event already proves its normal physical rearm.
            # This only starts measurement; independent peak geometry, return
            # and two consistent complete cycles are still mandatory below.
            self.cycle = {'start': now, 'last': now, 'frames': 0, 'targets':0, 'peak': 0.,'speed':0.}
            self.armed = False
        if (self.cycle is None and self.armed and self.context['phase'] == 'PRACTICE'
                and row.get('onset') and not row.get('conflict')):
            self.cycle = {'start': now, 'last': now, 'frames': 0, 'targets':0, 'peak': 0.,'speed':0.}
            self.armed = False
        if not row['target']:
            # Transition frames are allowed, but a conflicting gesture cancels.
            if row.get('conflict'): self.reset_motion()
            elif self.cycle is not None and row.get('onset'):
                self.cycle['frames']+=1;self.cycle['last']=now
            return None
        if self.cycle is None:
            if not self.armed or self.context['phase'] != 'PRACTICE': return None
            self.cycle = {'start': now, 'last': now, 'frames': 0, 'targets':0, 'peak': 0.,'speed':0.}
            self.armed = False
        self.cycle['frames'] += 1; self.cycle['last'] = now
        self.cycle['confirmed'] = self.cycle.get('confirmed',False) or bool(row.get('verified_commit'))
        self.cycle['targets'] += 1
        self.cycle['peak'] = max(self.cycle['peak'], float(row['amount']))
        speed=float(row.get('speed',0))
        if math.isfinite(speed):self.cycle['speed']=max(self.cycle['speed'],speed)
        return None


class PersonalGestureEngine(SixGestureEngine):
    """v26 with opt-in tutorial adaptation; no context means identical inference."""
    def _components(self):
        super()._components()
        self.trainer = PersonalTrainer()
        self.personal_signals = ChannelSignals(self.config)
        self.personal_changes = {}
        self.personal_raise = {}
        self.personal_fast_raise = set()
        self.personal_punch_proof = {}
        self.calibration_generation = getattr(self, 'calibration_generation', 0) + 1

    def tutorial_context(self, context, now):
        with self.lock: self.trainer.set_context(context, now)

    def profile(self):
        with self.lock:
            return {'schema': 1, 'baseline': 'v26', 'engine_version': self.config.version, 'identity': self.identity,
                    'calibration_generation': self.calibration_generation,
                    'samples': {k:list(v) for k,v in self.trainer.samples.items()}, 'changes': dict(self.personal_changes),
                    'cycle_details': {k:list(v) for k,v in self.trainer.cycle_details.items()},
                    'effective_overrides': {k:v for k,v in self.personal_changes.items()
                        if k in DEFAULT_RANGES and abs(v-DEFAULT_RANGES[k]) > 1e-6},
                    'status': self.personal_status}

    @property
    def personal_status(self):
        with self.lock:
            applied=[k for k,v in self.personal_changes.items()
                     if k in DEFAULT_RANGES and abs(v-DEFAULT_RANGES[k])>1e-6]
            # A later X confirmation does not undo an already fitted punch.
            return ('PERSONAL RANGE APPLIED: '+','.join(applied))[:100] if applied else self.trainer.status

    def _measurement(self, obs, state, arms):
        target = self.trainer.context['action'] if self.trainer.context else None
        gesture = {'JAB':'LEFT_PUNCH','STRAIGHT':'RIGHT_PUNCH',
                   'HIGH_KICK':'LEFT_RAISE','LOW_KICK':'RIGHT_RAISE','SPECIAL':'SPECIAL'}.get(target)
        committed = next((ev for ev in self.outbox.pending.values()
            if ev.source_frame_id == obs.frame_id
            and (ev.actor_id,ev.capture_epoch) == self.identity
            and ev.reason.get('gesture',ev.side+'_PUNCH') == gesture), None)
        anchors = self.attack.anchors()
        if (not self.calibrated or state.tracking != 'VALID' or set(arms) != {'LEFT','RIGHT'}
                or set(anchors) != {'LEFT','RIGHT'} or joint_validity(obs,self.config)['BLOCK']):
            self.personal_punch_proof.clear()
            return None
        d = {s:a['position']-anchors[s]['position'] for s,a in arms.items()}
        guard = all(self.attack.units[s].guard_neutral({s:a}) for s,a in arms.items())
        rest = guard and state.movement == 'NEUTRAL' and not self.special.owned and not state.block
        result = dict(valid=True,rest=rest,shared_rest=rest,target=False,amount=0.,conflict=False)
        if target in ('JAB','STRAIGHT'):
            side = 'LEFT' if target == 'JAB' else 'RIGHT'; other = 'RIGHT' if side == 'LEFT' else 'LEFT'
            a=arms[side]; dx,dy,dz=d[side]; travel=float(np.linalg.norm(d[side][:2]))
            extension = (a['angle3d'] is not None and a['angle3d'] >= 2.3
                and a['angle3d']-anchors[side]['angle3d'] >= self.attack.units[side].cfg.extension_change_rad)
            result.update(target=bool(extension and travel>=.27 and abs(dy)<=max(.12,abs(dx)*.65)
                and dy<=.12 and not self.special.owned and not state.block
                and self.attack.units[other].supporting_guard(arms[other],anchors[other])), amount=travel,
                # A diagonal punch can briefly rise above this height before its
                # strict horizontal target appears. Cancel only predominantly
                # upward motion, using the existing raise-channel ratio.
                conflict=bool(self.special.owned or state.block
                    or (dy<-.35 and -dy>abs(dx)*1.2)))
            result['speed']=float(a['speed_xy'])
            result['onset'] = bool(travel>=.18 and abs(dy)<=max(.12,abs(dx)*.65) and dy<=.12
                and a['angle3d'] is not None and a['angle3d']-anchors[side]['angle3d']>=.10
                and self.attack.units[other].supporting_guard(arms[other],anchors[other]))
            self._measure_recognized_punch(obs,state,arms,anchors,side,committed,result)
        elif target in ('HIGH_KICK','LOW_KICK'):
            side='LEFT' if target=='HIGH_KICK' else 'RIGHT';other='RIGHT' if side=='LEFT' else 'LEFT'
            dx,dy,_=d[side]
            result.update(target=bool(dy<=-.34 and abs(dx)<=.35 and -dy>abs(dx)*1.5
                and d[other][1]>-.30 and not self.special.owned and not state.block
                and side not in self.forward_sides),amount=float(-dy),
                conflict=bool(self.special.owned or state.block or side in self.forward_sides))
            # The live raise re-arms each hand separately. A completed teaching
            # stroke can likewise return while the other hand keeps its guard.
            result['rest']=bool(dy>-.20 and abs(dx)<=.35 and arms[side]['speed_xy']<=1.2
                and arms[side]['angle']<=1.8 and arms[side]['position'][1]<=.45
                and not self.special.owned and not state.block)
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
            result.update(target=bool(self.special.latched),amount=1.,conflict=False,
                rest=bool(not self.special.owned and not self.special.latched
                    and not self.cross_refinement_latched and not state.block))
        result['verified_commit'] = committed is not None
        return result

    def _measure_recognized_punch(self,obs,state,arms,anchors,side,event,row):
        """Measure the same independently proved stroke that advanced practice.

        Recognition may prove a low/planar/forward punch outside the original
        horizontal training envelope. A prompt alone never opens this route.
        Each subsequent target still needs visible extension; one event frame
        or an unreturned hold cannot train, and calibration resets every proof.
        """
        stamp=getattr(obs,'capture_ns',None)
        if stamp is None:return
        if self.trainer.last_ns is not None and not 0<stamp-self.trainer.last_ns<=100_000_000:
            self.personal_punch_proof.clear()
        context=self.trainer.context or {}
        key=(context.get('run'),context.get('action'))
        rule=event.reason.get('coordinate_rule',{}) if event is not None else {}
        if event is not None and rule.get('commit_rule') and not state.block and not self.special.owned:
            self.personal_punch_proof[side]={'stamp':stamp,'key':key,
                'position':arms[side]['position'].copy(),
                'forward':bool(rule.get('forward_extension') or rule.get('centered_depth_confirmation'))}
        proof=self.personal_punch_proof.get(side)
        if not proof:return
        if (proof['key']!=key or not 0<=stamp-proof['stamp']<=2_000_000_000
                or state.block or self.special.owned):
            self.personal_punch_proof.pop(side,None);return
        arm=arms[side];base=anchors[side];dx,dy,_=arm['position']-base['position']
        extension3d=(arm['angle3d'] is not None and arm['angle3d']>=2.2
            and arm['angle3d']-base['angle3d']>=.15)
        extension2d=arm['angle']>=2.2 and arm['angle']-base['angle']>=.7
        shape=(np.linalg.norm([dx,dy])>=.27 and (extension3d or extension2d)
            and np.linalg.norm(arm['position'][:2]-proof['position'][:2])<=.45
            and (abs(dy)<=max(.12,abs(dx)*1.0) and dy<=.9
                 or proof['forward'] and -1.3<=dy<=.12))
        # Return this hand, independently of the other hand's pose. The physical
        # return envelope is the live recognizer's, not an invented neutral pose.
        returned=(abs(dx)<=.45 and -.30<=dy<=.65 and arm['position'][1]<=.45
            and arm['speed_xy']<=1.2 and arm['angle']<=1.8
            and arm['angle3d'] is not None and arm['angle3d']<=min(2.6,base['angle3d']+.60))
        if shape:
            row.update(target=True,rest=False,conflict=False,recognized_path=True)
        elif returned:
            row.update(rest=True,target=False,conflict=False)

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
            pair=self.trainer.cycle_details.get(action,[])[-2:]
            if len(pair)==2 and all(r.get('confirmed') and r['peak']>=.65 for r in pair):
                self.personal_fast_raise.add(side)
                self.personal_changes[action+'_DWELL_MS']=120.
        elif action=='BLOCK':
            value=float(np.clip(amount*.90,.29,.32));self.block.cfg=replace(self.block.cfg,commit_displacement=value)
        elif action in ('MOVE_LEFT','MOVE_RIGHT'):
            value=float(np.clip(amount*.90,.18,.22))
            self.movement.personal_enter[action.removeprefix('MOVE_')]=value
        else: value='v26_cross_confirmed'
        self.personal_changes[action]=value
        defaults=dict(JAB=.32,STRAIGHT=.32,HIGH_KICK=.40,LOW_KICK=.40,BLOCK=.32,MOVE_LEFT=.22,MOVE_RIGHT=.22)
        self.trainer.status = (f'{action}: PERSONAL RANGE APPLIED'
            if action in defaults and abs(value-defaults[action])>1e-6 else f'{action}: BASELINE CONFIRMED')

    def _apply_punch_speed(self, action):
        if action not in ('JAB','STRAIGHT'):return
        pair=self.trainer.cycle_details.get(action,[])[-2:]
        if len(pair)!=2:return
        speeds=[r['speed'] for r in pair]
        # Two complete, independently validated extensions. Do not fit a held
        # pose, noise, an outlier or speed that the camera never observed.
        if min(speeds)<.45 or max(speeds)/min(speeds)>1.4:return
        value=float(np.clip(min(speeds)*.90,.65,.80))
        side='LEFT' if action=='JAB' else 'RIGHT';unit=self.attack.units[side]
        unit.cfg=replace(unit.cfg,onset_speed=value)
        self.personal_changes[action+'_SPEED']=value
        if value<.8:self.trainer.status=f'{action}: PERSONAL RANGE APPLIED'

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
            previous_cycle=self.trainer.cycle
            result=self.trainer.observe(self._measurement(obs,state,arms),obs.capture_ns)
            if previous_cycle is not None and self.trainer.cycle is None:
                self.personal_punch_proof.clear()
            if result:
                self._apply_personal(*result)
                self._apply_punch_speed(result[0])
                self._log('personal_range',now,action=result[0],profile=self.profile())
            return state
