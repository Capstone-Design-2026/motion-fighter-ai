"""Fresh capture continuity never extends the published 150ms input lease."""
import unittest
from dataclasses import replace
from action_engine.types import ActionEvent
from redbool_demo.six_engine import SixGestureEngine
from test_chest_special import pose

MS=1_000_000

class BoundedContinuityTests(unittest.TestCase):
    def ready_engine(self):
        engine=SixGestureEngine(movement=True);engine.request_calibration()
        for i in range(1,51):
            obs=pose(frame=i,stamp=i*33*MS)
            engine.process(obs,now_ns=obs.capture_ns);engine.drain_events(obs.capture_ns)
        self.assertTrue(engine.ready)
        return engine

    def step(self,engine,kind='guard',*,gap=90,age=90,mutate=None,drain=True):
        obs=pose(kind,engine.last_frame+1,engine.last_capture+gap*MS)
        if mutate:obs=mutate(obs)
        arrival=obs.capture_ns+age*MS
        for now in range(engine.last_now+10*MS,arrival,10*MS):engine.tick(now)
        result=engine.process(obs,now_ns=arrival)
        events=engine.drain_events(arrival) if drain else []
        return result,events

    def test_slow_but_fresh_continuous_guard_keeps_private_rearm(self):
        engine=self.ready_engine()
        for _ in range(20):
            state,events=self.step(engine)
            self.assertEqual(state.tracking,'VALID');self.assertTrue(engine.ready)
            self.assertFalse(events)
        self.assertEqual(engine.config.max_gap_ms,100)
        self.assertEqual(engine.config.evidence_age_ms,120)
        self.assertEqual(engine.config.safety_timeout_ms,150)

    def test_output_expiry_is_immediate_while_private_state_is_bounded(self):
        engine=self.ready_engine();stamp=engine.last_capture
        engine.control=replace(engine.control,block=True,movement='LEFT',movement_strength=1.)
        engine.outbox.offer(ActionEvent('pending',*engine.identity,engine.last_frame,stamp,stamp,stamp+500*MS,'ATTACK','LEFT',{},'test'))
        state=engine.tick(stamp+150*MS)
        self.assertEqual((state.tracking,state.block,state.movement,state.attack_state),('LOST',False,'NEUTRAL','UNAVAILABLE'))
        self.assertEqual(state.movement_strength,0);self.assertFalse(engine.ready)
        self.assertFalse(engine.outbox.pending);self.assertTrue(engine._continuity_pending)
        self.assertEqual(engine.drain_events(stamp+160*MS),[])
        engine.tick(stamp+220*MS)
        self.assertFalse(engine._continuity_pending)
        self.assertTrue(all(u.state=='UNAVAILABLE' for u in engine.attack.units.values()))
        self.assertIsNone(engine.signals.previous);self.assertIsNone(engine.route_signals.previous)
        self.assertIsNone(engine.vertical_signals.previous);self.assertFalse(engine.raise_armed['LEFT'])

    def test_actual_capture_gap_stale_or_invalid_arm_does_not_resume_history(self):
        for gap,age,mutation in (
            (101,90,None),(90,121,None),
            (90,90,lambda o:replace(o,pose_present=False)),
            (90,90,lambda o:replace(o,visibility=tuple(0. if i==15 else v for i,v in enumerate(o.visibility))))):
            with self.subTest(gap=gap,age=age,invalid=mutation is not None):
                engine=self.ready_engine();state,events=self.step(engine,gap=gap,age=age,mutate=mutation)
                self.assertFalse(engine.ready);self.assertFalse(events)
                self.assertFalse(engine._continuity_pending)
                self.assertFalse(any(engine.raise_armed.values()))
                self.assertFalse(any(u.state=='READY' for u in engine.attack.units.values()))

    def test_new_person_and_explicit_calibration_clear_private_history(self):
        for new_person in (False,True):
            engine=self.ready_engine();engine.tick(engine.last_capture+150*MS)
            if new_person:
                obs=replace(pose(frame=1,stamp=engine.last_capture+90*MS),capture_epoch='new-person')
                engine.process(obs,now_ns=obs.capture_ns+90*MS)
            else:engine.request_calibration()
            self.assertFalse(engine._continuity_pending);self.assertFalse(engine.calibrated)
            self.assertFalse(engine.ready);self.assertFalse(engine.outbox.pending)
            self.assertTrue(all(u.baseline is None for u in engine.attack.units.values()))

    def test_held_block_can_accumulate_but_expired_output_never_stays_blocked(self):
        engine=self.ready_engine();held=[]
        for _ in range(12):
            state,events=self.step(engine,'block');held.append(state.block);self.assertFalse(events)
            self.assertFalse(engine.tick(engine.last_capture+150*MS).block)
        self.assertTrue(any(held))
        state,_=self.step(engine,'guard')
        self.assertFalse(state.block)

    def test_fresh_cross_fires_once_no_revival_after_publisher_expiry(self):
        engine=self.ready_engine();events=[]
        for _ in range(18):
            _,new=self.step(engine,'cross');events.extend(new)
        self.assertEqual([e.reason['gesture'] for e in events],['SPECIAL'])
        for event in events:
            self.assertLessEqual(event.committed_ns-event.source_capture_ns,120*MS)
            self.assertEqual(event.expires_ns-event.committed_ns,150*MS)
        # The output lease can expire repeatedly; holding a fired X is not a new X.
        for _ in range(20):self.assertFalse(self.step(engine,'cross')[1])
        self.assertTrue(engine.special.latched)

    def test_undelivered_cross_is_dropped_and_never_reoffered(self):
        engine=self.ready_engine();pending=None
        for _ in range(18):
            self.step(engine,'cross',drain=False)
            if engine.outbox.pending:
                pending=next(iter(engine.outbox.pending.values()));break
        self.assertIsNotNone(pending)
        engine.tick(engine.last_capture+150*MS)
        self.assertFalse(engine.outbox.pending)
        self.assertFalse(engine.drain_events(pending.expires_ns+1))
        self.assertFalse(self.step(engine,'cross',gap=180)[1])
        for _ in range(20):self.assertFalse(self.step(engine,'cross')[1])

    def test_fast_path_never_enters_private_continuity_and_keeps_normal_events(self):
        engine=self.ready_engine();events=[]
        for kind,count in (('guard',20),('left_raise',20),('guard',30),('cross',40)):
            for _ in range(count):
                state,new=self.step(engine,kind,gap=33,age=5);events.extend(new)
                self.assertFalse(engine._continuity_pending)
                self.assertNotEqual(state.tracking,'LOST')
        self.assertEqual([e.reason['gesture'] for e in events],['LEFT_RAISE','SPECIAL'])

    def test_exact_capture_gap_boundary_can_resume_before_fixed_deadline(self):
        engine=self.ready_engine()
        state,events=self.step(engine,gap=100,age=119)
        self.assertEqual(state.tracking,'VALID');self.assertTrue(engine.ready)
        self.assertFalse(events)
        # At the fixed absolute deadline, private state has already expired,
        # even though both independent input bounds permit their exact maximum.
        engine=self.ready_engine()
        state,events=self.step(engine,gap=100,age=120)
        self.assertFalse(engine.ready);self.assertFalse(events)
        self.assertEqual(engine.attack.units['LEFT'].state,'UNAVAILABLE')

    def test_duplicate_and_out_of_order_samples_cannot_extend_deadline(self):
        engine=self.ready_engine();stamp=engine.last_capture;fid=engine.last_frame
        engine.tick(stamp+150*MS);deadline=engine._continuity_until
        for now,frame,capture in ((170,fid,stamp),(190,fid-1,stamp-33*MS),(210,fid,stamp)):
            engine.process(pose(frame=frame,stamp=capture),now_ns=stamp+now*MS)
            self.assertEqual(engine._continuity_until,deadline)
            self.assertFalse(engine.ready);self.assertFalse(engine.outbox.pending)
        engine.tick(deadline)
        self.assertFalse(engine._continuity_pending)
        self.assertIsNone(engine.signals.previous)

    def test_short_cross_cannot_accumulate_across_actual_missing_capture(self):
        engine=self.ready_engine()
        for _ in range(4):self.assertFalse(self.step(engine,'cross')[1])
        self.assertGreater(engine.special.confirmed_ns,0)
        self.assertFalse(self.step(engine,'cross',gap=101)[1])
        self.assertEqual(engine.special.confirmed_ns,0)
        for _ in range(7):self.assertFalse(self.step(engine,'cross')[1])
        self.assertEqual([e.reason['gesture'] for e in self.step(engine,'cross')[1]],['SPECIAL'])

    def test_movement_off_and_pause_cannot_restore_old_output(self):
        engine=self.ready_engine();engine.tick(engine.last_capture+150*MS)
        self.assertFalse(engine.toggle_movement())
        state,events=self.step(engine)
        self.assertEqual((state.movement,state.movement_strength),('NEUTRAL',0.))
        self.assertFalse(events)
        engine.tick(engine.last_capture+150*MS)
        engine.request_calibration();engine.calibrating=False
        state,events=self.step(engine)
        self.assertFalse(engine.ready);self.assertFalse(engine.calibrated)
        self.assertFalse(state.block);self.assertEqual(state.movement,'NEUTRAL')
        self.assertFalse(events)

if __name__=='__main__':unittest.main()
