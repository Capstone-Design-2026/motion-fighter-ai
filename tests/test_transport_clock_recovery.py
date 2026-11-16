"""Camera-free jitter/clock recovery checks; attack deadlines stay at 150 ms."""
import json
import unittest
from unittest.mock import Mock,patch

from action_engine.types import ActionEvent
from realtime_vision.transport import Sender,ReceiverState
from redbool_demo.transport import DemoSender

NOW=10_000_000_000

class TransportClockRecoveryTests(unittest.TestCase):
    def sender(self,kind=Sender):
        now=[NOW];sock=Mock();sock.recvfrom.side_effect=BlockingIOError()
        with patch('realtime_vision.transport.socket.socket',return_value=sock):
            sender=kind('127.0.0.1',9,'clock-test','P2',clock_ns=lambda:now[0])
        self.addCleanup(sender.close)
        return sender,sock,now

    def acknowledge(self,s,sock,now,sid,rtt_ns,offset=5_000_000_000):
        t1=now[0]-rtt_ns;s.pending[sid]=t1;s.last_sync=now[0]
        ack={'version':2,'stream_id':s.stream_id,'status':'SYNC','sync_id':sid,
             'sender_ns':t1,'receiver_ns':t1+rtt_ns//2+offset,
             'ack_sent_ns':t1+rtt_ns//2+offset}
        sock.recvfrom.side_effect=[(json.dumps(ack).encode(),s.target),BlockingIOError()]
        return s.service()

    def test_jittery_new_sample_does_not_replace_quieter_recent_clock(self):
        s,sock,now=self.sender()
        self.acknowledge(s,sock,now,'quiet',4_000_000)
        original=s.clock['updated_ns'];now[0]+=1_000_000_000
        rows=self.acknowledge(s,sock,now,'jitter',44_000_000)
        self.assertEqual(s.clock['sync_id'],'quiet')
        self.assertEqual(s.clock['updated_ns'],original)
        self.assertEqual(s.clock['clock_uncertainty_ns'],4_000_000)
        self.assertEqual(s.counts['sync_sample_accepted'],2)
        self.assertFalse(any(r.get('state')=='lost' for r in rows))

    def test_selection_ages_uncertainty_and_expires_original_sample(self):
        s,sock,now=self.sender()
        self.acknowledge(s,sock,now,'old-quiet',2_000_000)
        now[0]+=2_000_000_000
        self.acknowledge(s,sock,now,'fresh-noisier',12_000_000)
        self.assertEqual(s.clock['sync_id'],'old-quiet')
        now[0]+=1_000_000_001;s._select_clock(now[0])
        self.assertEqual(s.clock['sync_id'],'fresh-noisier')
        self.assertEqual(s.clock['updated_ns'],NOW+2_000_000_000)
        self.assertGreater(s.clock['clock_uncertainty_ns'],7_000_000)
        now[0]=NOW+5_000_000_001;s._select_clock(now[0])
        self.assertIsNone(s.clock)

    def test_high_rtt_samples_never_renew_expired_clock(self):
        s,sock,now=self.sender()
        self.acknowledge(s,sock,now,'quiet',4_000_000)
        for i in range(1,5):
            now[0]=NOW+i*1_000_000_000
            rows=self.acknowledge(s,sock,now,'slow'+str(i),100_000_000)
        self.assertIsNone(s.clock)
        self.assertEqual(s.counts['sync_sample_uncertain'],4)
        self.assertEqual(s.counts['clock_lost'],1)
        self.assertTrue(any(r.get('state')=='lost' for r in rows))
        packet=s.send('STATE',ttl_ms=150)['packet']
        self.assertNotIn('sync_id',packet)

    def test_recovery_probes_are_bounded_to_five_per_second(self):
        s,sock,now=self.sender()
        for i in range(101):
            now[0]=NOW+i*10_000_000;s.service()
        sent=[json.loads(x.args[0]) for x in sock.sendto.call_args_list]
        self.assertEqual(len(sent),6) # t=0, .2, .4, .6, .8, 1.0
        self.assertTrue(all(p['kind']=='SYNC' for p in sent))
        self.assertTrue(all(b['sent_ns']-a['sent_ns']>=200_000_000 for a,b in zip(sent,sent[1:])))

    def test_quiet_fresh_clock_keeps_one_second_probe_interval(self):
        s,sock,now=self.sender()
        self.acknowledge(s,sock,now,'quiet',4_000_000)
        sock.recvfrom.side_effect=BlockingIOError()
        for i in range(1,100):
            now[0]=NOW+i*10_000_000;s.service()
        self.assertEqual(s.counts['sent_SYNC'],0)
        now[0]=NOW+1_000_000_000;s.service()
        self.assertEqual(s.counts['sent_SYNC'],1)

    def test_recovery_never_revives_an_expired_attack(self):
        s,sock,now=self.sender(DemoSender)
        s.offer(ActionEvent('old','P2','clock-test',1,NOW,NOW,NOW+150_000_000,'ATTACK','LEFT',{},'test'))
        s.service();now[0]+=200_000_000
        self.acknowledge(s,sock,now,'recovered',8_000_000)
        self.assertIsNotNone(s.clock);self.assertFalse(s.events)
        self.assertEqual(s.delivery['expired_without_ack'],1)
        self.assertEqual(s.counts['sent_ATTACK'],0)
        self.assertEqual(s.counts['sent_GESTURE'],0)

    def test_custom_clock_is_used_for_feedback_and_original_event_deadline(self):
        s,sock,now=self.sender(DemoSender)
        self.acknowledge(s,sock,now,'quiet',4_000_000)
        s.feedback={'sent_ns':NOW,'game_can_act':True,'special_ready':True}
        s.tutorial={'sent_ns':NOW,'phase':'PRACTICE'}
        self.assertIn('GAME READY',s.game_hint());self.assertIsNotNone(s.tutorial_context())
        event=ActionEvent('fresh','P2','clock-test',1,NOW,NOW,NOW+150_000_000,'ATTACK','LEFT',{},'test')
        s.offer(event);sock.recvfrom.side_effect=BlockingIOError();now[0]+=100_000_000
        rows=s.service();attack=next(r['packet'] for r in rows if r['type']=='send' and r['packet']['kind']=='ATTACK')
        self.assertEqual(attack['sent_ns'],now[0])
        self.assertEqual(attack['event_expires_ns'],event.expires_ns)
        self.assertLessEqual(attack['sent_ns']+attack['ttl_ms']*1_000_000,event.expires_ns)
        now[0]+=500_000_000
        self.assertIsNone(s.tutorial_context());self.assertIn('UNKNOWN',s.game_hint())

    def test_negative_network_rtt_cannot_create_a_clock(self):
        s,sock,now=self.sender()
        s.pending['invalid']=NOW-10_000_000;s.last_sync=NOW
        ack={'version':2,'stream_id':s.stream_id,'status':'SYNC','sync_id':'invalid',
             'sender_ns':NOW-10_000_000,'receiver_ns':NOW,'ack_sent_ns':NOW+20_000_000}
        sock.recvfrom.side_effect=[(json.dumps(ack).encode(),s.target),BlockingIOError()]
        s.service();self.assertIsNone(s.clock);self.assertEqual(s.counts['bad_ack'],1)

    def test_noisy_clock_does_not_extend_receiver_input_lease(self):
        s,sock,now=self.sender();offset=5_000_000_000
        self.acknowledge(s,sock,now,'quiet',4_000_000,offset)
        receiver=ReceiverState(s.stream_id,'P2')
        receiver.accept({'version':2,'kind':'SYNC','stream_id':s.stream_id,'capture_epoch':s.stream_id,
                         'actor_id':'P2','sync_id':'quiet','sent_ns':NOW-4_000_000},NOW+offset-2_000_000)
        state=s.send('STATE',ttl_ms=100,block_active=True,movement='LEFT',movement_strength=.7)['packet']
        result=receiver.accept(state,NOW+offset+10_000_000)
        self.assertEqual(result['status'],'APPLIED');self.assertTrue(receiver.block)
        receiver.tick(NOW+offset+97_000_000)
        self.assertFalse(receiver.block);self.assertEqual(receiver.movement,'NEUTRAL')

if __name__=='__main__':unittest.main()
