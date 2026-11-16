"""Task 4.3 opt-in ablations. Frozen Task 4.2 engine remains unchanged.

Reuses its FSM lifecycle, clock guards, watchdog, movement and event outbox.
Image-3D angles are geometric hypotheses, never metric ground truth.
"""
from dataclasses import dataclass, asdict
from collections import deque
import threading
import numpy as np
from vision_v2.preprocess import features, Config
from .core import (ActionEngine, AttackStateMachine, BlockStateMachine,
                   MovementStateMachine, TrackingSafetyController, ns)
from .types import EngineConfig, ControlState, ActionEvent, PoseObservation


@dataclass(frozen=True)
class AblationConfig(EngineConfig):
    version: str = 'event-engine-0.2-development'
    channel_validity: bool = False
    calibrated_neutral_block: bool = False
    image_3d_extension: bool = False


def _immutable_observation(obs):
    return (type(obs) is PoseObservation and type(obs.landmarks) is tuple and type(obs.visibility) is tuple
        and all(type(p) is tuple for p in obs.landmarks))


_validity_cache = threading.local()


def joint_validity(obs, cfg):
    # A caller can append its own stale-time reason. Return fresh lists even
    # when the coordinate/visibility checks came from this same observation.
    cacheable = _immutable_observation(obs)
    entries = getattr(_validity_cache, 'entries', ())
    if cacheable:
        for prior, visibility, value in entries:
            if prior is obs and visibility == cfg.visibility:
                return {key: list(reasons) for key, reasons in value.items()}
    result = _joint_validity(obs, cfg)
    if cacheable:
        _validity_cache.entries = ((obs, cfg.visibility,
            {key: tuple(reasons) for key, reasons in result.items()}),) + entries[:3]
    return result


def _joint_validity(obs, cfg):
    reasons = {}
    p = np.asarray(obs.landmarks, dtype=float)
    v = np.asarray(obs.visibility, dtype=float)
    if not obs.pose_present or p.shape != (33, 3) or v.shape != (33,) or obs.width <= 0 or obs.height <= 0:
        return {k: ['missing_pose_or_schema'] for k in ('LEFT','RIGHT','BLOCK','MOVEMENT')}
    for name, ids in [('LEFT',[11,12,13,15]),('RIGHT',[11,12,14,16]),('BLOCK',[11,12,13,14,15,16]),('MOVEMENT',[11,12])]:
        r=[]
        for j in ids:
            if not np.isfinite(p[j]).all():r.append(f'joint_{j}_nonfinite')
            elif ((p[j,:2]<0)|(p[j,:2]>1)).any():r.append(f'joint_{j}_outside')
            if not np.isfinite(v[j]) or v[j]<cfg.visibility:r.append(f'joint_{j}_visibility')
        if np.linalg.norm((p[11]-p[12])[:2])<=1e-6:r.append('shoulder_width')
        reasons[name]=r
    return reasons


def elbow_angle(points, ids):
    p=np.asarray(points,dtype=float)
    if p.shape!=(33,3) or not np.isfinite(p[list(ids)]).all():return None
    a,b,c=p[list(ids)];u=a-b;v=c-b;den=np.linalg.norm(u)*np.linalg.norm(v)
    if den<=1e-12:return None
    return float(np.arccos(np.clip(np.dot(u,v)/den,-1,1)))


