"""Original RGB-reviewed development intervals; no participant-held-out accuracy claim."""
from dataclasses import replace
from pathlib import Path
import json,os,unittest
from action_engine.types import PoseObservation
from redbool_demo.six_engine import SixGestureEngine

DATA=os.environ.get('REDBOOL_CROSS_REPLAY_SET')
@unittest.skipUnless(DATA,'Reviewed multi-person recordings and derived same-frame observations required')
class CrossRecoveryRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.results=[]
        for spec in json.loads(Path(DATA).read_text()):
            s=Path(spec['session']);m=json.loads((s/'manifest.json').read_text())
            marks={x['event_frame_id']:x['label'] for x in map(json.loads,(s/'annotations.jsonl').read_text().splitlines())}
            clocks={x['frame_id']:x['state']['timestamp_ns'] for x in map(json.loads,(s.parent.parent/'trace.jsonl').read_text().splitlines()) if x['type']=='frame'}
            images={x['frame']:x for x in map(json.loads,Path(spec['image_aux']).read_text().splitlines())}
            videos={x['frame_id']:x for x in map(json.loads,Path(spec['video_aux']).read_text().splitlines())} if spec.get('video_aux') else {}
            e=SixGestureEngine(movement=True);events=[]
            def aux(o,c):return replace(o,landmarks=tuple(map(tuple,c['landmarks'])) or None,visibility=tuple(c['visibility']) or None,pose_present=bool(c['landmarks']))
            with (s/'frames.jsonl').open() as stream:
                for line in stream:
                    f=json.loads(line);o=PoseObservation.from_record(f,m);v=PoseObservation.from_record(videos[o.frame_id],m) if videos else aux(o,f['processing_metadata']['cross_pose']);im=aux(o,images[o.frame_id]);now=clocks[o.frame_id]
                    e.process(o,now_ns=now,special_observation=(v,im))
                    events.extend({'frame':x.source_frame_id,'gesture':x.reason.get('gesture',x.side+'_PUNCH')} for x in e.drain_events(now));e.take_logs()
                    if o.frame_id in marks:e.request_calibration();e.calibrating=marks[o.frame_id]=='CALIBRATE'
            cls.results.append((spec,events))
    def test_all_prior_true_attacks_and_special_holds_preserved(self):
        for spec,events in self.results:
            for old in json.loads(Path(spec['baseline']).read_text()):
                if old['frame'] in spec['false_punch_frames']:continue
                target=spec.get('earlier_same_hold',{}).get(str(old['frame']),old['frame'])
                self.assertTrue(any(x['gesture']==old['gesture'] and abs(x['frame']-target)<=15 for x in events),(spec['name'],old))
    def test_reviewed_x_as_punch_errors_removed(self):
        for spec,events in self.results:
            for frame in spec['false_punch_frames']:
                self.assertFalse(any('PUNCH' in x['gesture'] and abs(x['frame']-frame)<=15 for x in events),(spec['name'],frame))
    def test_only_reviewed_specials_added_no_extra_attacks(self):
        for spec,events in self.results:
            old=json.loads(Path(spec['baseline']).read_text())
            self.assertEqual(len(events),len(old)-len(spec['false_punch_frames'])+len(spec['new_special_frames']),spec['name'])
            for frame in spec['new_special_frames']:
                self.assertTrue(any(x['gesture']=='SPECIAL' and abs(x['frame']-frame)<=15 for x in events),(spec['name'],frame))
    def test_block_punch_and_special_followups_kept(self):
        spec,events=next(x for x in self.results if x[0]['name']=='latest')
        for old in json.loads(Path(spec['baseline']).read_text()):
            if any(abs(old['t']-t)<.25 for t in [212.054,451.319,100.424,289.702,300.246,354.161,355.74]):
                self.assertTrue(any(x['gesture']==old['gesture'] and abs(x['frame']-old['frame'])<=15 for x in events),old)
