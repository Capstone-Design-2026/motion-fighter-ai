"""Rule-based shoulder rotation movement detection."""

from dataclasses import dataclass
import math
from statistics import median
import time


LEFT_SHOULDER_INDEX = 11
RIGHT_SHOULDER_INDEX = 12

MOVEMENT_LEFT = "LEFT"
MOVEMENT_RIGHT = "RIGHT"
MOVEMENT_NEUTRAL = "NEUTRAL"
CALIBRATING = "CALIBRATING"
READY = "READY"


class NeutralBaselineCalibrator:
    """Collect valid scores in caller-provided monotonic time; freeze median/MAD."""

    def __init__(self, duration_seconds: float, min_samples: int):
        if not math.isfinite(duration_seconds) or duration_seconds < 0:
            raise ValueError("Calibration duration must be finite and non-negative.")
        if isinstance(min_samples, bool) or not isinstance(min_samples, int) or min_samples < 1:
            raise ValueError("Calibration minimum samples must be a positive integer.")
        self.duration_seconds = float(duration_seconds)
        self.min_samples = min_samples
        self.state = CALIBRATING
        self.baseline = None
        self.mad = None
        self.started_at = None
        self._samples = []

    @property
    def sample_count(self) -> int:
        return len(self._samples)

    def update(self, score: float, now: float):
        """Accept a finite score already checked for shoulder visibility."""
        if self.state == READY:
            return
        try:
            valid = math.isfinite(score) and math.isfinite(now)
        except (TypeError, ValueError, OverflowError):
            return
        if not valid:
            return
        if self.started_at is None:
            self.started_at = now
        self._samples.append(float(score))
        if (now - self.started_at >= self.duration_seconds
                and self.sample_count >= self.min_samples):
            self.baseline = median(self._samples)
            # Diagnostic only: no MAD threshold or rejection policy.
            self.mad = median(abs(value - self.baseline) for value in self._samples)
            self.state = READY


@dataclass(frozen=True)
class MovementResult:
    """Current movement detector output for one pose frame."""

    raw_movement: str
    final_movement: str
    rotation_score: float
    movement_strength: float
    corrected_rotation_score: float | None = None
    neutral_baseline: float | None = None
    calibration_state: str = CALIBRATING
    calibration_sample_count: int = 0
    calibration_mad: float | None = None
    score_valid: bool = False

    @property
    def raw_rotation_score(self) -> float:
        """Explicit alias; rotation_score retains its original raw meaning."""
        return self.rotation_score


class ConsecutiveStateStabilizer:
    """Change state only after the same candidate appears for N frames."""

    def __init__(self, stable_frame_count: int, initial_state: str):
        self.stable_frame_count = max(int(stable_frame_count), 1)
        self.stable_state = initial_state
        self.candidate_state = initial_state
        self.candidate_count = 0

    def reset(self, state: str):
        """Force the stabilizer to a known state."""
        self.stable_state = state
        self.candidate_state = state
        self.candidate_count = 0

    def update(self, raw_state: str) -> str:
        """Return the stable state after observing one raw state."""
        if raw_state == self.stable_state:
            self.candidate_state = raw_state
            self.candidate_count = 0
            return self.stable_state

        if raw_state != self.candidate_state:
            self.candidate_state = raw_state
            self.candidate_count = 1
        else:
            self.candidate_count += 1

        if self.candidate_count >= self.stable_frame_count:
            self.stable_state = raw_state
            self.candidate_count = 0

        return self.stable_state


