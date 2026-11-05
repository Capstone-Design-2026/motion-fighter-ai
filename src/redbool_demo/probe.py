"""User-launched camera-free Unity rehearsal, gated by fresh game-ready ACKs."""
import argparse
from datetime import datetime
import json
import math
from pathlib import Path
import time
import uuid
from action_engine.types import ActionEvent
from .transport import DemoSender

RECEIVER_VERSION='unity-55844cd-v5'


def guard_check(before, packets):
    acks=[r['ack'] for r in packets if r['type']=='ack' and 'guards_started' in r['ack']]
    count=max((a['guards_started'] for a in acks),default=before if before is not None else 0)
    starts=count-before if before is not None else None
    released=bool(acks) and acks[-1].get('game_guard_active') is False
    return {'starts_while_held':starts,'finished_after_release_window':released,
            'passed':starts==1 and released}


class ReadyGate:
    """Require fresh responses to this phase's neutral STATE requests.

    Sender already checks the peer, stream and protocol version. Matching the
    echoed sequence/time prevents a late ACK from a previous phase arming this
    one. Readiness is an observation, not a reservation of the game's state.
    """
    fresh_ns=150_000_000
    stable_ns=200_000_000

    def __init__(self):
        self.pending={};self.since=None;self.last_received=None;self.last_sent=None
        self.last_sequence=-1;self.last_ack=None

    def observe(self, rows, now_ns):
        self.pending={k:v for k,v in self.pending.items() if 0<=now_ns-v<=self.fresh_ns}
        if self.last_sent is not None and now_ns-self.last_sent>self.fresh_ns:self.since=None
        for row in rows:
            if row['type']=='send':
                p=row['packet']
                if p['kind']=='STATE' and p.get('block_active') is False and p.get('movement')=='NEUTRAL':
                    self.pending[p['sequence']]=p['sent_ns']
            elif row['type']=='ack':
                a=row['ack'];seq=a.get('sequence')
                if type(seq) is not int:continue
                sent=self.pending.pop(seq,None)
                received=row.get('ack_receive_ns')
                if (sent is None or a.get('sender_ns')!=sent or type(received) is not int
                        or seq<=self.last_sequence or not 0<=now_ns-sent<=self.fresh_ns
                        or not sent<=received<=now_ns):continue
                if self.last_sent is not None and sent-self.last_sent>self.fresh_ns:self.since=None
                self.last_sequence=seq;self.last_sent=sent;self.last_received=received;self.last_ack=a
                ok=(a.get('status')=='APPLIED' and a.get('receiver_version')==RECEIVER_VERSION
                    and a.get('game_can_act') is True and a.get('game_guard_active') is False)
                if not ok:self.since=None
                elif self.since is None:self.since=received

    def ready(self,now_ns):
        return (self.since is not None and self.last_sent is not None
                and 0<=now_ns-self.last_sent<=self.fresh_ns
                and self.last_received-self.since>=self.stable_ns)


