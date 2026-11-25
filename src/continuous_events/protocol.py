"""Manual sequence boundaries, independent of the legacy Trial schedule."""
import time
from pathlib import Path
from vision_v2.preprocess import sha256

VERSION = 'continuous-events-v1'


def verify_pose_asset(model, variant):
    """Prevent an accidental Lite asset being reported as Full (and vice versa)."""
    root = Path(__file__).resolve().parents[2]
    reference = root / ('pose_landmarker.task' if variant == 'Lite' else
        'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task')
    if not reference.is_file() or not model.is_file() or sha256(model) != sha256(reference):
        raise ValueError('Model SHA does not match the existing verified ' + variant + ' asset')
# Prompts are intentions, not performed action labels or ground truth.
SEQUENCES = (
    'GUARD -> LEFT_PUNCH -> HOLD -> RETURN -> GUARD',
    'GUARD -> RIGHT_PUNCH -> HOLD -> RETURN -> GUARD',
    'GUARD -> LEFT_PUNCH x3, full RETURN between attacks',
    'GUARD -> RIGHT_PUNCH x3, full RETURN between attacks',
    'GUARD -> LEFT/RIGHT alternating x3, full RETURN',
    'GUARD -> BLOCK onset -> HELD -> RELEASE -> GUARD',
    'GUARD -> BLOCK twice, release between holds',
    'GUARD -> LEFT_LOWER -> GUARD -> RIGHT_RAISE -> GUARD',
    'Stationary GUARD, then relaxed NONE',
    'NONE with natural small hand/body movements',
    'Game MOVE_LEFT / MOVE_RIGHT; then movement + punch/block',
    'Partial occlusion, brief tracking loss, approach frame edge; recover GUARD',
)


class SequenceController:
    def __init__(self, recorder):
        self.recorder = recorder
        self.preflight_confirmed = False
        self.active = None
        self.history = []

    def confirm_preflight(self, frame_id, operator_ns=None):
        if self.active:
            raise ValueError('Cannot confirm preflight during sequence')
        self.recorder.record_annotation(label='PREFLIGHT_CONFIRMATION', annotation_type='marker',
            event_frame_id=frame_id, source='human_capture_control',
            notes={'protocol': VERSION, 'operator_monotonic_ns': operator_ns or time.monotonic_ns(),
                   'is_action_ground_truth': False})
        self.preflight_confirmed = True

    def start(self, frame_id, operator_ns=None):
        if not self.preflight_confirmed or self.active:
            raise ValueError('Human preflight confirmation and idle state required')
        index = len(self.history)
        self.active = {'trial_id': f'sequence-{index+1:04d}', 'start_frame_id': frame_id,
                       'intended_prompt': SEQUENCES[index % len(SEQUENCES)],
                       'start_operator_ns': operator_ns or time.monotonic_ns()}
        self.recorder.record_annotation(label='SEQUENCE_START', annotation_type='marker',
            event_frame_id=frame_id, source='human_capture_control',
            notes={**self.active, 'protocol': VERSION, 'is_action_ground_truth': False})

    def stop(self, frame_id, *, completed=True, operator_ns=None):
        if self.active is None:
            raise ValueError('No sequence is recording')
        row = {**self.active, 'end_frame_id': frame_id,
               'end_operator_ns': operator_ns or time.monotonic_ns(),
               'status': 'COMPLETE' if completed else 'INTERRUPTED'}
        self.recorder.record_annotation(label='UNLABELED_CONTINUOUS', annotation_type='interval',
            start_frame_id=row['start_frame_id'], end_frame_id=frame_id,
            source='human_capture_control', notes={**row, 'protocol': VERSION,
                'event': 'sequence_boundary', 'is_action_ground_truth': False})
        self.history.append(row)
        self.active = None
        return row