class ChannelSignals:
    # The punch, raise and routing channels often examine the exact same two
    # immutable observations. Reuse only that pure feature calculation, never a
    # channel's history, masks, FSM or a result from another frame/configuration.
    _feature_cache = threading.local()

    @classmethod
    def _features(cls, previous, observation, cfg):
        key = (cfg.visibility, cfg.max_gap_ms)
        cacheable = all(_immutable_observation(o) for o in (previous, observation))
        cached = getattr(cls._feature_cache, 'last', None)
        if (cacheable and cached is not None and cached[0] is previous
                and cached[1] is observation and cached[2] == key):
            return cached[3]
        frames = [previous.as_frame(), observation.as_frame()]
        x, mask, _ = features(frames, Config(visibility_threshold=cfg.visibility,
            max_gap_seconds=cfg.max_gap_ms / 1000))
        # All clients only read these arrays and create their own arm values.
        x.setflags(write=False)
        mask.setflags(write=False)
        value = (frames, x, mask)
        cls._feature_cache.last = (previous, observation, key, value) if cacheable else None
        return value

    def __init__(self,cfg):self.cfg=cfg;self.previous=None;self.last_masks={}
    def reset(self):self.previous=None;self.last_masks={}
    def update(self,obs):
        prev=self.previous;self.previous=obs;self.last_masks={}
        if prev is None:return {}
        dt=(obs.capture_ns-prev.capture_ns)/1e9
        if not 0<dt<=self.cfg.max_gap_ms/1000:return {}
        if not obs.pose_present or obs.landmarks is None or prev.landmarks is None:return {}
        f,x,m=self._features(prev,obs,self.cfg)
        pv=joint_validity(prev,self.cfg);cv=joint_validity(obs,self.cfg)
        arms={};p=np.asarray(f[1]['image_landmarks'],float);p=p.copy();p[:,1]*=obs.height/obs.width
        for side,j,a,ids in [('LEFT',6,12,(11,13,15)),('RIGHT',9,13,(12,14,16))]:
            # Only own wrist relative xyz, own elbow angle, own wrist velocity;
            # both endpoints valid, including own elbow for angle derivative.
            mask_ok=bool(m[1,j:j+3].all() and m[:,a].all() and m[1,j+16:j+19].all())
            self.last_masks[side]=mask_ok
            gate=not cv[side] and not pv[side]
            if not self.cfg.channel_validity:
                gate=not cv['BLOCK'] and not pv['BLOCK']
                mask_ok=bool(m[1,:14].all() and m[1,16:28].all())
            if gate and mask_ok:
                arms[side]={'position':x[1,j:j+3].astype(float),'velocity':x[1,j+16:j+19].astype(float),
                            'speed_xy':float(np.linalg.norm(x[1,j+16:j+18])), 'angle':float(x[1,a]),
                            'angle_velocity':float((x[1,a]-x[0,a])/dt),'angle3d':elbow_angle(p,ids)}
        return arms


class DevelopmentAttack(AttackStateMachine):
    def __init__(self,cfg):super().__init__(cfg);self.history=deque();self.now=0
    def invalidate(self,now):super().invalidate(now);self.history.clear()
    def neutral(self,arms):
        if not arms:return False
        if not self.cfg.calibrated_neutral_block:return super().neutral(arms)
        # Acquire only a sustained compact bent pose. Speed spikes do not replace
        # an actual timestamp-spanning stability window; no forced READY.
        if any(a['angle']>self.cfg.neutral_angle_max_rad for a in arms.values()):return False
        if self.baseline is not None:
            return all(np.linalg.norm(a['position'][:2]-self.baseline[s]['position'][:2])<=self.cfg.neutral_radius for s,a in arms.items())
        if any(np.linalg.norm(a['position'][:2])>self.cfg.neutral_wrist_radius for a in arms.values()):return False
        return all(max(np.linalg.norm(a['position'][:2]-past[s][:2]) for _,past in self.history)<=self.cfg.neutral_radius for s,a in arms.items())
    def candidates(self,arms):
        out=super().candidates(arms)
        if not self.cfg.image_3d_extension:return out
        for side,a in arms.items():
            r=out[side];base=self.baseline[side].get('angle3d') if self.baseline else None
            delta=a['angle3d']-base if base is not None and a['angle3d'] is not None else None
            r['image_3d_angle']=a['angle3d'];r['image_3d_extension_delta']=delta
            ext=delta is not None and delta>=self.cfg.extension_change_rad
            r['extension_signal']=ext
            r['onset_rule']=bool(self.baseline is not None and r['displacement_signal'] and r['speed_signal'] and r['direction_signal'] and ext)
            r['commit_rule']=bool(self.baseline is not None and r['xy_displacement']>=self.cfg.commit_displacement and r['direction_signal'] and ext and a['angle3d'] is not None and a['angle3d']>=self.cfg.commit_angle_rad)
        return out
    def update(self,arms,now,allow_commit=True,block=False):
        self.now=now
        while self.history and now-self.history[0][0]>ns(self.cfg.neutral_dwell_ms):self.history.popleft()
        self.history.append((now,{s:a['position'].copy() for s,a in arms.items()}))
        absent=self.baseline is None
        result=super().update(arms,now,allow_commit,block)
        if absent and self.baseline is not None:
            for s in self.baseline:self.baseline[s]['angle3d']=arms[s]['angle3d']
        return result


