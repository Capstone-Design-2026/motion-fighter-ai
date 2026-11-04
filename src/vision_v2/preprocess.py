"""Read-only schema-v1 Trial preprocessing. No training or augmentation."""
import argparse
from collections import Counter
import csv
from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path

import numpy as np

from replay_session import iter_frames
from session_recording import check_schema

JOINTS = (11, 12, 13, 14, 15, 16, 23, 24)
NAMES = ('left_shoulder', 'right_shoulder', 'left_elbow', 'right_elbow',
         'left_wrist', 'right_wrist', 'left_hip', 'right_hip')
REL_NAMES = [f'{joint}_relative_{axis}' for joint in NAMES[2:6] for axis in 'xyz']
FEATURE_NAMES = (REL_NAMES + ['left_elbow_angle', 'right_elbow_angle']
                 + ['shoulder_displacement_x', 'shoulder_displacement_y']
                 + [name + '_per_second' for name in REL_NAMES]
                 + ['shoulder_velocity_x', 'shoulder_velocity_y']
                 + [name + '_visibility' for name in NAMES]
                 + [name + '_outside' for name in NAMES] + ['pose_missing'])
MLP_NAMES = [f'{stat}:{name}' for stat in ('mean', 'std', 'min', 'max', 'delta', 'valid_fraction')
             for name in FEATURE_NAMES]


@dataclass(frozen=True)
class Config:
    length: int = 96
    interval_seconds: float = .04
    max_gap_seconds: float = .2
    visibility_threshold: float = .5

    def __post_init__(self):
        if (self.length < 2 or not np.isfinite(self.interval_seconds)
                or self.interval_seconds <= 0 or not np.isfinite(self.max_gap_seconds)
                or self.max_gap_seconds <= 0 or not 0 <= self.visibility_threshold <= 1):
            raise ValueError('Invalid sampling configuration')


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def selection_rows(directory):
    """Validate all three disjoint selection files; only USE is a training input."""
    rows = []
    seen = set()
    for status in ('USE', 'REVIEW', 'EXCLUDE'):
        path = Path(directory) / f'{status.lower()}_trials.csv'
        with path.open(newline='', encoding='utf-8-sig') as stream:
            for row in csv.DictReader(stream):
                key = (row['subject'], row['session'], row['trial'])
                # session IDs are globally unique; reject conflicting subject aliases too.
                unique = (row['session'], row['trial'])
                if unique in seen or row['status'] != status or row['in_candidate_pool'] != 'True':
                    raise ValueError(f'Duplicate or invalid selection: {key}')
                if any(not part or '/' in part or '\\' in part or part in ('.', '..') for part in key):
                    raise ValueError(f'Invalid identifier: {key}')
                seen.add(unique)
                rows.append(row)
    return sorted(rows, key=lambda r: (r['subject'], r['session'], r['trial']))


