"""Camera-free ordered replay; control decisions are exact across platforms."""
from dataclasses import asdict, replace
import json
import math
from pathlib import Path
from .landmark_recording import iter_journal, engine_snapshot, _encode
from action_engine.types import PoseObservation


class ReplayClock:
    def __init__(self): self.readings = []
    def set(self, readings):
        if self.readings: raise ValueError('Unused recorded clock readings')
        self.readings = list(reversed(readings))
    def __call__(self):
        if not self.readings: raise ValueError('Missing recorded engine clock read')
        return self.readings.pop()
    def finish(self):
        if self.readings: raise ValueError('Replay used fewer clock reads than recorded')


class ReplayComparison:
    """Allow only reported float roundoff, never a changed decision or timestamp.

    Windows/x64 and Mac/arm64 can differ by a few ulps in derived angles despite
    identical recorded coordinates and clock reads. No input is rounded and no
    tolerance is applied to configuration, integers, booleans, IDs or ordering.
    """
    def __init__(self, strict_floats=False):
        self.strict_floats = strict_floats
        self.report = dict(mode='strict' if strict_floats else 'cross_platform_roundoff',
                           float_rel_tol=0. if strict_floats else 1e-12,
                           float_abs_tol=0. if strict_floats else 1e-12,
                           tolerated_float_differences=0, max_abs_float_difference=0.,
                           sample_paths=[])

    def matches(self, expected, actual, path=''):
        if type(expected) is not type(actual): return False
        if isinstance(expected, dict):
            if expected.keys() != actual.keys(): return False
            results = [self.matches(value, actual[key], f'{path}.{key}')
                       for key, value in expected.items()]
            return all(results)
        if isinstance(expected, list):
            if len(expected) != len(actual): return False
            return all([self.matches(a, b, f'{path}[{i}]')
                        for i, (a, b) in enumerate(zip(expected, actual))])
        if expected == actual: return True
        if (isinstance(expected, float) and not self.strict_floats
                and math.isfinite(expected) and math.isfinite(actual)
                and math.isclose(expected, actual, rel_tol=1e-12, abs_tol=1e-12)):
            self.report['tolerated_float_differences'] += 1
            self.report['max_abs_float_difference'] = max(
                self.report['max_abs_float_difference'], abs(expected-actual))
            paths = self.report['sample_paths']
            if path not in paths and len(paths) < 20: paths.append(path)
            return True
        return False


def observation(row):
    actor, epoch, fid, stamp, width, height, present, mirrored = row['observation']
    frame = row['frame']; points = frame['image']; vis = frame['visibility']['image']
    obs = PoseObservation(actor,epoch,fid,stamp,tuple(map(tuple,points)) if points else None,
                          tuple(vis) if vis else None,width,height,present,mirrored)
    def aux(data):
        if data is None:return None
        points=data['landmarks']; vis=data['visibility']
        return replace(obs,landmarks=tuple(map(tuple,points)) if points else None,
                       visibility=tuple(vis) if vis else None,pose_present=bool(points))
    video=aux(frame.get('full_video')); image=aux(frame.get('full_image'))
    return obs, (video,image) if frame.get('full_image') is not None else video


def verify_replay(directory, *, strict_floats=False):
    """Compare ordered state/events with reported roundoff, not human intent."""
    from .six_engine import SixGestureEngine
    from .personal import PersonalGestureEngine
    rows=iter(iter_journal(directory))
    header=next(rows)
    if header['kind'] != 'header' or header.get('schema') != 'redbool.engine-operations':
        raise ValueError('Engine header missing')
    choices={'SixGestureEngine':SixGestureEngine,'PersonalGestureEngine':PersonalGestureEngine}
    if header['engine'] not in choices: raise ValueError('Unsupported engine for exact replay')
    clock=ReplayClock(); engine=choices[header['engine']](movement=header['movement'],clock=clock)
    engine.run_id=header['run_id']
    if asdict(engine.config) != header['config']: raise ValueError('Engine configuration differs from recording')
    comparison = ReplayComparison(strict_floats)
    report=dict(operations=0,frames=0,events=0,calibrations=0,passed=True,mismatches=[],
                comparison=comparison.report,
                scope='ordered recorded operation equivalence with exact discrete decisions; not human-labelled accuracy')
    for row in rows:
        kind=row['kind']
        if kind == 'session':continue
        clock.set(row['clocks'])
        if kind == 'process':
            obs,aux=observation(row)
            if row['has_auxiliary']:engine.process(obs,special_observation=aux)
            else:engine.process(obs)
            report['frames']+=1
        elif kind == 'publisher':
            engine.tick(); events=engine.drain_events()
            context=row['context']
            if context:engine.tutorial_context(context['context'],context['context_ns'])
            actual=json.loads(_encode([asdict(ev) for ev in events]))
            if not comparison.matches(row['events'], actual, 'events'):
                report['mismatches'].append(dict(seq=row['seq'],field='events',expected=row['events'],actual=actual))
            report['events']+=len(events)
            engine.take_logs()
        elif kind in ('calibrate','pause'):
            engine.request_calibration()
            if kind=='pause':engine.calibrating=False
            else:report['calibrations']+=1
        elif kind=='movement':engine.toggle_movement()
        else:raise ValueError('Unknown engine operation: '+kind)
        clock.finish()
        expected=row['expected']; actual=json.loads(_encode(engine_snapshot(engine)))
        if not comparison.matches(expected, actual, 'state'):
            report['mismatches'].append(dict(seq=row['seq'],field='state',expected=expected,actual=actual))
        report['operations']+=1
        if len(report['mismatches'])>=20:break
    report['passed']=not report['mismatches']
    report['all_values_exact']=report['passed'] and comparison.report['tolerated_float_differences']==0
    if hasattr(engine,'profile'):report['final_personal_profile']=engine.profile()
    return report


def main():
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path,help='Session recognition directory')
    parser.add_argument('--strict-floats', action='store_true',
                        help='Also require derived float values to match exactly across platforms')
    args=parser.parse_args();result=verify_replay(args.directory, strict_floats=args.strict_floats)
    print(json.dumps(result,indent=2))
    if not result['passed']:raise SystemExit(1)

if __name__=='__main__':main()
