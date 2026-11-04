"""UDP v2 pinned stream, bounded leases, conservative clock sync and deduplication."""
import json
import math
import socket
import time
from collections import Counter

VERSION=2
MAX_LEASE_MS=150


def clock_estimate(t1,t2,t3,t4):
    rtt=(t4-t1)-(t3-t2)
    if rtt<0 or t3<t2:raise ValueError('Invalid clock sample')
    return ((t2-t1)+(t3-t4))//2, rtt//2+1_000_000


class ReceiverState:
    """Reference receiver. Time arguments use receiver's own monotonic clock."""
    def __init__(self,stream_id,actor_id):
        self.stream_id=stream_id;self.actor_id=actor_id;self.last_sequence=0
        self.seen=set();self.syncs={};self.block=False;self.movement='NEUTRAL';self.strength=0.
        self.deadline=0;self.attacks=[];self.counts=Counter()
    def tick(self,now):
        if now>=self.deadline:
            if self.block or self.movement!='NEUTRAL':self.counts['lease_expiry']+=1
            self.block=False;self.movement='NEUTRAL';self.strength=0.
    def accept(self,p,received_ns,applied_ns=None):
        now=received_ns if applied_ns is None else applied_ns;self.tick(now)
        def reject(reason):self.counts[reason]+=1;return {'status':reason}
        if not isinstance(p,dict) or p.get('version')!=2:return reject('bad_version')
        if p.get('stream_id')!=self.stream_id or p.get('capture_epoch')!=self.stream_id or p.get('actor_id')!=self.actor_id:return reject('wrong_identity')
        if p.get('kind')=='SYNC':
            ident=p.get('sync_id')
            if not isinstance(ident,str) or len(ident)>100:return reject('bad_sync')
            self.syncs={k:v for k,v in self.syncs.items() if now-v<5_000_000_000}
            self.syncs[ident]=received_ns
            return {'status':'SYNC','sync_id':ident,'sender_ns':p['sent_ns'],'receiver_ns':received_ns}
        seq=p.get('sequence')
        if type(seq) is not int or seq<=self.last_sequence:return reject('old_sequence')
        if p.get('kind') not in ('STATE','ATTACK'):return reject('bad_kind')
        sid=p.get('sync_id');uncertainty=p.get('clock_uncertainty_ns');offset=p.get('receiver_offset_ns');sent=p.get('sent_ns');ttl=p.get('ttl_ms')
        if sid not in self.syncs or now-self.syncs[sid]>5_000_000_000:return reject('unsynchronized')
        if any(type(v) is not int for v in (uncertainty,offset,sent,ttl)) or not 0<=uncertainty<=25_000_000 or not 0<ttl<=MAX_LEASE_MS:return reject('invalid_timing')
        age=now-(sent+offset)
        if age < -uncertainty or age+uncertainty>=ttl*1_000_000:return reject('expired')
        deadline=sent+offset+ttl*1_000_000-uncertainty
        if p['kind']=='ATTACK':
            eid=p.get('event_id')
            if not isinstance(eid,str) or not eid or len(eid)>200 or p.get('side') not in ('LEFT','RIGHT'):return reject('bad_event')
            if eid in self.seen:return reject('duplicate_event')
            if len(self.seen)>=10000:return reject('dedup_capacity_restart_required')
            self.seen.add(eid);self.attacks.append({'event_id':eid,'side':p['side'],'received_ns':received_ns,'applied_ns':now})
        else:
            if type(p.get('block_active')) is not bool or p.get('movement') not in ('LEFT','RIGHT','NEUTRAL'):return reject('bad_state')
            strength=p.get('movement_strength')
            if type(strength) not in (int,float) or not math.isfinite(strength) or not 0<=strength<=1:return reject('bad_strength')
            self.block=p['block_active'];self.movement=p['movement'];self.strength=float(strength);self.deadline=deadline
        self.last_sequence=seq;self.counts['accepted_'+p['kind']]+=1
        return {'status':'APPLIED','event_id':p.get('event_id'),'sequence':seq,'receiver_ns':received_ns,'applied_ns':now}


class Sender:
    def __init__(self,host,port,stream_id,actor_id):
        self.target=(socket.gethostbyname(host),port);self.stream_id=stream_id;self.actor_id=actor_id;self.sequence=0
        self.sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.sock.bind(('0.0.0.0',0));self.sock.setblocking(False)
        self.pending={};self.clock=None;self.last_sync=0;self.counts=Counter()
    def send(self,kind,**fields):
        now=time.monotonic_ns();self.sequence+=1
        p={'version':2,'kind':kind,'stream_id':self.stream_id,'actor_id':self.actor_id,'capture_epoch':self.stream_id,
           'sequence':self.sequence,'sent_ns':now,**fields}
        if self.clock:
            p.update({k:self.clock[k] for k in ('sync_id','receiver_offset_ns','clock_uncertainty_ns')})
        # SYNC must preserve its new ID, not the previous accepted calibration.
        if kind=='SYNC':p['sync_id']=fields['sync_id'];self.pending[p['sync_id']]=now
        try:self.sock.sendto(json.dumps(p,allow_nan=False).encode(),self.target);self.counts['sent_'+kind]+=1
        except (OSError,ValueError) as exc:self.counts['send_error']+=1;return {'type':'send_error','error':str(exc),'packet':p}
        return {'type':'send','packet':p,'send_return_ns':time.monotonic_ns()}
    def service(self):
        rows=[];now=time.monotonic_ns()
        self.pending={k:v for k,v in self.pending.items() if now-v<5_000_000_000}
        if now-self.last_sync>=1_000_000_000:
            self.last_sync=now;rows.append(self.send('SYNC',sync_id=f'{self.stream_id}:{self.sequence+1}'))
        for _ in range(100):
            try:data,peer=self.sock.recvfrom(8192)
            except BlockingIOError:break
            t4=time.monotonic_ns()
            if peer!=self.target:continue
            try:
                ack=json.loads(data)
                if not isinstance(ack,dict):raise ValueError('ACK must be an object')
                if ack.get('version')!=2 or ack.get('stream_id')!=self.stream_id:continue
                rows.append({'type':'ack','ack':ack,'ack_receive_ns':t4})
                sid=ack.get('sync_id')
                if ack.get('status')=='SYNC' and sid in self.pending:
                    t1=self.pending.pop(sid)
                    if ack['sender_ns']!=t1:continue
                    offset,uncertainty=clock_estimate(t1,ack['receiver_ns'],ack['ack_sent_ns'],t4)
                    if uncertainty<=25_000_000:self.clock={'sync_id':sid,'receiver_offset_ns':offset,'clock_uncertainty_ns':uncertainty,'updated_ns':t4}
            except (ValueError,KeyError,TypeError):self.counts['bad_ack']+=1
        if self.clock and now-self.clock['updated_ns']>3_000_000_000:self.clock=None
        return rows
    def close(self):self.sock.close()
