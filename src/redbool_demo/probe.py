"""User-launched camera-free Unity connection rehearsal. Uses real UDP v2 ACKs."""
import argparse
from dataclasses import asdict
from datetime import datetime
import json
from pathlib import Path
import time
import uuid
from action_engine.types import ActionEvent
from .transport import DemoSender


def guard_check(before, packets):
    acks=[r['ack'] for r in packets if r['type']=='ack' and 'guards_started' in r['ack']]
    count=max((a['guards_started'] for a in acks),default=before if before is not None else 0)
    starts=count-before if before is not None else None
    released=bool(acks) and acks[-1].get('game_guard_active') is False
    return {'starts_while_held':starts,'finished_after_release_window':released,
            'passed':starts==1 and released}


def run(host,port,output):
    stream=uuid.uuid4().hex;sender=DemoSender(host,port,stream,'DEV01')
    directory=output/(datetime.now().strftime('%Y%m%d_%H%M%S')+'_'+stream[:8]);directory.mkdir(parents=True,exist_ok=False)
    rows=[];expected={};guard_result={'passed':False,'reason':'not_run'}
    def pump(seconds,block=False,movement='NEUTRAL'):
        until=time.monotonic()+seconds
        while time.monotonic()<until:
            if sender.clock:rows.append(sender.send('STATE',ttl_ms=150,block_active=block,movement=movement,
                movement_strength=.4 if movement!='NEUTRAL' else 0.,tracking='SYNTHETIC',fsm_state='READY'))
            rows.extend(sender.service());time.sleep(.02)
    try:
        print(f'Connecting to {host}:{port}; stream {stream}',flush=True)
        deadline=time.monotonic()+5
        while sender.clock is None and time.monotonic()<deadline:pump(.05)
        if sender.clock is None:raise RuntimeError('No clock ACK. Start Unity Play Mode, check target LAN IP and UDP 5065 firewall.')
        pump(1)
        for gesture in ('LEFT_PUNCH','RIGHT_PUNCH','LEFT_RAISE','RIGHT_RAISE'):
            side=gesture.split('_')[0]
            eid=uuid.uuid4().hex;expected[eid]={'gesture':gesture,'expected':'APPLIED','expected_action':{'LEFT_PUNCH':'JAB','RIGHT_PUNCH':'STRAIGHT','LEFT_RAISE':'HIGH_KICK','RIGHT_RAISE':'LOW_KICK'}[gesture]}
            now=time.monotonic_ns();sender.offer(ActionEvent(eid,'DEV01',stream,0,now,now,now+150_000_000,'ATTACK',side,{'synthetic':True,'gesture':gesture},'probe'))
            # Latest high kick lasts 1.24 s plus up to .4 s combat recovery.
            print(gesture,flush=True);pump(2.5)
        counters=[r['ack']['guards_started'] for r in rows if r['type']=='ack' and 'guards_started' in r['ack']]
        before=counters[-1] if counters else None;guard_start=len(rows)
        print('BLOCK: one timed guard, then release input',flush=True);pump(3,True);pump(1)
        guard_result=guard_check(before,rows[guard_start:])
        print('MOVE left / right / neutral',flush=True);pump(1,movement='LEFT');pump(1,movement='RIGHT');pump(.5)
    finally:
        if sender.clock:rows.append(sender.send('STATE',ttl_ms=150,block_active=False,movement='NEUTRAL',movement_strength=0.,tracking='STOPPED',fsm_state='UNAVAILABLE'))
        sender.close()
        (directory/'udp.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))
        checks=[]
        for eid,info in expected.items():
            acks=[r['ack']['status'] for r in rows if r['type']=='ack' and r['ack'].get('event_id')==eid]
            actions=[r['ack'].get('applied_action') for r in rows if r['type']=='ack' and r['ack'].get('event_id')==eid and r['ack'].get('status')=='APPLIED']
            checks.append({**info,'event_id':eid,'statuses':acks,'actions':actions,'passed':info['expected'] in acks and info['expected_action'] in actions})
        versions=sorted({r['ack'].get('receiver_version','unknown') for r in rows if r['type']=='ack'})
        compatible='unity-55844cd-v5' in versions
        report={'checks':checks,'guard_check':guard_result,'receiver_compatible':compatible,
            'passed':len(checks)==4 and all(c['passed'] for c in checks) and guard_result['passed'] and compatible,
            'scope':'synthetic attack/timed-guard command rehearsal; movement screen behavior, camera recognition and screen latency require separate verification',
            'delivery':dict(sender.delivery),'receiver_clocks':sorted({r['ack'].get('receiver_clock','unknown') for r in rows if r['type']=='ack'}),
            'receiver_versions':versions,
            'guard_modes':sorted({r['ack'].get('guard_mode','unknown') for r in rows if r['type']=='ack'})}
        (directory/'summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));print(directory)
    if not report['passed']:raise RuntimeError('Unity gesture rehearsal failed; see summary checks. This is not a recognition test.')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=5065)
    p.add_argument('--output',type=Path,default=Path('local_data/demo_probe'))
    a=p.parse_args();run(a.host,a.port,a.output)
