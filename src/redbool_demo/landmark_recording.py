"""Bounded, lossless-observation diagnostics. No pixels, camera or network I/O.

Only the worker serializes/compresses/writes. Each published gzip is complete;
.part files are never replay inputs. A gap invalidates exact replay, not gameplay.
"""
from dataclasses import asdict, is_dataclass
import gzip
import hashlib
import io
import json
import math
import os
from pathlib import Path
import queue
import re
import threading
import time

SCHEMA = 'redbool.landmarks'
VERSION = 1
INDEX = 'landmarks.index.json'
MAX_BYTES = 128 * 1024 * 1024
MAX_CHUNK_BYTES = 8 * 1024 * 1024


def _json_default(value):
    if is_dataclass(value):
        return asdict(value)
    raise TypeError('Unsupported diagnostic value: ' + type(value).__name__)


def _compact_row(row):
    if row.get('kind') != 'process' or 'observation' not in row:
        return row
    frame = row['frame']; obs = row['observation']
    meta = frame.get('processing_metadata') or {}
    # Raw model values override sanitized model payloads only. Capture identity
    # belongs to the original frame and is absent from live's raw_models dict.
    models = row.get('raw_models') or meta
    result = {k:v for k,v in row.items() if k not in ('frame', 'observation', 'raw_models')}
    # Positional coordinate arrays eliminate unused per-frame baseline fields;
    # no rounding or omission of valid/invalid/lost observations occurs.
    result['observation'] = [obs.actor_id,obs.capture_epoch,obs.frame_id,obs.capture_ns,
                             obs.width,obs.height,obs.pose_present,obs.source_mirrored]
    result['frame'] = dict(camera_frame_id=meta.get('camera_frame_id'),
        capture_attempt_id=frame.get('capture_attempt_id'),
        capture_success=frame.get('capture_success'),pose_success=frame.get('pose_success'),
        pose_validity=frame.get('pose_validity'),
        pose_start_ns=frame.get('pose_start_monotonic_ns'),pose_end_ns=frame.get('pose_end_monotonic_ns'),
        image=frame.get('image_landmarks'),world=frame.get('world_landmarks'),
        visibility=frame.get('landmark_visibility'),presence=frame.get('landmark_presence'),
        full_video=models.get('cross_pose'),full_image=models.get('cross_recovery'))
    return result


def _mark_nonfinite(value):
    if isinstance(value, float) and not math.isfinite(value):
        return {'$float': 'nan' if math.isnan(value) else '+inf' if value > 0 else '-inf'}
    if is_dataclass(value): return _mark_nonfinite(asdict(value))
    if isinstance(value, dict): return {k:_mark_nonfinite(v) for k,v in value.items()}
    if isinstance(value, (list, tuple)): return [_mark_nonfinite(v) for v in value]
    return value


def _restore_nonfinite(value):
    if set(value) == {'$float'}:
        choices={'nan':float('nan'),'+inf':float('inf'),'-inf':-float('inf')}
        if value['$float'] not in choices:raise ValueError('Unknown nonfinite float marker')
        return choices[value['$float']]
    return value


def _encode(value):
    value = _compact_row(value) if isinstance(value, dict) else value
    try:
        payload=json.dumps(value, separators=(',', ':'), allow_nan=False, default=_json_default)
    except ValueError:
        # Rare invalid model outputs stay explicit; ordinary finite rows avoid
        # a second recursive traversal on the compression worker.
        payload=json.dumps(_mark_nonfinite(value), separators=(',', ':'), allow_nan=False)
    return (payload + '\n').encode('utf-8')


