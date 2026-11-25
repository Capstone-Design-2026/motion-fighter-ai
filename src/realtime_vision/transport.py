"""UDP v2 pinned stream, bounded leases, conservative clock sync and deduplication."""
import json
import math
import socket
import time
from collections import Counter

VERSION=2
MAX_LEASE_MS=150
CLOCK_MAX_AGE_NS=3_000_000_000
SYNC_NORMAL_NS=1_000_000_000
SYNC_RECOVERY_NS=200_000_000
# Increase the uncertainty of an old sample by 1 ms per second. Selecting a
# quieter exchange must never silently refresh that sample's original expiry.
CLOCK_DRIFT_DIVISOR=1000
NETWORK_CLOCK_LIMITS_NS={'lan':25_000_000,'online':75_000_000}


def network_policy(profile='lan'):
    """Transport-only policy. A usable clock is not proof that input is fresh."""
    if profile not in NETWORK_CLOCK_LIMITS_NS:
        raise ValueError('Network profile must be lan or online')
    return {'network_profile':profile,
            'max_clock_uncertainty_ns':NETWORK_CLOCK_LIMITS_NS[profile],
            'max_input_lease_ms':MAX_LEASE_MS,'clock_max_age_ns':CLOCK_MAX_AGE_NS,
            'scope':'Clock synchronization only; original capture/event deadlines still apply; not a latency guarantee'}


def clock_estimate(t1,t2,t3,t4):
    rtt=(t4-t1)-(t3-t2)
    if rtt<0 or t3<t2:raise ValueError('Invalid clock sample')
    return ((t2-t1)+(t3-t4))//2, rtt//2+1_000_000


class ReceiverState:
    """Reference receiver. Time arguments use receiver's own monotonic clock."""
    def __init__(self,stream_id,actor_id,*,network_profile='lan'):
        self.network_profile=network_profile
        self.max_clock_uncertainty_ns=network_policy(network_profile)['max_clock_uncertainty_ns']
        self.stream_id=stream_id;self.actor_id=actor_id;self.last_sequence=0
        self.last_state_sequence=0;self.last_event_sequence=0
        self.calibration_generation=None;self.generation_capture_ns=0;self.live=False
        self.seen=set();self.syncs={};self.block=False;self.movement='NEUTRAL';self.strength=0.
        self.deadline=0;self.attacks=[];self.counts=Counter()
    def tick(self,now):
        if now>=self.deadline:
            if self.block or self.movement!='NEUTRAL':self.counts['lease_expiry']+=1
            self.block=False;self.movement='NEUTRAL';self.strength=0.
            self.live=False
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
        one_shot=p.get('kind')=='ATTACK' or self.network_profile=='online' and p.get('kind')=='GESTURE'
        floor=(self.last_state_sequence if p.get('kind')=='STATE' else self.last_event_sequence) if self.network_profile=='online' else self.last_sequence
        if type(seq) is not int or seq<=floor:return reject('old_sequence')
        if p.get('kind')!='STATE' and not one_shot:return reject('bad_kind')
        sid=p.get('sync_id');uncertainty=p.get('clock_uncertainty_ns');offset=p.get('receiver_offset_ns');sent=p.get('sent_ns');ttl=p.get('ttl_ms')
        if sid not in self.syncs or now-self.syncs[sid]>5_000_000_000:return reject('unsynchronized')
        if any(type(v) is not int for v in (uncertainty,offset,sent,ttl)) or not 0<=uncertainty<=self.max_clock_uncertainty_ns or not 0<ttl<=MAX_LEASE_MS:return reject('invalid_timing')
        age=now-(sent+offset)
        if age < -uncertainty or age+uncertainty>=ttl*1_000_000:return reject('expired')
        deadline=sent+offset+ttl*1_000_000-uncertainty
        if one_shot:
            if self.network_profile=='online':
                expiry=p.get('event_expires_ns')
                if type(expiry) is not int or now+uncertainty>=expiry+offset:return reject('expired')
                commit=p.get('commit_ns');capture=p.get('capture_ns')
                if (type(commit) is not int or type(capture) is not int or not 0<capture<=commit<=sent
                        or not commit<expiry<=commit+MAX_LEASE_MS*1_000_000):return reject('invalid_timing')
            eid=p.get('event_id')
            side_valid=p.get('side') in ('LEFT','RIGHT') or self.network_profile=='online' and p.get('kind')=='GESTURE' and p.get('gesture')=='SPECIAL' and p.get('side')=='BOTH'
            if not isinstance(eid,str) or not eid or len(eid)>200 or not side_valid:return reject('bad_event')
            if eid in self.seen:return reject('duplicate_event')
            if self.network_profile=='online' and (not self.live or p.get('calibration_generation')!=self.calibration_generation
                    or p['capture_ns']<self.generation_capture_ns):return reject('input_locked')
            if len(self.seen)>=10000:return reject('dedup_capacity_restart_required')
            self.seen.add(eid);self.attacks.append({'event_id':eid,'side':p['side'],'received_ns':received_ns,'applied_ns':now})
        else:
            if self.network_profile=='online':
                capture=p.get('capture_ns')
                if type(capture) is not int or not 0<capture<=sent:return reject('invalid_timing')
                deadline=min(deadline,capture+offset+MAX_LEASE_MS*1_000_000-uncertainty)
                if now>=deadline:return reject('expired')
                generation=p.get('calibration_generation')
                if (type(generation) is int and self.calibration_generation is not None
                        and generation<self.calibration_generation):return reject('input_locked')
            if type(p.get('block_active')) is not bool or p.get('movement') not in ('LEFT','RIGHT','NEUTRAL'):return reject('bad_state')
            strength=p.get('movement_strength')
            if type(strength) not in (int,float) or not math.isfinite(strength) or not 0<=strength<=1:return reject('bad_strength')
            self.block=p['block_active'];self.movement=p['movement'];self.strength=float(strength);self.deadline=deadline
            if self.network_profile=='online':
                generation=p.get('calibration_generation')
                valid=type(generation) is int and p.get('calibrated') is True and p.get('tracking')=='VALID'
                if generation!=self.calibration_generation:
                    self.calibration_generation=generation;self.generation_capture_ns=0
                if valid and self.generation_capture_ns==0:self.generation_capture_ns=capture
                self.live=valid
                if not valid:self.block=False;self.movement='NEUTRAL';self.strength=0.
        self.last_sequence=max(self.last_sequence,seq)
        if p['kind']=='STATE':self.last_state_sequence=seq
        else:self.last_event_sequence=seq
        self.counts['accepted_'+p['kind']]+=1
        return {'status':'APPLIED','event_id':p.get('event_id'),'sequence':seq,'receiver_ns':received_ns,'applied_ns':now}


