import unittest
from redbool_demo.probe import guard_check

def packet(count,active=False):
    return {'type':'ack','ack':{'guards_started':count,'game_guard_active':active}}

class GuardProbeTests(unittest.TestCase):
    def test_one_guard_and_finish_pass(self):
        self.assertTrue(guard_check(4,[packet(4),packet(5,True),packet(5)])['passed'])

    def test_held_pose_repeated_guard_fails(self):
        self.assertFalse(guard_check(4,[packet(5,True),packet(6)])['passed'])

    def test_missing_old_receiver_ack_or_still_guarding_fails(self):
        for base,packets in [(None,[]),(0,[packet(0)]),(0,[packet(1,True)])]:
            with self.subTest(base=base,packets=packets):
                self.assertFalse(guard_check(base,packets)['passed'])

from collections import Counter
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
from unittest.mock import patch
from redbool_demo.probe import ReadyGate,run,RECEIVER_VERSION


def ready_rows(seq,stamp,ready=True,active=False,**extra):
    packet={'kind':'STATE','sequence':seq,'sent_ns':stamp,'block_active':False,'movement':'NEUTRAL'}
    ack={'sequence':seq,'sender_ns':stamp,'status':'APPLIED','receiver_version':RECEIVER_VERSION,
         'game_can_act':ready,'game_guard_active':active,**extra}
    return [{'type':'send','packet':packet},{'type':'ack','ack':ack,'ack_receive_ns':stamp}]


class ReadyGateTests(unittest.TestCase):
    def test_fresh_stable_neutral_replies_required(self):
        g=ReadyGate()
        for seq,t in enumerate((1_000_000_000,1_100_000_000,1_200_000_000),1):
            g.observe(ready_rows(seq,t),t)
            self.assertEqual(g.ready(t),seq==3)
        self.assertFalse(g.ready(1_351_000_000))

    def test_busy_or_active_guard_resets_readiness(self):
        for kw in ({'ready':False},{'active':True}):
            with self.subTest(kw=kw):
                g=ReadyGate();g.observe(ready_rows(1,1_000_000_000),1_000_000_000)
                g.observe(ready_rows(2,1_100_000_000,**kw),1_100_000_000)
                g.observe(ready_rows(3,1_200_000_000),1_200_000_000)
                self.assertFalse(g.ready(1_200_000_000))

    def test_old_unmatched_wrong_version_or_echo_cannot_arm(self):
        variants=[]
        for k in range(3):
            t=1_000_000_000+k*100_000_000
            variants.append((ready_rows(k+1,t),t))
        for scenario in ('unmatched','stale','wrong_echo','wrong_version','no_can_act'):
            g=ReadyGate()
            for original,t in variants:
                rows=json.loads(json.dumps(original));now=t
                if scenario=='unmatched':rows=rows[1:]
                elif scenario=='stale':now+=200_000_000
                elif scenario=='wrong_echo':rows[1]['ack']['sender_ns']-=1
                elif scenario=='wrong_version':rows[1]['ack']['receiver_version']='old'
                else:rows[1]['ack'].pop('game_can_act')
                g.observe(rows,now)
            with self.subTest(scenario=scenario):self.assertFalse(g.ready(now))

    def test_receive_gap_requires_a_new_stable_window(self):
        g=ReadyGate();g.observe(ready_rows(1,1_000_000_000),1_000_000_000)
        g.observe(ready_rows(2,1_300_000_000),1_300_000_000)
        self.assertFalse(g.ready(1_300_000_000))


class FakeTime:
    def __init__(self):self.t=10.
    def monotonic(self):return self.t
    def monotonic_ns(self):return round(self.t*1e9)
    def sleep(self,seconds):self.t+=seconds