def run(host,port,output,ready_timeout=8.):
    if not math.isfinite(ready_timeout) or ready_timeout<=0:raise ValueError('Positive finite ready timeout required')
    stream=uuid.uuid4().hex;sender=DemoSender(host,port,stream,'DEV01')
    directory=output/(datetime.now().strftime('%Y%m%d_%H%M%S')+'_'+stream[:8]);directory.mkdir(parents=True,exist_ok=False)
    rows=[];expected={};guard_result={'passed':False,'reason':'not_run'};readiness=[];failure=None;completed=False;guard_start=None;before=None

    def pump(seconds,block=False,movement='NEUTRAL'):
        start=len(rows);until=time.monotonic()+seconds
        while time.monotonic()<until:
            if sender.clock:rows.append(sender.send('STATE',ttl_ms=150,block_active=block,movement=movement,
                movement_strength=.4 if movement!='NEUTRAL' else 0.,tracking='SYNTHETIC',fsm_state='READY',calibration_version=1,calibrated=True))
            rows.extend(sender.service());time.sleep(.02)
        return rows[start:]

    def wait_ready(label):
        print('WAIT FOR GAME READY: '+label,flush=True)
        gate=ReadyGate();start=time.monotonic();deadline=start+ready_timeout
        while time.monotonic()<deadline:
            new=pump(.04);now=time.monotonic_ns()
            if not sender.clock:gate=ReadyGate()
            else:gate.observe(new,now)
            if sender.clock and gate.ready(now):
                readiness.append({'before':label,'passed':True,'wait_seconds':time.monotonic()-start,
                    'ack':gate.last_ack})
                return gate.last_ack
        readiness.append({'before':label,'passed':False,'wait_seconds':time.monotonic()-start,'ack':gate.last_ack})
        raise RuntimeError('Game not ready before '+label+'. Check KO/result screen, hit stun, guard/recovery, pause and receiver state. No new test action was queued.')

    try:
        print(f'Connecting to {host}:{port}; stream {stream}',flush=True)
        deadline=time.monotonic()+5
        while sender.clock is None and time.monotonic()<deadline:pump(.05)
        if sender.clock is None:raise RuntimeError('No clock ACK. Start Unity Play Mode, check target LAN IP and UDP 5065 firewall.')
        for gesture in ('LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE'):
            wait_ready(gesture)
            side=gesture.split('_')[0]
            eid=uuid.uuid4().hex;expected[eid]={'gesture':gesture,'expected':'APPLIED','expected_action':{'LEFT_PUNCH':'JAB','RIGHT_PUNCH':'STRAIGHT','LEFT_RAISE':'HIGH_KICK','RIGHT_RAISE':'LOW_KICK'}[gesture]}
            now=time.monotonic_ns();sender.offer(ActionEvent(eid,'DEV01',stream,0,now,now,now+150_000_000,'ATTACK',side,{'synthetic':True,'gesture':gesture},'probe'))
            print(gesture,flush=True);packets=pump(.35)
            ack=[r['ack'] for r in packets if r['type']=='ack' and r['ack'].get('event_id')==eid]
            if not any(a.get('status')=='APPLIED' and a.get('applied_action')==expected[eid]['expected_action'] for a in ack):
                statuses=[a.get('status') for a in ack]
                raise RuntimeError(f'{gesture} was not applied ({statuses or ["NO_ACK"]}). No replacement attack will be sent.')
        before=wait_ready('BLOCK').get('guards_started');guard_start=len(rows)
        print('BLOCK: one timed guard, then release input',flush=True);pump(3,True);pump(.1)
        wait_ready('MOVE')
        guard_result=guard_check(before,rows[guard_start:])
        if not guard_result['passed']:raise RuntimeError('Timed guard did not start exactly once and finish; movement test not run.')
        print('MOVE left / right / neutral',flush=True);pump(1,movement='LEFT');pump(1,movement='RIGHT');pump(.5)
        completed=True
    except RuntimeError as exc:
        failure=str(exc)
    finally:
        if sender.clock:rows.append(sender.send('STATE',ttl_ms=150,block_active=False,movement='NEUTRAL',movement_strength=0.,tracking='STOPPED',fsm_state='UNAVAILABLE',calibration_version=1,calibrated=False))
        sender.close()
        if guard_start is not None:guard_result=guard_check(before,rows[guard_start:])
        (directory/'udp.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))
        checks=[]
        for eid,info in expected.items():
            acks=[r['ack']['status'] for r in rows if r['type']=='ack' and r['ack'].get('event_id')==eid]
            actions=[r['ack'].get('applied_action') for r in rows if r['type']=='ack' and r['ack'].get('event_id')==eid and r['ack'].get('status')=='APPLIED']
            checks.append({**info,'event_id':eid,'statuses':acks,'actions':actions,'passed':info['expected'] in acks and info['expected_action'] in actions})
        versions=sorted({r['ack'].get('receiver_version','unknown') for r in rows if r['type']=='ack'})
        compatible=RECEIVER_VERSION in versions
        report={'probe_version':'ready-aware-v1','checks':checks,'readiness_checks':readiness,'failure':failure,
            'guard_check':guard_result,'receiver_compatible':compatible,
            'passed':completed and failure is None and len(checks)==4 and all(c['passed'] for c in checks) and guard_result['passed'] and compatible,
            'scope':'synthetic attack/timed-guard command rehearsal; movement screen behavior, camera recognition and screen latency require separate verification',
            'delivery':dict(sender.delivery),'receiver_clocks':sorted({r['ack'].get('receiver_clock','unknown') for r in rows if r['type']=='ack'}),
            'receiver_versions':versions,'guard_modes':sorted({r['ack'].get('guard_mode','unknown') for r in rows if r['type']=='ack'})}
        (directory/'summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));print(directory)
    if not report['passed']:raise RuntimeError(failure or 'Unity gesture rehearsal failed; see summary checks. This is not a recognition test.')
    return report


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065)
    p.add_argument('--output',type=Path,default=Path('local_data/demo_probe'))
    p.add_argument('--ready-timeout',type=float,default=8.,help='Maximum seconds waiting for game readiness before each new synthetic action')
    a=p.parse_args()
    if not math.isfinite(a.ready_timeout) or a.ready_timeout<=0:p.error('--ready-timeout must be positive and finite')
    try:run(a.host,a.port,a.output,a.ready_timeout)
    except RuntimeError as exc:p.exit(1,'Rehearsal failed: '+str(exc)+'\n')
