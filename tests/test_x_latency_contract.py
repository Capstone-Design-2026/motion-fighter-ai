"""Selected 300ms X-refinement scheduling contract, with no devices or native models.

Portable repository test: PYTHONPATH=src:tests python -m unittest -v test_x_latency_contract
The existing test_chest_special.pose fixture supplies synthetic landmarks.
The actual worker and engine run, but a deterministic fake model replaces native
inference. Fake completions are synchronized to avoid CI thread-scheduling noise;
Real 120ms/10ms deadline behavior is covered by test_heavy_auxiliary and native A/B.
These tests do not measure model accuracy, camera throughput, or gameplay latency.
"""
from dataclasses import replace
import time
import unittest

from redbool_demo.heavy_auxiliary import BoundedHeavyAuxiliary, FrameKey, refinement_owned
from redbool_demo.six_engine import SixGestureEngine
from redbool_demo.special import CrossSpecial
from test_chest_special import pose

MS = 1_000_000

class Clock:
    value = 0
    def __call__(self):
        return self.value


class Model:
    def close(self):
        pass


class NormalXStopTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.engine = SixGestureEngine()
        self.engine.request_calibration()
        self.helper = BoundedHeavyAuxiliary(Model, lambda model, observation: observation,
                                            clock_ns=self.clock)
        self.addCleanup(self.helper.close)
        self.pending_since = None
        self.frame = 0
        self.events = []
        self.rows = []
        self.feed(100, 'guard')
        until = time.monotonic() + 1
        while not self.helper.ready and time.monotonic() < until:
            time.sleep(.001)
        self.assertTrue(self.engine.calibrated)
        self.assertTrue(self.helper.ready)
        self.assertTrue(self.helper.healthy)

    def feed(self, count, kind, extra=None):
        new_rows = []
        for _ in range(count):
            self.frame += 1
            stamp = self.frame * 33_333_333
            source = pose(kind, self.frame, stamp)
            if kind == 'ambiguous':
                # Base models preserve inward ownership but cannot confirm X.
                # This is the pending state in which the live helper can start;
                # ordinary guard would release ownership before the pending gate.
                points = list(source.landmarks)
                points[15] = (.515, .40, 0.)
                points[16] = (.485, .40, 0.)
                source = replace(source, landmarks=tuple(points))
            before = dict(owned=self.engine.special.owned, latched=self.engine.special.latched,
                          refinement_latched=self.engine.cross_refinement_latched)
            # Same actual pure function and ordering as live: previous engine
            # result is inspected before current primary inference.
            eligible = refinement_owned(**before)
            latched = before['latched'] or before['refinement_latched']
            if not eligible:
                self.pending_since = None
            elif self.pending_since is None:
                self.pending_since = self.engine.last_capture
            self.clock.value = stamp + 5 * MS
            key = FrameKey(source.actor_id, source.capture_epoch, source.frame_id,
                           stamp, source.width, source.height)
            ticket = self.helper.plan(key, calibrated=self.engine.calibrated,
                owned=eligible, latched=latched, pending_since_ns=self.pending_since)
            submitted = False
            result = None
            if ticket is not None:
                value = pose(extra, self.frame, stamp) if extra else None
                submitted = self.helper.submit(ticket, value)
                if submitted:
                    # This fixture tests eligibility/state transitions, not OS
                    # scheduling speed. Let its immediate fake model finish
                    # before consuming the actual same-frame worker result.
                    with self.helper.condition:
                        completed = self.helper.condition.wait_for(
                            lambda: self.helper.completed is not None or self.helper.disabled,
                            timeout=2.)
                    self.assertTrue(completed, 'Fake worker did not complete')
                    result = self.helper.result(ticket)
                    self.assertIsNotNone(result)
                    self.assertEqual(result.key, key)
            auxiliary = (source, source)
            if result is not None and result.value is not None:
                auxiliary += (result.value,)
            self.clock.value = stamp + 15 * MS
            state = self.engine.process(source, now_ns=self.clock.value,
                                        special_observation=auxiliary)
            events = self.engine.drain_events(self.clock.value)
            self.events.extend(events)
            self.engine.take_logs()
            self.helper.observe_performance(capture_ns=stamp,
                primary_end_ns=stamp + 10 * MS, processing_end_ns=self.clock.value,
                calibrated=self.engine.calibrated, overload=False)
            row = dict(frame=self.frame, before=before, submitted=submitted,
                       pending_since=self.pending_since, state=state, events=events)
            self.rows.append(row)
            new_rows.append(row)
        return new_rows

    def specials(self):
        return [event for event in self.events if event.reason.get('gesture') == 'SPECIAL']

    def test_eligibility_truth_table_only_pending_or_external_repeat_lock(self):
        for owned in (False, True):
            for latched in (False, True):
                for external in (False, True):
                    with self.subTest(owned=owned, latched=latched, external=external):
                        expected = external or (owned and not latched)
                        self.assertEqual(refinement_owned(owned=owned, latched=latched,
                            refinement_latched=external), expected)

    def test_normal_x_stops_extra_work_from_frame_after_first_event(self):
        cross_rows = self.feed(100, 'cross', 'cross')
        self.assertEqual(len(self.specials()), 1)
        fired = self.specials()[0].source_frame_id
        self.assertTrue(any(row['submitted'] for row in cross_rows if row['frame'] <= fired))
        self.assertFalse(any(row['submitted'] for row in cross_rows if row['frame'] > fired))
        self.assertFalse(self.engine.cross_refinement_latched)
        # A faulty optional model would report X throughout this real block;
        # eligibility now prevents those unnecessary inference requests entirely.
        block_rows = self.feed(30, 'block', 'cross')
        self.assertFalse(any(row['submitted'] for row in block_rows))
        self.assertTrue(all(row['state'].block for row in block_rows[12:]))
        self.assertIsNone(self.pending_since)

    def test_new_normal_x_after_release_starts_a_new_pending_interval(self):
        self.feed(40, 'cross', 'cross')
        self.feed(20, 'guard', 'cross')
        self.assertFalse(self.engine.special.owned)
        self.assertFalse(self.helper.active)
        onset = (self.frame + 1) * 33_333_333
        rows = self.feed(40, 'cross', 'cross')
        submitted = [row for row in rows if row['submitted']]
        self.assertTrue(submitted)
        self.assertGreaterEqual(submitted[0]['frame'] * 33_333_333 - onset,
                                self.helper.PENDING_NS)
        self.assertEqual(len(self.specials()), 2)

    def test_refined_x_keeps_helper_when_only_external_memory_remains(self):
        self.feed(3, 'cross')
        self.feed(65, 'ambiguous', 'cross')
        self.assertEqual(len(self.specials()), 1)
        self.assertTrue(self.engine.cross_refinement_latched)
        # Missing optional detections permit ordinary base-model recovery but
        # are not trusted external X-release evidence.
        rows = self.feed(30, 'guard')
        self.assertFalse(self.engine.special.owned)
        self.assertFalse(self.engine.special.latched)
        self.assertTrue(self.engine.cross_refinement_latched)
        self.assertTrue(all(row['submitted'] for row in rows[-5:]))
        self.assertTrue(self.helper.active)
        self.feed(30, 'guard', 'cross')
        self.assertEqual(len(self.specials()), 1)

    def test_verified_external_release_stops_helper_and_restarts_next_delay(self):
        self.feed(3, 'cross')
        self.feed(65, 'ambiguous', 'cross')
        self.feed(20, 'guard', 'guard')
        self.assertFalse(self.engine.cross_refinement_latched)
        self.assertFalse(self.engine.special.owned)
        self.assertFalse(self.helper.active)
        self.assertIsNone(self.pending_since)
        onset = (self.frame + 1) * 33_333_333
        self.feed(3, 'cross')
        rows = self.feed(65, 'ambiguous', 'cross')
        first = next(row for row in rows if row['submitted'])
        self.assertGreaterEqual(first['frame'] * 33_333_333 - onset, self.helper.PENDING_NS)
        self.assertEqual(len(self.specials()), 2)