class ShoulderMovementDetector:
    """Detect LEFT/RIGHT/NEUTRAL movement from shoulder depth difference."""

    def __init__(
        self,
        visibility_threshold: float,
        enter_threshold: float,
        exit_threshold: float,
        stable_frame_count: int,
        mirror: bool,
        max_strength_delta: float,
        calibration_seconds: float,
        calibration_min_samples: int,
    ):
        if exit_threshold > enter_threshold:
            raise ValueError("Movement exit threshold must be <= enter threshold.")

        self.calibrator = NeutralBaselineCalibrator(
            calibration_seconds, calibration_min_samples
        )
        self.visibility_threshold = float(visibility_threshold)
        self.enter_threshold = float(enter_threshold)
        self.exit_threshold = float(exit_threshold)
        self.mirror = bool(mirror)
        self.max_strength_delta = max(float(max_strength_delta), 1e-6)
        self.raw_movement = MOVEMENT_NEUTRAL
        self.stabilizer = ConsecutiveStateStabilizer(
            stable_frame_count=stable_frame_count,
            initial_state=MOVEMENT_NEUTRAL,
        )

    def update(self, landmarks, now: float | None = None) -> MovementResult:
        """Calibrate or classify a pose; invalid observations force neutral."""
        if landmarks is None:
            return self.reset()
        try:
            left_shoulder = landmarks[LEFT_SHOULDER_INDEX]
            right_shoulder = landmarks[RIGHT_SHOULDER_INDEX]
            if not self._shoulders_are_visible(left_shoulder, right_shoulder):
                return self.reset()
            coordinates = (
                left_shoulder.x, left_shoulder.y, left_shoulder.z,
                right_shoulder.x, right_shoulder.y, right_shoulder.z,
            )
            if not all(math.isfinite(value) for value in coordinates):
                return self.reset()
            rotation_score = self.calculate_rotation_score(left_shoulder, right_shoulder)
            if not math.isfinite(rotation_score):
                return self.reset()
        except (AttributeError, IndexError, TypeError, ValueError, OverflowError):
            return self.reset()

        if self.calibrator.state == CALIBRATING:
            self.calibrator.update(rotation_score, time.monotonic() if now is None else now)
            # Includes the completion frame: no direction candidate may carry over.
            self.reset()
            return self._result(rotation_score=rotation_score, score_valid=True)

        corrected_score = rotation_score - self.calibrator.baseline
        if not math.isfinite(corrected_score):
            return self.reset()
        self.raw_movement = self._classify_with_hysteresis(corrected_score)
        self.stabilizer.update(self.raw_movement)
        return self._result(
            rotation_score=rotation_score,
            score_valid=True,
            movement_strength=self.calculate_strength(corrected_score),
        )

    def _result(
        self, rotation_score: float = 0.0, score_valid: bool = False,
        movement_strength: float = 0.0,
    ) -> MovementResult:
        baseline = self.calibrator.baseline
        return MovementResult(
            raw_movement=self.raw_movement,
            final_movement=self.stabilizer.stable_state,
            rotation_score=rotation_score,
            movement_strength=movement_strength,
            corrected_rotation_score=(
                rotation_score - baseline if score_valid and baseline is not None else None
            ),
            neutral_baseline=baseline,
            calibration_state=self.calibrator.state,
            calibration_sample_count=self.calibrator.sample_count,
            calibration_mad=self.calibrator.mad,
            score_valid=score_valid,
        )

    def reset(self, movement: str = MOVEMENT_NEUTRAL) -> MovementResult:
        """Reset movement only; retain calibration samples and any ready baseline."""
        if self.calibrator.state == CALIBRATING:
            movement = MOVEMENT_NEUTRAL
        self.raw_movement = movement
        self.stabilizer.reset(movement)
        return self._result()

    def calculate_rotation_score(self, left_shoulder, right_shoulder) -> float:
        """
        Return normalized shoulder depth difference.

        Positive means the configured RIGHT direction by default. Set
        MOVEMENT_MIRROR when the camera/feed convention needs the opposite sign.
        """
        shoulder_width = math.hypot(
            left_shoulder.x - right_shoulder.x,
            left_shoulder.y - right_shoulder.y,
        )
        depth_delta = (left_shoulder.z - right_shoulder.z) / max(shoulder_width, 1e-6)

        if self.mirror:
            depth_delta *= -1

        return float(depth_delta)

    def calculate_strength(self, rotation_score: float) -> float:
        """Return clamped movement strength in the 0.0-1.0 range."""
        strength = abs(rotation_score) / self.max_strength_delta
        return min(max(strength, 0.0), 1.0)

    def _shoulders_are_visible(self, left_shoulder, right_shoulder) -> bool:
        return (
            math.isfinite(left_shoulder.visibility)
            and math.isfinite(right_shoulder.visibility)
            and left_shoulder.visibility >= self.visibility_threshold
            and right_shoulder.visibility >= self.visibility_threshold
        )

    def _classify_with_hysteresis(self, rotation_score: float) -> str:
        if self.raw_movement == MOVEMENT_RIGHT:
            if rotation_score <= -self.enter_threshold:
                return MOVEMENT_LEFT
            if abs(rotation_score) <= self.exit_threshold:
                return MOVEMENT_NEUTRAL
            return MOVEMENT_RIGHT

        if self.raw_movement == MOVEMENT_LEFT:
            if rotation_score >= self.enter_threshold:
                return MOVEMENT_RIGHT
            if abs(rotation_score) <= self.exit_threshold:
                return MOVEMENT_NEUTRAL
            return MOVEMENT_LEFT

        if rotation_score >= self.enter_threshold:
            return MOVEMENT_RIGHT

        if rotation_score <= -self.enter_threshold:
            return MOVEMENT_LEFT

        return MOVEMENT_NEUTRAL
