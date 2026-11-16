"""Two real native games: camera-free LAN versus authenticated Photon diagnostics.

Nothing launches without --run. No SDK substitutes, webcam, backend, inference,
input-deadline changes, or success inferred merely from a process starting.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
import uuid


MS = 1_000_000
WORLD_FIELDS = ('round', 'winner', 'remaining', 'paused', 'resolved', 'fighting')
ACTOR_FIELDS = ('hp', 'maxHp', 'charge', 'step', 'hits', 'attackSerial', 'phase', 'live')
LOG_TAGS = ('REDBOOL PERF', 'REDBOOL VIEW CPU', 'REDBOOL VIEW METRIC',
            'REDBOOL ONLINE ANTICIPATION', 'REDBOOL ONLINE INPUT')


def distribution(values):
    values = sorted(float(v) for v in values if math.isfinite(v))
    if not values:
        return {'samples': 0}
    def percentile(fraction):
        index = (len(values) - 1) * fraction
        lo = math.floor(index)
        return values[lo] + (values[math.ceil(index)] - values[lo]) * (index - lo)
    return dict(samples=len(values), minimum=values[0], p50=percentile(.5),
                p95=percentile(.95), p99=percentile(.99), maximum=values[-1])


def parse_metrics(text):
    result = {tag: [] for tag in LOG_TAGS}
    for line in text.splitlines():
        for tag in LOG_TAGS:
            marker = '[' + tag + ']'
            if marker not in line:
                continue
            fields = {}
            for key, raw in re.findall(r'(\w+)=([^\s]+)', line.split(marker, 1)[1]):
                if raw.lower() in ('true', 'false'):
                    fields[key] = raw.lower() == 'true'
                else:
                    try:
                        fields[key] = float(raw) if '.' in raw else int(raw)
                    except ValueError:
                        fields[key] = raw
            result[tag].append(fields)
    return result


def read_status(path):
    try:
        value = json.loads(path.read_text(encoding='utf-8'))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        # A direct WriteAllText can briefly expose an incomplete diagnostic file.
        return {}


def counter_window(before, after):
    elapsed = after.get('realtime_s', 0) - before.get('realtime_s', 0)
    result = {'seconds': elapsed, 'scope': 'SDK byte counters; excludes transport headers and is not a billing estimate'}
    for direction in ('in', 'out'):
        count = after.get('photon_bytes_' + direction, 0) - before.get('photon_bytes_' + direction, 0)
        result[direction + '_bytes'] = count
        result[direction + '_bytes_per_second'] = count / elapsed if elapsed > 0 and count >= 0 else None
    return result


def native_clock_time(realtime_s, status, host_clock):
    """Diagnostic only: both Unity processes use this machine's Stopwatch clock."""
    if not status.get('clock_ns') or not host_clock:
        return None
    native_ns = status['clock_ns'] + (realtime_s - status['realtime_s']) * 1_000_000_000
    return native_ns - host_clock['receiver_offset_ns']


def combat_performance(rows, before, after):
    beginning, end = before.get('realtime_s'), after.get('realtime_s')
    complete = [row for row in rows if beginning is not None and end is not None
                and row.get('realtime_s', 0) - row.get('seconds', 0) >= beginning
                and row.get('realtime_s', 0) <= end]
    frames = sum(row.get('frames', 0) for row in complete)
    seconds = sum(row.get('seconds', 0) for row in complete)
    return {'scope': 'Only complete native PERF windows within measured combat diagnostic boundaries',
            'windows': complete, 'frames': frames, 'seconds': seconds,
            'weighted_fps': frames / seconds if seconds else None,
            'focused_frame_fraction': sum(row.get('focused_frames', 0) for row in complete) / frames if frames else None}


def practice_controls(context, now, previous, since):
    """Normal source states and real practice events, never practice-skip commands."""
    target, phase = context.get('action', ''), context.get('phase', '')
    block, movement, gesture = False, 'NEUTRAL', None
    if target != previous:
        previous, since = target, now
    if phase == 'PRACTICE':
        held = (now - since) % 1.2 > .6
        if target == 'BLOCK':
            block = held
        elif target.startswith('MOVE_'):
            movement = target.removeprefix('MOVE_') if held else 'NEUTRAL'
        elif now - since > .8:
            gesture = {'JAB': 'LEFT_PUNCH', 'STRAIGHT': 'RIGHT_PUNCH',
                       'HIGH_KICK': 'LEFT_RAISE', 'LOW_KICK': 'RIGHT_RAISE',
                       'SPECIAL': 'SPECIAL'}.get(target)
            since = now
    return block, movement, gesture, previous, since


