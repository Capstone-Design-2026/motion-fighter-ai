"""Camera-free sender for RedboolMotionPlayCheck with REDBOOL_UDP_CHECK_DIR.

Run using the paired Python project's venv and PYTHONPATH=src:tests.
The output directory must be fresh and shared with the isolated Unity batch run.
"""
import argparse
from dataclasses import replace
import json
from pathlib import Path
import time
import uuid

from redbool_demo.probe import run as run_probe, ReadyGate
from redbool_demo.transport import DemoSender
from action_engine.types import ActionEvent
from redbool_demo.six_engine import SixGestureEngine
from test_chest_special import pose


def run(directory):
    directory.mkdir(parents=True, exist_ok=True)
    if any((directory / x).exists() for x in ('done', 'charge.request', 'charged')):
        raise RuntimeError('Use a fresh output directory for each run')
    sender = None
    rows, checks = [], []
    passed = False
    try:
        deadline = time.monotonic() + 240
        while not (directory / 'ready').exists():
            if time.monotonic() >= deadline:
                raise TimeoutError('Unity did not become ready')
            time.sleep(.1)
        report = run_probe('127.0.0.1', 5065, directory / 'probe')
        checks.append({'check': 'four_attacks_and_held_guard', 'passed': report['passed']})
        time.sleep(.65)  # The receiver permits a new stream after the old stream stops.
        sender = DemoSender('127.0.0.1', 5065, uuid.uuid4().hex, 'DEV01')

        def pump(seconds):
            start = len(rows)
            until = time.monotonic() + seconds
            while time.monotonic() < until:
                if sender.clock:
                    rows.append(sender.send('STATE', ttl_ms=150, block_active=False,
                        movement='NEUTRAL', movement_strength=0., tracking='SYNTHETIC',
                        fsm_state='READY', calibration_version=1, calibrated=True))
                rows.extend(sender.service())
                time.sleep(.01)
            return rows[start:]

        def ready():
            gate = ReadyGate()
            deadline = time.monotonic() + 8
            while time.monotonic() < deadline:
                gate.observe(pump(.03), time.monotonic_ns())
                if sender.clock and gate.ready(time.monotonic_ns()):
                    return gate.last_ack
            raise TimeoutError('Unity not ready for special')

        engine = SixGestureEngine(movement=True)
        engine.request_calibration()
        frame = 0

        def frames(kind, count):
            nonlocal frame
            emitted = []
            for _ in range(count):
                frame += 1
                now = time.monotonic_ns()
                observation = replace(pose(kind, frame, now), capture_epoch=sender.stream_id)
                engine.process(observation, now_ns=now)
                for event in engine.drain_events(now):
                    emitted.append(event)
                    sender.offer(event)
                    # Duplicate the same event immediately: native applied count must stay one.
                    rows.extend(sender.service())
                    if event.reason.get('gesture') == 'SPECIAL':
                        rows.append(sender.send('GESTURE', ttl_ms=100, event_id=event.event_id,
                            side='BOTH', gesture='SPECIAL', event_expires_ns=event.expires_ns,
                            commit_ns=event.committed_ns, capture_ns=event.source_capture_ns,
                            source_frame_id=event.source_frame_id))
                engine.take_logs()
                pump(.033)
            return emitted

        def special(expected):
            ready()
            # The readiness wait has no pose frames; rearm after that intentional gap.
            frames('guard', 30)
            events = frames('cross', 45)
            if len(events) != 1 or events[0].reason.get('gesture') != 'SPECIAL':
                raise AssertionError('Expected exactly one SPECIAL from held synthetic X pose')
            pump(.1)
            acks = [r['ack'] for r in rows if r['type'] == 'ack'
                    and r['ack'].get('event_id') == events[0].event_id]
            ok = bool(acks) and all(a['status'] == expected for a in acks)
            if expected == 'APPLIED':
                ok = ok and all(a.get('applied_action') == 'SPECIAL' for a in acks)
            checks.append({'check': 'special_' + expected.lower(), 'passed': ok,
                           'statuses': [a['status'] for a in acks]})
            if not ok:
                raise AssertionError(checks[-1])

        ready()
        frames('guard', 55)
        if not engine.calibrated or not engine.ready:
            raise AssertionError('Synthetic guard did not calibrate')
        special('NOT_CHARGED')
        frames('guard', 30)
        (directory / 'charge.request').write_text('charge for isolated test')
        deadline = time.monotonic() + 3
        while not (directory / 'charged').exists():
            pump(.03)
            if time.monotonic() > deadline:
                raise TimeoutError('Unity did not charge')
        if not ready().get('special_ready'):
            raise AssertionError('Charged state missing in actual ACK')
        special('APPLIED')
        frames('guard', 30)
        if not engine.ready:
            raise AssertionError('X did not return to ready')
        checks.append({'check': 'engine_guard_return', 'passed': True})
        ready()  # Wait for the new QiPalm animation and recovery to finish.
        eid = uuid.uuid4().hex; now = time.monotonic_ns()
        sender.offer(ActionEvent(eid, 'DEV01', sender.stream_id, 0, now, now,
            now + 150_000_000, 'ATTACK', 'RIGHT', {'synthetic': True, 'gesture': 'RIGHT_PUNCH'}, 'post-qipalm-check'))
        pump(.2)
        follow = [r['ack'] for r in rows if r['type'] == 'ack' and r['ack'].get('event_id') == eid]
        assert follow and all(a['status'] == 'APPLIED' and a['applied_action'] == 'STRAIGHT' for a in follow), follow
        checks.append({'check': 'post_qipalm_straight', 'passed': True})
        samples = [r['ack'] for r in rows if r['type'] == 'ack']
        busy = [a for a in samples if a.get('game_attacking') or a.get('game_special_active')]
        assert busy and all(a.get('game_can_act') is False for a in busy), 'Busy combat must not advertise ready'
        checks.append({'check': 'busy_ack_not_ready', 'passed': True, 'samples': len(busy)})
        assert any(a.get('game_special_active') for a in samples), 'QiPalm activity missing from ACK'
        checks.append({'check': 'qipalm_activity_diagnostics', 'passed': True})
        passed = True
    finally:
        if sender:
            if sender.clock:
                rows.append(sender.send('STATE', ttl_ms=150, block_active=False,
                    movement='NEUTRAL', movement_strength=0., tracking='STOPPED',
                    fsm_state='UNAVAILABLE', calibration_version=1, calibrated=False))
            sender.close()
        (directory / 'special_udp.jsonl').write_text(''.join(json.dumps(r) + '\n' for r in rows))
        (directory / 'summary.json').write_text(json.dumps({'passed': passed, 'checks': checks,
            'scope': 'Synthetic landmarks -> actual engine -> loopback UDP -> native Unity. No camera or visual latency test.'}, indent=2))
        (directory / 'done').write_text('PASS' if passed else 'FAIL')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    run(parser.parse_args().directory)
