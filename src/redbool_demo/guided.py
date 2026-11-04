"""Prompt schedule only: annotations describe instructions, not human truth."""
import random

class GuidedCheck:
    def __init__(self, movement=False, self_paced=False, focus="all"):
        self.self_paced = self_paced
        self.advance_requested = False
        self.preparing = False
        self.ready_since = None
        gestures=['LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE','BLOCK']
        if movement: gestures.extend(['MOVE_LEFT','MOVE_RIGHT'])
        self.protocol='raised-kicks-movement-check-v4' if movement else 'raised-kicks-check-v3'
        if self_paced: self.protocol = 'raised-kicks-self-paced-v7'
        rng=random.Random(20261103)
        self.steps=[('GUARD',4.)]
        for _ in range(3):
            order=gestures.copy();rng.shuffle(order)
            for gesture in order:self.steps.extend([(gesture,3.),('GUARD',3.)])
        if focus == 'left-return':
            self.steps=[('GUARD',4.)]+[step for _ in range(5) for step in [('LEFT_RAISE',3.),('GUARD',3.)]]
            self.protocol += '-left-return'
        elif focus != 'all':
            raise ValueError('Unknown guided focus')
        self.steps.extend([('NONE',10.),('GUARD',4.)])
        self.index=0;self.start_ns=None;self.start_frame=None;self.last_frame=None
        self.completed=False;self.interrupted=False

    def update(self, fid, stamp, calibrated, recorder, ready=True):
        if self.completed:return 'CHECK COMPLETE'
        if self.interrupted:return 'CHECK INTERRUPTED - Q then restart'
        if self.start_ns is None:
            if not calibrated:return 'C: calibrate first'
            self.start_ns=stamp;self.start_frame=fid
        if not calibrated:
            self.finish(recorder);return 'CHECK INTERRUPTED - Q then restart'
        label,duration=self.steps[self.index]
        if (self.advance_requested if self.self_paced else stamp-self.start_ns >= duration*1e9):
            self.advance_requested = False
            self._record(recorder,self.last_frame,'complete')
            self.index+=1
            if self.index==len(self.steps):
                self.completed=True;return 'CHECK COMPLETE'
            self.start_ns=stamp;self.start_frame=fid
            label,duration=self.steps[self.index]
            self.preparing = self.self_paced and label not in ('GUARD','NONE')
            self.ready_since = None
        if self.preparing:
            if ready:
                if self.ready_since is None: self.ready_since=stamp
            else:
                self.ready_since=None
            if (self.ready_since is not None and stamp-self.ready_since>=700_000_000
                    and stamp-self.start_ns>=2_000_000_000):
                self._record(recorder,self.last_frame,'complete')
                self.preparing=False
                self.start_ns=stamp;self.start_frame=fid
            else:
                self.last_frame=fid
                status='hold guard' if not ready else 'hold steady'
                return f'{self.index+1}/{len(self.steps)} PREPARE {label}: {status}; wait for GO'
        self.last_frame=fid
        remaining=max(0.,duration-(stamp-self.start_ns)/1e9)
        instruction={'GUARD':'GUARD: fists by chin, center; wait READY',
            'NONE':'REST: hands relaxed, body centered',
            'MOVE_LEFT':'LEAN YOUR LEFT, keep guard',
            'MOVE_RIGHT':'LEAN YOUR RIGHT, keep guard',
            'LEFT_RAISE':'YOUR LEFT HAND UP: high kick',
            'RIGHT_RAISE':'YOUR RIGHT HAND UP: low kick',
            'LEFT_PUNCH':'YOUR LEFT PUNCH: jab',
            'RIGHT_PUNCH':'YOUR RIGHT PUNCH: straight'}.get(label,label)
        suffix = ' | GO; SPACE when done' if self.self_paced else f' {remaining:.1f}s'
        return f'{self.index+1}/{len(self.steps)} {instruction}{suffix}'

    def advance(self, stamp):
        # Keypresses mark operator progress, never recognition success. Require
        # a real interval and ignore accidental immediate/repeated keypresses.
        if (self.self_paced and not self.preparing and not self.completed and not self.interrupted
                and self.start_ns is not None and stamp-self.start_ns >= 1_000_000_000):
            self.advance_requested = True

    def _record(self, recorder, end, outcome):
        if end is not None and end>=self.start_frame:
            recorder.record_annotation(label='PREPARE' if self.preparing else self.steps[self.index][0],annotation_type='interval',
                start_frame_id=self.start_frame,end_frame_id=end,source='prompt',
                notes={'protocol':self.protocol,'step':self.index+1,'outcome':outcome,
                       'ground_truth_status':'NOT_REVIEWED','intended_action':self.steps[self.index][0],
                       'advance_mode':'operator_space' if self.self_paced else 'timer'})

    def finish(self, recorder):
        if self.start_ns is not None and not self.completed and not self.interrupted:
            self._record(recorder,self.last_frame,'interrupted');self.interrupted=True