class AttackGroup:
    def __init__(self,cfg):
        self.cfg=cfg;self.units={s:DevelopmentAttack(cfg) for s in (('LEFT','RIGHT') if cfg.channel_validity else ('BOTH',))}
    @property
    def state(self):
        order=['ACTIVE','EXTENDED','CANDIDATE','RETURNING','READY','PREPARING','UNAVAILABLE']
        return min((u.state for u in self.units.values()),key=order.index)
    def invalidate(self,now):
        for u in self.units.values():u.invalidate(now)
    def anchors(self):
        out={}
        for u in self.units.values():
            if u.baseline:out.update(u.baseline)
        return out


class RelativeBlock(BlockStateMachine):
    """Require bilateral upward displacement from acquired neutral, then dwell.

    Static guard at startup is UNKNOWN/OFF, not asserted BLOCK.
    """
    def update_relative(self,arms,anchors,now):
        if set(arms)!={'LEFT','RIGHT'} or set(anchors)!={'LEFT','RIGHT'}:
            self.reset();return False
        candidate=all(a['position'][1]<=self.cfg.block_wrist_y_max and a['angle']<self.cfg.neutral_angle_max_rad
                      and anchors[s]['position'][1]-a['position'][1]>=self.cfg.commit_displacement for s,a in arms.items())
        if self.state=='OFF' and candidate:self.state='ENTERING';self.since=now
        elif self.state=='ENTERING':
            if not candidate:self.reset()
            elif now-self.since>=ns(self.cfg.block_enter_ms):self.state='HELD'
        elif self.state=='HELD' and not candidate:self.state='EXITING';self.since=now
        elif self.state=='EXITING':
            if candidate:self.state='HELD'
            elif now-self.since>=ns(self.cfg.block_exit_ms):self.reset()
        return candidate


