"""Coordinate-driven FSMs, independent watchdog and pre-UDP event outbox.

No camera, UDP or model fitting. Rule thresholds are experimental configuration.
"""
from collections import deque
from dataclasses import asdict
from threading import RLock, Event, Thread
from types import SimpleNamespace
import time
import uuid
import numpy as np
from movement import ShoulderMovementDetector
from vision_v2.preprocess import features, Config
from .types import EngineConfig, PoseObservation, ActionEvidence, ActionEvent, ControlState


def ns(ms):return ms*1_000_000

class TrackingSafetyController:
    def __init__(self,config):self.config=config;self.last_valid_ns=None
    def observe(self,stamp,valid):
        if valid:self.last_valid_ns=stamp
    def expired(self,now):return self.last_valid_ns is None or now-self.last_valid_ns>=ns(self.config.safety_timeout_ms)


class CoordinateSignals:
    """Reuse frozen V2 relative geometry, masks and true-dt derivative."""
    def __init__(self,cfg):self.cfg=cfg;self.previous=None;self.last_angles=None
    def reset(self):self.previous=None;self.last_angles=None
    def update(self,obs):
        current=obs.as_frame()
        if self.previous is None:self.previous=current;return None
        prev=self.previous;self.previous=current
        dt=(obs.capture_ns-prev['capture_return_monotonic_ns'])/1e9
        if not 0<dt<=self.cfg.max_gap_ms/1000:self.last_angles=None;return None
        x,m,_=features([prev,current],Config(visibility_threshold=self.cfg.visibility,max_gap_seconds=self.cfg.max_gap_ms/1000))
        if not m[1,:14].all() or not m[1,16:28].all():return None
        arms={}
        for side,j,a in [('LEFT',6,12),('RIGHT',9,13)]:
            arms[side]={'position':x[1,j:j+3].astype(float),'velocity':x[1,j+16:j+19].astype(float),
                        'speed_xy':float(np.linalg.norm(x[1,j+16:j+18])),
                        'angle':float(x[1,a]),'angle_velocity':float((x[1,a]-x[0,a])/dt)}
        return arms


