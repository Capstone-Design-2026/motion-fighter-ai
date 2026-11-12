import unittest
from unittest.mock import Mock,patch
from action_engine.types import ActionEvent
from realtime_vision.transport import Sender
from redbool_demo.transport import DemoSender

NOW=10_000_000_000

class LateAckDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.now=NOW;self.sock=Mock()
        with patch('realtime_vision.transport.socket.socket',return_value=self.sock):
            self.sender=DemoSender('127.0.0.1',9,'late','P2',clock_ns=lambda:self.now)
        self.addCleanup(self.sender.close)
    def event(self,eid,expiry=None):
        return ActionEvent(eid,'P2','late',1,NOW,NOW,expiry or NOW-1,'ATTACK','LEFT',{},'test')
    def ack(self,eid,status):
        return {'type':'ack','ack':{'event_id':eid,'status':status},'ack_receive_ns':self.now}
    def service(self,rows=()):
        with patch.object(Sender,'service',return_value=list(rows)):
            return self.sender.service()
    def expire(self,eid='old'):
        self.sender.offer(self.event(eid));self.service()

    def test_applied_after_deadline_reconciles_once_without_resending(self):
        self.expire();self.now+=232_000_000
        rows=self.service([self.ack('old','APPLIED'),self.ack('old','APPLIED')])
        self.assertEqual(self.sender.delivery['expired_without_ack'],1)
        self.assertEqual(self.sender.delivery['expired_unconfirmed'],0)
        self.assertEqual(self.sender.delivery['late_ack_APPLIED'],1)
        self.assertEqual(self.sender.delivery['APPLIED'],0)
        self.assertEqual(len([r for r in rows if r['type']=='late_event_ack']),1)
        self.assertEqual(self.sender.last_status,'APPLIED (LATE ACK)')
        self.assertFalse(self.sender.events);self.sock.sendto.assert_not_called()

    def test_old_late_ack_does_not_overwrite_new_action_status(self):
        self.expire();self.sender.offer(self.event('new',NOW+150_000_000))
        self.now+=100_000_000
        self.service([self.ack('new','GAME_BUSY'),self.ack('old','APPLIED')])
        self.assertEqual(self.sender.last_status,'GAME_BUSY')
        self.assertEqual(self.sender.last_status_event_id,'new')
        self.assertEqual(self.sender.delivery['late_ack_APPLIED'],1)

    def test_retry_expired_reply_cannot_hide_later_original_applied(self):
        self.expire();self.now+=20_000_000
        self.service([self.ack('old','EXPIRED')])
        self.assertEqual(self.sender.delivery['late_ack_EXPIRED'],1)
        self.assertEqual(self.sender.delivery['expired_unconfirmed'],0)
        self.now+=30_000_000
        self.service([self.ack('old','APPLIED'),self.ack('old','EXPIRED')])
        self.assertEqual(self.sender.delivery['late_ack_EXPIRED'],0)
        self.assertEqual(self.sender.delivery['late_ack_APPLIED'],1)
        self.assertEqual(self.sender.delivery['expired_unconfirmed'],0)
        self.assertFalse(self.sender.events);self.sock.sendto.assert_not_called()

    def test_late_rejection_is_known_outcome_not_success(self):
        self.expire();self.now+=20_000_000
        self.service([self.ack('old','INPUT_LOCKED')])
        self.assertEqual(self.sender.delivery['late_ack_INPUT_LOCKED'],1)
        self.assertEqual(self.sender.delivery['late_ack_APPLIED'],0)
        self.assertEqual(self.sender.delivery['expired_unconfirmed'],0)

    def test_late_cache_is_bounded_and_old_ack_cannot_revive_input(self):
        for batch in range(5):
            for i in range(30):self.sender.offer(self.event(str(batch*30+i)))
            self.service();self.now+=100_000_000
        self.assertEqual(len(self.sender.expired_diagnostics),128)
        self.assertEqual(self.sender.delivery['expired_unconfirmed'],150)
        self.now+=5_000_000_001
        rows=self.service([self.ack('149','APPLIED')])
        self.assertFalse(self.sender.expired_diagnostics)
        self.assertFalse(any(r['type']=='late_event_ack' for r in rows))
        self.assertFalse(self.sender.events);self.sock.sendto.assert_not_called()

if __name__=='__main__':unittest.main()
