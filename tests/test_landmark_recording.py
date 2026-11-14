import gzip
import copy
import json
import math
from pathlib import Path
import random
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from dataclasses import asdict, replace
from redbool_demo.landmark_recording import (LandmarkJournal, ObservedClock,
    record_operation, iter_journal, INDEX, _encode)
from redbool_demo.landmark_replay import verify_replay, observation
from redbool_demo.personal import PersonalGestureEngine
from test_chest_special import pose


def frame_for(obs, *, auxiliary=False):
    frame=obs.as_frame()
    frame.update(pose_start_monotonic_ns=obs.capture_ns+10,
        pose_end_monotonic_ns=obs.capture_ns+20,capture_attempt_id=obs.frame_id,
        capture_success=True,world_landmarks=obs.landmarks,
        landmark_presence=dict(image=[.9876543210987654]*33,world=[.4]*33),
        landmark_visibility=dict(image=obs.visibility,world=[.7]*33))
    def aux(mode):
        return dict(variant='Full',mode=mode,start_ns=obs.capture_ns+30,
            end_ns=obs.capture_ns+40,landmarks=obs.landmarks,visibility=obs.visibility,
            presence=[.8765432109876543]*33,world=[[*p,.6,.8] for p in obs.landmarks] if obs.landmarks else [])
    frame['processing_metadata']=dict(camera_frame_id=obs.frame_id+2,
        cross_pose=aux('VIDEO') if auxiliary else None,
        cross_recovery=aux('IMAGE') if auxiliary else None)
    return frame


def wait_until(predicate, seconds=2.):
    deadline=time.monotonic()+seconds
    while not predicate():
        if time.monotonic()>=deadline:raise AssertionError('Worker did not reach expected state')
        time.sleep(.005)


