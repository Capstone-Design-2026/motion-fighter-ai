"""Deterministic command-path impairments; no socket, sleep, camera or Unity.

The actual Python sender exchanges serialized datagrams with the reference
receiver. This is not a display-channel, rendered-frame or real-WAN test.
"""
from collections import deque
import contextlib
import hashlib
import heapq
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from action_engine.types import ActionEvent
from realtime_vision.transport import ReceiverState, network_policy
from redbool_demo.transport import DemoSender

MS=1_000_000
ORIGIN=10_000*MS
OFFSET=5_000*MS


class Wire:
    """A virtual bidirectional datagram link, with a separate receiver clock."""
    def __init__(self,profile='online',one_way_ms=40):
        self.now=ORIGIN;self.one_way_ms=one_way_ms;self.main_ms=10
        self.target=('127.0.0.1',5066);self.pending=[];self.received=deque()
        self.sequence=0;self.sent=[];self.outcomes=[];self.rows=[]
        self.policy=lambda direction,packet: self.one_way_ms
        self.receiver=ReceiverState('online-test','P2',network_profile=profile)
        with patch('realtime_vision.transport.socket.socket',return_value=self), \
             patch('realtime_vision.transport.socket.gethostbyname',return_value=self.target[0]):
            self.sender=DemoSender(*self.target,'online-test','P2',clock_ns=lambda:self.now,network_profile=profile)
    def bind(self,peer):pass
    def setblocking(self,value):pass
    def close(self):pass
    def schedule(self,at,kind,value):
        self.sequence+=1;heapq.heappush(self.pending,(at,self.sequence,kind,value))
    def sendto(self,data,peer):
        assert peer==self.target
        packet=json.loads(data);self.sent.append(packet)
        delay=self.policy('input',packet)
        if delay is not None:self.schedule(self.now+int(delay*MS),'input',packet)
        return len(data)
    def recvfrom(self,size):
        if not self.received:raise BlockingIOError()
        return self.received.popleft(),self.target
    def advance(self,milliseconds):
        target=self.now+int(milliseconds*MS)
        while self.pending and self.pending[0][0]<=target:
            self.now,_,kind,value=heapq.heappop(self.pending)
            if kind=='input':self.schedule(self.now+self.main_ms*MS,'apply',(value,self.now))
            elif kind=='apply':
                packet,arrival=value
                result=self.receiver.accept(packet,arrival+OFFSET,self.now+OFFSET)
                self.outcomes.append((packet,result,self.now))
                ack={**result,'status':result['status'].upper(),'version':2,'stream_id':'online-test',
                     'sender_ns':packet['sent_ns'],'receiver_ns':arrival+OFFSET,
                     'ack_sent_ns':self.now+OFFSET,'event_id':packet.get('event_id'),'sequence':packet['sequence']}
                delay=self.policy('ack',ack)
                if delay is not None:self.schedule(self.now+int(delay*MS),'ack',json.dumps(ack).encode())
            else:self.received.append(value)
        self.now=target;self.receiver.tick(self.now+OFFSET)
    def service(self):self.rows.extend(self.sender.service())
    def pump(self,milliseconds,step=10):
        for _ in range(milliseconds//step):self.advance(step);self.service()
    def connect(self):self.service();self.pump(100)
    def state(self,*,capture_age_ms=0,block=False,generation=1,calibrated=True):
        return self.sender.send('STATE',ttl_ms=max(1,150-capture_age_ms),
            capture_ns=self.now-capture_age_ms*MS,calibration_generation=generation,
            calibrated=calibrated,tracking='VALID' if calibrated else 'CALIBRATING',
            block_active=block,movement='NEUTRAL',movement_strength=0.)['packet']
    def event(self,eid='attack',generation=1,gesture=None):
        event=ActionEvent(eid,'P2','online-test',1,self.now,self.now,self.now+150*MS,'ATTACK',
            'BOTH' if gesture=='SPECIAL' else 'LEFT',{'gesture':gesture} if gesture else {},'fixture')
        self.sender.offer(event,calibration_generation=generation);self.service();return event


class OnlineTransportTests(unittest.TestCase):
    def test_online_high_rtt_gesture_uses_original_identity_and_generation(self):
        wire=Wire();wire.connect();wire.state();wire.pump(50)
        event=wire.event(gesture='SPECIAL');wire.advance(50)
        self.assertEqual(len(wire.receiver.attacks),1)
        packet=next(p for p in wire.sent if p.get('event_id')==event.event_id)
        self.assertEqual(packet['kind'],'GESTURE');self.assertEqual(packet['side'],'BOTH')
        self.assertEqual(packet['calibration_generation'],1)
        self.assertEqual(packet['capture_ns'],event.source_capture_ns)
        self.assertEqual(packet['commit_ns'],event.committed_ns)
        self.assertEqual(packet['event_expires_ns'],event.expires_ns)

    def test_default_lan_rejects_80ms_sync_but_online_accepts_actual_sample(self):
        for profile,expected in [('lan',False),('online',True)]:
            with self.subTest(profile=profile):
                wire=Wire(profile);wire.connect()
                self.assertEqual(wire.sender.clock is not None,expected)
                if expected:
                    self.assertEqual(wire.sender.clock['receiver_offset_ns'],OFFSET)
                    self.assertGreaterEqual(wire.sender.clock['clock_uncertainty_ns'],41*MS)
                    self.assertEqual(wire.sender.transport_diagnostics()['last_sync_corrected_rtt_ns'],80*MS)
                    sample=next(row for row in wire.rows if row['type']=='network_clock_sample')
                    self.assertTrue(sample['accepted']);self.assertEqual(sample['corrected_rtt_ns'],80*MS)
                else:self.assertEqual(wire.sender.counts['sync_sample_uncertain'],1)

    def test_online_limit_is_not_unlimited_and_does_not_fake_clock(self):
        wire=Wire(one_way_ms=80);wire.connect();wire.pump(300)
        self.assertIsNone(wire.sender.clock)
        self.assertGreater(wire.sender.counts['sync_sample_uncertain'],0)
        wire.event();wire.pump(200)
        self.assertFalse(any(p['kind']=='ATTACK' for p in wire.sent))

    def test_quieter_sample_survives_jitter_without_refreshing_original_age(self):
        wire=Wire();wire.connect();original=wire.sender.clock['updated_ns']
        wire.one_way_ms=60;wire.pump(400)
        self.assertEqual(wire.sender.clock['updated_ns'],original)
        self.assertGreater(wire.sender.clock['clock_uncertainty_ns'],41*MS)
        self.assertGreaterEqual(wire.sender.counts['sync_sample_accepted'],2)
        wire.policy=lambda direction,packet:None
        wire.pump(3100)
        self.assertIsNone(wire.sender.clock)

    def test_synchronizable_120ms_route_rejects_old_source_even_with_fresh_send(self):
        wire=Wire(one_way_ms=60);wire.service();wire.pump(150)
        self.assertIsNotNone(wire.sender.clock)
        packet=wire.state(capture_age_ms=40,block=True)
        wire.pump(70)
        outcome=next(r for p,r,_ in wire.outcomes if p['sequence']==packet['sequence'])
        self.assertEqual(outcome['status'],'expired');self.assertFalse(wire.receiver.block)

    def test_guard_expires_at_original_capture_deadline_not_last_arrival(self):
        wire=Wire();wire.connect();packet=wire.state(capture_age_ms=20,block=True)
        wire.advance(50)
        self.assertTrue(wire.receiver.block)
        self.assertLessEqual(wire.receiver.deadline,packet['capture_ns']+OFFSET+150*MS-41*MS)
        wire.advance(50);self.assertFalse(wire.receiver.block)
        # A malicious fresh packet TTL cannot extend the old source lease.
        renewed=wire.sender.send('STATE',ttl_ms=150,capture_ns=packet['capture_ns'],
            calibration_generation=1,calibrated=True,tracking='VALID',block_active=True,
            movement='NEUTRAL',movement_strength=0.)['packet']
        wire.advance(50)
        outcome=next(r for p,r,_ in wire.outcomes if p['sequence']==renewed['sequence'])
        self.assertEqual(outcome['status'],'expired');self.assertFalse(wire.receiver.block)

    def test_state_overtake_does_not_discard_fresh_online_event(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state();wire.pump(20)
        wire.policy=lambda direction,p:30 if direction=='input' and p['kind']=='ATTACK' else 10
        event=wire.event();wire.advance(1);state=wire.state();wire.pump(40)
        self.assertGreater(state['sequence'],next(p['sequence'] for p in wire.sent if p.get('event_id')==event.event_id))
        self.assertEqual(len(wire.receiver.attacks),1)
        self.assertEqual(wire.receiver.attacks[0]['event_id'],event.event_id)

    def test_reordered_event_packet_is_rejected_after_newer_event_packet(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state();wire.pump(20)
        wire.policy=lambda direction,p:50 if direction=='input' and p.get('event_id')=='older' else 10
        wire.event('older');wire.advance(1);wire.event('newer');wire.advance(60)
        self.assertEqual([a['event_id'] for a in wire.receiver.attacks],['newer'])
        self.assertTrue(any(p.get('event_id')=='older' and r['status']=='old_sequence' for p,r,_ in wire.outcomes))

    def test_lan_wire_keeps_original_fields_and_omits_online_event_metadata(self):
        wire=Wire('lan',one_way_ms=10);wire.connect();wire.event(generation=7)
        packet=next(p for p in wire.sent if p['kind']=='ATTACK')
        self.assertNotIn('calibration_generation',packet)
        self.assertNotIn('network_profile',packet)
        self.assertEqual(set(packet),{'version','kind','stream_id','actor_id','capture_epoch',
            'sequence','sent_ns','ttl_ms','event_id','side','gesture','event_expires_ns',
            'commit_ns','capture_ns','source_frame_id','sync_id','receiver_offset_ns','clock_uncertainty_ns'})

    def test_loss_retry_and_late_ack_never_renew_event_or_repeat_attack(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state();wire.pump(20)
        attempts=[]
        def policy(direction,packet):
            if direction=='input' and packet['kind']=='ATTACK':
                attempts.append(packet)
                return None if len(attempts)==1 else 10
            if direction=='ack' and packet.get('event_id'):return 120
            return 10
        wire.policy=policy;event=wire.event()
        wire.pump(250)
        self.assertEqual(len(attempts),3);self.assertEqual(len(wire.receiver.attacks),1)
        self.assertTrue(all(p['event_expires_ns']==event.expires_ns for p in attempts))
        self.assertTrue(all(p['sent_ns']+p['ttl_ms']*MS<=event.expires_ns for p in attempts))
        self.assertEqual(wire.sender.delivery['late_ack_APPLIED'],1)
        self.assertFalse(wire.sender.events)

    def test_old_inflight_event_is_locked_after_calibration_generation_changes(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state();wire.pump(20)
        wire.policy=lambda direction,p:40 if direction=='input' and p['kind']=='ATTACK' else 10
        wire.event(generation=1);wire.advance(1);wire.state(generation=2);wire.pump(50)
        self.assertFalse(wire.receiver.attacks)
        self.assertTrue(any(p.get('event_id')=='attack' and r['status']=='input_locked' for p,r,_ in wire.outcomes))
        self.assertTrue(all(p.get('calibration_generation')==1 for p in wire.sent if p['kind']=='ATTACK'))

    def test_calibration_reset_releases_guard_and_locks_old_attack(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state(block=True);wire.pump(20)
        self.assertTrue(wire.receiver.block)
        wire.state(generation=2,calibrated=False);wire.pump(20)
        self.assertFalse(wire.receiver.block);wire.event(generation=1);wire.pump(20)
        self.assertFalse(wire.receiver.attacks)

    def test_old_generation_state_cannot_reopen_a_reset_with_newer_sequence(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state(generation=2,calibrated=False);wire.pump(20)
        old=wire.state(generation=1,block=True);wire.pump(20)
        outcome=next(r for p,r,_ in wire.outcomes if p['sequence']==old['sequence'])
        self.assertEqual(outcome['status'],'input_locked')
        self.assertEqual(wire.receiver.calibration_generation,2)
        self.assertFalse(wire.receiver.live);self.assertFalse(wire.receiver.block)
        wire.event(generation=1);wire.pump(20);self.assertFalse(wire.receiver.attacks)

    def test_source_capture_cannot_be_in_the_future_of_its_send_timestamp(self):
        wire=Wire(one_way_ms=10);wire.connect()
        packet=wire.sender.send('STATE',ttl_ms=150,capture_ns=wire.now+MS,
            calibration_generation=1,calibrated=True,tracking='VALID',block_active=True,
            movement='NEUTRAL',movement_strength=0.)['packet']
        wire.pump(20)
        outcome=next(r for p,r,_ in wire.outcomes if p['sequence']==packet['sequence'])
        self.assertEqual(outcome['status'],'invalid_timing')
        self.assertFalse(wire.receiver.live);self.assertFalse(wire.receiver.block)
        self.assertEqual(wire.receiver.deadline,0)

    def test_expired_attack_cannot_be_revived_with_fresh_packet_ttl(self):
        wire=Wire(one_way_ms=10);wire.connect();wire.state();wire.pump(20)
        event=wire.event();wire.pump(30)
        wire.advance(160);wire.state();wire.pump(20)
        packet=wire.sender.send('ATTACK',ttl_ms=150,event_id='expired-new-id',side='LEFT',
            capture_ns=event.source_capture_ns,commit_ns=event.committed_ns,event_expires_ns=event.expires_ns,
            calibration_generation=1)['packet']
        wire.pump(20)
        outcome=next(r for p,r,_ in wire.outcomes if p['sequence']==packet['sequence'])
        self.assertEqual(outcome['status'],'expired');self.assertEqual(len(wire.receiver.attacks),1)

    def test_profile_rejects_unknown_value_before_creating_socket(self):
        with patch('realtime_vision.transport.socket.socket') as create:
            with self.assertRaises(ValueError):DemoSender('127.0.0.1',9,'x','P2',network_profile='unbounded')
            create.assert_not_called()
        self.assertEqual(network_policy()['max_clock_uncertainty_ns'],25*MS)
        self.assertEqual(network_policy('online')['max_clock_uncertainty_ns'],75*MS)


class OnlineIntegrationTests(unittest.TestCase):
    def test_live_parser_default_and_explicit_profile_without_starting_runtime(self):
        from redbool_demo.live import main
        for argv,expected in [([], 'lan'),(['--network-profile','online'],'online')]:
            with patch('redbool_demo.live.run') as run:
                main(argv);self.assertEqual(run.call_args.args[0].network_profile,expected)

    def test_packaged_entry_forwards_profile_without_altering_model_defaults(self):
        from redbool_demo.backend import main
        with patch('redbool_demo.live.main') as live,contextlib.redirect_stdout(io.StringIO()):
            main(['--check','--network-profile','online'])
        argv=live.call_args.args[0]
        self.assertEqual(argv[argv.index('--network-profile')+1],'online')
        self.assertIn('--personal-calibration',argv)
        self.assertTrue(argv[argv.index('--pose')+1].endswith('pose_landmarker_lite.task'))
        self.assertTrue(argv[argv.index('--cross-pose')+1].endswith('pose_landmarker_full.task'))

    def test_current_reviewed_recognition_and_calibration_hashes_match(self):
        root=Path(__file__).resolve().parents[1]
        manifest=json.loads((root/'config/recognition_baseline_online.json').read_text())
        for name in ('recognition','calibration_diagnostics'):
            for relative,expected in manifest['groups'][name]['files'].items():
                digest=hashlib.sha256((root/relative).read_bytes().replace(b'\r\n',b'\n')).hexdigest()
                self.assertEqual(digest,expected,relative)


if __name__=='__main__':unittest.main()