class LandmarkJournal:
    """Nonblocking producer, one bounded compression worker, append-only chunks."""
    def __init__(self, directory, *, queue_rows=512, max_bytes=MAX_BYTES,
                 max_chunk_bytes=MAX_CHUNK_BYTES, chunk_frames=60,
                 chunk_rows=512, chunk_seconds=2., max_gap_ranges=512):
        if min(queue_rows, max_bytes, max_chunk_bytes, chunk_frames, chunk_rows) <= 0:
            raise ValueError('Positive recording limits required')
        self.directory = Path(directory)
        self.queue = queue.Queue(queue_rows)
        self.lock = threading.Lock()
        self.stop = threading.Event()
        self.closed = False
        self.close_completed = True
        self.max_bytes = max_bytes
        self.max_chunk_bytes = max_chunk_bytes
        self.chunk_frames = chunk_frames
        self.chunk_rows = chunk_rows
        self.chunk_seconds = chunk_seconds
        self.max_gap_ranges = max_gap_ranges
        self.sequence = 0
        self.processed_frames = 0
        self.queue_dropped_rows = 0
        self.budget_omitted_rows = 0
        self.error_omitted_rows = 0
        self.error = None
        self.budget_exhausted = False
        self.gaps = []
        self.gap_ranges_omitted = 0
        self.chunks = []
        self.written_rows = 0
        self.recorded_frames = 0
        self.total_bytes = 0
        self.active_part = None
        self.worker = threading.Thread(target=self._worker, name='landmark-journal', daemon=True)
        self.worker.start()

    def _gap_locked(self, first, last, reason):
        if self.gaps and self.gaps[-1]['reason'] == reason and self.gaps[-1]['last_seq'] + 1 == first:
            self.gaps[-1]['last_seq'] = last
        elif len(self.gaps) < self.max_gap_ranges:
            self.gaps.append(dict(first_seq=first, last_seq=last, reason=reason))
        else:
            self.gap_ranges_omitted += 1

    def offer(self, kind, **data):
        """Caller owns immutable snapshots until worker consumes them; never waits."""
        with self.lock:
            if self.closed:
                return False
            self.sequence += 1
            seq = self.sequence
            if kind == 'process':
                self.processed_frames += 1
            if self.error or self.budget_exhausted:
                reason = 'writer_error' if self.error else 'budget_exhausted'
                if self.error: self.error_omitted_rows += 1
                else: self.budget_omitted_rows += 1
                self._gap_locked(seq, seq, reason)
                return False
            row = dict(seq=seq, kind=kind, **data)
            try:
                self.queue.put_nowait(row)
                return True
            except queue.Full:
                self.queue_dropped_rows += 1
                self._gap_locked(seq, seq, 'queue_full')
                return False

    def health(self):
        with self.lock:
            return self._health_locked()

    def _health_locked(self):
        missing = self.queue_dropped_rows + self.budget_omitted_rows + self.error_omitted_rows
        state = ('error' if self.error else 'budget_exhausted' if self.budget_exhausted
                 else 'incomplete' if missing or (self.closed and not self.close_completed)
                 else 'complete' if self.closed and not self.worker.is_alive() else 'recording')
        return dict(state=state, queue_dropped_rows=self.queue_dropped_rows,
                    budget_omitted_rows=self.budget_omitted_rows,
                    error_omitted_rows=self.error_omitted_rows, error=self.error,
                    active_part=self.active_part)

    def _index(self, final=False):
        with self.lock:
            health = self._health_locked()
            if final and health['state'] == 'recording': health['state'] = 'complete'
            complete = final and health['state'] == 'complete' and self.close_completed
            return dict(schema=SCHEMA, version=VERSION, compression='gzip',
                float_encoding='json-float64', nonfinite_encoding='$float:nan|+inf|-inf', complete=complete,
                coverage=dict(first_seq=1 if self.sequence else None, last_seq=self.sequence or None,
                    offered_rows=self.sequence, written_rows=self.written_rows,
                    processed_frames=self.processed_frames, recorded_frames=self.recorded_frames,
                    gaps=[dict(g) for g in self.gaps], gap_ranges_omitted=self.gap_ranges_omitted),
                limits=dict(max_compressed_bytes=self.max_bytes, max_chunk_bytes=self.max_chunk_bytes),
                health=health, chunks=[dict(c) for c in self.chunks])

    def _write_index(self, final=False):
        temporary = self.directory / (INDEX + '.tmp')
        with temporary.open('wb') as out:
            out.write(_encode(self._index(final)))
            out.flush()
            os.fsync(out.fileno())
        os.replace(temporary, self.directory / INDEX)
        self._last_index_write = time.monotonic()

    def _omit(self, rows, reason):
        with self.lock:
            for row in rows:
                self._gap_locked(row['seq'], row['seq'], reason)
            if reason == 'budget_exhausted': self.budget_omitted_rows += len(rows)
            else: self.error_omitted_rows += len(rows)

    def _worker(self):
        raw = zipped = None
        rows = []
        nframes = raw_bytes = 0
        chunk_started = time.monotonic()
        part = None
        try:
            self.directory.mkdir(parents=True, exist_ok=False)
            self._write_index()
            while not self.stop.is_set() or not self.queue.empty():
                try: row = self.queue.get(timeout=.05)
                except queue.Empty: row = None
                if row is not None:
                    if self.budget_exhausted:
                        self._omit([row], 'budget_exhausted')
                        continue
                    encoded = _encode(row)
                    # gzip's worst-case overhead stays below this safety margin.
                    raw_limit = max(1, self.max_chunk_bytes - 65536)
                    if len(encoded) > raw_limit:
                        raise ValueError('Single diagnostic row exceeds chunk limit')
                    if rows and raw_bytes + len(encoded) > raw_limit:
                        self._finish_chunk(raw, zipped, part, rows, nframes)
                        raw = zipped = None; rows = []; nframes = raw_bytes = 0
                    if self.budget_exhausted:
                        self._omit([row], 'budget_exhausted')
                        continue
                    if raw is None:
                        name = 'landmarks-%06d.jsonl.gz' % (len(self.chunks) + 1)
                        part = self.directory / (name + '.part')
                        with self.lock: self.active_part = part.name
                        self._write_index()
                        raw = part.open('xb')
                        zipped = gzip.GzipFile(filename='', mode='wb', fileobj=raw, compresslevel=1, mtime=0)
                        chunk_started = time.monotonic()
                    zipped.write(encoded)
                    rows.append(row)
                    nframes += row['kind'] == 'process'
                    raw_bytes += len(encoded)
                if rows and (nframes >= self.chunk_frames or len(rows) >= self.chunk_rows
                             or time.monotonic() - chunk_started >= self.chunk_seconds):
                    self._finish_chunk(raw, zipped, part, rows, nframes)
                    raw = zipped = None; rows = []; nframes = raw_bytes = 0
                if time.monotonic() - self._last_index_write >= self.chunk_seconds:
                    self._write_index()
            if rows:
                self._finish_chunk(raw, zipped, part, rows, nframes)
                raw = zipped = None; rows = []
            self._write_index(final=True)
        except BaseException as exc:
            with self.lock:
                self.error = '%s: %s' % (type(exc).__name__, str(exc)[:500])
            # Only newly-owned partial files may remain. Completed chunks survive.
            if rows: self._omit(rows, 'writer_error')
            if 'row' in locals() and row is not None and row not in rows:
                self._omit([row], 'writer_error')
            while True:
                try: self._omit([self.queue.get_nowait()], 'writer_error')
                except queue.Empty: break
            try: self._write_index(final=True)
            except OSError: pass
        finally:
            if zipped is not None:
                try: zipped.close()
                except OSError: pass
            if raw is not None:
                try: raw.close()
                except OSError: pass

    def _finish_chunk(self, raw, zipped, part, rows, nframes):
        zipped.close()
        raw.flush()
        os.fsync(raw.fileno())
        raw.close()
        size = part.stat().st_size
        if size > self.max_chunk_bytes:
            raise ValueError('Compressed diagnostic chunk exceeds limit')
        if self.total_bytes + size > self.max_bytes:
            with self.lock: self.budget_exhausted = True; self.active_part = None
            self._omit(rows, 'budget_exhausted')
            part.unlink()  # This writer's unpublished partial chunk only.
        else:
            digest = hashlib.sha256(part.read_bytes()).hexdigest()
            destination = part.with_suffix('')
            os.replace(part, destination)
            with self.lock:
                self.chunks.append(dict(file=destination.name, bytes=size, sha256=digest,
                    first_seq=rows[0]['seq'], last_seq=rows[-1]['seq'], rows=len(rows), frames=nframes))
                self.written_rows += len(rows)
                self.recorded_frames += nframes
                self.total_bytes += size
                self.active_part = None
        self._write_index()

    def close(self, *, completed=True, timeout=3.):
        with self.lock:
            self.closed = True
            self.close_completed = completed
        self.stop.set()
        self.worker.join(timeout=max(0., timeout))
        if self.worker.is_alive():
            # Do not wait on disk/inference indefinitely; previous index is incomplete.
            with self.lock:
                self.error = self.error or 'Recorder close timed out; active chunk may be incomplete'
        return self.health()