def compact_frame(frame, observed_ns):
    return {'seq': str(frame['seq']), 'time': frame['time'], 'arrival': frame['arrival'],
            'observed_ns': observed_ns,
            'world': {key: frame['world'].get(key) for key in WORLD_FIELDS},
            'poses': [actor.get('bones', []) for actor in frame['actors']],
            'actors': [{key: actor.get(key) for key in ACTOR_FIELDS}
                       for actor in frame['actors']]}


def pose_difference_degrees(left, right):
    angles = []
    for a, b in zip(left, right):
        av, bv = [a.get(k, 0) for k in 'xyzw'], [b.get(k, 0) for k in 'xyzw']
        norm = math.sqrt(sum(v*v for v in av) * sum(v*v for v in bv))
        if norm:
            angles.append(math.degrees(2 * math.acos(min(1., abs(sum(x*y for x,y in zip(av,bv))) / norm))))
    return max(angles, default=0.)


class BehaviorChecks:
    """Bounded post-measurement checks using only real source commands/ACKs."""
    def __init__(self, now):
        self.player, self.stage, self.since = 0, 'settle', now
        self.cases, self.current = [], {'name': 'held_guard_release_punch', 'player': 1}
        self.done = False

    @staticmethod
    def fresh(feedback, now):
        return feedback if 0 <= int(now*1_000_000_000) - feedback.get('sender_ns', 0) <= 150*MS else {}

    def offer(self, label, event_id):
        if label:
            self.current[label + '_event_id'] = event_id

    def ack(self, player, ack, received_ns=0):
        if self.done or player != self.player:
            return
        if self.stage == 'hold' and ack.get('sender_ns', 0) >= self.current.get('held_since_ns', 0) and ack.get('game_guard_active'):
            self.current['held_guard_acknowledged'] = True
        for label in ('release', 'special', 'post_special'):
            if ack.get('event_id') and ack['event_id'] == self.current.get(label + '_event_id'):
                if ack.get('status') == 'APPLIED':
                    self.current[label + '_applied'] = True
                    self.current[label + '_ack_received_ns'] = received_ns
                    if label == 'release':
                        self.current['release_guard_inactive_ack'] = ack.get('game_guard_active') is False
                self.current[label + '_last_status'] = ack.get('status')

    def finish(self, status, reason, now):
        self.current.update(status=status, reason=reason)
        self.current.pop('_held_pose', None)
        self.cases.append(self.current)
        if self.current['name'] == 'held_guard_release_punch' and self.player == 0:
            self.player, self.stage = 1, 'settle'
            self.current = {'name': 'held_guard_release_punch', 'player': 2}
        elif self.current['name'] == 'held_guard_release_punch':
            self.player, self.stage = 0, 'special_ready'
            self.current = {'name': 'special_then_normal_punch', 'player': 1}
        elif self.player == 0:
            self.player, self.stage = 1, 'special_ready'
            self.current = {'name': 'special_then_normal_punch', 'player': 2}
        else:
            self.cases.append({'name': 'native_rematch', 'status': 'unavailable',
                'reason': 'Normal rematch requires resolved-round host UI/R. No existing command transport API; no runtime or keyboard injection added.'})
            self.done = True
        self.since = now

    def controls(self, now, feedback, guest_frame):
        outputs = [(False, 'NEUTRAL', None, None) for _ in range(2)]
        if self.done:
            return outputs
        current = self.fresh(feedback[self.player], now)
        actor = guest_frame['actors'][self.player] if guest_frame else {}
        age = now - self.since
        if self.stage == 'settle':
            if age >= .8 and current.get('game_can_act'):
                self.stage, self.since = 'hold', now
                self.current['held_since_ns'] = int(now*1_000_000_000)
                outputs[self.player] = (True, 'NEUTRAL', None, None)
            elif age > 4:
                self.finish('failed', 'No fresh actionable state before guard trial', now)
        elif self.stage == 'hold':
            outputs[self.player] = (True, 'NEUTRAL', None, None)
            if age >= 1 and self.current.get('held_guard_acknowledged') and guest_frame:
                self.current['held_guest_attack_serial'] = actor.get('attackSerial', 0)
                self.current['_held_pose'] = guest_frame['poses'][self.player]
                self.stage, self.since = 'release', now
                # Same publication: block=false STATE immediately precedes one-shot.
                outputs[self.player] = (False, 'NEUTRAL', 'LEFT_PUNCH', 'release')
            elif age > 3:
                self.finish('failed', 'Held guard was not acknowledged with a guest snapshot', now)
        elif self.stage == 'release':
            changed = guest_frame and actor.get('attackSerial', 0) > self.current.get('held_guest_attack_serial', 0)
            if changed:
                angle = pose_difference_degrees(self.current.get('_held_pose', []), guest_frame['poses'][self.player])
                self.current['guest_pose_change_degrees'] = max(angle, self.current.get('guest_pose_change_degrees', 0))
                self.current['guest_attack_serial_advanced'] = True
            if (self.current.get('release_applied') and self.current.get('release_guard_inactive_ack')
                    and self.current.get('guest_attack_serial_advanced') and self.current.get('guest_pose_change_degrees', 0) > 1):
                self.finish('passed', 'Real held/released guard ACKs, APPLIED punch and changed guest complete pose/attack serial', now)
            elif age > 4:
                self.finish('failed', 'Release/punch or authoritative guest pose evidence did not arrive', now)
        elif self.stage == 'special_ready':
            if current and not current.get('special_ready'):
                self.finish('unavailable', 'Not legitimately charged. PvP disables the existing I-key refill; no charge/health mutation was used.', now)
            elif current.get('special_ready') and current.get('game_can_act'):
                self.stage, self.since = 'special_active', now
                outputs[self.player] = (False, 'NEUTRAL', 'SPECIAL', 'special')
            elif age > 4:
                self.finish('unavailable', 'No fresh legitimately charged/actionable feedback', now)
        elif self.stage == 'special_active':
            if (self.current.get('special_applied') and current.get('sender_ns', 0) > self.current.get('special_ack_received_ns', 0)
                    and current.get('game_can_act') and not current.get('game_special_active')):
                self.current['pre_post_guest_attack_serial'] = actor.get('attackSerial', 0)
                self.stage, self.since = 'post_special', now
                outputs[self.player] = (False, 'NEUTRAL', 'LEFT_PUNCH', 'post_special')
            elif guest_frame and guest_frame['world'].get('resolved'):
                self.finish('unavailable', 'Round resolved before a legal post-special punch; special acknowledgement retained', now)
            elif age > 12:
                self.finish('failed', 'Requested special did not return to a fresh actionable state', now)
        elif self.stage == 'post_special':
            if self.current.get('post_special_applied') and actor.get('attackSerial', 0) > self.current.get('pre_post_guest_attack_serial', 0):
                self.finish('passed', 'Legitimately charged SPECIAL and following normal punch both APPLIED; guest attack serial advanced', now)
            elif age > 4:
                self.finish('failed', 'Post-special normal punch did not receive matching application/view evidence', now)
        return outputs


