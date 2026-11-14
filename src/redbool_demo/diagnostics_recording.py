"""Packaged-play diagnostics without RGB; full-rate observations live in the async landmark journal."""
import threading
from session_recording import SessionRecorder


class DiagnosticsRecorder(SessionRecorder):
    """Use the exact normal observation conversion, omitting only disk storage."""
    def __init__(self, *args, **kwargs):
        config = dict(kwargs.get('recording_config', {}))
        config.update(observation_storage='external_compressed_journal', stored_pose_frames=0,
                      observation_index='../../recognition/landmarks.index.json',
                      scope='no RGB; full-rate observations and engine operations in indexed gzip chunks; replay requires complete coverage')
        kwargs['recording_config'] = config
        super().__init__(*args, **kwargs)
        (self.directory/'frames.jsonl').unlink()  # Newly created empty file only.
        self._log_names.remove('frames.jsonl')

    def _append_record(self, name, record):
        if name != 'frames.jsonl':
            super()._append_record(name, record)


class DiagnosticLog:
    """Keep events/transitions, sample stable STATE/ACK at10Hz, omit pose rows."""
    def __init__(self, target):
        self.target = target
        self.last = {}
        self.times = {}
        self.lock = threading.Lock()

    @property
    def error(self):return self.target.error

    @property
    def dropped(self):return self.target.dropped

    def write(self, row):
        kind = row.get('type')
        if kind == 'frame':
            return
        key = None
        stamp = 0
        always = True
        if kind == 'send' and row['packet'].get('kind') == 'STATE':
            data = row['packet']
            stamp = data.get('sent_ns', 0)
            key = tuple(data.get(k) for k in ('calibration_generation', 'calibrated',
                'tracking', 'fsm_state', 'block_active', 'movement', 'personal_status'))
            always = False
        elif kind == 'ack' and not row['ack'].get('event_id'):
            data = row['ack']
            stamp = row.get('ack_receive_ns', 0)
            key = tuple(data.get(k) for k in ('status', 'tutorial_run', 'tutorial_action',
                'tutorial_phase', 'tutorial_hits', 'game_can_act', 'battle_preparing',
                'game_preparation_locked'))
            always = False
        elif kind == 'engine':
            if row.get('log', {}).get('type') not in ('event', 'personal_range',
                    'demo_calibrated', 'calibration_feedback', 'identity_reset', 'safety'):
                return
        with self.lock:
            if always or key != self.last.get(kind) or stamp-self.times.get(kind, 0) >= 100_000_000:
                self.last[kind] = key
                self.times[kind] = stamp
                self.target.write(row)

    def close(self):self.target.close()