class ObservedClock:
    """Capture original clock reads without substituting or monkeypatching time."""
    def __init__(self, source):
        self.source = source
        self.local = threading.local()

    def __call__(self):
        value = self.source()
        readings = getattr(self.local, 'readings', None)
        if readings is not None: readings.append(value)
        return value

    def begin(self):
        self.local.readings = []

    def end(self):
        readings = tuple(self.local.readings)
        self.local.readings = None
        return readings


def engine_snapshot(engine):
    """Read existing fields only, under the engine lock; never advance its FSM."""
    return dict(control=engine.control, calibrated=engine.calibrated,
                calibrating=engine.calibrating, calibration_progress=engine.calibration_progress,
                calibration_generation=getattr(engine, 'calibration_generation', 0),
                movement_enabled=engine.movement_enabled,
                personal_changes=dict(getattr(engine, 'personal_changes', {})),
                personal_status=engine.trainer.status if hasattr(engine, 'trainer') else None)


def record_operation(journal, clock, engine, kind, operation, **payload):
    """Linearize only the existing operation and its immutable diagnostic snapshot."""
    with engine.lock:
        clock.begin()
        try:
            result = operation()
        except BaseException:
            clock.end()
            raise
        readings = clock.end()
        journal.offer(kind, clocks=readings, expected=engine_snapshot(engine), **payload,
                      **({'events': tuple(result)} if kind == 'publisher' else {}))
        return result