class EarlyActivationContractTests(unittest.TestCase):
    def helper(self):
        self.now = 1_000 * MS
        self.loads = []
        helper = BoundedHeavyAuxiliary(
            lambda: self.loads.append('load') or object(),
            lambda model, image: image, clock_ns=lambda: self.now)
        self.addCleanup(helper.close)
        return helper

    def key(self, frame, capture):
        self.now = capture + 5 * MS
        return FrameKey('P1', 'run', frame, capture, 640, 480)

    def test_selected_300ms_policy_keeps_original_timing_contract(self):
        self.assertEqual(BoundedHeavyAuxiliary.PENDING_NS, 300 * MS)
        self.assertEqual(BoundedHeavyAuxiliary.WINDOW, 30)
        self.assertEqual(BoundedHeavyAuxiliary.HEALTHY_AGE_NS, 45 * MS)
        self.assertEqual(BoundedHeavyAuxiliary.HEALTHY_GAP_NS, 60 * MS)
        self.assertEqual(BoundedHeavyAuxiliary.WAIT_NS, 10 * MS)
        self.assertEqual(BoundedHeavyAuxiliary.EVIDENCE_NS, 120 * MS)
        self.assertEqual(CrossSpecial.hold_ns, 700 * MS)
        self.assertEqual(CrossSpecial.return_ns, 180 * MS)
        engine = SixGestureEngine()
        self.assertEqual((engine.config.max_gap_ms, engine.config.evidence_age_ms,
                          engine.config.safety_timeout_ms), (100, 120, 150))

    def test_300ms_boundary_does_not_change_evidence_or_loading_gates(self):
        helper=self.helper();helper.ready=helper.healthy=True
        start=1000*MS
        self.assertIsNone(helper.plan(self.key(1,start+300*MS-1),
            calibrated=True,owned=True,latched=False,pending_since_ns=start))
        self.assertIsNotNone(helper.plan(self.key(2,start+300*MS),
            calibrated=True,owned=True,latched=False,pending_since_ns=start))
        self.assertEqual(self.loads,[])

    def test_brief_250ms_ownership_does_not_start_optional_work(self):
        helper = self.helper()
        # Isolate plan gating after a model was already prepared. No thread is
        # started and no infer call is made by this test fixture.
        helper.ready = helper.healthy = True
        start = 1000 * MS
        frame = 0
        for elapsed in range(0, 251, 25):
            frame += 1
            ticket = helper.plan(self.key(frame, start + elapsed * MS),
                                 calibrated=True, owned=True, latched=False,
                                 pending_since_ns=start)
            self.assertIsNone(ticket)
        frame += 1
        self.assertIsNone(helper.plan(self.key(frame, start + 275 * MS),
            calibrated=True, owned=False, latched=False, pending_since_ns=None))
        self.assertFalse(helper.active)
        self.assertEqual(helper.snapshot()['counts'].get('submitted', 0), 0)

    def test_capture_gap_cannot_use_old_pending_duration_to_skip_health_gate(self):
        helper = self.helper()
        helper.ready = helper.healthy = True
        start = 3000 * MS
        self.assertIsNotNone(helper.plan(self.key(1, start),
            calibrated=True, owned=True, latched=False,
            pending_since_ns=start - 2000 * MS))
        self.assertIsNone(helper.plan(self.key(2, start + 101 * MS),
            calibrated=True, owned=True, latched=False, pending_since_ns=start - 500 * MS))
        self.assertFalse(helper.healthy)

    def test_low_pc_cadence_and_processing_cost_never_start_model(self):
        for gap, age in ((88, 100), (33, 80), (100, 20)):
            with self.subTest(gap=gap, age=age):
                helper = self.helper()
                for frame in range(1, 121):
                    capture = (1000 + frame * gap) * MS
                    helper.observe_performance(capture_ns=capture,
                        primary_end_ns=capture + min(age, 35) * MS,
                        processing_end_ns=capture + age * MS,
                        calibrated=True, overload=False)
                self.assertFalse(helper.healthy)
                self.assertIsNone(helper.thread)
                self.assertEqual(self.loads, [])

    def test_existing_overload_stays_disabled_after_c_reset(self):
        helper = self.helper()
        helper.observe_performance(capture_ns=self.now, primary_end_ns=self.now,
            processing_end_ns=self.now, calibrated=True, overload=True)
        helper.reset('calibrate')
        helper.ready = helper.healthy = True
        key = self.key(1, 2000 * MS)
        self.assertIsNone(helper.plan(key, calibrated=True, owned=True, latched=False,
                                     pending_since_ns=1000 * MS))
        self.assertTrue(helper.disabled)
        self.assertIsNone(helper.thread)

    def test_no_initial_ownership_means_heavy_cannot_create_a_special(self):
        engine = SixGestureEngine()
        engine.request_calibration()
        events = []
        for frame in range(1, 155):
            source = pose('guard', frame, frame * 33_333_333)
            # Even a persistently wrong optional X cannot acquire an initial
            # candidate when every primary/base observation is ordinary guard.
            heavy = pose('cross', frame, source.capture_ns)
            engine.process(source, now_ns=source.capture_ns,
                           special_observation=(source, source, heavy))
            events.extend(engine.drain_events(source.capture_ns))
            engine.take_logs()
        self.assertFalse(events)
        self.assertFalse(engine.special.owned)

    def test_short_heavy_positive_interval_does_not_complete_700ms_hold(self):
        engine = SixGestureEngine()
        engine.request_calibration()
        events = []
        stages = [('guard', None)] * 55 + [('cross', None)] * 3
        stages += [('guard', 'cross')] * 10 + [('guard', 'guard')] * 20
        for frame, (kind, extra) in enumerate(stages, 1):
            source = pose(kind, frame, frame * 33_333_333)
            aux = (source, source)
            if extra:
                aux += (pose(extra, frame, source.capture_ns),)
            engine.process(source, now_ns=source.capture_ns, special_observation=aux)
            events.extend(engine.drain_events(source.capture_ns))
            engine.take_logs()
        self.assertFalse(events)

if __name__ == '__main__':
    unittest.main()