def load_trials(sessions, rows):
    """Join selected rows to authoritative interval annotations, strictly read-only."""
    sessions = Path(sessions)
    for sid in sorted({r['session'] for r in rows}):
        directory = sessions / sid
        manifest = read_json(directory / 'manifest.json')
        summary = read_json(directory / 'summary.json')
        check_schema(manifest)
        check_schema(summary)
        if manifest['session_id'] != sid:
            raise ValueError(f'Session folder mismatch: {sid}')
        source_mirrored = manifest['recording_config'].get('source_mirrored')
        if source_mirrored is not False:
            raise ValueError(f'Unsupported/unknown source mirroring: {sid}; verify anatomical indices first')
        with (directory / 'annotations.jsonl').open() as stream:
            annotations = [json.loads(line) for line in stream]
        invalid = set()
        for a in annotations:
            check_schema(a)
            if any(a[k] != manifest[k] for k in ('subject_id', 'session_id', 'take_id')):
                raise ValueError(f'Annotation identity mismatch: {sid}')
            notes = a.get('notes') or {}
            if notes.get('event') in ('skipped', 'superseded'):
                invalid.add(notes['trial_id'])
            if notes.get('retry_of'):
                invalid.add(notes['retry_of'])
        history = summary['metadata']['trials']
        if len({t['trial_id'] for t in history}) != len(history):
            raise ValueError(f'Duplicate summary trial: {sid}')
        for t in history:
            if t.get('retry_of'):
                invalid.add(t['retry_of'])
        intervals = {}
        for row in [r for r in rows if r['session'] == sid]:
            tid = row['trial']
            matches = [a for a in annotations if a['annotation_type'] == 'interval'
                       and (a.get('notes') or {}).get('trial_id') == tid
                       and a['notes'].get('event') == 'recording_interval']
            trials = [t for t in history if t['trial_id'] == tid]
            if tid in invalid or len(matches) != 1 or len(trials) != 1:
                raise ValueError(f'Invalid, skipped or superseded selected Trial: {sid}/{tid}')
            a, t = matches[0], trials[0]
            repetition = float(row['repetition'])
            if (row['subject'] != manifest['subject_id'] or row['take'] != manifest['take_id']
                    or not repetition.is_integer() or t['label'] != row['label']
                    or t['repetition_index'] != repetition or a['label'] != row['label']
                    or a['notes']['label'] != row['label']
                    or a['notes']['repetition_index'] != repetition
                    or t['outcome'] != 'recording_complete'
                    or a['notes']['outcome'] != 'recording_complete'
                    or t.get('retry_of') != a['notes'].get('retry_of')):
                raise ValueError(f'Trial metadata mismatch: {sid}/{tid}')
            dirty = manifest['recording_config'].get('code_dirty')
            if type(dirty) is not bool or row['source_code_dirty'].lower() != str(dirty).lower():
                raise ValueError(f'Code provenance mismatch: {sid}/{tid}')
            lo, hi = a['start_frame_id'], a['end_frame_id']
            if type(lo) is not int or type(hi) is not int or lo < 1 or hi < lo:
                raise ValueError(f'Invalid interval: {sid}/{tid}')
            intervals[tid] = (row, a, t, [])
        last = 0
        for frame in iter_frames(directory, allow_truncated_tail=False):
            last = frame['frame_id']
            for row, a, t, frames in intervals.values():
                if a['start_frame_id'] <= last <= a['end_frame_id']:
                    frames.append(frame)
        if last != summary['captured_frame_count']:
            raise ValueError(f'Summary frame count mismatch: {sid}')
        for tid, (row, a, t, frames) in sorted(intervals.items()):
            if len(frames) != a['end_frame_id'] - a['start_frame_id'] + 1 or len(frames) != float(row['frames']):
                raise ValueError(f'Missing frames/count mismatch: {sid}/{tid}')
            yield row, manifest, a, t, frames