class DevelopmentEngine(ActionEngine):
    def _components(self):
        self.attack=AttackGroup(self.config);self.block=RelativeBlock(self.config)
        self.movement=MovementStateMachine(self.config);self.safety=TrackingSafetyController(self.config)
        self.signals=ChannelSignals(self.config);self.last_block_valid=None
    def tick(self,now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns
            state=super().tick(now)
            if self.last_block_valid is None or now-self.last_block_valid>=ns(self.config.safety_timeout_ms):
                self.block.reset()
                if self.control.block:
                    self.control=ControlState(state.actor_id,state.capture_epoch,now,state.tracking,False,state.movement,state.movement_strength,state.attack_state)
                    self._log('block_timeout',now)
            return self.control
    def process(self,obs,evidence=None,now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns;self.tick(now);ident=(obs.actor_id,obs.capture_epoch)
            if ident in self.retired:
                self._log('rejected',now,reason='retired_actor_epoch');return self.control
            if ident!=self.identity:
                if self.identity is not None:self.retired.add(self.identity)
                self.identity=ident;self._components();self.outbox.clear();self.last_capture=self.last_frame=None
                self._log('identity_reset',now)
            if self.last_capture is not None and (obs.capture_ns<=self.last_capture or obs.frame_id<=self.last_frame):
                self._log('rejected',now,reason='duplicate_or_out_of_order_frame');return self.control
            if self.last_capture is not None and obs.capture_ns-self.last_capture>ns(self.config.max_gap_ms):self._safe(now,'capture_gap')
            self.last_capture,self.last_frame=obs.capture_ns,obs.frame_id
            validity=joint_validity(obs,self.config)
            if not 0<=now-obs.capture_ns<=ns(self.config.evidence_age_ms):
                for r in validity.values():r.append('stale_capture')
            common='MOVEMENT' if self.config.channel_validity else 'BLOCK'
            if not validity[common]:
                self.safety.observe(obs.capture_ns,True);self.movement.update(obs,now)
            arms=self.signals.update(obs) if not validity[common] else {}
            if validity[common]:self.signals.reset()
            if not validity['BLOCK']:self.last_block_valid=obs.capture_ns
            block_candidate=False
            if set(arms)=={'LEFT','RIGHT'}:
                block_candidate=self.block.update_relative(arms,self.attack.anchors(),now) if self.config.calibrated_neutral_block else self.block.update(arms,now)
            evidence_valid=evidence is not None and evidence.valid_for(obs,now,self.config.evidence_age_ms)
            if evidence is not None and not evidence_valid:self._log('evidence_rejected',now,reason='stale_or_identity_or_probability')
            for name,unit in self.attack.units.items():
                selected=arms if name=='BOTH' else ({name:arms[name]} if name in arms else {})
                required={'LEFT','RIGHT'} if name=='BOTH' else {name}
                old=unit.state
                if set(selected)!=required:
                    unit.invalidate(now)
                    self.outbox.pending={k:e for k,e in self.outbox.pending.items() if name!='BOTH' and e.side!=name}
                    self._log('channel_invalid',now,channel=name,reasons=validity['BLOCK' if name=='BOTH' else name] or ['derivative_pair_or_mask'])
                    continue
                keys=('ATTACK','LEFT_PUNCH','RIGHT_PUNCH') if name=='BOTH' else ('ATTACK',name+'_PUNCH')
                probability=sum(evidence.probabilities.get(k,0.) for k in keys) if evidence_valid else None
                allow=not self.config.model_gate or (evidence_valid and probability>=self.config.model_attack_probability)
                neutral=unit.neutral(selected) if unit.history else False
                side,signals,reason=unit.update(selected,now,allow,self.block.held or block_candidate)
                self._log('signals',now,channel=name,frame_id=obs.frame_id,arms=signals,neutral=neutral,block_candidate=block_candidate,
                          state_before=old,state_after=unit.state,baseline_valid=unit.baseline is not None,validity=validity)
                if old!=unit.state:self._log('transition',now,channel=name,before=old,after=unit.state,reason=reason)
                if side:
                    self.sequence+=1
                    event=ActionEvent(f'{self.run_id}:{self.sequence}',*ident,obs.frame_id,obs.capture_ns,now,now+ns(self.config.event_ttl_ms),'ATTACK',side,
                                      {'coordinate_rule':signals[side],'model_gate':self.config.model_gate,'model_id':evidence.model_id if evidence_valid else None,'attack_probability':probability},self.config.version)
                    self.outbox.offer(event);self._log('event',now,event=asdict(event))
            tracking='VALID' if not validity[common] else ('LOST' if self.safety.expired(now) else 'INVALID')
            self.control=ControlState(*ident,now,tracking,self.block.held,self.movement.state,self.movement.strength,self.attack.state)
            self._log('control',now,frame_id=obs.frame_id,state=asdict(self.control),channel_states={k:u.state for k,u in self.attack.units.items()},validity=validity)
            return self.control
