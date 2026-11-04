"""Deterministic Winsock reset injection; also runnable on Windows without Unity."""
import errno
import json
import unittest
from unittest.mock import Mock,patch
from action_engine.types import ActionEvent
from realtime_vision.transport import Sender
from redbool_demo.transport import DemoSender

NOW=10_000_000_000

def windows_reset():
    exc=ConnectionResetError(10054,'An existing connection was forcibly closed by the remote host')
    exc.winerror=10054
    return exc

class UdpReceiveResetTests(unittest.TestCase):
    def sender(self,kind=Sender):
        sock=Mock()
        sock.recvfrom.side_effect=windows_reset()
        with patch('realtime_vision.transport.socket.socket',return_value=sock):
            sender=kind('127.0.0.1',9,'reset-test','DEV01')
        self.addCleanup(sender.close)
        return sender,sock

    def test_reset_does_not_prevent_expired_event_removal(self):
        s,sock=self.sender(DemoSender)
        s.offer(ActionEvent('old','DEV01','reset-test',1,NOW,NOW,NOW-1,'ATTACK','LEFT',{},'test'))
        with patch('realtime_vision.transport.time.monotonic_ns',return_value=NOW):rows=s.service()
        self.assertFalse(s.events);self.assertEqual(s.delivery['expired_without_ack'],1)
        self.assertEqual(s.counts['sent_ATTACK'],0);self.assertEqual(s.counts['sent_GESTURE'],0)
        self.assertEqual(s.counts['receive_reset'],1);self.assertEqual(sock.recvfrom.call_count,1)
        self.assertTrue(any(r.get('winerror')==10054 and r.get('recoverable') for r in rows))
        self.assertFalse(any(r['type']=='ack' for r in rows))
        self.assertIsNone(s.clock)

    def test_reset_still_expires_clock_without_busy_loop(self):
        s,sock=self.sender();s.last_sync=NOW
        s.clock={'sync_id':'old','receiver_offset_ns':0,'clock_uncertainty_ns':1_000_000,'updated_ns':NOW-3_000_000_001}
        with patch('realtime_vision.transport.time.monotonic_ns',return_value=NOW):s.service()
        self.assertIsNone(s.clock);self.assertEqual(sock.recvfrom.call_count,1)

    def test_later_sync_can_recover_on_same_socket_without_reviving_expired_attack(self):
        s,sock=self.sender(DemoSender)
        s.offer(ActionEvent('old','DEV01','reset-test',1,NOW,NOW,NOW+150_000_000,'ATTACK','LEFT',{},'test'))
        with patch('realtime_vision.transport.time.monotonic_ns',return_value=NOW):s.service()
        later=NOW+1_000_000_000
        ack={'version':2,'stream_id':'reset-test','status':'SYNC','sync_id':'reset-test:2',
             'sender_ns':later,'receiver_ns':later,'ack_sent_ns':later}
        sock.recvfrom.side_effect=[(json.dumps(ack).encode(),s.target),BlockingIOError()]
        with patch('realtime_vision.transport.time.monotonic_ns',return_value=later):rows=s.service()
        self.assertEqual(s.clock['sync_id'],'reset-test:2');self.assertFalse(s.events)
        self.assertEqual(s.delivery['expired_without_ack'],1)
        self.assertEqual(s.counts['sent_SYNC'],2);self.assertEqual(s.counts['sent_ATTACK'],0)
        self.assertTrue(any(r['type']=='ack' for r in rows))
        self.assertEqual(sock.recvfrom.call_count,3)

    def test_unrelated_socket_failure_is_not_swallowed(self):
        s,sock=self.sender();sock.recvfrom.side_effect=OSError(errno.EBADF,'bad descriptor')
        with patch('realtime_vision.transport.time.monotonic_ns',return_value=NOW):
            with self.assertRaises(OSError):s.service()

if __name__=='__main__':unittest.main()