def features(frames, config):
    """47 channels; zero-filled invalid values always have a separate bool mask.

    Image x/y/z use image-width units (y scaled by H/W by the caller).
    Anatomical left/right indices are never swapped by display mirroring.
    """
    n = len(frames)
    stamps = np.array([f['capture_return_monotonic_ns'] for f in frames], dtype=np.int64)
    if n < 2 or np.any(np.diff(stamps) <= 0):
        raise ValueError('Trial requires strictly increasing capture timestamps')
    times = (stamps - stamps[0]) / 1e9
    xyz = np.full((n, 8, 3), np.nan)
    vis = np.full((n, 8), np.nan)
    missing = np.ones(n, dtype=bool)
    for i, f in enumerate(frames):
        points = f.get('image_landmarks')
        if points is not None:
            if np.shape(points) != (33, 3):
                raise ValueError('Expected 33 image landmarks with xyz triples')
            xyz[i] = np.asarray(points, dtype=float)[list(JOINTS)]
            missing[i] = not f.get('pose_success', False) or not f.get('pose_validity', {}).get('pose_present', False)
        visibility = (f.get('landmark_visibility') or {}).get('image')
        if visibility is not None:
            if len(visibility) != 33:
                raise ValueError('Expected 33 visibility values')
            vis[i] = np.asarray(visibility, dtype=float)[list(JOINTS)]
    finite = np.isfinite(xyz).all(axis=2)
    outside = ((xyz[:, :, :2] < 0) | (xyz[:, :, :2] > 1)).any(axis=2)
    good = finite & np.isfinite(vis) & (vis >= config.visibility_threshold) & ~missing[:, None]
    # Aspect ratio supplied explicitly from manifest, not guessed from camera defaults.
    aspect = frames[0]['_height_over_width']
    xyz[:, :, 1] *= aspect
    center = (xyz[:, 0] + xyz[:, 1]) / 2
    width = np.linalg.norm(xyz[:, 0, :2] - xyz[:, 1, :2], axis=1)
    shoulders = good[:, 0] & good[:, 1] & (width > 1e-6)
    safe_width = np.where(shoulders, width, 1)
    rel = np.stack([(xyz[:, j] - xyz[:, j % 2]) / safe_width[:, None] for j in range(2, 6)], axis=1)
    relmask = np.repeat((good[:, 2:6] & shoulders[:, None])[:, :, None], 3, axis=2).reshape(n, 12)
    rel = rel.reshape(n, 12)
    angles, angle_masks = [], []
    for sh, el, wr in ((0, 2, 4), (1, 3, 5)):
        # 2D angle avoids treating MediaPipe image depth as calibrated metric depth.
        u, v = xyz[:, sh, :2] - xyz[:, el, :2], xyz[:, wr, :2] - xyz[:, el, :2]
        denom = np.linalg.norm(u, axis=1) * np.linalg.norm(v, axis=1)
        angles.append(np.arccos(np.clip(np.sum(u * v, axis=1) / np.maximum(denom, 1e-12), -1, 1)))
        angle_masks.append(good[:, sh] & good[:, el] & good[:, wr] & (denom > 1e-12))
    displacement = np.zeros((n, 2))
    if shoulders.any():
        first = np.flatnonzero(shoulders)[0]
        displacement = (center[:, :2] - center[first, :2]) / width[first]
    base = np.column_stack((rel, displacement))
    base_mask = np.column_stack((relmask, np.repeat(shoulders[:, None], 2, axis=1)))
    velocity = np.zeros_like(base)
    velocity_mask = np.zeros_like(base_mask)
    dt = np.diff(times)
    velocity[1:] = np.diff(base, axis=0) / dt[:, None]
    velocity_mask[1:] = base_mask[1:] & base_mask[:-1] & (dt <= config.max_gap_seconds)[:, None]
    x = np.column_stack((rel, *angles, displacement, velocity, vis, outside, missing))
    mask = np.column_stack((relmask, *angle_masks, np.repeat(shoulders[:, None], 2, axis=1),
                            velocity_mask, np.isfinite(vis) & ~missing[:, None],
                            finite & ~missing[:, None], np.ones(n, dtype=bool)))
    mask &= np.isfinite(x)
    return np.where(mask, x, 0).astype(np.float32), mask, stamps


def resample(x, mask, stamps, config):
    """Nearest observation, half-step tolerance, no interpolation/extrapolation.

    Pad after Trial end. Long gaps stay unobserved; source indices permit audit.
    """
    times = (stamps - stamps[0]) / 1e9
    grid = np.arange(config.length, dtype=np.float64) * config.interval_seconds
    if times[-1] > grid[-1] + 1e-9:
        raise ValueError('Trial exceeds sequence horizon; increase --length (no silent truncation)')
    right = np.minimum(np.searchsorted(times, grid), len(times) - 1)
    left = np.maximum(right - 1, 0)
    indices = np.where(abs(times[left] - grid) <= abs(times[right] - grid), left, right)
    tolerance = min(config.interval_seconds / 2, config.max_gap_seconds / 2)
    observed = (abs(times[indices] - grid) <= tolerance + 1e-9) & (grid <= times[-1] + 1e-9)
    valid = mask[indices] & observed[:, None]
    return (np.where(valid, x[indices], 0).astype(np.float32), valid, observed,
            np.where(observed, indices, -1).astype(np.int64), grid)


def aggregate(x, mask):
    """Masked mean/std/min/max/last-first and coverage; no learned statistics."""
    out = np.zeros((6, x.shape[-1]), dtype=np.float32)
    valid = np.zeros_like(out, dtype=bool)
    for j in range(x.shape[-1]):
        values = x[mask[:, j], j]
        if len(values):
            out[:5, j] = (values.mean(), values.std(), values.min(), values.max(), values[-1] - values[0])
            valid[:5, j] = True
        out[5, j] = mask[:, j].mean()
        valid[5, j] = True
    return out.ravel(), valid.ravel()


