"""X-miss safety regressions: lost wrists and old ownership are not new proof.

Uses only synthetic coordinates and the public engine interface. The proposed
owned-aware fallback must not acquire a new hold from an already expired strict
seed. This is a conservative policy test, not a labelled human-motion fixture.
"""
import unittest
from dataclasses import replace
from action_engine.types import EngineConfig
from redbool_demo.special import CrossSpecial, cross_geometry
from redbool_demo.six_engine import SixGestureEngine
from test_chest_special import pose


def close_hands(kind, frame=1, stamp=0):
    obs = pose('guard', frame, stamp)
    p = [list(x) for x in obs.landmarks]
    p[13] = [.70, .60, 0.]; p[14] = [.30, .60, 0.]
    if kind == 'ambiguous':
        p[15] = [.53, .40, 0.]; p[16] = [.47, .40, 0.]
        left_tip, right_tip = [.56, .40, 0.], [.44, .40, 0.]
    else:
        p[15] = [.58, .40, 0.]; p[16] = [.42, .40, 0.]
        left_tip, right_tip = [.49, .40, 0.], [.51, .40, 0.]
    for i in (17,19,21): p[i] = left_tip[:]
    for i in (18,20,22): p[i] = right_tip[:]
    return replace(obs, landmarks=tuple(map(tuple,p)))


class CrossMissSafetyTests(unittest.TestCase):
    def test_identical_shoulders_elbows_do_not_distinguish_held_x_and_guard(self):
        crossed, released = pose('cross'), pose('guard')
        for i in (11,12,13,14):
            self.assertEqual(crossed.landmarks[i], released.landmarks[i])
        self.assertTrue(cross_geometry(crossed,EngineConfig())[1])
        self.assertTrue(cross_geometry(released,EngineConfig())[2])
        visibility = list(crossed.visibility); visibility[15]=visibility[16]=0
        for obs in (crossed,released):
            self.assertEqual(cross_geometry(replace(obs,visibility=tuple(visibility)),EngineConfig()),(False,False,False))

    def test_long_loss_cannot_rearm_already_fired_x(self):
        state=CrossSpecial(); stamp=0; events=0
        for _ in range(30):
            stamp+=33_333_333
            events+=state.step(stamp,calibrated=True,valid=True,neutral=False,approach=True,strict=True)
        self.assertEqual(events,1)
        for _ in range(300):
            stamp+=33_333_333
            self.assertFalse(state.step(stamp,calibrated=True,valid=False,neutral=True,approach=False,strict=False))
        self.assertTrue(state.latched)
        for _ in range(60):
            stamp+=33_333_333
            self.assertFalse(state.step(stamp,calibrated=True,valid=True,neutral=False,approach=True,strict=True))

    def test_disjoint_short_release_proof_cannot_accumulate(self):
        state=CrossSpecial(); state.latched=True; state.owned=True; stamp=0
        for _ in range(4):
            for _ in range(4):
                stamp+=33_333_333
                state.step(stamp,calibrated=True,valid=True,neutral=True,approach=False,strict=False)
            stamp+=33_333_333
            state.step(stamp,calibrated=True,valid=False,neutral=False,approach=False,strict=False)
        self.assertTrue(state.latched)
        self.assertFalse(state.armed)

    def test_one_missing_wrist_is_not_a_release(self):
        obs=pose('guard'); visibility=list(obs.visibility); visibility[16]=0
        partial=replace(obs,visibility=tuple(visibility))
        self.assertEqual(cross_geometry(partial,EngineConfig()),(False,False,False))
        state=CrossSpecial(); state.latched=True; state.owned=True
        for frame in range(60):
            state.step(frame*33_333_333,calibrated=True,valid=False,neutral=False,approach=False,strict=False)
        self.assertTrue(state.latched)

    def drive(self, with_old_seed):
        engine=SixGestureEngine(movement=True); engine.request_calibration()
        frame=0; events=[]
        def frames(count, kind):
            nonlocal frame
            for _ in range(count):
                frame+=1; stamp=frame*33_333_333
                obs=close_hands(kind,frame,stamp) if kind in ('ambiguous','wide_fists') else pose(kind,frame,stamp)
                engine.process(obs,now_ns=stamp)
                events.extend(engine.drain_events(stamp)); engine.take_logs()
        frames(50,'guard'); self.assertTrue(engine.calibrated)
        if with_old_seed:
            frames(3,'cross'); self.assertTrue(engine.special.owned)
            self.assertFalse(engine.special.latched)
            frames(20,'ambiguous')
            self.assertTrue(engine.special.owned)
            self.assertIsNone(engine.special.last_strict)
            self.assertEqual(engine.special.confirmed_ns,0)
        frames(45,'wide_fists')
        return engine,events

    def test_wide_fists_do_not_acquire_a_new_x_without_fresh_seed(self):
        self.assertEqual(cross_geometry(close_hands('wide_fists'),EngineConfig())[:2],(False,False))
        _,events=self.drive(False)
        self.assertFalse(any(e.reason.get('gesture')=='SPECIAL' for e in events))

    def test_expired_pending_seed_cannot_authorize_wide_fist_fallback(self):
        # The original strict seed ended >600ms ago and its entire hold dwell
        # was discarded. Mere old ownership must not become a new acquisition.
        _,events=self.drive(True)
        self.assertFalse(any(e.reason.get('gesture')=='SPECIAL' for e in events))


if __name__=='__main__': unittest.main()
