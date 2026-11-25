"""Unit tests for shoulder-based movement detection."""

from pathlib import Path
from types import SimpleNamespace
import sys
import unittest


SRC_DIR = Path(__file__).resolve().parent.parent / "src"
sys.path.insert(0, str(SRC_DIR))

from movement import (  # noqa: E402
    MOVEMENT_LEFT,
    MOVEMENT_NEUTRAL,
    MOVEMENT_RIGHT,
    ShoulderMovementDetector,
)


def make_landmarks(left_z=0.0, right_z=0.0, left_visibility=1.0, right_visibility=1.0):
    """Create a 33-landmark pose with configurable shoulders."""
    landmarks = [
        SimpleNamespace(x=0.5, y=0.5, z=0.0, visibility=1.0)
        for _ in range(33)
    ]
    landmarks[11] = SimpleNamespace(
        x=0.4,
        y=0.5,
        z=left_z,
        visibility=left_visibility,
    )
    landmarks[12] = SimpleNamespace(
        x=0.6,
        y=0.5,
        z=right_z,
        visibility=right_visibility,
    )
    return landmarks


def create_detector(stable_frame_count=1, mirror=False):
    """Create a detector with deterministic test thresholds."""
    detector = ShoulderMovementDetector(
        visibility_threshold=0.5,
        enter_threshold=0.30,
        exit_threshold=0.15,
        stable_frame_count=stable_frame_count,
        mirror=mirror,
        max_strength_delta=0.60,
        calibration_seconds=1.0,
        calibration_min_samples=2,
    )
    detector.update(make_landmarks(), now=0.0)
    detector.update(make_landmarks(), now=1.0)
    return detector


class ShoulderMovementDetectorTest(unittest.TestCase):
    def test_neutral_inside_enter_threshold(self):
        detector = create_detector()

        result = detector.update(make_landmarks(left_z=0.02, right_z=-0.02))

        self.assertEqual(result.raw_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.final_movement, MOVEMENT_NEUTRAL)

    def test_right_when_rotation_score_exceeds_enter_threshold(self):
        detector = create_detector()

        result = detector.update(make_landmarks(left_z=0.04, right_z=-0.04))

        self.assertEqual(result.raw_movement, MOVEMENT_RIGHT)
        self.assertEqual(result.final_movement, MOVEMENT_RIGHT)

    def test_left_when_rotation_score_is_below_negative_enter_threshold(self):
        detector = create_detector()

        result = detector.update(make_landmarks(left_z=-0.04, right_z=0.04))

        self.assertEqual(result.raw_movement, MOVEMENT_LEFT)
        self.assertEqual(result.final_movement, MOVEMENT_LEFT)

    def test_visibility_failure_forces_neutral(self):
        detector = create_detector()
        detector.update(make_landmarks(left_z=0.04, right_z=-0.04))

        result = detector.update(
            make_landmarks(left_z=0.04, right_z=-0.04, left_visibility=0.2)
        )

        self.assertEqual(result.raw_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.final_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.rotation_score, 0.0)
        self.assertEqual(result.movement_strength, 0.0)

    def test_reset_forces_neutral_when_pose_is_unavailable(self):
        detector = create_detector()
        detector.update(make_landmarks(left_z=0.04, right_z=-0.04))

        result = detector.reset()
        next_result = detector.update(make_landmarks(left_z=0.02, right_z=-0.02))

        self.assertEqual(result.raw_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(result.final_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(next_result.raw_movement, MOVEMENT_NEUTRAL)

    def test_mirror_flips_left_and_right(self):
        normal_detector = create_detector(mirror=False)
        mirrored_detector = create_detector(mirror=True)
        landmarks = make_landmarks(left_z=0.04, right_z=-0.04)

        normal_result = normal_detector.update(landmarks)
        mirrored_result = mirrored_detector.update(landmarks)

        self.assertEqual(normal_result.raw_movement, MOVEMENT_RIGHT)
        self.assertEqual(mirrored_result.raw_movement, MOVEMENT_LEFT)

    def test_hysteresis_holds_until_exit_threshold(self):
        detector = create_detector()

        detector.update(make_landmarks(left_z=0.04, right_z=-0.04))
        held = detector.update(make_landmarks(left_z=0.02, right_z=-0.02))
        neutral = detector.update(make_landmarks(left_z=0.01, right_z=-0.01))

        self.assertEqual(held.raw_movement, MOVEMENT_RIGHT)
        self.assertEqual(neutral.raw_movement, MOVEMENT_NEUTRAL)

    def test_stabilization_requires_consecutive_frames(self):
        detector = create_detector(stable_frame_count=3)
        landmarks = make_landmarks(left_z=0.04, right_z=-0.04)

        first = detector.update(landmarks)
        second = detector.update(landmarks)
        third = detector.update(landmarks)

        self.assertEqual(first.raw_movement, MOVEMENT_RIGHT)
        self.assertEqual(first.final_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(second.final_movement, MOVEMENT_NEUTRAL)
        self.assertEqual(third.final_movement, MOVEMENT_RIGHT)

    def test_strength_is_clamped(self):
        detector = create_detector()

        result = detector.update(make_landmarks(left_z=0.2, right_z=-0.2))

        self.assertEqual(result.movement_strength, 1.0)


if __name__ == "__main__":
    unittest.main()
