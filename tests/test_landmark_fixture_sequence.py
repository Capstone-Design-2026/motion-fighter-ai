"""Deterministic fixture ordering; no camera, model, socket or waiting."""
import unittest
from test_landmark_live import CalibratedCrossScript


class CalibrationDrivenFixtureTests(unittest.TestCase):
    def setUp(self):
        self.now = 0.
        self.script = CalibratedCrossScript(clock=lambda: self.now)

    def observe(self, calibrated, ready, frame=1):
        self.script.observe(calibrated=calibrated, helper_ready=ready, frame_id=frame)

    def test_camera_count_or_elapsed_time_cannot_start_uncalibrated_cross(self):
        self.assertEqual(self.script.command(1), 'calibrate')
        for frame in (60,90,110,1000):
            self.now += 5.
            self.observe(False,True,frame)
            self.assertEqual(self.script.pose_code(),0)
            self.assertIsNone(self.script.command(frame))
        self.assertEqual(self.script.phase,'waiting')

    def test_calibration_and_real_worker_readiness_are_both_required(self):
        self.script.command(1)
        self.observe(True,False)
        self.assertEqual(self.script.pose_code(),0)
        self.observe(False,True)
        self.assertEqual(self.script.pose_code(),0)
        self.observe(True,True)
        self.assertEqual(self.script.pose_code(),1)

    def test_hold_starts_at_first_captured_cross_not_ready_notification(self):
        self.script.command(1);self.observe(True,True)
        self.now=10.
        self.assertEqual(self.script.pose_code(),1)
        self.now=11.19
        self.assertEqual(self.script.pose_code(),1)
        self.now=11.21
        self.assertEqual(self.script.pose_code(),0)
        self.assertIsNone(self.script.command(100))
        self.now=11.47
        self.assertEqual(self.script.pose_code(),0)
        self.assertEqual(self.script.command(110),'pause')

    def test_return_pause_second_calibration_and_stop_are_ordered(self):
        self.script.command(1);self.observe(True,True);self.script.pose_code()
        self.now=1.21;self.script.pose_code()
        self.now=1.47;self.script.pose_code()
        self.assertEqual(self.script.command(80),'pause')
        self.assertIsNone(self.script.command(84))
        self.assertEqual(self.script.command(85),'calibrate')
        self.observe(False,True,100)
        self.assertIsNone(self.script.command(1000))
        self.assertEqual(self.script.pose_code(),0)
        self.observe(True,True,101)
        self.assertIsNone(self.script.command(120))
        self.assertEqual(self.script.command(121),'stop')
        self.assertIsNone(self.script.command(200))
        self.assertEqual(self.script.commands,['calibrate','pause','calibrate','stop'])


if __name__=='__main__':unittest.main()