def loso_folds(metadata):
    subjects = sorted({m['subject_id'] for m in metadata})
    if len(subjects) < 2:
        raise ValueError('LOSO requires at least two subjects')
    return [{'held_out_subject': subject,
             'train': [i for i, m in enumerate(metadata) if m['subject_id'] != subject],
             'test': [i for i, m in enumerate(metadata) if m['subject_id'] == subject]}
            for subject in subjects]


def window_indices(fold, length, window, stride):
    """Create windows only AFTER Trial split. No windows cross Trial boundaries."""
    if set(fold['train']) & set(fold['test']) or not 0 < window <= length or stride <= 0:
        raise ValueError('Invalid split/window configuration')
    return {part: [(trial, start, start + window) for trial in fold[part]
                   for start in range(0, length - window + 1, stride)] for part in ('train', 'test')}


def fit_standardizer(x, mask, metadata, held_out_subject):
    """Fit per-channel statistics using training subjects only, never test Trials."""
    fold = next(f for f in loso_folds(metadata) if f['held_out_subject'] == held_out_subject)
    values = x[fold['train']].reshape(-1, x.shape[-1]).astype(np.float64)
    valid = mask[fold['train']].reshape(values.shape)
    count = valid.sum(axis=0)
    mean = np.where(valid, values, 0).sum(axis=0) / np.maximum(count, 1)
    variance = np.where(valid, (values - mean) ** 2, 0).sum(axis=0) / np.maximum(count, 1)
    scale = np.sqrt(variance)
    scale[scale < 1e-8] = 1
    return {'mean': mean, 'scale': scale, 'count': count, 'train_indices': fold['train']}


def apply_standardizer(x, mask, stats):
    return np.where(mask, (x - stats['mean']) / stats['scale'], 0).astype(np.float32)