class TraceTail:
    def __init__(self, path):
        self.path, self.offset, self.partial = path, 0, b''
        self.frames = {}
        self.order = []
        self.errors = []

    def poll(self):
        if not self.path.exists():
            return
        with self.path.open('rb') as stream:
            stream.seek(self.offset)
            data = stream.read()
            self.offset = stream.tell()
        lines = (self.partial + data).split(b'\n')
        self.partial = lines.pop()
        observed_ns = time.monotonic_ns()
        for line in lines:
            try:
                frame = compact_frame(json.loads(line), observed_ns)
            except (ValueError, KeyError, TypeError) as exc:
                self.errors.append(type(exc).__name__)
                continue
            self.frames[frame['seq']] = frame
            self.order.append(int(frame['seq']))


def frame_comparison(host, guest):
    matched, mismatches, missing, pose_error = 0, [], 0, 0.
    for sequence, frame in guest.frames.items():
        original = host.frames.get(sequence)
        if original is None:
            missing += 1
            continue
        matched += 1
        if original['world'] != frame['world'] or original['actors'] != frame['actors']:
            mismatches.append(sequence)
        for left, right in zip(original.get('poses', []), frame.get('poses', [])):
            for a, b in zip(left, right):
                pose_error = max(pose_error, max(abs(a.get(k, 0) - b.get(k, 0)) for k in 'xyzw'))
    return {'scope': 'Shared gameplay fields and decoded bone quaternions in complete snapshots; no pixel or physical display comparison',
            'matched_snapshots': matched, 'mismatch_sequences': mismatches,
            'maximum_bone_quaternion_component_error': pose_error,
            'guest_snapshots_without_host_trace': missing,
            'guest_sequence_strictly_increasing': all(b > a for a, b in zip(guest.order, guest.order[1:])),
            'trace_parse_errors': host.errors + guest.errors}


