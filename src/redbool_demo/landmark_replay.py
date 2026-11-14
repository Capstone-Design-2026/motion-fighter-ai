"""Camera-free deterministic verification of an intact landmark operation journal."""
from dataclasses import asdict, replace
import json
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


def verify_replay(directory):
    """Compare exact state/events, never infer human intent or recognition accuracy."""
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
    report=dict(operations=0,frames=0,events=0,calibrations=0,passed=True,mismatches=[],
                scope='exact recorded operation equivalence, not human-labelled accuracy')
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
            if actual != row['events']:
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
        if actual != expected:
            report['mismatches'].append(dict(seq=row['seq'],field='state',expected=expected,actual=actual))
        report['operations']+=1
        if len(report['mismatches'])>=20:break
    report['passed']=not report['mismatches']
    if hasattr(engine,'profile'):report['final_personal_profile']=engine.profile()
    return report


def main():
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path,help='Session recognition directory')
    args=parser.parse_args();result=verify_replay(args.directory)
    print(json.dumps(result,indent=2))
    if not result['passed']:raise SystemExit(1)

if __name__=='__main__':main()