class FakeGameSender:
    """A deterministic responsive game; no network, camera or scene mutation."""
    def __init__(self,clock,always_busy=False,reject_index=None):
        self.timer=clock;self.clock={'synced':True};self.delivery=Counter();self.rows=[];self.sent=[]
        self.offered=[];self.sequence=0;self.busy_until=0.;self.guard_until=0.;self.guards=0
        self.held=False;self.always_busy=always_busy;self.reject_index=reject_index;self.closed=False
    def ready(self):return not self.always_busy and self.timer.t>=self.busy_until
    def ack(self,p,**fields):
        return {'type':'ack','ack_receive_ns':self.timer.monotonic_ns(),'ack':{
            'version':2,'sequence':p['sequence'],'sender_ns':p['sent_ns'],'status':'APPLIED',
            'receiver_version':RECEIVER_VERSION,'receiver_clock':'unity_stopwatch_ns',
            'guard_mode':'one_timed_guard_per_pose','guards_started':self.guards,
            'game_can_act':self.ready(),'game_guard_active':self.timer.t<self.guard_until,**fields}}
    def send(self,kind,**fields):
        self.sequence+=1;p={'kind':kind,'sequence':self.sequence,'sent_ns':self.timer.monotonic_ns(),**fields};self.sent.append(p)
        if kind=='STATE':
            held=fields['block_active']
            if held and not self.held and self.ready():
                self.guards+=1;self.guard_until=self.timer.t+1.5;self.busy_until=self.timer.t+2.
            self.held=held
        self.rows.append(self.ack(p))
        return {'type':'send','packet':p}
    def offer(self,event):
        self.offered.append((event,self.timer.t));index=len(self.offered)
        self.sequence+=1;p={'sequence':self.sequence,'sent_ns':self.timer.monotonic_ns()}
        applied=self.ready() and index!=self.reject_index
        status='APPLIED' if applied else 'GAME_BUSY';self.delivery[status]+=1
        if applied:self.busy_until=self.timer.t+(.8,1.,4.,.8)[index-1]
        gesture=event.reason['gesture'];action={'LEFT_PUNCH':'JAB','RIGHT_PUNCH':'STRAIGHT','LEFT_RAISE':'HIGH_KICK','RIGHT_RAISE':'LOW_KICK'}[gesture]
        self.rows.append(self.ack(p,status=status,event_id=event.event_id,applied_action=action if applied else None))
    def service(self):rows=self.rows;self.rows=[];return rows
    def close(self):self.closed=True


class ReadyAwareProbeTests(unittest.TestCase):
    def execute(self,**settings):
        clock=FakeTime();sender=FakeGameSender(clock,**settings)
        with tempfile.TemporaryDirectory() as tmp:
            with patch('redbool_demo.probe.DemoSender',return_value=sender), \
                 patch('redbool_demo.probe.time.monotonic',clock.monotonic), \
                 patch('redbool_demo.probe.time.monotonic_ns',clock.monotonic_ns), \
                 patch('redbool_demo.probe.time.sleep',clock.sleep),redirect_stdout(io.StringIO()):
                error=None
                try:run('127.0.0.1',5065,Path(tmp),ready_timeout=6.)
                except RuntimeError as exc:error=str(exc)
            report=json.loads(next(Path(tmp).glob('*/summary.json')).read_text())
        self.assertTrue(sender.closed)
        self.assertEqual(sender.sent[-1]['movement'],'NEUTRAL')
        self.assertFalse(sender.sent[-1]['block_active'])
        return sender,report,error

    def test_slow_high_kick_recovery_is_waited_before_low_kick(self):
        s,r,error=self.execute()
        self.assertIsNone(error);self.assertTrue(r['passed']);self.assertEqual(len(s.offered),4)
        self.assertGreaterEqual(s.offered[3][1]-s.offered[2][1],4.)
        self.assertTrue(all(x['passed'] for x in r['checks']));self.assertTrue(r['guard_check']['passed'])
        for event,_ in s.offered:self.assertEqual(event.expires_ns-event.committed_ns,150_000_000)
        self.assertEqual(len({event.event_id for event,_ in s.offered}),4)

    def test_ko_times_out_without_creating_any_attack(self):
        s,r,error=self.execute(always_busy=True)
        self.assertIn('Game not ready before LEFT_PUNCH',error)
        self.assertFalse(s.offered);self.assertFalse(r['passed']);self.assertFalse(r['checks'])
        self.assertFalse(r['readiness_checks'][0]['passed'])

    def test_busy_after_ready_race_is_reported_not_replayed(self):
        s,r,error=self.execute(reject_index=4)
        self.assertIn('GAME_BUSY',error);self.assertFalse(r['passed'])
        self.assertEqual(len(s.offered),4);self.assertEqual(s.guards,0)
        self.assertEqual(r['checks'][-1]['statuses'],['GAME_BUSY'])
        self.assertFalse(r['checks'][-1]['passed'])