class LandmarkJournalTests(unittest.TestCase):
    def test_full_precision_all_models_and_lost_pose_roundtrip(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition',chunk_frames=1)
            o=pose(); p=list(o.landmarks);p[15]=(float.fromhex('0x1.123456789abcdp-2'),-.0,None)
            o=replace(o,landmarks=tuple(p)); frame=frame_for(o,auxiliary=True)
            journal.offer('process',observation=o,frame=frame,has_auxiliary=True)
            lost=replace(o,frame_id=2,landmarks=None,visibility=None,pose_present=False)
            journal.offer('process',observation=lost,frame=frame_for(lost),has_auxiliary=False)
            self.assertEqual(journal.close()['state'],'complete')
            rows=list(iter_journal(journal.directory)); restored,aux=observation(rows[0])
            self.assertEqual(restored,o);self.assertEqual(aux[0].landmarks,o.landmarks)
            self.assertEqual(rows[0]['frame']['full_image']['world'][15],list(p[15])+[.6,.8])
            self.assertEqual(rows[0]['frame']['presence']['image'][0],.9876543210987654)
            self.assertEqual(observation(rows[1])[0],lost)
            index=json.loads((journal.directory/INDEX).read_text())
            self.assertEqual(index['coverage']['recorded_frames'],2)
            self.assertEqual(len(index['chunks']),2)
            self.assertFalse(list(journal.directory.glob('*.part')))
            self.assertFalse(any(x.suffix in ('.avi','.mp4','.jpg') for x in journal.directory.iterdir()))

    def test_nonfinite_auxiliary_values_remain_explicit_and_do_not_stop_recording(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition')
            o=pose();frame=frame_for(o,auxiliary=True)
            raw=frame['processing_metadata'];raw['cross_pose']['landmarks']=list(map(list,o.landmarks))
            raw['cross_pose']['landmarks'][15][0]=float('nan')
            raw['cross_recovery']['presence'][16]=float('inf')
            raw['cross_recovery']['world'][15][2]=-float('inf')
            journal.offer('process',observation=o,frame=frame,has_auxiliary=True,raw_models=raw)
            self.assertEqual(journal.close()['state'],'complete')
            row=next(iter_journal(journal.directory))
            self.assertTrue(math.isnan(row['frame']['full_video']['landmarks'][15][0]))
            self.assertEqual(row['frame']['full_image']['presence'][16],float('inf'))
            self.assertEqual(row['frame']['full_image']['world'][15][2],-float('inf'))
            raw_text=gzip.decompress(next(journal.directory.glob('*.gz')).read_bytes()).decode()
            self.assertNotIn('NaN',raw_text);self.assertIn('"$float":"nan"',raw_text)

    def test_raw_auxiliary_override_preserves_capture_identity(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition')
            obs=pose(frame=55,stamp=9_000_000_000);frame=frame_for(obs,auxiliary=True)
            frame['processing_metadata']['camera_frame_id']=987
            frame['capture_attempt_id']=123
            # Live passes only the two raw model entries; base camera metadata
            # still belongs to the SessionRecorder frame. Raw NaN must override
            # the normalized None without erasing the source capture identity.
            raw={k:copy.deepcopy(frame['processing_metadata'][k]) for k in ('cross_pose','cross_recovery')}
            raw['cross_pose']['landmarks']=list(map(list,obs.landmarks))
            raw['cross_pose']['landmarks'][15][0]=float('nan')
            frame['processing_metadata']['cross_pose']['landmarks']=list(map(list,obs.landmarks))
            frame['processing_metadata']['cross_pose']['landmarks'][15][0]=None
            try:
                journal.offer('process',observation=obs,frame=frame,has_auxiliary=True,raw_models=raw)
                self.assertEqual(journal.close()['state'],'complete')
                row=next(iter_journal(journal.directory))
                self.assertEqual(row['frame']['camera_frame_id'],987)
                self.assertEqual(row['frame']['capture_attempt_id'],123)
                self.assertEqual(row['observation'][2:4],[55,9_000_000_000])
                self.assertTrue(math.isnan(row['frame']['full_video']['landmarks'][15][0]))
                self.assertEqual(row['frame']['full_image'],json.loads(_encode(raw['cross_recovery'])))
            finally:journal.close()

    def test_each_closed_chunk_valid_and_active_chunk_excluded(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition',chunk_rows=1,chunk_seconds=100)
            try:
                index_path=journal.directory/INDEX
                def published(second_active=False):
                    if not index_path.is_file():return False
                    value=json.loads(index_path.read_text())
                    return (len(value['chunks'])==1
                        and value['chunks'][0]['first_seq']==1
                        and (not second_active or value['health']['active_part']=='landmarks-000002.jsonl.gz.part'))
                journal.offer('header',test=1)
                # In-memory metadata may precede its atomic on-disk publication.
                # Wait for the exact exported snapshot, not any old active part.
                wait_until(published)
                journal.chunk_rows=100
                journal.offer('publisher',test=2)
                wait_until(lambda:published(second_active=True))
                index=json.loads(index_path.read_text())
                self.assertFalse(index['complete'])
                self.assertEqual(index['chunks'][0]['first_seq'],1)
                self.assertEqual(index['chunks'][0]['last_seq'],1)
                self.assertEqual(index['health']['active_part'],'landmarks-000002.jsonl.gz.part')
                self.assertEqual(len(list(iter_journal(journal.directory,require_complete=False))),1)
                with self.assertRaisesRegex(ValueError,'Incomplete'):list(iter_journal(journal.directory))
                self.assertEqual(journal.close()['state'],'complete')
                self.assertEqual(len(list(iter_journal(journal.directory))),2)
            finally:
                # Even a failed assertion must close owned handles before the
                # temporary directory exits, especially on Windows.
                journal.close()

    def test_queue_is_bounded_nonblocking_and_reports_exact_sequence_gaps(self):
        entered=threading.Event();release=threading.Event()
        original=LandmarkJournal._write_index
        def slow(self,*a,**kw):
            entered.set();release.wait(2);return original(self,*a,**kw)
        with tempfile.TemporaryDirectory() as td, patch.object(LandmarkJournal,'_write_index',slow):
            journal=LandmarkJournal(Path(td)/'recognition',queue_rows=2)
            self.assertTrue(entered.wait(1))
            start=time.perf_counter()
            accepted=[journal.offer('process',i=i) for i in range(100)]
            duration=time.perf_counter()-start
            self.assertLess(duration,.1);self.assertEqual(sum(accepted),2)
            release.set();health=journal.close()
            self.assertEqual(health['queue_dropped_rows'],98)
            self.assertEqual(health['state'],'incomplete')
            index=json.loads((journal.directory/INDEX).read_text())
            self.assertEqual(index['coverage']['gaps'],[dict(first_seq=3,last_seq=100,reason='queue_full')])
            with self.assertRaises(ValueError):list(iter_journal(journal.directory))
            self.assertEqual(len(list(iter_journal(journal.directory,require_complete=False))),2)

    def test_budget_never_publishes_partial_chunk_or_breaks_producer(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition',max_bytes=800,chunk_rows=1)
            journal.offer('header',small=True)
            wait_until(lambda:len(journal.chunks)==1)
            noise=[random.Random(9+i).random() for i in range(1000)]
            journal.offer('process',points=noise)
            wait_until(lambda:journal.budget_exhausted)
            self.assertFalse(journal.offer('process',points=noise))
            health=journal.close();index=json.loads((journal.directory/INDEX).read_text())
            self.assertEqual(health['state'],'budget_exhausted')
            self.assertEqual(health['budget_omitted_rows'],2)
            self.assertLessEqual(sum(c['bytes'] for c in index['chunks']),800)
            self.assertEqual(len(index['chunks']),1)
            self.assertEqual(index['coverage']['recorded_frames'],0)
            self.assertFalse(list(journal.directory.glob('*.part')))

    def test_write_error_is_diagnostic_only(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/'file';path.write_text('existing')
            journal=LandmarkJournal(path/'recognition')
            wait_until(lambda:journal.error is not None)
            self.assertFalse(journal.offer('process',x=1))
            self.assertEqual(journal.close()['state'],'error')
            self.assertEqual(path.read_text(),'existing')

    def test_corruption_and_path_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition');journal.offer('header',x=1);journal.close()
            path=journal.directory/INDEX;index=json.loads(path.read_text())
            chunk=journal.directory/index['chunks'][0]['file'];chunk.write_bytes(b'broken')
            with self.assertRaises(ValueError):list(iter_journal(journal.directory))
            index['chunks'][0]['file']='../escape.jsonl.gz';path.write_text(json.dumps(index))
            with self.assertRaisesRegex(ValueError,'Unsafe'):list(iter_journal(journal.directory))

    def test_reader_rejects_false_complete_counts_oversize_and_symlinks(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition');journal.offer('header',x=1);journal.close()
            path=journal.directory/INDEX;original=json.loads(path.read_text())
            broken=json.loads(json.dumps(original));broken['coverage']['written_rows']=0
            path.write_text(json.dumps(broken))
            with self.assertRaisesRegex(ValueError,'coverage'):list(iter_journal(journal.directory))
            path.write_text(json.dumps(original))
            chunk=journal.directory/original['chunks'][0]['file']
            with chunk.open('wb') as stream:stream.truncate(9*1024*1024)
            with self.assertRaisesRegex(ValueError,'size'):list(iter_journal(journal.directory))
            chunk.unlink();target=Path(td)/'unrelated';target.write_bytes(b'secret')
            try:chunk.symlink_to(target)
            except (OSError,NotImplementedError):return  # Windows runners may deny symlink privileges.
            with self.assertRaisesRegex(ValueError,'symlink'):list(iter_journal(journal.directory))
            self.assertEqual(target.read_bytes(),b'secret')

    def test_clock_wrapper_preserves_source_and_thread_isolation(self):
        values=iter(range(100));clock=ObservedClock(lambda:next(values));clock.begin()
        self.assertEqual(clock(),0)
        child=[];thread=threading.Thread(target=lambda:child.append(clock()));thread.start();thread.join()
        self.assertEqual(clock(),2);self.assertEqual(clock.end(),(0,2));self.assertEqual(child,[1])


class OperationReplayTests(unittest.TestCase):
    def test_exact_full_rate_state_events_context_reset_pause_and_watchdog(self):
        with tempfile.TemporaryDirectory() as td:
            journal=LandmarkJournal(Path(td)/'recognition',queue_rows=2048)
            stamp=[1_000_000_000]
            def source():stamp[0]+=7;return stamp[0]
            clock=ObservedClock(source);engine=PersonalGestureEngine(movement=True,clock=clock)
            journal.offer('header',schema='redbool.engine-operations',version=1,engine=type(engine).__name__,
                config=asdict(engine.config),run_id=engine.run_id,movement=True)
            record_operation(journal,clock,engine,'calibrate',engine.request_calibration,after_frame_id=0)
            events=[];i=0
            for kind,n in [('guard',50),('cross',35),('guard',30),('left_raise',25),('guard',25),('block',15),('guard',10)]:
                for _ in range(n):
                    i+=1;capture=1_000_000_000+i*33_333_333;stamp[0]=capture+1000
                    obs=pose(kind,i,capture);frame=frame_for(obs,auxiliary=True)
                    record_operation(journal,clock,engine,'process',lambda:engine.process(obs,special_observation=(obs,obs)),
                        observation=obs,frame=frame,has_auxiliary=True)
                    context=dict(context=dict(run='t1',action='HIGH_KICK',phase='PRACTICE',sent_ns=capture),context_ns=capture+2000)
                    def publish():
                        engine.tick();ev=engine.drain_events();engine.tutorial_context(context['context'],context['context_ns']);return ev
                    events+=record_operation(journal,clock,engine,'publisher',publish,context=context)
                    engine.take_logs()
            self.assertTrue(engine.calibrated)
            self.assertIn('SPECIAL',[ev.reason.get('gesture') for ev in events])
            self.assertIn('LEFT_RAISE',[ev.reason.get('gesture') for ev in events])
            # Freeze input long enough to exercise stale-control and event expiry.
            stamp[0]+=600_000_000
            def late():engine.tick();return engine.drain_events()
            record_operation(journal,clock,engine,'publisher',late,context={})
            self.assertNotEqual(engine.control.tracking,'VALID')
            record_operation(journal,clock,engine,'movement',engine.toggle_movement)
            def pause():engine.request_calibration();engine.calibrating=False
            record_operation(journal,clock,engine,'pause',pause,after_frame_id=i)
            record_operation(journal,clock,engine,'calibrate',engine.request_calibration,after_frame_id=i)
            expected=engine.profile();self.assertEqual(journal.close()['state'],'complete')
            result=verify_replay(journal.directory)
            self.assertTrue(result['passed'],result['mismatches'][:1])
            self.assertEqual(result['frames'],i);self.assertEqual(result['events'],len(events))
            self.assertEqual(json.loads(_encode(result['final_personal_profile'])),json.loads(_encode(expected)))

if __name__=='__main__':unittest.main()
