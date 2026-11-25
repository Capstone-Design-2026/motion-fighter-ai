import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SCRIPT=Path(__file__).resolve().parents[1]/'scripts/export_diagnostics.py'
spec=importlib.util.spec_from_file_location('practice_export_under_test',SCRIPT)
exporter=importlib.util.module_from_spec(spec);spec.loader.exec_module(exporter)

class PracticeCoverageTests(unittest.TestCase):
    def make_recording(self,root,counts=(100,20,20,20),omit_clock=None):
        trace=root/'run/trace.jsonl';frames=root/'run/sessions/session/frames.jsonl'
        frames.parent.mkdir(parents=True);rows=[];poses=[];fid=0
        for i,count in enumerate(counts):
            start=10_000_000_000+i*20_000_000_000;gen=1+i//2;action=('JAB','HIGH_KICK')[i%2]
            rows.append({'type':'send','packet':{'kind':'STATE','sent_ns':start-1,'calibrated':True,'calibration_generation':gen}})
            for j in (0,count-1):
                stamp=start+j*50_000_000
                rows.append({'type':'ack','ack':{'sender_ns':stamp,'tutorial_run':'person'+str(gen),'tutorial_action':action,'tutorial_phase':'PRACTICE' if j==0 else 'RETURN'},'ack_receive_ns':stamp+30_000_000})
            for j in range(count):
                fid+=1;stamp=start+j*50_000_000
                poses.append({'frame_id':fid,'capture_return_monotonic_ns':stamp,'marker':[i,j]})
                if fid!=omit_clock:rows.append({'type':'frame','frame_id':fid,'capture_ns':stamp,'state':{'timestamp_ns':stamp+20_000_000}})
        trace.write_text(''.join(json.dumps(r)+'\n' for r in rows))
        frames.write_text(''.join(json.dumps(r)+'\n' for r in poses))
        (frames.parent/'annotations.jsonl').write_text(json.dumps({'event_frame_id':3,'notes':'original annotation can reference omitted frames'})+'\n')
        (frames.parent/'raw.avi').write_bytes(b'not a real video')
        return trace,frames

    def read_export(self,root,limit=16):
        output=root/'diagnostics.zip';original=exporter.export_practice
        with patch.object(exporter,'export_practice',side_effect=lambda t,r,a:original(t,r,a,limit)):
            exporter.export(root,output,True)
        with zipfile.ZipFile(output) as z:
            poses=[json.loads(x) for x in z.read('run/sessions/session/practice_frames.jsonl').splitlines()]
            clocks=[json.loads(x) for x in z.read('run/practice_clock.jsonl').splitlines()]
            scope=json.loads(z.read('run/practice_coverage.json'))
            per_session=json.loads(z.read('run/sessions/session/practice_pose_scope.json'))
            return poses,clocks,scope,per_session,z.namelist(),z.read('run/sessions/session/annotations.jsonl')

    def test_later_person_and_actions_get_fair_contiguous_chunks(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);trace,frames=self.make_recording(root);original=frames.read_bytes()
            poses,clocks,scope,per_session,names,annotations=self.read_export(root)
            self.assertEqual(len(poses),16);self.assertEqual([w['frames'] for w in scope['windows']],[4,4,4,4])
            self.assertEqual({w['calibration_generation'] for w in scope['windows']},{1,2})
            for window in scope['windows']:
                self.assertEqual([s['frames'] for s in window['segments']],[2,2])
                self.assertTrue(all(s['end_frame_id']-s['start_frame_id']==1 for s in window['segments']))
            self.assertEqual({(p['frame_id'],p['capture_return_monotonic_ns']) for p in poses},
                             {(c['frame_id'],c['capture_ns']) for c in clocks})
            self.assertEqual(scope['frames'],per_session['frames']);self.assertTrue(scope['truncated'])
            self.assertEqual(frames.read_bytes(),original)
            self.assertFalse(any(n.endswith('.avi') for n in names))
            self.assertNotIn(3,{p['frame_id'] for p in poses})
            self.assertEqual(json.loads(annotations)['event_frame_id'],3)
            self.assertIn('omitted frames',scope['annotations_note'])
            self.assertIn('Reset motion history',scope['replay_note'])

    def test_missing_clock_is_explicit_and_cannot_misalign_pose_timestamps(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);self.make_recording(root,omit_clock=1)
            poses,clocks,scope,*_=self.read_export(root)
            self.assertEqual(scope['missing_clock_frames'],1)
            self.assertEqual(len(poses),15);self.assertEqual(len(clocks),15)
            self.assertNotIn(1,{p['frame_id'] for p in poses})

    def test_small_windows_donate_budget_without_decimating_others(self):
        self.assertEqual(exporter.fair_budgets([2,100,100,100],17),[2,5,5,5])
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);self.make_recording(root,counts=(2,2,2,2))
            poses,clocks,scope,*_=self.read_export(root)
            self.assertEqual(len(poses),8);self.assertFalse(scope['truncated'])
            self.assertTrue(all(len(w['segments'])==1 for w in scope['windows']))

    def test_late_old_ack_uses_its_original_calibration_generation(self):
        with tempfile.TemporaryDirectory() as td:
            trace=Path(td)/'trace.jsonl'
            rows=[{'type':'send','packet':{'kind':'STATE','sent_ns':10,'calibrated':True,'calibration_generation':1}},
                  {'type':'send','packet':{'kind':'STATE','sent_ns':100,'calibrated':False,'calibration_generation':2}},
                  {'type':'send','packet':{'kind':'STATE','sent_ns':200,'calibrated':True,'calibration_generation':2}},
                  {'type':'ack','ack':{'sender_ns':210,'tutorial_run':'new','tutorial_action':'JAB','tutorial_phase':'PRACTICE'},'ack_receive_ns':220},
                  {'type':'ack','ack':{'sender_ns':20,'tutorial_run':'old','tutorial_action':'JAB','tutorial_phase':'RETURN'},'ack_receive_ns':230}]
            trace.write_text(''.join(json.dumps(r)+'\n' for r in rows))
            windows=exporter.practice_windows(trace)
            self.assertEqual([w['calibration_generation'] for w in windows],[1,2])
            self.assertLess(windows[0]['end_ns'],windows[1]['start_ns'])

    def test_bounded_calibration_feedback_keeps_reason_counts_without_rgb(self):
        with tempfile.TemporaryDirectory() as td:
            trace=Path(td)/'trace.jsonl'
            row={'type':'engine','log':{'type':'calibration_feedback','timestamp_ns':123,
                 'frames':60,'reason_counts':{'holding_guard':40,'moving':20},
                 'current_reason':'holding_guard','elapsed_seconds':2.,
                 'window_counts':{'holding_guard':40,'moving':20},'complete':False}}
            trace.write_text(json.dumps(row)+'\n')
            self.assertEqual([json.loads(r) for r in exporter.small_trace(trace)],[row])

    def test_late_event_outcome_is_kept_in_small_diagnostics(self):
        with tempfile.TemporaryDirectory() as td:
            trace=Path(td)/'trace.jsonl';row={'type':'late_event_ack','event_id':'e','status':'APPLIED','ack_receive_ns':123}
            trace.write_text(json.dumps(row)+'\n')
            self.assertEqual([json.loads(r) for r in exporter.small_trace(trace)],[row])

if __name__=='__main__':unittest.main()