def game_executable(build):
    if build.is_file():
        return build
    if build.suffix == '.app':
        return build / 'Contents/MacOS/Vision Striker'
    return build / ('REDBOOL.exe' if os.name == 'nt' else 'REDBOOL.app/Contents/MacOS/Vision Striker')


def launch_arguments(executable, mode, role, folder, room_code=None):
    command = [str(executable), '--redbool-external-camera', '--redbool-pvp-no-lobby']
    if mode == 'online':
        command += ['--redbool-online-host'] if role == 'host' else ['--redbool-online-join', room_code]
    else:
        command += ['--redbool-pvp', '--redbool-require-view'] if role == 'host' else ['--redbool-pvp-client', '127.0.0.1']
    return command + ['-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
                      '-logFile', str(folder / (role + '.log'))]


def stop_owned(processes):
    for process in processes:
        if process.poll() is None:
            process.terminate()
    for process in processes:
        try:
            process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=3)


def ensure_ports_available(mode):
    # Close probes immediately. Never stop or reuse an unrelated existing game.
    ports = (5065, 5066, 5067) if mode == 'lan' else (5065,)
    for port in ports:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            probe.bind(('0.0.0.0', port))


def run_mode(args, mode, executable, DemoSender, ActionEvent):
    folder = args.output / mode
    folder.mkdir(parents=True, exist_ok=False)
    status_paths = {role: folder / (role + '-status.json') for role in ('host', 'guest')}
    tails = {role: TraceTail(folder / (role + '.jsonl')) for role in ('host', 'guest')}
    processes, senders, statuses = [], [], {'host': {}, 'guest': {}}
    session_evidence = {'host': False, 'guest': False}
    events, acknowledgements = {}, []
    ack_rtts, application_delays = [[], []], [[], []]
    sender_diagnostics, snapshots, traffic_start, traffic_end = [], {}, {}, {}
    clock_anchors = {'host': [], 'guest': []}
    feedback = [{}, {}]
    previous, practice_since, attack_since = ['', ''], [0., 0.], [-10., -10.]
    state_capture = [0, 0]
    combat_source_times = [[], []]
    started, combat_started, combat_ended, error = time.monotonic(), None, None, None
    behavior = None
    launch_ns = time.monotonic_ns()

    def launch(role, room_code=None):
        env = dict(os.environ)
        env.pop('REDBOOL_VIEW_CAPTURE_DIR', None)
        env['REDBOOL_VIEW_TRACE_FILE'] = str(tails[role].path)
        env['REDBOOL_ONLINE_CHECK_STATUS'] = str(status_paths[role])
        process = subprocess.Popen(launch_arguments(executable, mode, role, folder, room_code),
                                   env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        processes.append(process)

    def observe():
        for role in statuses:
            current = read_status(status_paths[role])
            if current:
                statuses[role] = current
                if current.get('clock_ns') and (not clock_anchors[role] or current['clock_ns'] != clock_anchors[role][-1]['clock_ns']):
                    clock_anchors[role].append({key: current[key] for key in ('clock_ns', 'realtime_s', 'utc') if key in current})
                if current.get('supported') and current.get('in_room') and current.get('peer_present'):
                    session_evidence[role] = True
            tails[role].poll()
        if any(process.poll() is not None for process in processes):
            raise RuntimeError('A launched native game exited before the check completed')

    try:
        ensure_ports_available(mode)
        launch('host')
        if mode == 'online':
            while True:
                observe()
                status = statuses['host']
                if status.get('error'):
                    raise RuntimeError('Real Photon host failed: ' + str(status['error']))
                code = status.get('room_code', '')
                if status.get('in_room') and re.fullmatch(r'KR-[A-Z0-9]{6}', code):
                    break
                if time.monotonic() - started > args.connect_timeout:
                    raise TimeoutError('No authenticated Photon room appeared; no offline fallback was used')
                time.sleep(.05)
            launch('guest', code)
            while not (statuses['guest'].get('local_input_port', 0) > 0
                       and all(session_evidence.values())):
                observe()
                if statuses['guest'].get('error'):
                    raise RuntimeError('Real Photon guest failed: ' + str(statuses['guest']['error']))
                if time.monotonic() - started > args.connect_timeout:
                    raise TimeoutError('Both native games did not join the same real Photon room')
                time.sleep(.05)
            p2_port = int(statuses['guest']['local_input_port'])
        else:
            launch('guest')
            p2_port = 5066
        senders = [DemoSender('127.0.0.1', port, uuid.uuid4().hex, 'P' + str(i + 1),
                             network_profile='online' if mode == 'online' and i == 1 else 'lan')
                   for i, port in enumerate((5065, p2_port))]
        next_state, next_observe = time.monotonic(), 0.
        while time.monotonic() - started < args.timeout:
            now = time.monotonic()
            if now >= next_observe:
                observe()
                next_observe = now + .02
            for i, sender in enumerate(senders):
                for row in sender.service():
                    if row['type'] != 'ack':
                        continue
                    ack, received_ns = row['ack'], row['ack_receive_ns']
                    if ack.get('sequence', -1) > feedback[i].get('sequence', -1):
                        feedback[i] = ack
                    if behavior is not None:
                        behavior.ack(i, ack, received_ns)
                    if combat_started is not None and combat_ended is None and ack.get('sender_ns'):
                        ack_rtts[i].append((received_ns - ack['sender_ns']) / MS)
                    eid = ack.get('event_id')
                    if eid not in events:
                        continue
                    record = {'event_id': eid, 'player': i + 1, 'status': ack.get('status'),
                              'received_ns': received_ns, 'receiver_ns': ack.get('receiver_ns'),
                              'applied_ns': ack.get('applied_ns'), 'game_attack_started': ack.get('game_attack_started', False),
                              'applied_action': ack.get('applied_action')}
                    acknowledgements.append(record)
                    event = events[eid]
                    event.setdefault('first_ack', record)
                    if ack.get('status') == 'APPLIED' and 'applied_ack' not in event:
                        event['applied_ack'] = record
                        # Both synthetic senders live in this process. Local P1 SYNC
                        # anchors the host clock with less uncertainty than relay P2.
                        host_clock = senders[0].clock
                        if host_clock and ack.get('applied_ns'):
                            uncertainty = host_clock['clock_uncertainty_ns'] / MS
                            estimate = (ack['applied_ns'] - host_clock['receiver_offset_ns'] - event['commit_ns']) / MS
                            event['estimated_commit_to_host_apply_ms'] = estimate
                            event['clock_uncertainty_ms'] = uncertainty
                            event['measurement_clock_basis'] = 'P1 local SYNC; diagnostic conversion only, no packet timestamp change'
                            if event['phase'] == 'combat':
                                application_delays[i].append(estimate)
            if combat_started is None and all(
                    sender.tutorial_context() and sender.tutorial_context().get('phase') == 'COMPLETE'
                    and sender.feedback.get('game_can_act') for sender in senders):
                combat_started = now
                traffic_start = {role: dict(status) for role, status in statuses.items()}
                snapshots = {role: {str(i): max((frame['actors'][i]['attackSerial'] or 0
                    for frame in tail.frames.values()), default=0) for i in range(2)}
                    for role, tail in tails.items()}
                attack_since = [now + .8, now]
            if combat_started is not None and combat_ended is None and now - combat_started >= args.duration:
                combat_ended = now
                traffic_end = {role: dict(status) for role, status in statuses.items()}
                behavior = BehaviorChecks(now)
            if behavior is not None and behavior.done:
                break
            if now >= next_state:
                next_state = max(next_state + 1. / args.source_hz, now + .001)
                guest_frame = next(reversed(tails['guest'].frames.values()), None) if tails['guest'].frames else None
                behavior_controls = behavior.controls(now, feedback, guest_frame) if behavior is not None else None
                for i, sender in enumerate(senders):
                    context = sender.tutorial_context() or {}
                    block, movement, gesture, previous[i], practice_since[i] = practice_controls(
                        context, now, previous[i], practice_since[i])
                    phase = 'practice' if combat_started is None else 'behavior' if behavior is not None else 'combat'
                    behavior_label = None
                    if behavior_controls is not None:
                        block, movement, gesture, behavior_label = behavior_controls[i]
                    elif combat_started is not None:
                        block, movement, gesture = False, 'NEUTRAL', None
                        # Normal punches only in measured combat: no cut-in/time-scale action.
                        if sender.feedback.get('game_can_act') and now - attack_since[i] > 1.6:
                            gesture = 'LEFT_PUNCH' if int((now - combat_started) / 1.6) % 2 == 0 else 'RIGHT_PUNCH'
                            attack_since[i] = now
                    if not sender.clock:
                        continue
                    state_capture[i] = time.monotonic_ns()
                    if combat_started is not None and combat_ended is None:
                        combat_source_times[i].append(state_capture[i])
                    sender.send('STATE', ttl_ms=150, capture_ns=state_capture[i],
                                calibration_version=1, calibration_generation=1, calibrated=True,
                                tracking='VALID', fsm_state='READY', block_active=block,
                                movement=movement, movement_strength=.75 if movement != 'NEUTRAL' else 0.)
                    if gesture:
                        commit_ns = time.monotonic_ns()
                        eid = uuid.uuid4().hex
                        side = 'BOTH' if gesture == 'SPECIAL' else 'LEFT' if gesture.startswith('LEFT') else 'RIGHT'
                        event = ActionEvent(eid, sender.actor_id, sender.stream_id, 1, state_capture[i],
                                            commit_ns, commit_ns + 150 * MS, 'ATTACK', side,
                                            {'gesture': gesture}, 'native-network-synthetic-check')
                        events[eid] = dict(player=i + 1, phase=phase, gesture=gesture,
                                           commit_ns=commit_ns, capture_ns=state_capture[i], expires_ns=event.expires_ns,
                                           local_host_clock=dict(senders[0].clock) if senders[0].clock else None)
                        if behavior is not None and behavior_label:
                            behavior.offer(behavior_label, eid)
                        sender.offer(event, calibration_generation=1)
                        # Next2ms service iteration sends this after the STATE.
                        # All service rows are consumed above; no ACK is discarded.
            time.sleep(.002)
        if combat_started is None:
            raise TimeoutError('Fresh real receiver feedback never reached combat for both players')
        if time.monotonic() - combat_started < args.duration:
            raise TimeoutError('Native run timed out before the requested measured combat interval')
        if behavior is None or not behavior.done:
            raise TimeoutError('Native run ended before the bounded post-measurement behavior checks completed')
        observe()
    except Exception as exc:
        error = type(exc).__name__ + ': ' + str(exc)
    finally:
        sender_diagnostics = [sender.transport_diagnostics() for sender in senders]
        for sender in senders:
            sender.close()
        stop_owned(processes)
        for tail in tails.values():
            tail.poll()

    logs = {role: (folder / (role + '.log')).read_text(errors='replace')
            if (folder / (role + '.log')).exists() else '' for role in statuses}
    metrics = {role: parse_metrics(text) for role, text in logs.items()}
    comparison = frame_comparison(tails['host'], tails['guest'])
    combat_events = {eid: event for eid, event in events.items() if event['phase'] == 'combat'}
    failures = [error] if error else []
    if behavior is not None:
        failures += [f"Behavior P{case.get('player', '')} {case['name']}: {case['reason']}"
                     for case in behavior.cases if case['status'] == 'failed']
    if any('[REDBOOL CAMERA] child_started' in text for text in logs.values()):
        failures.append('Unexpected camera backend launch was logged')
    if mode == 'online' and not all(session_evidence.values()):
        failures.append('Authenticated Photon peer evidence is missing')
    if not all(metrics[role]['REDBOOL PERF'] for role in metrics):
        failures.append('Native frame-performance measurements are missing')
    if mode == 'online' and not all(any(row.get('route') == 'photon' for row in metrics[role]['REDBOOL VIEW METRIC']) for role in metrics):
        failures.append('Both native display paths were not observed using Photon')
    if not comparison['matched_snapshots'] or comparison['mismatch_sequences'] or comparison['trace_parse_errors']:
        failures.append('Complete snapshot agreement was not demonstrated')
    if comparison['maximum_bone_quaternion_component_error'] >= .0001:
        failures.append('Decoded guest bone snapshot exceeded the existing0.0001 component tolerance')
    if not comparison['guest_sequence_strictly_increasing']:
        failures.append('Guest complete snapshot sequence rewound')
    if not all(any(e['player'] == i + 1 and 'applied_ack' in e for e in combat_events.values()) for i in range(2)):
        failures.append('Both players did not receive a real APPLIED combat acknowledgement')
    first = {}
    for i in range(2):
        successful = [(eid, event) for eid, event in combat_events.items() if event['player'] == i + 1 and 'applied_ack' in event]
        if not successful:
            continue
        eid, event = successful[0]
        row = dict(event_id=eid, commit_to_ack_ms=(event['applied_ack']['received_ns'] - event['commit_ns']) / MS,
                   estimated_commit_to_host_apply_ms=event.get('estimated_commit_to_host_apply_ms'),
                   clock_uncertainty_ms=event.get('clock_uncertainty_ms'))
        for role, tail in tails.items():
            baseline = snapshots.get(role, {}).get(str(i), 0)
            frame = next((f for f in tail.frames.values() if f['actors'][i]['attackSerial'] > baseline), None)
            if frame:
                realtime = frame['arrival'] if role == 'guest' else frame['time']
                native_time = native_clock_time(realtime, min(clock_anchors[role], key=lambda a: abs(a['realtime_s'] - realtime))
                    if clock_anchors[role] else {}, event.get('local_host_clock'))
                row[role + '_first_authoritative_snapshot'] = dict(sequence=frame['seq'],
                    unity_realtime_s=realtime,
                    estimated_commit_to_snapshot_ms=(native_time - event['commit_ns']) / MS if native_time is not None else None,
                    measurement_clock_uncertainty_ms=event['local_host_clock']['clock_uncertainty_ns'] / MS if event.get('local_host_clock') else None,
                    commit_to_file_observation_upper_bound_ms=(frame['observed_ns'] - event['commit_ns']) / MS)
        first['P' + str(i + 1)] = row
    safe_statuses = {role: {key: value for key, value in status.items() if key not in ('room_code', 'app_id', 'appId')}
                     for role, status in statuses.items()}
    report = {'mode': mode, 'passed': not failures, 'failures': failures,
              'scope': 'Two actual native games on one machine; synthetic source commands; real LAN loopback or Photon relay. Not camera recognition, physical Internet separation, or screen scanout latency.',
              'camera_backend_launch_seen': any('[REDBOOL CAMERA] child_started' in text for text in logs.values()),
              'actual_cloud_peer_evidence': session_evidence if mode == 'online' else None,
              'configuration': dict(source_hz=args.source_hz, requested_combat_seconds=args.duration,
                                    resolution='1280x720', physical_camera=False, inference=False,
                                    view_trace_enabled=True, special_moves_in_measured_combat=False),
              'launched_process_ids': [p.pid for p in processes], 'launch_monotonic_ns': launch_ns,
              'seconds_to_combat': combat_started - started if combat_started else None,
              'actual_source_publication': {f'P{i+1}': {'samples': len(times),
                  'hz': (len(times) - 1) * 1_000_000_000 / (times[-1] - times[0]) if len(times) > 1 else None,
                  'interval_ms': distribution([(b-a)/MS for a,b in zip(times,times[1:])])}
                  for i,times in enumerate(combat_source_times)},
              'sender_ack_roundtrip_ms': {f'P{i+1}': distribution(v) for i, v in enumerate(ack_rtts)},
              'estimated_commit_to_host_apply_ms': {f'P{i+1}': distribution(v) for i, v in enumerate(application_delays)},
              'first_successful_attacks': first,
              'combat_outcomes': dict(Counter(f"P{e['player']}:{e.get('first_ack', {}).get('status', 'NO_ACK')}" for e in combat_events.values())),
              'snapshot_agreement': comparison, 'transport': sender_diagnostics,
              'post_measurement_behavior': {'counts': dict(Counter(case['status'] for case in behavior.cases)),
                  'checks': behavior.cases, 'complete': behavior.done} if behavior else {'complete': False},
              'photon_traffic_during_combat': {role: counter_window(traffic_start.get(role, {}), status)
                  for role, status in traffic_end.items()} if mode == 'online' and traffic_end else None,
              'combat_frame_performance': {role: combat_performance(metrics[role]['REDBOOL PERF'],
                  traffic_start.get(role, {}), status) for role, status in traffic_end.items()},
              'clock_anchors': clock_anchors,
              'native_metrics': metrics, 'final_diagnostic_status': safe_statuses,
              'limitations': ['Two processes share CPU/GPU and network uplink; this is not two distant players.',
                              'Full view trace adds disk/serialization work equally to both modes.',
                              'ACK RTT includes local scheduling and both directions; Photon server RTT is separate.',
                              'Host application delay is a clock estimate; uncertainty must accompany it.',
                              'Same-machine snapshot clock conversion uses P1 local SYNC and Unity Stopwatch anchors; it is not a cross-machine measurement.',
                              'File-observation upper bounds include diagnostic buffering and20ms polling.',
                              'Local anticipation metrics end at a bone write, not physical display scanout.']}
    (folder / 'events.json').write_text(json.dumps({'events': events, 'acks': acknowledgements}, indent=2) + '\n')
    (folder / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, required=True, help='Native executable, .app, or release directory')
    parser.add_argument('--python-root', type=Path, required=True, help='Matching isolated Python source directory')
    parser.add_argument('--output', type=Path, required=True, help='New output directory; existing runs are never overwritten')
    parser.add_argument('--modes', default='lan,online', choices=('lan', 'online', 'lan,online'))
    parser.add_argument('--duration', type=float, default=20., help='Measured combat seconds per mode')
    parser.add_argument('--timeout', type=float, default=180., help='Total startup/practice/combat seconds per mode')
    parser.add_argument('--connect-timeout', type=float, default=60.)
    parser.add_argument('--source-hz', type=float, default=60.)
    parser.add_argument('--run', action='store_true', help='Explicitly launch real native processes and use network')
    args = parser.parse_args(argv)
    args.build, args.python_root, args.output = args.build.resolve(), args.python_root.resolve(), args.output.resolve()
    if not 1 <= args.source_hz <= 100 or not 10 <= args.duration <= 120 or args.timeout <= args.duration:
        parser.error('Use source_hz1..100, duration10..120, and timeout greater than duration')
    executable = game_executable(args.build)
    if not args.run:
        print(json.dumps({'launches_processes': False, 'uses_network': False,
                          'executable': str(executable), 'output': str(args.output),
                          'modes': args.modes.split(','), 'next_step': 'Add --run only when real native checks are authorized'}, indent=2))
        return 0
    if not executable.is_file() or not (args.python_root / 'src/redbool_demo/transport.py').is_file():
        parser.error('Real native executable and matching Python transport source are required')
    if args.output.exists():
        parser.error('Output directory already exists; choose a new run directory')
    args.output.mkdir(parents=True)
    sys.path.insert(0, str(args.python_root / 'src'))
    from redbool_demo.transport import DemoSender
    from action_engine.types import ActionEvent
    results = []
    for mode in args.modes.split(','):
        results.append(run_mode(args, mode, executable, DemoSender, ActionEvent))
        if not results[-1]['passed']:
            break
    summary = dict(passed=len(results) == len(args.modes.split(',')) and all(r['passed'] for r in results),
                   build_sha256=hashlib.sha256(executable.read_bytes()).hexdigest(),
                   platform=sys.platform, results=results)
    (args.output / 'comparison.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'passed': summary['passed'], 'report': str(args.output / 'comparison.json'),
                      'modes_completed': [r['mode'] for r in results]}, indent=2))
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
