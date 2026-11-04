"""Immutable event-engine contracts; timestamps are host monotonic nanoseconds."""
from dataclasses import dataclass, field
import math

@dataclass(frozen=True)
class EngineConfig:
    version: str = 'event-engine-0.1-frozen-20261024'
    visibility: float = .5
    max_gap_ms: int = 100
    evidence_age_ms: int = 120
    safety_timeout_ms: int = 150
    neutral_dwell_ms: int = 150
    cooldown_ms: int = 250
    candidate_timeout_ms: int = 400
    active_timeout_ms: int = 2000
    event_ttl_ms: int = 150
    onset_displacement: float = .18
    commit_displacement: float = .32
    onset_speed: float = .8
    extension_change_rad: float = .15
    commit_angle_rad: float = 2.3
    neutral_angle_max_rad: float = 2.4
    neutral_radius: float = .18
    neutral_speed: float = .8
    neutral_wrist_radius: float = 1.5
    vertical_fraction_max: float = .8
    block_wrist_y_max: float = .15
    block_enter_ms: int = 120
    block_exit_ms: int = 120
    movement_dwell_ms: int = 120
    movement_calibration_ms: int = 500
    movement_enter_threshold: float = .30
    movement_exit_threshold: float = .15
    movement_max_strength: float = .75
    movement_calibration_min_samples: int = 10
    return_speed: float = .2
    model_gate: bool = False
    model_attack_probability: float = .7

    def __post_init__(self):
        for k,v in vars(self).items():
            if isinstance(v,(int,float)) and not isinstance(v,bool) and (not math.isfinite(v) or v<0):raise ValueError('Invalid configuration: '+k)
        if not 0<=self.visibility<=1 or not 0<=self.model_attack_probability<=1:raise ValueError('Invalid probability threshold')
        if self.max_gap_ms<=0 or self.safety_timeout_ms<=0:raise ValueError('Positive timeouts required')
        if self.movement_exit_threshold>self.movement_enter_threshold:raise ValueError('Movement hysteresis reversed')

@dataclass(frozen=True)
class PoseObservation:
    actor_id: str
    capture_epoch: str
    frame_id: int
    capture_ns: int
    landmarks: tuple | None
    visibility: tuple | None
    width: int
    height: int
    pose_present: bool = True
    source_mirrored: bool = False

    def as_frame(self):
        # Anatomical indices are preserved. Only reflected image x is canonicalized.
        points=None if self.landmarks is None else [[1-p[0] if self.source_mirrored else p[0],p[1],p[2]] for p in self.landmarks]
        return {'frame_id':self.frame_id,'capture_return_monotonic_ns':self.capture_ns,
                'pose_success':self.pose_present,'pose_validity':{'pose_present':self.pose_present},
                'image_landmarks':points,'landmark_visibility':{'image':self.visibility},'_height_over_width':self.height/self.width}

    @classmethod
    def from_record(cls,frame,manifest,epoch=None):
        points=frame.get('image_landmarks');vis=(frame.get('landmark_visibility') or {}).get('image')
        return cls(manifest['subject_id'],epoch or manifest['session_id'],frame['frame_id'],frame['capture_return_monotonic_ns'],
                   tuple(tuple(p) for p in points) if points else None,tuple(vis) if vis else None,*manifest['actual_resolution'],
                   bool(frame.get('pose_success') and frame.get('pose_validity',{}).get('pose_present')),
                   manifest['recording_config'].get('source_mirrored',False))

@dataclass(frozen=True)
class ActionEvidence:
    actor_id: str
    capture_epoch: str
    source_frame_id: int
    source_capture_ns: int
    produced_ns: int
    model_id: str
    probabilities: dict = field(default_factory=dict)

    def valid_for(self,obs,now,age_ms):
        p=list(self.probabilities.values())
        return (self.actor_id==obs.actor_id and self.capture_epoch==obs.capture_epoch
                and self.source_frame_id<=obs.frame_id and self.source_capture_ns<=obs.capture_ns
                and 0<=now-self.source_capture_ns<=age_ms*1_000_000
                and self.source_capture_ns<=self.produced_ns<=now and bool(p)
                and all(math.isfinite(v) and 0<=v<=1 for v in p) and abs(sum(p)-1)<1e-4)

@dataclass(frozen=True)
class ActionEvent:
    event_id: str
    actor_id: str
    capture_epoch: str
    source_frame_id: int
    source_capture_ns: int
    committed_ns: int
    expires_ns: int
    kind: str
    side: str
    reason: dict
    config_version: str

@dataclass(frozen=True)
class ControlState:
    actor_id: str = ''
    capture_epoch: str = ''
    timestamp_ns: int = 0
    tracking: str = 'UNAVAILABLE'
    block: bool = False
    movement: str = 'NEUTRAL'
    movement_strength: float = 0.
    attack_state: str = 'PREPARING'
