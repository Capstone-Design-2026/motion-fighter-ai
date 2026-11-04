"""v6 behavior and reviewed capture regressions, not held-out accuracy."""
from dataclasses import replace
import json
import math
import os
from pathlib import Path
import unittest
from action_engine.types import EngineConfig, PoseObservation
from redbool_demo.lateral import LateralMovement
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.guided import GuidedCheck


class LeanGeometryTests(unittest.TestCase):
    def setUp(self):
        p = [(0.5, 0.5, 0.) for _ in range(33)]
        p[11], p[12] = (.7, .4, 0.), (.3, .4, 0.)
        self.base = PoseObservation('test', 'test', 1, 0, tuple(p), (1.,)*33, 640, 480)
        self.movement = LateralMovement(EngineConfig())
        self.movement.calibrate(self.base)
        self.now = 0

    def feed(self, offset=0., roll=0., count=10, mirror=False):
        for _ in range(count):
            self.now += 33_333_333
            p = [list(v) for v in self.base.landmarks]
            for v in p: v[0] += offset*.4
            dy = math.tan(math.radians(roll))*.4/.75/2
            p[11][1] += dy
            p[12][1] -= dy
            if mirror:
                for v in p: v[0] = 1-v[0]
            obs = replace(self.base, capture_ns=self.now, landmarks=tuple(map(tuple,p)), source_mirrored=mirror)
            self.movement.update(obs, self.now)
        return self.movement.state

    def test_small_shift_needs_same_direction_roll_and_releases_at_center(self):
        self.assertEqual(self.feed(.16, 4), 'LEFT')
        self.assertGreaterEqual(self.movement.strength, .35)
        self.assertEqual(self.feed(), 'NEUTRAL')
        self.assertEqual(self.feed(-.16, -4), 'RIGHT')
        self.assertEqual(self.feed(), 'NEUTRAL')
        self.assertEqual(self.movement.strength, 0.)

    def test_shoulder_raise_head_sway_and_conflicting_signs_cannot_steer(self):
        for offset, roll in [(0.,15), (.1,8), (.16,0), (.16,-10), (-.16,10)]:
            with self.subTest(offset=offset,roll=roll):
                self.assertEqual(self.feed(offset,roll), 'NEUTRAL')

    def test_brief_noise_does_not_enter_and_reverse_requires_entry_threshold(self):
        for _ in range(6):
            self.assertEqual(self.feed(.16,4,count=2), 'NEUTRAL')
            self.assertEqual(self.feed(count=2), 'NEUTRAL')
        self.assertEqual(self.feed(.3,0), 'LEFT')
        self.assertEqual(self.feed(-.13,-4), 'NEUTRAL')
        self.assertEqual(self.feed(-.16,-4), 'RIGHT')

    def test_mirrored_capture_preserves_anatomical_direction(self):
        self.assertEqual(self.feed(.16,4,mirror=True),'LEFT')
        self.assertEqual(self.feed(mirror=True),'NEUTRAL')
        self.assertEqual(self.feed(-.16,-4,mirror=True),'RIGHT')

    def test_camera_aspect_ratio_does_not_change_lean(self):
        p=tuple((x,y*.75,z) for x,y,z in self.base.landmarks)
        square=replace(self.base,landmarks=p,width=640,height=640)
        other=LateralMovement(EngineConfig());other.calibrate(square)
        self.feed(.16,4)
        p=[list(v) for v in square.landmarks]
        for v in p:v[0]+=.16*.4
        dy=math.tan(math.radians(4))*.4/2
        p[11][1]+=dy;p[12][1]-=dy
        for i in range(10):other.update(replace(square,landmarks=tuple(map(tuple,p))),i*33_333_333)
        self.assertEqual(other.state,self.movement.state)
        self.assertAlmostEqual(other.strength,self.movement.strength)