def run(sessions, selection, output, config=Config(), expected_counts=None):
    sessions, selection, output = Path(sessions).resolve(), Path(selection).resolve(), Path(output).resolve()
    if output.exists() or any(output == p or p in output.parents for p in (sessions, selection)):
        raise ValueError('Output must be new and outside source/selection directories')
    rows = selection_rows(selection)
    use = [r for r in rows if r['status'] == 'USE']
    inputs = list(selection.glob('*.csv')) + list(selection.glob('*.json'))
    inputs += [sessions / sid / name for sid in sorted({r['session'] for r in rows})
               for name in ('manifest.json', 'summary.json', 'annotations.jsonl', 'frames.jsonl')]
    hashes = {str(p): sha256(p) for p in inputs}
    metadata, seq, masks, observed, source_idx, raw, raw_masks, stamps, frame_ids = [], [], [], [], [], [], [], [], []
    offsets = [0]
    # Validate REVIEW/EXCLUDE linkage as well, without adding them to model arrays.
    audit = []
    for row, manifest, annotation, trial, frames in load_trials(sessions, rows):
        audit.append({'subject': row['subject'], 'session': row['session'], 'trial': row['trial'], 'status': row['status']})
        if row['status'] != 'USE':
            continue
        resolution = manifest['actual_resolution']
        if len(resolution) != 2 or min(resolution) <= 0:
            raise ValueError('Invalid manifest resolution')
        frames[0]['_height_over_width'] = resolution[1] / resolution[0]
        x, valid, ts = features(frames, config)
        sx, sm, obs, idx, grid = resample(x, valid, ts, config)
        seq.append(sx); masks.append(sm); observed.append(obs); source_idx.append(idx)
        raw.append(x); raw_masks.append(valid); stamps.append(ts)
        frame_ids.append(np.array([f['frame_id'] for f in frames], dtype=np.int64))
        offsets.append(offsets[-1] + len(frames))
        metadata.append({'subject_id': row['subject'], 'session_id': row['session'], 'trial_id': row['trial'],
                         'take_id': row['take'], 'label': row['label'], 'repetition_index': trial['repetition_index'],
                         'retry_of': trial.get('retry_of'), 'source_code_dirty': manifest['recording_config']['code_dirty'],
                         'code_commit_sha': manifest.get('code_commit_sha'), 'source_mirrored': False,
                         'mirror_display': manifest.get('mirror_display'), 'annotation_id': annotation['annotation_id'],
                         'start_frame_id': annotation['start_frame_id'], 'end_frame_id': annotation['end_frame_id'],
                         'source_directory': str(sessions / row['session']), 'selection_row': row})
    counts = dict(sorted(Counter(m['subject_id'] for m in metadata).items()))
    if expected_counts is not None and counts != {k: v for k, v in expected_counts.items() if v}:
        raise ValueError(f'Unexpected participant counts: {counts}')
    labels = dict(sorted(Counter(m['label'] for m in metadata).items()))
    if labels != dict(Counter(r['label'] for r in use)) or len(metadata) != len(use):
        raise ValueError('Selection label/count mismatch')
    if not metadata:
        raise ValueError('No USE Trials')
    classes = sorted(labels)
    sequence, mask = np.stack(seq), np.stack(masks)
    vectors = [aggregate(x, m) for x, m in zip(sequence, mask)]
    arrays = {'sequence': sequence, 'sequence_mask': mask, 'observed': np.stack(observed),
              'source_index': np.stack(source_idx), 'grid_seconds': grid,
              'mlp': np.stack([v[0] for v in vectors]), 'mlp_mask': np.stack([v[1] for v in vectors]),
              'labels': np.array([classes.index(m['label']) for m in metadata], dtype=np.int64),
              'raw_features': np.concatenate(raw), 'raw_mask': np.concatenate(raw_masks),
              'raw_timestamps_ns': np.concatenate(stamps), 'raw_frame_ids': np.concatenate(frame_ids),
              'raw_offsets': np.array(offsets, dtype=np.int64)}
    if any(not np.isfinite(a).all() for a in arrays.values()):
        raise ValueError('Nonfinite output')
    if any(sha256(p) != digest for p, digest in hashes.items()):
        raise ValueError('Input changed during preprocessing')
    report = {'schema_version': 1, 'config': asdict(config), 'subject_counts': counts, 'label_counts': labels,
              'selection_counts': dict(Counter(r['status'] for r in rows)), 'linked_trials': len(audit),
              'input_sha256': hashes, 'originals_unchanged': True,
              'feature_names': FEATURE_NAMES, 'mlp_feature_names': MLP_NAMES,
              'class_mapping': {name: i for i, name in enumerate(classes)},
              'arrays': {k: {'shape': list(a.shape), 'dtype': str(a.dtype)} for k, a in arrays.items()},
              'observed_samples': int(arrays['observed'].sum()),
              'code_sha256': sha256(__file__), 'numpy_version': np.__version__,
              'label_scope': 'Trial-level prompt label; not precise action onset/offset',
              'augmentation': 'None. Future augmentation must be applied only to fold train indices.'}
    output.mkdir(parents=True, exist_ok=False)
    np.savez_compressed(output / 'dataset.npz', **arrays)
    for name, value in [('metadata', metadata), ('report', report), ('folds', loso_folds(metadata)), ('selection_audit', audit)]:
        (output / f'{name}.json').write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sessions', type=Path, default=Path('sessions'))
    parser.add_argument('--selection', type=Path, default=Path('local_data/vision_v2/selection_20261021'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--length', type=int, default=96)
    parser.add_argument('--interval-seconds', type=float, default=.04)
    parser.add_argument('--max-gap-seconds', type=float, default=.2)
    parser.add_argument('--verify-20261021', action='store_true')
    args = parser.parse_args()
    report = run(args.sessions, args.selection, args.output,
                 Config(args.length, args.interval_seconds, args.max_gap_seconds),
                 {'P01': 45, 'P02': 33, 'P03': 42, 'P04': 36, 'P05': 0} if args.verify_20261021 else None)
    print(json.dumps({k: report[k] for k in ('subject_counts', 'label_counts', 'selection_counts', 'arrays')}, indent=2))


if __name__ == '__main__':
    main()
