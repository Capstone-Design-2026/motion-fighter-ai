"""Unit tests for inference runtime validation and send decisions."""

from pathlib import Path
from types import ModuleType, SimpleNamespace
import sys
import unittest


SRC_DIR = Path(__file__).resolve().parent.parent / "src"
sys.path.insert(0, str(SRC_DIR))


def install_inference_dependency_stubs():
    """Install minimal stubs so run_inference can be imported without vision deps."""
    sys.modules.setdefault("cv2", ModuleType("cv2"))

    mediapipe = ModuleType("mediapipe")
    tasks = ModuleType("mediapipe.tasks")
    tasks_python = ModuleType("mediapipe.tasks.python")
    vision = ModuleType("mediapipe.tasks.python.vision")
    tasks.python = tasks_python
    tasks_python.vision = vision
    mediapipe.tasks = tasks
    sys.modules.setdefault("mediapipe", mediapipe)
    sys.modules.setdefault("mediapipe.tasks", tasks)
    sys.modules.setdefault("mediapipe.tasks.python", tasks_python)
    sys.modules.setdefault("mediapipe.tasks.python.vision", vision)

    tensorflow = ModuleType("tensorflow")
    keras = ModuleType("tensorflow.keras")
    models = ModuleType("tensorflow.keras.models")
    models.load_model = lambda *_args, **_kwargs: None
    keras.models = models
    tensorflow.keras = keras
    sys.modules.setdefault("tensorflow", tensorflow)
    sys.modules.setdefault("tensorflow.keras", keras)
    sys.modules.setdefault("tensorflow.keras.models", models)


install_inference_dependency_stubs()

from config import ACTIONS  # noqa: E402
from run_inference import (  # noqa: E402
    PASSIVE_SEND_INTERVAL_SECONDS,
    should_send_to_unity,
    validate_model_artifacts,
)


def fake_model(output_dim):
    """Create a minimal model-like object exposing Keras output_shape."""
    return SimpleNamespace(output_shape=(None, output_dim))


class RunInferenceValidationTest(unittest.TestCase):
    def test_validates_matching_model_and_labels(self):
        validate_model_artifacts(fake_model(len(ACTIONS)), ACTIONS)

    def test_rejects_model_output_label_count_mismatch(self):
        with self.assertRaisesRegex(RuntimeError, "output dimension"):
            validate_model_artifacts(fake_model(len(ACTIONS) + 1), ACTIONS)

    def test_rejects_missing_label(self):
        labels = [label for label in ACTIONS if label != "NONE"]

        with self.assertRaisesRegex(RuntimeError, "Missing labels"):
            validate_model_artifacts(fake_model(len(labels)), labels)

    def test_rejects_duplicate_label(self):
        labels = ACTIONS[:-1] + [ACTIONS[0]]

        with self.assertRaisesRegex(RuntimeError, "Duplicate labels"):
            validate_model_artifacts(fake_model(len(labels)), labels)

    def test_rejects_unexpected_label(self):
        labels = ACTIONS[:-1] + ["MOVE_LEFT"]

        with self.assertRaisesRegex(RuntimeError, "Unexpected labels"):
            validate_model_artifacts(fake_model(len(labels)), labels)


class SendDecisionTest(unittest.TestCase):
    def test_action_only_change_sends_immediately(self):
        self.assertTrue(
            should_send_to_unity("ATTACK", "NEUTRAL", "IDLE", "NEUTRAL", 10.0, 10.0)
        )

    def test_movement_only_change_sends_immediately(self):
        self.assertTrue(
            should_send_to_unity("IDLE", "RIGHT", "IDLE", "NEUTRAL", 10.0, 10.0)
        )

    def test_action_and_movement_change_sends_immediately(self):
        self.assertTrue(
            should_send_to_unity("ATTACK", "LEFT", "IDLE", "NEUTRAL", 10.0, 10.0)
        )

    def test_idle_to_none_sends_immediately(self):
        self.assertTrue(
            should_send_to_unity("NONE", "NEUTRAL", "IDLE", "NEUTRAL", 10.0, 10.0)
        )

    def test_none_to_idle_sends_immediately(self):
        self.assertTrue(
            should_send_to_unity("IDLE", "NEUTRAL", "NONE", "NEUTRAL", 10.0, 10.0)
        )

    def test_unchanged_passive_before_interval_does_not_send(self):
        self.assertFalse(
            should_send_to_unity(
                "IDLE",
                "NEUTRAL",
                "IDLE",
                "NEUTRAL",
                10.0,
                10.0 + PASSIVE_SEND_INTERVAL_SECONDS - 0.01,
            )
        )

    def test_unchanged_passive_after_interval_sends(self):
        self.assertTrue(
            should_send_to_unity(
                "IDLE",
                "NEUTRAL",
                "IDLE",
                "NEUTRAL",
                10.0,
                10.0 + PASSIVE_SEND_INTERVAL_SECONDS,
            )
        )

    def test_unchanged_active_state_does_not_repeat(self):
        self.assertFalse(
            should_send_to_unity("ATTACK", "RIGHT", "ATTACK", "RIGHT", 10.0, 20.0)
        )


if __name__ == "__main__":
    unittest.main()
