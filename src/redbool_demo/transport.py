"""Bounded event retries preserve ID and the original event deadline."""
import time
from collections import Counter
from realtime_vision.transport import Sender


class DemoSender(Sender):
    def __init__(self,*args,**kwargs):
        super().__init__(*args,**kwargs)
        self.events={};self.delivery=Counter();self.last_status='WAITING FOR RECEIVER'
        self.feedback={};self.feedback_sequence=-1;self.tutorial={}

    def game_hint(self, now_ns=None):
        now=time.monotonic_ns() if now_ns is None else now_ns
        f=self.feedback
        if not f or not 0<=now-f['sent_ns']<=500_000_000:
            return 'GAME UNKNOWN / SPECIAL UNKNOWN'
        game='GAME READY' if f['game_can_act'] else 'GAME BUSY'
        special='SPECIAL READY' if f['special_ready'] else 'SPECIAL NEEDS CHARGE'
        return game+' / '+special

    def tutorial_context(self, now_ns=None):
        now=time.monotonic_ns() if now_ns is None else now_ns
        return dict(self.tutorial) if self.tutorial and 0<=now-self.tutorial['sent_ns']<=500_000_000 else None

    def offer(self,event):
        if len(self.events)>=32:
            self.delivery['queue_full']+=1;return
        self.events.setdefault(event.event_id,{'event':event,'sent':0,'last':0})

    def service(self):
        rows=super().service();now=time.monotonic_ns()
        for row in rows:
            if row['type']!='ack':continue
            ack=row['ack'];status=ack.get('status','').upper();eid=ack.get('event_id')
            seq=ack.get('sequence');sent=ack.get('sender_ns')
            if (type(seq) is int and seq>self.feedback_sequence and type(sent) is int
                    and 0<=now-sent<=150_000_000
                    and type(ack.get('game_can_act')) is bool
                    and type(ack.get('special_ready')) is bool):
                self.feedback={'sent_ns':sent,'game_can_act':ack['game_can_act'],
                               'special_ready':ack['special_ready']}
                self.feedback_sequence=seq
                self.tutorial={'sent_ns':sent,'run':ack.get('tutorial_run',''),
                    'action':ack.get('tutorial_action',''),'phase':ack.get('tutorial_phase',''),
                    'hits':ack.get('tutorial_hits',0)}
            if eid in self.events and status in ('APPLIED','DUPLICATE_EVENT','GAME_BUSY','NO_PLAYER','INPUT_LOCKED','UNASSIGNED','TUTORIAL_APPLIED','BAD_GESTURE','BAD_KIND','NOT_CHARGED','NO_SPECIAL'):
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
