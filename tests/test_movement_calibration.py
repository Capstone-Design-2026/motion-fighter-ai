"""Calibration math and detector lifecycle tests without camera dependencies."""

import unittest
from unittest.mock import patch

from test_movement import make_landmarks
from movement import (
    CALIBRATING, READY, MOVEMENT_NEUTRAL, MOVEMENT_LEFT, MOVEMENT_RIGHT,
    NeutralBaselineCalibrator, ShoulderMovementDetector,
)


def create_uncalibrated_detector(stable_frame_count=1, mirror=False):
    return ShoulderMovementDetector(
        visibility_threshold=0.5, enter_threshold=0.30, exit_threshold=0.15,
        stable_frame_count=stable_frame_count, mirror=mirror,
        max_strength_delta=0.60, calibration_seconds=3.0, calibration_min_samples=3,
    )


def pose(score, **kwargs):
    # make_landmarks has shoulder width approximately 0.2.
    return make_landmarks(left_z=score * 0.2, **kwargs)


def calibrate(detector, offset=0.0):
    for now in (0.0, 1.0, 3.0):
        result = detector.update(pose(offset), now=now)
    return result


class NeutralBaselineCalibratorTest(unittest.TestCase):
    def test_known_offset_median(self):
        for offset in (-0.596, 0.185, 0.130, 0.079):
            with self.subTest(offset=offset):
                calibrator = NeutralBaselineCalibrator(3.0, 3)
                self.assertEqual(calibrator.state, CALIBRATING)
                self.assertIsNone(calibrator.baseline)
                for value, now in zip((offset - 0.01, offset + 0.01, offset), (0, 1, 3)):
                    calibrator.update(value, now)
                self.assertEqual(calibrator.state, READY)
                self.assertAlmostEqual(calibrator.baseline, offset)

    def test_outlier_robustness(self):
        calibrator = NeutralBaselineCalibrator(4.0, 5)
        for now, value in enumerate((-100, 0.18, 0.185, 0.19, 1000)):
            calibrator.update(value, now)
        self.assertEqual(calibrator.baseline, 0.185)

    def test_median_absolute_deviation(self):
        calibrator = NeutralBaselineCalibrator(4.0, 5)
        for now, value in enumerate((1, 2, 3, 4, 100)):
            calibrator.update(value, now)
        self.assertEqual(calibrator.baseline, 3)
        self.assertEqual(calibrator.mad, 1)

    def test_even_sample_median_and_mad(self):
        calibrator = NeutralBaselineCalibrator(3.0, 4)
        for now, value in enumerate((0, 2, 4, 6)):
            calibrator.update(value, now)
        self.assertEqual(calibrator.baseline, 3)
        self.assertEqual(calibrator.mad, 2)

    def test_large_mad_does_not_reject_calibration(self):
        calibrator = NeutralBaselineCalibrator(2.0, 3)
        for now, value in enumerate((-1000, 0, 1000)):
            calibrator.update(value, now)
        self.assertEqual(calibrator.mad, 1000)
        self.assertEqual(calibrator.state, READY)

    def test_insufficient_samples(self):
        calibrator = NeutralBaselineCalibrator(3.0, 3)
        calibrator.update(0.2, 0)
        calibrator.update(0.2, 100)
        self.assertEqual(calibrator.state, CALIBRATING)
        self.assertIsNone(calibrator.baseline)
        self.assertIsNone(calibrator.mad)
        calibrator.update(0.2, 101)
        self.assertEqual(calibrator.state, READY)

    def test_insufficient_time(self):
        calibrator = NeutralBaselineCalibrator(3.0, 3)
        for now in (0, 1, 2.99):
            calibrator.update(0.2, now)
        self.assertEqual(calibrator.state, CALIBRATING)
        calibrator.update(0.2, 3)
        self.assertEqual(calibrator.state, READY)

    def test_invalid_scores_excluded_and_do_not_start_timer(self):
        calibrator = NeutralBaselineCalibrator(3.0, 3)
        for value in (float('nan'), float('inf'), -float('inf'), None, 'invalid'):
            calibrator.update(value, 0)
        self.assertEqual(calibrator.sample_count, 0)
        self.assertIsNone(calibrator.started_at)
        calibrator.update(0.2, 100)
        self.assertEqual(calibrator.started_at, 100)

    def test_ready_baseline_mad_and_count_are_frozen(self):
        calibrator = NeutralBaselineCalibrator(2.0, 3)
        for now, value in enumerate((0.1, 0.2, 0.3)):
            calibrator.update(value, now)
        snapshot = (calibrator.baseline, calibrator.mad, calibrator.sample_count)
        calibrator.update(100, 10)
        self.assertEqual(snapshot, (calibrator.baseline, calibrator.mad, calibrator.sample_count))

    def test_invalid_configuration(self):
        for duration, count in ((-1, 3), (float('nan'), 3), (float('inf'), 3), (3, 0), (3, 1.5)):
            with self.subTest(duration=duration, count=count):
                with self.assertRaises(ValueError):
                    NeutralBaselineCalibrator(duration, count)