class AttackStateMachine:
    def __init__(self,cfg):
        self.cfg=cfg;self.state='PREPARING';self.baseline=None;self.neutral_since=None;self.last_commit=None;self.entered=0;self.side=None
    def invalidate(self,now):
        self.state='UNAVAILABLE';self.neutral_since=None;self.entered=now
        # Preserve baseline within actor/epoch: an extended pose cannot become new neutral after a loss.
    def neutral(self,arms):
        if any(a['speed_xy']>self.cfg.neutral_speed or a['angle']>self.cfg.neutral_angle_max_rad for a in arms.values()):return False
        if self.baseline is None:
            return all(np.linalg.norm(a['position'][:2])<=self.cfg.neutral_wrist_radius for a in arms.values())
        return all(np.linalg.norm(a['position'][:2]-self.baseline[s]['position'][:2])<=self.cfg.neutral_radius for s,a in arms.items())
    def candidates(self,arms):
        rows={}
        for side,a in arms.items():
            delta=a['position']-(self.baseline[side]['position'] if self.baseline else a['position'])
            distance=float(np.linalg.norm(delta[:2]));speed=a['speed_xy']
            outward=float(np.dot(delta[:2],a['velocity'][:2]))>0
            vertical=abs(float(delta[1]))/max(distance,1e-9)
            extension=a['angle']-(self.baseline[side]['angle'] if self.baseline else a['angle'])
            displacement_ok=distance>=self.cfg.onset_displacement
            direction_ok=outward and vertical<=self.cfg.vertical_fraction_max
            extension_ok=extension>=self.cfg.extension_change_rad
            rows[side]={'baseline_valid':self.baseline is not None,'xy_displacement':distance if self.baseline else None,'xy_speed':speed,'relative_z_delta':float(delta[2]) if self.baseline else None,
                        'image_z_rate':float(a['velocity'][2]),'vertical_fraction':vertical if self.baseline else None,'outward':outward,
                        'elbow_angle':a['angle'],'extension_delta_rad':extension if self.baseline else None,'elbow_angle_rate':a['angle_velocity'],
                        'displacement_signal':displacement_ok,'speed_signal':speed>=self.cfg.onset_speed,
                        'direction_signal':direction_ok,'extension_signal':extension_ok,
                        'onset_rule':bool(self.baseline is not None and displacement_ok and speed>=self.cfg.onset_speed and direction_ok and extension_ok),
                        'commit_rule':bool(self.baseline is not None and distance>=self.cfg.commit_displacement and direction_ok and extension_ok and a['angle']>=self.cfg.commit_angle_rad)}
        return rows
    def update(self,arms,now,allow_commit=True,block=False):
        c=self.cfg;old=self.state;reason='';commit=None
        neutral=self.neutral(arms);signals=self.candidates(arms)
        if self.state=='UNAVAILABLE':self.state='PREPARING'
        if self.state in ('PREPARING','RETURNING'):
            if neutral and not block:
                if self.neutral_since is None:self.neutral_since=now
                if now-self.neutral_since>=ns(c.neutral_dwell_ms) and (self.last_commit is None or now-self.last_commit>=ns(c.cooldown_ms)):
                    if self.baseline is None:self.baseline={s:{'position':a['position'].copy(),'angle':a['angle']} for s,a in arms.items()}
                    self.state='READY';reason='neutral_dwell_and_cooldown'
            else:self.neutral_since=None
        elif self.state=='READY':
            choices=[s for s,r in signals.items() if r['onset_rule']]
            if choices and not block:
                self.side=max(choices,key=lambda s:signals[s]['xy_displacement']);self.state='CANDIDATE';reason='coordinate_onset'
        elif self.state=='CANDIDATE':
            if block or neutral or now-self.entered>ns(c.candidate_timeout_ms):
                self.state='PREPARING';self.neutral_since=None;reason='candidate_cancelled'
            elif signals[self.side]['commit_rule'] and allow_commit:
                self.state='ACTIVE';self.last_commit=now;commit=self.side;reason='coordinate_commit'
        elif self.state in ('ACTIVE','EXTENDED'):
            if neutral or (signals[self.side]['xy_displacement']>c.neutral_radius and not signals[self.side]['outward'] and arms[self.side]['speed_xy']>c.return_speed):
                self.state='RETURNING';self.neutral_since=None;reason='return_observed'
            elif self.state=='ACTIVE' and arms[self.side]['speed_xy']<c.neutral_speed:
                self.state='EXTENDED';reason='extension_hold_observed'
            elif self.last_commit is not None and now-self.last_commit>ns(c.active_timeout_ms):
                self.invalidate(now);reason='active_timeout_requires_neutral'
        if old!=self.state:self.entered=now
        return commit,signals,reason


class BlockStateMachine:
    def __init__(self,cfg):self.cfg=cfg;self.state='OFF';self.since=None
    @property
    def held(self):return self.state in ('HELD','EXITING')
    def reset(self):self.state='OFF';self.since=None
    def update(self,arms,now):
        c=self.cfg
        candidate=all(a['position'][1]<=c.block_wrist_y_max and a['angle']<c.neutral_angle_max_rad for a in arms.values())
        if self.state=='OFF' and candidate:self.state='ENTERING';self.since=now
        elif self.state=='ENTERING':
            if not candidate:self.reset()
            elif now-self.since>=ns(c.block_enter_ms):self.state='HELD'
        elif self.state=='HELD' and not candidate:self.state='EXITING';self.since=now
        elif self.state=='EXITING':
            if candidate:self.state='HELD'
            elif now-self.since>=ns(c.block_exit_ms):self.reset()
        return candidate


class MovementStateMachine:
    def __init__(self,cfg):
        self.cfg=cfg;self.detector=ShoulderMovementDetector(visibility_threshold=cfg.visibility,enter_threshold=cfg.movement_enter_threshold,exit_threshold=cfg.movement_exit_threshold,
            stable_frame_count=1,mirror=False,max_strength_delta=cfg.movement_max_strength,calibration_seconds=cfg.movement_calibration_ms/1000,calibration_min_samples=cfg.movement_calibration_min_samples)
        self.state='NEUTRAL';self.candidate='NEUTRAL';self.since=None;self.strength=0.
    def reset(self):self.detector.reset();self.state=self.candidate='NEUTRAL';self.since=None;self.strength=0.
    def update(self,obs,now):
        frame=obs.as_frame();lm=[SimpleNamespace(x=p[0],y=p[1],z=p[2],visibility=v) for p,v in zip(frame['image_landmarks'],obs.visibility)]
        r=self.detector.update(lm,now=obs.capture_ns/1e9)
        if r.final_movement!=self.candidate:self.candidate=r.final_movement;self.since=now
        if self.since is not None and now-self.since>=ns(self.cfg.movement_dwell_ms):self.state=self.candidate
        self.strength=r.movement_strength if self.state!='NEUTRAL' else 0.


