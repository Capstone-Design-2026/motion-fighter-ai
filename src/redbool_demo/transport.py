"""Bounded event retries preserve ID and the original event deadline."""
import time
from collections import Counter
from realtime_vision.transport import Sender


class DemoSender(Sender):
    def __init__(self,*args,**kwargs):
        super().__init__(*args,**kwargs)
        self.events={};self.delivery=Counter();self.last_status='WAITING FOR RECEIVER'

    def offer(self,event):
        if len(self.events)>=32:
            self.delivery['queue_full']+=1;return
        self.events.setdefault(event.event_id,{'event':event,'sent':0,'last':0})

    def service(self):
        rows=super().service();now=time.monotonic_ns()
        for row in rows:
            if row['type']!='ack':continue
            ack=row['ack'];status=ack.get('status','').upper();eid=ack.get('event_id')
            if eid in self.events and status in ('APPLIED','DUPLICATE_EVENT','GAME_BUSY','NO_PLAYER','INPUT_LOCKED','UNASSIGNED','TUTORIAL_APPLIED','BAD_GESTURE','BAD_KIND'):
                self.events.pop(eid)
                self.delivery[status]+=1;self.last_status=status
        for eid,item in list(self.events.items()):
            event=item['event'];remaining=int((event.expires_ns-now)//1_000_000)-1
            if remaining<=0:
                self.events.pop(eid);self.delivery['expired_without_ack']+=1
                self.last_status='EVENT EXPIRED WITHOUT ACK';continue
            if self.clock and item['sent']<3 and now-item['last']>=35_000_000:
                gesture=event.reason.get('gesture')
                rows.append(self.send('GESTURE' if gesture else 'ATTACK',ttl_ms=min(150,remaining),event_id=eid,side=event.side,
                    gesture=gesture,
                    event_expires_ns=event.expires_ns,commit_ns=event.committed_ns,
                    capture_ns=event.source_capture_ns,source_frame_id=event.source_frame_id))
                item['last']=now;item['sent']+=1
                self.delivery['transmissions']+=1
        return rows