def iter_journal(directory, *, require_complete=True):
    """Validated offline reader. Missing coverage is never interpolated."""
    directory = Path(directory)
    index_path = directory / INDEX
    if index_path.is_symlink() or index_path.stat().st_size > 2 * 1024 * 1024:
        raise ValueError('Unsafe or oversized landmark index')
    with index_path.open('rb') as stream:
        encoded_index = stream.read(2 * 1024 * 1024 + 1)
    if len(encoded_index) > 2 * 1024 * 1024:
        raise ValueError('Oversized landmark index')
    index = json.loads(encoded_index)
    if (index.get('schema') != SCHEMA or index.get('version') != VERSION
            or index.get('compression') != 'gzip' or index.get('float_encoding') != 'json-float64'):
        raise ValueError('Unsupported landmark index')
    coverage = index['coverage']; health = index['health']
    if require_complete and not index.get('complete'):
        raise ValueError('Incomplete landmark coverage; exact replay refused')
    if index.get('complete') and (coverage.get('gaps') or coverage.get('gap_ranges_omitted')
            or health.get('state') != 'complete' or health.get('error') or health.get('active_part')
            or any(health.get(k,0) for k in ('queue_dropped_rows','budget_omitted_rows','error_omitted_rows'))
            or coverage['offered_rows'] != coverage['written_rows']
            or coverage['processed_frames'] != coverage['recorded_frames']):
        raise ValueError('Contradictory complete landmark coverage')
    if coverage['last_seq'] != (coverage['offered_rows'] or None):
        raise ValueError('Incorrect offered landmark coverage')
    previous = total_rows = total_frames = total_bytes = 0
    for chunk in index['chunks']:
        name = chunk['file']
        if not isinstance(name,str) or not re.fullmatch(r'landmarks-[0-9]{6}\.jsonl\.gz', name):
            raise ValueError('Unsafe landmark chunk path')
        path = directory / name
        if path.is_symlink():
            raise ValueError('Unsafe landmark chunk symlink')
        size = path.stat().st_size
        if size != chunk['bytes'] or not 0 < size <= MAX_CHUNK_BYTES:
            raise ValueError('Invalid landmark chunk size')
        total_bytes += size
        if total_bytes > MAX_BYTES:
            raise ValueError('Landmark session exceeds byte limit')
        with path.open('rb') as stream:
            data = stream.read(MAX_CHUNK_BYTES + 1)
        if len(data) != size:
            raise ValueError('Landmark chunk changed while reading')
        if hashlib.sha256(data).hexdigest() != chunk['sha256']:
            raise ValueError('Corrupt landmark chunk')
        rows = frames = 0
        with gzip.GzipFile(fileobj=io.BytesIO(data),mode='rb') as compressed:
            decoded=compressed.read(MAX_CHUNK_BYTES+1)
        if len(decoded)>MAX_CHUNK_BYTES:
            raise ValueError('Uncompressed landmark chunk exceeds limit')
        for line in decoded.splitlines():
            row = json.loads(line,object_hook=_restore_nonfinite)
            if row['seq'] <= previous or (require_complete and row['seq'] != previous + 1):
                raise ValueError('Noncontiguous landmark sequence')
            previous = row['seq']; rows += 1; frames += row['kind'] == 'process'
            if rows == 1 and previous != chunk['first_seq']:
                raise ValueError('Incorrect chunk first sequence')
            yield row
        if rows != chunk['rows'] or frames != chunk['frames'] or previous != chunk['last_seq']:
            raise ValueError('Incorrect landmark chunk coverage')
        total_rows += rows; total_frames += frames
    if total_rows != coverage['written_rows'] or total_frames != coverage['recorded_frames']:
        raise ValueError('Incorrect total landmark coverage')
    if require_complete and previous != coverage['last_seq']:
        raise ValueError('Missing final landmark rows')