class EventOutbox:
    """At-most-once local dispatch; caller handles future UDP retries with same ID."""
    def __init__(self,capacity=128):self.capacity=capacity;self.pending={};self.dispatched={};self.last_now=-1
    def clear(self):self.pending.clear();self.dispatched.clear()
    def offer(self,event):
        if event.expires_ns<=self.last_now:return False
        if event.event_id in self.pending or event.event_id in self.dispatched:return False
        if len(self.pending)>=self.capacity:raise OverflowError('Event outbox full')
        self.pending[event.event_id]=event;return True
    def drain(self,now,identity):
        self.last_now=max(self.last_now,now)
        results=[]
        self.dispatched={key:expiry for key,expiry in self.dispatched.items() if expiry>now}
        for key,event in list(self.pending.items()):
            if now>=event.expires_ns or (event.actor_id,event.capture_epoch)!=identity:self.pending.pop(key);continue
            results.append(event);self.dispatched[key]=event.expires_ns;self.pending.pop(key)
        return results


class ActionEngine:
    def __init__(self,config=EngineConfig(),clock=time.monotonic_ns):
        self.config=config;self.clock=clock;self.lock=RLock();self.run_id=uuid.uuid4().hex;self.sequence=0
        self.identity=None;self.last_frame=None;self.last_capture=None;self.last_now=None;self.retired=set()
        self.logs=deque(maxlen=10000);self.log_dropped=0;self.outbox=EventOutbox();self.control=ControlState()
        self._components()
    def _components(self):
        self.attack=AttackStateMachine(self.config);self.block=BlockStateMachine(self.config);self.movement=MovementStateMachine(self.config)
        self.safety=TrackingSafetyController(self.config);self.signals=CoordinateSignals(self.config)
    def _log(self,kind,now,**details):
        if len(self.logs)==self.logs.maxlen:self.log_dropped+=1
        self.logs.append({'type':kind,'timestamp_ns':now,'identity':self.identity,'config_version':self.config.version,**details})
    def take_logs(self):
        with self.lock:r=list(self.logs);self.logs.clear();return r
    def _time(self,now):
        if self.last_now is not None and now<self.last_now:raise ValueError('Processing monotonic time reversed')
        self.last_now=now
    def _safe(self,now,reason):
        self.attack.invalidate(now);self.block.reset();self.movement.reset();self.signals.reset();self.outbox.pending.clear()
        self.control=ControlState(*(self.identity or ('','')),now,'LOST',False,'NEUTRAL',0.,self.attack.state)
        self._log('safety',now,reason=reason,state=asdict(self.control))
    def tick(self,now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns;self._time(now)
            if self.safety.expired(now) and self.control.tracking!='LOST':self._safe(now,'observation_timeout')
            return self.control
    def process(self,obs,evidence=None,now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns;self._time(now);ident=(obs.actor_id,obs.capture_epoch)
            if ident in self.retired:
                self._log('rejected',now,reason='retired_actor_epoch',frame_id=obs.frame_id);return self.control
            if self.identity!=ident:
                if self.identity is not None:self.retired.add(self.identity)
                self.identity=ident;self._components();self.outbox.clear();self.last_frame=self.last_capture=None
                self._log('identity_reset',now,reason='new_actor_or_epoch')
            if self.last_capture is not None and (obs.capture_ns<=self.last_capture or obs.frame_id<=self.last_frame):
                self._log('rejected',now,reason='duplicate_or_out_of_order_frame',frame_id=obs.frame_id)
                return self.tick(now)
            gap=self.last_capture is not None and obs.capture_ns-self.last_capture>ns(self.config.max_gap_ms)
            self.last_capture,self.last_frame=obs.capture_ns,obs.frame_id
            valid=self._valid(obs) and 0<=now-obs.capture_ns<=ns(self.config.evidence_age_ms)
            if gap:self._safe(now,'capture_gap')
            evidence_valid=evidence is not None and evidence.valid_for(obs,now,self.config.evidence_age_ms)
            if evidence is not None and not evidence_valid:self._log('evidence_rejected',now,reason='stale_or_identity_or_probability',model_id=evidence.model_id)
            if not valid:
                self.attack.invalidate(now);self.signals.reset();self.outbox.pending.clear()
                if self.safety.expired(now):self._safe(now,'invalid_pose_timeout')
                else:self.control=ControlState(*ident,now,'INVALID',self.block.held,self.movement.state,self.movement.strength,self.attack.state)
                self._log('observation',now,frame_id=obs.frame_id,capture_ns=obs.capture_ns,valid=False,reason='invalid_required_joint_or_stale_capture',state=asdict(self.control));return self.control
            old=(self.attack.state,self.block.state,self.movement.state)
            self.safety.observe(obs.capture_ns,True);arms=self.signals.update(obs)
            self.movement.update(obs,now)
            if arms is not None:
                block_candidate=self.block.update(arms,now)
                attack_probability=sum(v for k,v in evidence.probabilities.items() if k in ('ATTACK','LEFT_PUNCH','RIGHT_PUNCH')) if evidence_valid else None
                allow=not self.config.model_gate or (evidence_valid and attack_probability>=self.config.model_attack_probability)
                side,signals,reason=self.attack.update(arms,now,allow_commit=allow,block=self.block.held or block_candidate)
                self._log('signals',now,frame_id=obs.frame_id,capture_ns=obs.capture_ns,valid=True,arms=signals,model_valid=evidence_valid,
                          model_id=evidence.model_id if evidence else None,probabilities=evidence.probabilities if evidence_valid else None,
                          model_gate=self.config.model_gate,allow_commit=bool(allow),block_candidate=block_candidate)
                if side:
                    self.sequence+=1;event=ActionEvent(f'{self.run_id}:{self.sequence}',*ident,obs.frame_id,obs.capture_ns,now,now+ns(self.config.event_ttl_ms),'ATTACK',side,
                        {'coordinate_rule':signals[side],'model_gate':self.config.model_gate,'model_id':evidence.model_id if evidence_valid else None,'attack_probability':attack_probability},self.config.version)
                    self.outbox.offer(event);self._log('event',now,event=asdict(event))
                if old!=(self.attack.state,self.block.state,self.movement.state):self._log('transition',now,before=old,after=(self.attack.state,self.block.state,self.movement.state),reason=reason)
            self.control=ControlState(*ident,now,'VALID',self.block.held,self.movement.state,self.movement.strength,self.attack.state)
            self._log('control',now,frame_id=obs.frame_id,state=asdict(self.control));return self.control
    def _valid(self,obs):
        if not obs.pose_present or obs.width<=0 or obs.height<=0:return False
        try:
            xyz=np.asarray(obs.landmarks,dtype=float);vis=np.asarray(obs.visibility,dtype=float)
            if xyz.shape!=(33,3) or vis.shape!=(33,):return False
            ids=[11,12,13,14,15,16];p=xyz[ids]
            return bool(np.isfinite(p).all() and np.isfinite(vis[ids]).all() and (vis[ids]>=self.config.visibility).all()
                        and ((p[:,:2]>=0)&(p[:,:2]<=1)).all() and np.linalg.norm((xyz[11]-xyz[12])[:2])>1e-6)
        except (TypeError,ValueError):return False
    def drain_events(self,now_ns=None):
        with self.lock:
            now=self.clock() if now_ns is None else now_ns;self.tick(now)
            return self.outbox.drain(now,self.identity)


class Watchdog:
    """Independent thread: no camera callback needed to expire states."""
    def __init__(self,engine,interval_seconds=.02):
        if interval_seconds<=0:raise ValueError('Positive watchdog interval required')
        self.engine=engine;self.interval=interval_seconds;self.stop=Event();self.thread=None;self.error=None
    def __enter__(self):
        def loop():
            try:
                while not self.stop.wait(self.interval):self.engine.tick()
            except Exception as exc:self.error=exc
        self.thread=Thread(target=loop,name='action-safety-watchdog',daemon=True);self.thread.start();return self
    def __exit__(self,*args):
        self.stop.set();self.thread.join(timeout=1)
        if self.error is not None:raise RuntimeError('Watchdog failed') from self.error
