import json
from pathlib import Path
import tempfile
import unittest
from redbool_demo.diagnostics_recording import DiagnosticLog, DiagnosticsRecorder
from session_recording import SessionRecorder


class Target:
    error = None
    dropped = 0
    def __init__(self):self.rows=[]
    def write(self,row):self.rows.append(row)
    def close(self):pass


class DiagnosticsRecordingTests(unittest.TestCase):
    def test_observations_and_annotations_unchanged_but_no_pose_file(self):
        with tempfile.TemporaryDirectory() as td:
            full=SessionRecorder(Path(td)/'full')
            small=DiagnosticsRecorder(Path(td)/'small')
            point=dict(x=.2,y=.3,z=.4,visibility=.9,presence=.9)
            for i in range(1,4):
                args=dict(capture_return_monotonic_ns=i*100,pose_success=True,
                          pose_start_monotonic_ns=i*100+1,pose_end_monotonic_ns=i*100+2,
                          image_landmarks=[point]*33,world_landmarks=[point]*33)
                self.assertEqual(full.record_frame(**args),small.record_frame(**args))
            small.record_annotation(label='CALIBRATE',annotation_type='marker',event_frame_id=3)
            full.finalize(completed=True);small.finalize(completed=True)
            self.assertFalse((small.directory/'frames.jsonl').exists())
            self.assertTrue((full.directory/'frames.jsonl').is_file())
            self.assertEqual(json.loads((small.directory/'annotations.jsonl').read_text())['event_frame_id'],3)
            self.assertEqual(small.manifest['recording_config']['stored_pose_frames'],0)

    def test_stable_state_ack_sampled_all_transitions_events_preserved(self):
        sink=Target();log=DiagnosticLog(sink)
        for i in range(101):
            stamp=1_000_000_000+i*10_000_000
            log.write({'type':'send','packet':{'kind':'STATE','sent_ns':stamp,
                       'calibrated':True,'block_active':i==55}})
            log.write({'type':'ack','ack_receive_ns':stamp,'ack':{'status':'STATE'}})
            log.write({'type':'frame','frame_id':i,'landmarks':'must not be stored'})
        event={'type':'engine','log':{'type':'event','event':{'event_id':'attack'}}}
        ack={'type':'ack','ack_receive_ns':1,'ack':{'status':'APPLIED','event_id':'attack'}}
        log.write(event);log.write(ack)
        states=[r for r in sink.rows if r['type']=='send']
        self.assertLessEqual(len(states),13)
        self.assertTrue(any(r['packet']['block_active'] for r in states))
        self.assertEqual(sum(r['type']=='ack' for r in sink.rows),12)
        self.assertIn(event,sink.rows);self.assertIn(ack,sink.rows)
        self.assertFalse(any(r['type']=='frame' for r in sink.rows))


if __name__=='__main__':unittest.main()
