"""No game process, socket, SDK or camera is created by these harness checks."""
import contextlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('native_online_compare', Path(__file__).with_name('native_online_compare.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class HarnessChecks(unittest.TestCase):
    def test_prepare_mode_never_launches_or_opens_network(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(check.subprocess, 'Popen') as launch, \
                patch.object(check.socket, 'socket') as socket, contextlib.redirect_stdout(io.StringIO()):
            output = Path(folder) / 'not-created'
            result = check.main(['--build', '/missing/game.app', '--python-root', '/missing/python', '--output', str(output)])
            self.assertEqual(result, 0)
            self.assertFalse(output.exists())
            launch.assert_not_called()
            socket.assert_not_called()

    def test_both_routes_explicitly_disable_owned_camera_backend(self):
        for mode in ('lan', 'online'):
            for role in ('host', 'guest'):
                args = check.launch_arguments(Path('/real/game'), mode, role, Path('/tmp/report'), 'KR-ABC234')
                self.assertIn('--redbool-external-camera', args)
                self.assertIn('--redbool-pvp-no-lobby', args)
                self.assertNotIn('-batchmode', args)
                self.assertNotIn('-nographics', args)
                if mode == 'online':
                    self.assertNotIn('--redbool-pvp-client', args)
                else:
                    self.assertNotIn('--redbool-online-host', args)

    def test_fps_and_server_rtt_remain_distinct_metrics(self):
        rows = check.parse_metrics('[REDBOOL PERF] frames=600 seconds=10.00 fps=60.00 focused_frames=450 realtime_s=30.00\n'
                                   '[REDBOOL VIEW METRIC] rtt_ms=52.00 route=photon photon_rtt_ms=19 fresh=True')
        self.assertEqual(rows['REDBOOL PERF'][0]['fps'], 60.)
        self.assertEqual(rows['REDBOOL VIEW METRIC'][0]['rtt_ms'], 52.)
        self.assertEqual(rows['REDBOOL VIEW METRIC'][0]['photon_rtt_ms'], 19)
        summary = check.combat_performance(rows['REDBOOL PERF'], {'realtime_s': 19}, {'realtime_s': 31})
        self.assertEqual(summary['weighted_fps'], 60.)
        self.assertEqual(summary['focused_frame_fraction'], .75)
        self.assertIsNone(check.combat_performance(rows['REDBOOL PERF'], {'realtime_s': 21}, {'realtime_s': 31})['weighted_fps'])

    def test_practice_requires_real_rest_and_event_transitions(self):
        result = check.practice_controls({'action': 'BLOCK', 'phase': 'PRACTICE'}, 1, 'BLOCK', 0)
        self.assertTrue(result[0])
        result = check.practice_controls({'action': 'BLOCK', 'phase': 'RETURN'}, 1, 'BLOCK', 0)
        self.assertFalse(result[0])
        result = check.practice_controls({'action': 'HIGH_KICK', 'phase': 'PRACTICE'}, 1, 'HIGH_KICK', 0)
        self.assertEqual(result[2], 'LEFT_RAISE')

    def test_native_snapshot_clock_conversion_does_not_mutate_source(self):
        anchor = {'clock_ns': 10_000_000_000, 'realtime_s': 5.}
        clock = {'receiver_offset_ns': 1_000_000_000}
        self.assertEqual(check.native_clock_time(5.02, anchor, clock), 9_020_000_000)
        self.assertEqual(clock['receiver_offset_ns'], 1_000_000_000)
        self.assertIsNone(check.native_clock_time(5., {}, clock))

    def test_sdk_counter_window_reports_actual_bytes_without_billing_claim(self):
        result = check.counter_window({'realtime_s': 10, 'photon_bytes_in': 50, 'photon_bytes_out': 100},
                                      {'realtime_s': 12, 'photon_bytes_in': 250, 'photon_bytes_out': 500})
        self.assertEqual(result['in_bytes'], 200)
        self.assertEqual(result['out_bytes_per_second'], 200)
        self.assertIn('not a billing', result['scope'])

    def test_empty_statistics_cannot_fabricate_zero_latency(self):
        self.assertEqual(check.distribution([]), {'samples': 0})
        self.assertEqual(check.distribution([10, 20, 30])['p50'], 20)

    def test_snapshot_mismatch_and_rewind_are_failures(self):
        host, guest = check.TraceTail(Path('/unused/host')), check.TraceTail(Path('/unused/guest'))
        host.frames = {'1': {'world': {'round': 0}, 'actors': [{'hp': 100}]}}
        guest.frames = {'1': {'world': {'round': 0}, 'actors': [{'hp': 99}]}}
        guest.order = [2, 1]
        result = check.frame_comparison(host, guest)
        self.assertEqual(result['mismatch_sequences'], ['1'])
        self.assertFalse(result['guest_sequence_strictly_increasing'])

    def test_guard_release_and_punch_share_publication_and_require_real_evidence(self):
        trial = check.BehaviorChecks(10.)
        feedback = [{'sender_ns': 10_900_000_000, 'game_can_act': True}, {}]
        held = {'actors': [{'attackSerial': 0}, {}], 'poses': [[dict(x=0,y=0,z=0,w=1)], []], 'world': {}}
        self.assertTrue(trial.controls(10.9, feedback, held)[0][0])
        trial.ack(0, {'sender_ns': 11_000_000_000, 'game_guard_active': True})
        outputs = trial.controls(12., feedback, held)
        self.assertEqual(outputs[0], (False, 'NEUTRAL', 'LEFT_PUNCH', 'release'))
        trial.offer('release', 'guard-event')
        trial.ack(0, {'event_id': 'guard-event', 'status': 'APPLIED', 'game_guard_active': False})
        changed = {'actors': [{'attackSerial': 1}, {}], 'poses': [[dict(x=0,y=.70710678,z=0,w=.70710678)], []], 'world': {}}
        trial.controls(12.1, feedback, changed)
        self.assertEqual(trial.cases[0]['status'], 'passed')
        self.assertGreater(trial.cases[0]['guest_pose_change_degrees'], 89)

    def test_no_guard_ack_does_not_fabricate_a_guard_pass(self):
        trial = check.BehaviorChecks(10.)
        trial.controls(10.9, [{'sender_ns': 10_900_000_000, 'game_can_act': True}, {}], None)
        trial.controls(14., [{}, {}], None)
        self.assertEqual(trial.cases[0]['status'], 'failed')

    def test_uncharged_special_is_unavailable_not_passed_or_refilled(self):
        trial = check.BehaviorChecks(10.)
        trial.stage = 'special_ready'
        trial.current = {'name': 'special_then_normal_punch', 'player': 1}
        outputs = trial.controls(10.1, [{'sender_ns': 10_100_000_000, 'special_ready': False}, {}], None)
        self.assertTrue(all(row[2] is None for row in outputs))
        self.assertEqual(trial.cases[0]['status'], 'unavailable')

    def test_post_special_waits_for_feedback_generated_after_special_ack(self):
        trial = check.BehaviorChecks(20.)
        trial.stage = 'special_active'
        trial.current = {'name': 'special_then_normal_punch', 'player': 1,
                         'special_applied': True, 'special_ack_received_ns': 20_000_000_000}
        old = [{'sender_ns': 19_990_000_000, 'game_can_act': True, 'game_special_active': False}, {}]
        self.assertIsNone(trial.controls(20.05, old, None)[0][2])
        fresh = [{'sender_ns': 20_090_000_000, 'game_can_act': True, 'game_special_active': False}, {}]
        self.assertEqual(trial.controls(20.1, fresh, None)[0][2], 'LEFT_PUNCH')


if __name__ == '__main__':
    unittest.main()