class MovementCalibrationTest(unittest.TestCase):
    def assert_neutral(self, result):
        self.assertEqual(result.raw_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.final_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.movement_strength, 0.0)

    def test_calibrating_always_neutral_without_candidates(self):
        detector = create_uncalibrated_detector()
        for now, score in enumerate((-2, 2, -2)):
            result = detector.update(pose(score), now=now)
            self.assert_neutral(result)
            self.assertEqual(result.calibration_state, CALIBRATING)
            self.assertIsNone(result.corrected_rotation_score)
            self.assertTrue(result.score_valid)
            self.assertEqual(detector.stabilizer.candidate_count, 0)
            self.assertEqual(detector.stabilizer.candidate_state, MOVEMENT_NEUTRAL)

    def test_completion_frame_neutral_and_all_states_reset(self):
        detector = create_uncalibrated_detector()
        detector.update(pose(0.2), now=0)
        detector.update(pose(0.2), now=1)
        # Even a stale candidate cannot survive the completion boundary.
        detector.raw_movement = MOVEMENT_RIGHT
        detector.stabilizer.stable_state = MOVEMENT_RIGHT
        detector.stabilizer.candidate_state = MOVEMENT_LEFT
        detector.stabilizer.candidate_count = 9
        result = detector.update(pose(1.0), now=3)
        self.assertEqual(result.calibration_state, READY)
        self.assert_neutral(result)
        self.assertGreater(result.corrected_rotation_score, 0.3)
        self.assertEqual(detector.raw_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(detector.stabilizer.stable_state, MOVEMENT_NEUTRAL)
        self.assertEqual(detector.stabilizer.candidate_state, MOVEMENT_NEUTRAL)
        self.assertEqual(detector.stabilizer.candidate_count, 0)

    def test_next_valid_frame_starts_classification(self):
        detector = create_uncalibrated_detector()
        self.assert_neutral(calibrate(detector, -0.596))
        self.assert_neutral(detector.update(None, now=4))
        result = detector.update(pose(-0.196), now=5)
        self.assertEqual(result.final_movement, MOVEMENT_RIGHT)

    def test_correction_raw_compatibility_and_directions(self):
        for delta, direction in ((0, MOVEMENT_NEUTRAL), (0.4, MOVEMENT_RIGHT), (-0.4, MOVEMENT_LEFT)):
            with self.subTest(delta=delta):
                detector = create_uncalibrated_detector()
                calibrate(detector, -0.596)
                result = detector.update(pose(-0.596 + delta), now=4)
                self.assertAlmostEqual(result.rotation_score, -0.596 + delta)
                self.assertEqual(result.raw_rotation_score, result.rotation_score)
                self.assertAlmostEqual(result.corrected_rotation_score, delta)
                self.assertEqual(result.final_movement, direction)

    def test_low_visibility_sample_exclusion(self):
        detector = create_uncalibrated_detector()
        detector.update(pose(0.2), now=0)
        for kwargs in ({'left_visibility': 0.49}, {'right_visibility': 0.49}):
            result = detector.update(pose(100, **kwargs), now=4)
            self.assert_neutral(result)
            self.assertFalse(result.score_valid)
            self.assertEqual(result.calibration_sample_count, 1)
            self.assertEqual(result.calibration_state, CALIBRATING)
        detector.update(pose(0.2, left_visibility=0.5, right_visibility=0.5), now=5)
        result = detector.update(pose(0.2), now=6)
        self.assertEqual(result.calibration_state, READY)
        self.assertAlmostEqual(result.neutral_baseline, 0.2)

    def test_invalid_landmarks_and_score_excluded(self):
        detector = create_uncalibrated_detector()
        for value in (float('nan'), float('inf'), -float('inf'), None):
            landmarks = pose(0.2)
            landmarks[11].z = value
            result = detector.update(landmarks, now=0)
            self.assert_neutral(result)
            self.assertFalse(result.score_valid)
            self.assertEqual(result.calibration_sample_count, 0)
        for value in (float('nan'), float('inf')):
            with patch.object(detector, 'calculate_rotation_score', return_value=value):
                self.assertFalse(detector.update(pose(0.2), now=0).score_valid)
        self.assertEqual(detector.calibrator.sample_count, 0)

    def test_nonfinite_xy_and_visibility_excluded(self):
        for attribute in ('x', 'y', 'visibility'):
            for value in (float('nan'), float('inf')):
                detector = create_uncalibrated_detector()
                landmarks = pose(0.2)
                setattr(landmarks[12], attribute, value)
                self.assertFalse(detector.update(landmarks, now=0).score_valid)
                self.assertEqual(detector.calibrator.sample_count, 0)

    def test_reset_pose_loss_and_low_visibility_preserve_ready_baseline(self):
        for loss in ('reset', 'pose', 'visibility', 'invalid'):
            with self.subTest(loss=loss):
                detector = create_uncalibrated_detector()
                ready = calibrate(detector, -0.596)
                detector.update(pose(-0.196), now=4)
                if loss == 'reset':
                    result = detector.reset()
                elif loss == 'pose':
                    result = detector.update(None, now=5)
                elif loss == 'visibility':
                    result = detector.update(pose(0, left_visibility=0.1), now=5)
                else:
                    result = detector.update(pose(float('nan')), now=5)
                self.assert_neutral(result)
                self.assertEqual(result.calibration_state, READY)
                self.assertEqual(result.neutral_baseline, ready.neutral_baseline)
                self.assertEqual(result.calibration_mad, ready.calibration_mad)
                self.assertEqual(result.calibration_sample_count, 3)
                self.assertFalse(result.score_valid)
                self.assertIsNone(result.corrected_rotation_score)
                self.assertEqual(detector.update(pose(-0.196), now=6).final_movement, MOVEMENT_RIGHT)

    def test_samples_survive_pose_loss_and_reset(self):
        detector = create_uncalibrated_detector()
        detector.update(pose(0.2), now=0)
        detector.reset()
        result = detector.update(None, now=10)
        self.assertEqual(result.calibration_sample_count, 1)
        self.assertEqual(result.calibration_state, CALIBRATING)
        detector.update(pose(0.2), now=11)
        self.assertEqual(detector.update(pose(0.2), now=12).calibration_state, READY)

    def test_hysteresis_regression_with_offset(self):
        for offset in (0, -0.596, 0.185):
            detector = create_uncalibrated_detector()
            calibrate(detector, offset)
            for delta, expected in ((0.4, MOVEMENT_RIGHT), (0.2, MOVEMENT_RIGHT),
                                    (0.1, MOVEMENT_NEUTRAL), (-0.4, MOVEMENT_LEFT),
                                    (-0.2, MOVEMENT_LEFT), (-0.1, MOVEMENT_NEUTRAL),
                                    (0.4, MOVEMENT_RIGHT), (-0.4, MOVEMENT_LEFT)):
                result = detector.update(pose(offset + delta), now=4)
                self.assertEqual(result.raw_movement, expected)
                self.assertEqual(result.final_movement, expected)

    def test_stabilization_regression_with_offset(self):
        detector = create_uncalibrated_detector(stable_frame_count=3)
        calibrate(detector, -0.596)
        for delta, expected in ((0.4, MOVEMENT_NEUTRAL), (0.4, MOVEMENT_NEUTRAL),
                                (0, MOVEMENT_NEUTRAL), (0.4, MOVEMENT_NEUTRAL),
                                (0.4, MOVEMENT_NEUTRAL), (0.4, MOVEMENT_RIGHT)):
            result = detector.update(pose(-0.596 + delta), now=4)
            self.assertEqual(result.final_movement, expected)

    def test_mirror_coordinates_match_baseline(self):
        for mirror, sign in ((False, 1), (True, -1)):
            detector = create_uncalibrated_detector(mirror=mirror)
            result = calibrate(detector, 0.2)
            self.assertAlmostEqual(result.neutral_baseline, sign * 0.2)
            result = detector.update(pose(0.6), now=4)
            self.assertAlmostEqual(result.corrected_rotation_score, sign * 0.4)
            self.assertEqual(result.final_movement, MOVEMENT_LEFT if mirror else MOVEMENT_RIGHT)

    def test_strength_uses_corrected_score(self):
        detector = create_uncalibrated_detector()
        calibrate(detector, -0.596)
        for delta in (0, 0.3, -0.3, 2):
            result = detector.update(pose(-0.596 + delta), now=4)
            self.assertAlmostEqual(result.movement_strength, min(abs(delta) / 0.6, 1))

    def test_new_detector_requires_new_calibration(self):
        calibrate(create_uncalibrated_detector(), 0.2)
        detector = create_uncalibrated_detector()
        self.assertEqual(detector.calibrator.state, CALIBRATING)
        self.assertIsNone(detector.calibrator.baseline)


if __name__ == '__main__':
    unittest.main()