class Sender:
    def __init__(self,host,port,stream_id,actor_id,*,clock_ns=None,network_profile='lan'):
        self.network_profile=network_profile
        self.max_clock_uncertainty_ns=network_policy(network_profile)['max_clock_uncertainty_ns']
        self.clock_ns=clock_ns or (lambda:time.monotonic_ns())
        self.target=(socket.gethostbyname(host),port);self.stream_id=stream_id;self.actor_id=actor_id;self.sequence=0
        self.sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);self.sock.bind(('0.0.0.0',0));self.sock.setblocking(False)
        self.pending={};self.clock=None;self.last_sync=0;self.counts=Counter()
        self.clock_samples=[];self.clock_diagnostics=[]
        self.last_sync_rtt_ns=None
    def _select_clock(self,now):
        previous=self.clock
        self.clock_samples=[s for s in self.clock_samples if 0<=now-s['updated_ns']<=CLOCK_MAX_AGE_NS]
        candidates=[]
        for sample in self.clock_samples:
            uncertainty=sample['clock_uncertainty_ns']+(now-sample['updated_ns']+CLOCK_DRIFT_DIVISOR-1)//CLOCK_DRIFT_DIVISOR
            if uncertainty<=self.max_clock_uncertainty_ns:
                candidates.append({**sample,'clock_uncertainty_ns':uncertainty})
        self.clock=min(candidates,key=lambda s:(s['clock_uncertainty_ns'],-s['updated_ns'])) if candidates else None
        old_id=previous.get('sync_id') if previous else None
        new_id=self.clock.get('sync_id') if self.clock else None
        if old_id==new_id:return
        state='lost' if self.clock is None else 'recovered' if previous is None else 'selected'
        self.counts['clock_'+state]+=1
        self.clock_diagnostics.append({'type':'clock_sync','state':state,'timestamp_ns':now,
            'sync_id':new_id,'uncertainty_ns':self.clock['clock_uncertainty_ns'] if self.clock else None,
            'sample_age_ns':now-self.clock['updated_ns'] if self.clock else None,
            'network_profile':self.network_profile,'max_uncertainty_ns':self.max_clock_uncertainty_ns})
        self.clock_diagnostics=self.clock_diagnostics[-32:]
    def send(self,kind,**fields):
        now=self.clock_ns();self._select_clock(now);self.sequence+=1
        p={'version':2,'kind':kind,'stream_id':self.stream_id,'actor_id':self.actor_id,'capture_epoch':self.stream_id,
           'sequence':self.sequence,'sent_ns':now,**fields}
        if self.clock:
            p.update({k:self.clock[k] for k in ('sync_id','receiver_offset_ns','clock_uncertainty_ns')})
        # SYNC must preserve its new ID, not the previous accepted calibration.
        if kind=='SYNC':p['sync_id']=fields['sync_id'];self.pending[p['sync_id']]=now
        try:self.sock.sendto(json.dumps(p,allow_nan=False).encode(),self.target);self.counts['sent_'+kind]+=1
        except (OSError,ValueError) as exc:self.counts['send_error']+=1;return {'type':'send_error','error':str(exc),'packet':p}
        return {'type':'send','packet':p,'send_return_ns':self.clock_ns()}
    def service(self):
        rows=[];now=self.clock_ns()
        self._select_clock(now)
        self.pending={k:v for k,v in self.pending.items() if now-v<5_000_000_000}
        recovering=(self.clock is None or self.clock['clock_uncertainty_ns']>8_000_000
                    or now-self.clock['updated_ns']>=1_000_000_000)
        interval=SYNC_RECOVERY_NS if recovering else SYNC_NORMAL_NS
        if now-self.last_sync>=interval:
            self.last_sync=now;rows.append(self.send('SYNC',sync_id=f'{self.stream_id}:{self.sequence+1}'))
        for _ in range(100):
            try:data,peer=self.sock.recvfrom(8192)
            except BlockingIOError:break
            except ConnectionResetError as exc:
                # UDP on Windows reports a previous ICMP port-unreachable as
                # WSAECONNRESET (10054). It is not a successful ACK or a TCP
                # disconnect. End this receive batch, but still run lease and
                # event expiry, and allow the next scheduled SYNC to retry.
                self.counts['receive_reset']+=1
                rows.append({'type':'receive_error','error':str(exc),
                    'errno':exc.errno,'winerror':getattr(exc,'winerror',None),
                    'recoverable':True})
                break
            t4=self.clock_ns()
            if peer!=self.target:continue
            try:
                ack=json.loads(data)
                if not isinstance(ack,dict):raise ValueError('ACK must be an object')
                if ack.get('version')!=2 or ack.get('stream_id')!=self.stream_id:continue
                rows.append({'type':'ack','ack':ack,'ack_receive_ns':t4})
                if self.network_profile=='online':
                    status=ack.get('status')
                    if isinstance(status,str) and len(status)<=64:self.counts['ack_'+status]+=1
                sid=ack.get('sync_id')
                if ack.get('status')=='SYNC' and sid in self.pending:
                    t1=self.pending.pop(sid)
                    if ack['sender_ns']!=t1:continue
                    offset,uncertainty=clock_estimate(t1,ack['receiver_ns'],ack['ack_sent_ns'],t4)
                    self.last_sync_rtt_ns=(t4-t1)-(ack['ack_sent_ns']-ack['receiver_ns'])
                    if self.network_profile=='online':
                        rows.append({'type':'network_clock_sample','network_profile':self.network_profile,
                            'timestamp_ns':t4,'sync_id':sid,'corrected_rtt_ns':self.last_sync_rtt_ns,
                            'uncertainty_ns':uncertainty,'max_uncertainty_ns':self.max_clock_uncertainty_ns,
                            'accepted':uncertainty<=self.max_clock_uncertainty_ns})
                    if uncertainty<=self.max_clock_uncertainty_ns:
                        self.counts['sync_sample_accepted']+=1
                        self.clock_samples.append({'sync_id':sid,'receiver_offset_ns':offset,'clock_uncertainty_ns':uncertainty,'updated_ns':t4})
                    else:self.counts['sync_sample_uncertain']+=1
            except (ValueError,KeyError,TypeError):self.counts['bad_ack']+=1
        self._select_clock(self.clock_ns())
        rows.extend(self.clock_diagnostics);self.clock_diagnostics.clear()
        return rows
    def transport_diagnostics(self):
        now=self.clock_ns();self._select_clock(now)
        return {**network_policy(self.network_profile),'synchronized':self.clock is not None,
                'clock_uncertainty_ns':self.clock['clock_uncertainty_ns'] if self.clock else None,
                'clock_sample_age_ns':now-self.clock['updated_ns'] if self.clock else None,
                'last_sync_corrected_rtt_ns':self.last_sync_rtt_ns,'counts':dict(self.counts)}
    def close(self):self.sock.close()
