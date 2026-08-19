"""Rule-based shoulder rotation movement detection."""

from dataclasses import dataclass
import math


LEFT_SHOULDER_INDEX = 11
RIGHT_SHOULDER_INDEX = 12

MOVEMENT_LEFT = "LEFT"
MOVEMENT_RIGHT = "RIGHT"
MOVEMENT_NEUTRAL = "NEUTRAL"


@dataclass(frozen=True)
class MovementResult:
    """Current movement detector output for one pose frame."""

    raw_movement: str
    final_movement: str
    rotation_score: float
    movement_strength: float


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
    ):
        if exit_threshold > enter_threshold:
            raise ValueError("Movement exit threshold must be <= enter threshold.")

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

    def update(self, landmarks) -> MovementResult:
        """Classify one pose frame and return raw/stable movement data."""
        left_shoulder = landmarks[LEFT_SHOULDER_INDEX]
        right_shoulder = landmarks[RIGHT_SHOULDER_INDEX]

        if not self._shoulders_are_visible(left_shoulder, right_shoulder):
            self.raw_movement = MOVEMENT_NEUTRAL
            self.stabilizer.reset(MOVEMENT_NEUTRAL)
            return MovementResult(
                raw_movement=MOVEMENT_NEUTRAL,
                final_movement=MOVEMENT_NEUTRAL,
                rotation_score=0.0,
                movement_strength=0.0,
            )

        rotation_score = self.calculate_rotation_score(left_shoulder, right_shoulder)
        self.raw_movement = self._classify_with_hysteresis(rotation_score)
        final_movement = self.stabilizer.update(self.raw_movement)

        return MovementResult(
            raw_movement=self.raw_movement,
            final_movement=final_movement,
            rotation_score=rotation_score,
            movement_strength=self.calculate_strength(rotation_score),
        )

    def reset(self, movement: str = MOVEMENT_NEUTRAL) -> MovementResult:
        """Reset raw and stable movement state when pose data is unavailable."""
        self.raw_movement = movement
        self.stabilizer.reset(movement)
        return MovementResult(
            raw_movement=movement,
            final_movement=movement,
            rotation_score=0.0,
            movement_strength=0.0,
        )

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
            left_shoulder.visibility >= self.visibility_threshold
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
