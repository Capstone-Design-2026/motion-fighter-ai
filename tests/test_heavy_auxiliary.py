"""No native model, camera, network, or production files used."""
from dataclasses import replace
import importlib.util
from pathlib import Path
import sys
import threading
import time
import unittest

FILE=Path(__file__).resolve().parents[1]/'src/redbool_demo/heavy_auxiliary.py'
spec=importlib.util.spec_from_file_location('audit_heavy_auxiliary',FILE)
m=importlib.util.module_from_spec(spec);sys.modules[spec.name]=m;spec.loader.exec_module(m)


class Clock:
    def __init__(self):self.now=1_000_000_000
    def __call__(self):return self.now


class Model:
    def __init__(self):self.closed=threading.Event()
    def close(self):self.closed.set()


class HeavyAuxiliaryTests(unittest.TestCase):
    def setUp(self):
        self.clock=Clock();self.model=Model();self.calls=[]
        self.h=None;self.fid=30;self.capture=0

    def helper(self,loader=None,infer=None,warm=None):
        self.h=m.BoundedHeavyAuxiliary(loader or (lambda:self.model),
            infer or (lambda model,image:self.calls.append(image) or image),warm=warm,clock_ns=self.clock)
        self.addCleanup(self.h.close)
        return self.h

    def healthy(self,n=30,gap=33,age=15,calibrated=True,overload=False):
        for _ in range(n):
            self.clock.now+=gap*1_000_000
            self.capture=self.clock.now
            self.h.observe_performance(capture_ns=self.capture,primary_end_ns=self.capture+5_000_000,
                processing_end_ns=self.capture+age*1_000_000,calibrated=calibrated,overload=overload)
        self.clock.now=self.capture+age*1_000_000

    def wait(self,predicate,timeout=1):
        end=time.monotonic()+timeout
        while not predicate() and time.monotonic()<end:time.sleep(.001)
        self.assertTrue(predicate())

    def ready(self):
        self.healthy();self.wait(lambda:self.h.snapshot()['ready'])

    def key(self,gap=33,**kw):
        self.fid+=1;self.capture+=gap*1_000_000;self.clock.now=self.capture+5_000_000
        return m.FrameKey('P1','epoch',self.fid,self.capture,640,480,**kw)

    def plan(self,key=None,**kw):
        key=key or self.key()
        options=dict(calibrated=True,owned=True,latched=False,pending_since_ns=key.capture_ns-1_000_000_000)
        options.update(kw)
        return self.h.plan(key,**options)

    def test_no_model_work_before_thirty_healthy_calibrated_frames(self):
        self.helper();self.healthy(29)
        self.assertIsNone(self.h.thread)
        self.healthy(1);self.wait(lambda:self.h.ready)
        self.assertTrue(self.h.thread.daemon);self.assertEqual(self.calls,[])

    def test_slow_source_uncalibrated_and_overload_never_load(self):
        for options in (dict(gap=90),dict(age=70),dict(calibrated=False),dict(overload=True)):
            with self.subTest(options=options):
                h=self.helper();self.healthy(35,**options)
                self.assertIsNone(h.thread)
                self.assertFalse(h.healthy)

    def test_loading_and_warmup_are_not_on_the_inference_caller(self):
        entered=threading.Event();release=threading.Event();threads=[]
        def warm(model):threads.append(threading.current_thread().name);entered.set();release.wait(2)
        h=self.helper(warm=warm)
        self.healthy();self.assertTrue(entered.wait(1))
        self.assertTrue(h.snapshot()['warming']);self.assertFalse(h.ready)
        self.assertIsNone(self.plan())
        release.set();self.wait(lambda:h.ready)
        self.assertEqual(threads,['optional-heavy-aux'])

    def test_pending_gate_and_latched_ownership_lifecycle(self):
        h=self.helper();self.ready()
        self.assertIsNone(self.plan(owned=False))
        key=self.key();self.assertIsNone(self.plan(key,pending_since_ns=key.capture_ns-(h.PENDING_NS-1)))
        self.assertIsNone(self.plan(latched=True))
        self.assertIsNotNone(self.plan())
        self.assertIsNotNone(self.plan(latched=True,pending_since_ns=None))
        self.assertIsNone(self.plan(owned=False))
        self.assertIsNone(self.plan(latched=True))

    def test_normal_fired_cross_stops_next_frame_jobs_and_restarts_pending_gate(self):
        h=self.helper();self.ready();ticket=self.plan()
        self.assertTrue(h.submit(ticket,'pending-cross'));self.assertIsNotNone(h.result(ticket))
        owns=m.refinement_owned(owned=True,latched=True,refinement_latched=False)
        self.assertIsNone(self.plan(owned=owns,latched=True))
        self.assertFalse(h.active);self.assertTrue(h.ready)
        self.assertEqual(self.calls,['pending-cross'])
        key=self.key()
        self.assertIsNone(self.plan(key,owned=m.refinement_owned(owned=True,latched=False,refinement_latched=False),
                                   pending_since_ns=key.capture_ns-(h.PENDING_NS-1)))
        self.assertIsNotNone(self.plan())

    def test_refined_repeat_latch_keeps_helper_after_ordinary_ownership_clears(self):
        h=self.helper();self.ready();ticket=self.plan()
        self.assertTrue(h.submit(ticket,'refined-cross'));self.assertIsNotNone(h.result(ticket))
        owns=m.refinement_owned(owned=False,latched=False,refinement_latched=True)
        ticket=self.plan(owned=owns,latched=True,pending_since_ns=None)
        self.assertIsNotNone(ticket);self.assertTrue(h.submit(ticket,'release-evidence'))
        self.assertIsNotNone(h.result(ticket))
        self.assertIsNone(self.plan(owned=m.refinement_owned(owned=False,latched=False,refinement_latched=False)))
        self.assertEqual(self.calls,['refined-cross','release-evidence'])

    def test_normal_fire_does_not_reuse_previously_completed_optional_pose(self):
        h=self.helper();self.ready();ticket=self.plan()
        self.assertTrue(h.submit(ticket,'old-pose'));self.wait(lambda:h.completed is not None)
        self.assertIsNone(self.plan(owned=m.refinement_owned(owned=True,latched=True,refinement_latched=False),latched=True))
        self.assertIsNone(h.result(ticket))
        new=self.plan();self.assertTrue(h.submit(new,'new-pose'))
        result=h.result(new);self.assertIsNotNone(result);self.assertEqual(result.value,'new-pose')
        self.assertEqual(result.key,new.key)

    def test_same_frame_result_carries_original_identity_and_is_single_use(self):
        h=self.helper();self.ready();ticket=self.plan();image=object()
        self.assertTrue(h.submit(ticket,image))
        result=h.result(ticket)
        self.assertIsNotNone(result)
        self.assertEqual(result.key,ticket.key);self.assertIs(result.value,image)
        self.assertEqual(result.started_ns,self.clock.now)
        self.assertEqual(result.finished_ns,self.clock.now)
        self.assertIsNone(h.result(ticket));self.assertEqual(self.calls,[image])
        self.assertFalse(h.submit(ticket,'duplicate-submit'))

    def test_wrong_frame_actor_epoch_dimensions_mirror_and_generation_never_match(self):
        h=self.helper();self.ready();ticket=self.plan();self.assertTrue(h.submit(ticket,'image'))
        self.wait(lambda:h.completed is not None)
        for field,value in [('actor_id','P2'),('capture_epoch','other'),('frame_id',999),
                ('capture_ns',0),('width',320),('height',240),('source_mirrored',True),('calibration_generation',2)]:
            with self.subTest(field=field):
                bad=replace(ticket,key=replace(ticket.key,**{field:value}))
                self.assertIsNone(h.result(bad))
        self.assertIsNotNone(h.result(ticket))

    def test_wait_is_bounded_and_busy_worker_does_not_queue_new_images(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'first')
        self.assertTrue(entered.wait(1))
        before=time.monotonic();self.assertIsNone(h.result(ticket))
        self.assertLess(time.monotonic()-before,.10)
        second=self.plan();self.assertFalse(h.submit(second,'second'))
        self.assertEqual(h.snapshot()['queued'],0)
        release.set();self.wait(lambda:h.running is None)
        self.assertIsNone(h.result(ticket));self.assertIsNone(h.result(second))

    def test_reset_during_model_work_discards_old_result(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'before-C')
        self.assertTrue(entered.wait(1));h.reset('calibrate');release.set()
        self.wait(lambda:h.running is None)
        self.assertIsNone(h.result(ticket));self.assertFalse(h.healthy)
        self.assertFalse(h.active)

    def test_source_gap_and_person_changes_invalidate_without_rearming_normal_pose(self):
        h=self.helper();self.ready();ticket=self.plan();h.submit(ticket,'old');self.wait(lambda:h.completed is not None)
        old_generation=h.generation
        self.assertIsNone(self.plan(self.key(gap=101),latched=True))
        self.assertGreater(h.generation,old_generation);self.assertTrue(h.active)
        self.assertIsNone(h.result(ticket))
        self.healthy(30);self.wait(lambda:h.healthy)
        old_generation=h.generation
        changed=replace(self.key(),capture_epoch='new-person')
        self.assertIsNone(self.plan(changed,latched=True))
        self.assertGreater(h.generation,old_generation);self.assertFalse(h.active)

    def test_duplicate_source_and_uncalibrated_call_discard_tickets(self):
        h=self.helper();self.ready();ticket=self.plan();h.submit(ticket,'old');self.wait(lambda:h.completed is not None)
        self.assertIsNone(self.plan(ticket.key))
        self.assertIsNone(h.result(ticket))
        h.reset('calibrate')
        self.healthy(30);self.wait(lambda:h.healthy)
        ticket=self.plan();h.submit(ticket,'old');self.wait(lambda:h.completed is not None)
        self.assertIsNone(self.plan(calibrated=False));self.assertIsNone(h.result(ticket))

    def test_original_capture_deadline_is_not_replaced_with_inference_completion(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'late')
        self.assertTrue(entered.wait(1));self.clock.now=ticket.key.capture_ns+121_000_000
        release.set();self.wait(lambda:h.running is None)
        self.assertIsNone(h.result(ticket));self.assertEqual(h.counts['late_result'],1)

    def test_three_model_errors_disable_only_optional_helper(self):
        def infer(model,image):raise RuntimeError('synthetic model error')
        h=self.helper(infer=infer);self.ready()
        for _ in range(3):
            ticket=self.plan();self.assertIsNotNone(ticket);self.assertTrue(h.submit(ticket,'bad'))
            self.assertIsNone(h.result(ticket))
        self.assertTrue(h.disabled);self.assertEqual(h.disable_reason,'model_error')
        h.reset('calibrate');self.assertTrue(h.disabled)
        self.assertIsNone(self.plan())

    def test_model_load_failure_does_not_retry_or_escape_to_caller(self):
        def load():raise RuntimeError('no model')
        h=self.helper(loader=load);self.healthy();self.wait(lambda:h.disabled)
        self.assertEqual(h.disable_reason,'model_load_error')
        thread=h.thread;self.healthy(35);self.assertIs(h.thread,thread)

    def test_owner_close_is_bounded_even_if_native_call_is_stuck(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'work')
        self.assertTrue(entered.wait(1))
        begin=time.monotonic();status=h.close(timeout=.01)
        self.assertLess(time.monotonic()-begin,.10);self.assertTrue(status['worker_alive'])
        self.assertIsNone(h.result(ticket));release.set()
        self.wait(lambda:self.model.closed.is_set())
        self.assertTrue(h.thread.daemon)

    def test_overload_latch_during_inference_discards_result_and_stays_disabled(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'work')
        self.assertTrue(entered.wait(1))
        h.observe_performance(capture_ns=self.capture,primary_end_ns=self.capture+5_000_000,
            processing_end_ns=self.capture+15_000_000,calibrated=True,overload=True)
        release.set();self.wait(lambda:h.running is None)
        self.assertIsNone(h.result(ticket));self.assertTrue(h.disabled)
        h.reset('calibrate');self.assertTrue(h.disabled)

    def test_repeated_busy_or_deadline_failures_disable_optional_worker(self):
        entered=threading.Event();release=threading.Event()
        def infer(model,image):entered.set();release.wait(2);return image
        h=self.helper(infer=infer);self.ready();ticket=self.plan();h.submit(ticket,'first')
        self.assertTrue(entered.wait(1));self.assertIsNone(h.result(ticket))
        for _ in range(2):
            other=self.plan();self.assertIsNotNone(other)
            self.assertFalse(h.submit(other,'never-queued'))
        self.assertTrue(h.disabled);self.assertEqual(h.snapshot()['queued'],0)
        release.set();self.wait(lambda:h.running is None)
        self.assertIsNone(h.result(ticket))

    def test_nonfinite_or_repeated_performance_samples_cannot_warm(self):
        h=self.helper();self.healthy(29)
        for bad in (float('nan'),float('inf'),-1,None,'invalid'):
            self.assertFalse(h.observe_performance(capture_ns=bad,primary_end_ns=1,
                processing_end_ns=2,calibrated=True,overload=False))
        self.assertIsNone(h.thread)
        for _ in range(40):
            h.observe_performance(capture_ns=1_000,primary_end_ns=1_005,
                processing_end_ns=1_010,calibrated=True,overload=False)
        self.assertIsNone(h.thread)


if __name__=='__main__':unittest.main()
