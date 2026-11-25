"""Real UDP socket contract tests; not Unity runtime tests."""
import json
import socket
import threading
import time
import unittest
from action_engine.types import ActionEvent
from realtime_vision.transport import ReceiverState
from redbool_demo.transport import DemoSender

class GestureTransportTests(unittest.TestCase):
    def run_receiver(self, status):
        received=[];stop=threading.Event();errors=[]
        sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
        sock.bind(('127.0.0.1',0));sock.settimeout(.01)
        reference=ReceiverState('six-test','DEV01')
        def worker():
            try:
                while not stop.is_set():
                    try: data,peer=sock.recvfrom(8192)
                    except socket.timeout: continue
                    p=json.loads(data)
                    if p['kind']=='SYNC': ack=reference.accept(p,time.monotonic_ns())
                    else:
                        received.append(p);ack={'status':status}
                    ack.update(version=2,stream_id='six-test',event_id=p.get('event_id'),ack_sent_ns=time.monotonic_ns())
                    sock.sendto(json.dumps(ack).encode(),peer)
            except BaseException as exc: errors.append(repr(exc))
        thread=threading.Thread(target=worker);thread.start()
        sender=DemoSender('127.0.0.1',sock.getsockname()[1],'six-test','DEV01')
        try:
            deadline=time.monotonic()+1
            while sender.clock is None and time.monotonic()<deadline:sender.service();time.sleep(.002)
            self.assertIsNotNone(sender.clock)
            now=time.monotonic_ns()
            sender.offer(ActionEvent('right-raise','DEV01','six-test',1,now,now,now+150_000_000,
                'ATTACK','RIGHT',{'gesture':'RIGHT_RAISE'},'six-test'))
            deadline=time.monotonic()+.2
            while time.monotonic()<deadline:sender.service();time.sleep(.002)
            self.assertFalse(sender.events)
            self.assertEqual(sender.delivery[status],1)
            self.assertTrue(received)
            self.assertTrue(all(p['kind']=='GESTURE' and p['gesture']=='RIGHT_RAISE' for p in received))
            self.assertEqual(len({p['event_id'] for p in received}),1)
        finally:stop.set();thread.join();sender.close();sock.close()
        self.assertFalse(errors)

    def test_right_raise_is_delivered_without_punch_fallback(self):
        self.run_receiver('APPLIED')

    def test_old_receiver_rejection_never_falls_back_to_punch(self):
        self.run_receiver('BAD_KIND')