class SelfPacedGuideTests(unittest.TestCase):
    def setUp(self):
        self.guide=GuidedCheck(movement=True,self_paced=True)
        self.rows=[]
    def record_annotation(self,**row):self.rows.append(row)

    def test_no_timer_advancement_or_recognition_based_success(self):
        g=self.guide
        self.assertIn('calibrate',g.update(1,0,False,self))
        g.advance(100_000_000_000)
        g.update(2,1_000_000_000,True,self)
        self.assertIn('SPACE',g.update(3,121_000_000_000,True,self))
        self.assertEqual(g.index,0);self.assertFalse(self.rows)
        g.advance(121_000_000_000);g.update(4,122_000_000_000,True,self)
        self.assertEqual(g.index,1);self.assertEqual(self.rows[0]['end_frame_id'],3)
        self.assertEqual(self.rows[0]['notes']['ground_truth_status'],'NOT_REVIEWED')
        g.finish(self);g.finish(self)
        self.assertEqual(len(self.rows),2);self.assertTrue(g.interrupted)

    def test_complete_all_intervals_only_after_operator_advance(self):
        g=self.guide;stamp=0;fid=1
        while not g.completed:
            g.update(fid,stamp,True,self)
            if g.completed:break
            g.advance(stamp+100_000_000) # immediate repeat ignored
            self.assertFalse(g.advance_requested)
            g.advance(stamp+2_000_000_000)
            stamp+=3_000_000_000;fid+=1
        self.assertEqual(sum(r['label']!='PREPARE' for r in self.rows),45)
        self.assertEqual(sum(r['label']=='PREPARE' for r in self.rows),21)
        self.assertTrue(all(a['end_frame_id']<b['start_frame_id'] for a,b in zip(self.rows,self.rows[1:])))
        g.finish(self);self.assertEqual(len(self.rows),66)


@unittest.skipUnless(os.environ.get('REDBOOL_LEAN_SESSION'),'Reviewed 15:47 capture required')
class LeanCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        p=Path(os.environ['REDBOOL_LEAN_SESSION'])
        m=json.loads((p/'manifest.json').read_text())
        aa=list(map(json.loads,(p/'annotations.jsonl').read_text().splitlines()))
        cls.intervals=[a for a in aa if a['annotation_type']=='interval']
        markers={a['event_frame_id'] for a in aa if a['label']=='CALIBRATE'}
        e=SixGestureEngine(movement=True);cls.events=[];cls.states={}
        for f in map(json.loads,(p/'frames.jsonl').read_text().splitlines()):
            o=PoseObservation.from_record(f,m);s=e.process(o,now_ns=o.capture_ns)
            cls.events.extend(e.drain_events(o.capture_ns));cls.states[o.frame_id]=(e.ready,s.movement,s.block)
            e.take_logs()
            if o.frame_id in markers:e.request_calibration()

    def test_all_actual_punches_and_raises_once_including_late_right_punch(self):
        for i,a in enumerate(self.intervals):
            if a['label'] in ('GUARD','NONE'):continue
            end=self.intervals[i+1]['end_frame_id']
            events=[e.reason['gesture'] for e in self.events if a['start_frame_id']<=e.source_frame_id<=end]
            expected=[a['label']] if a['label'] in ('LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE') and a['notes']['step']!=2 else []
            with self.subTest(step=a['notes']['step']):self.assertEqual(events,expected)
        self.assertEqual(len(self.events),11)

    def test_each_lean_correct_direction_stops_and_has_no_opposite_command(self):
        for i,a in enumerate(self.intervals):
            if not a['label'].startswith('MOVE_'):continue
            end=self.intervals[i+1]['end_frame_id'];direction=a['label'].split('_')[1]
            states=[self.states[f][1] for f in range(a['start_frame_id'],end+1)]
            with self.subTest(step=a['notes']['step']):
                self.assertIn(direction,states)
                self.assertNotIn('LEFT' if direction=='RIGHT' else 'RIGHT',states)
                self.assertEqual(states[-1],'NEUTRAL')

    def test_no_movement_during_other_actions_or_rest(self):
        allowed=set()
        for i,a in enumerate(self.intervals):
            if a['label'].startswith('MOVE_'):allowed.update(range(a['start_frame_id'],self.intervals[i+1]['end_frame_id']+1))
        for fid,(_,movement,_) in self.states.items():
            if fid not in allowed:self.assertEqual(movement,'NEUTRAL',f'frame {fid}')

    def test_every_action_returns_to_guard_and_block_releases(self):
        for i,a in enumerate(self.intervals):
            if a['label']=='GUARD' and 3<=a['notes']['step']<=43:
                self.assertTrue(any(self.states[f][0] for f in range(a['start_frame_id'],a['end_frame_id']+1)),a['notes']['step'])
            if a['label']=='BLOCK':
                self.assertTrue(any(self.states[f][2] for f in range(a['start_frame_id'],a['end_frame_id']+1)))
                self.assertFalse(self.states[self.intervals[i+1]['end_frame_id']][2])
            if a['label']=='NONE':
                self.assertFalse([e for e in self.events if a['start_frame_id']<=e.source_frame_id<=a['end_frame_id']])
                self.assertFalse(any(self.states[f][2] for f in range(a['start_frame_id'],a['end_frame_id']+1)))
