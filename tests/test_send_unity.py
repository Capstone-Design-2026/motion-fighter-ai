"""Unit tests for Unity UDP payload generation."""

from pathlib import Path
import sys
import unittest


SRC_DIR = Path(__file__).resolve().parent.parent / "src"
sys.path.insert(0, str(SRC_DIR))

from send_unity import build_payload  # noqa: E402


class SendUnityPayloadTest(unittest.TestCase):
    def test_build_payload_uses_action_movement_schema(self):
        payload = build_payload("ATTACK", "RIGHT", 0.94, 0.61)

        self.assertEqual(payload["action"], "ATTACK")
        self.assertEqual(payload["movement"], "RIGHT")
        self.assertEqual(payload["confidence"], 0.94)
        self.assertEqual(payload["movement_strength"], 0.61)
        self.assertIn("timestamp", payload)

    def test_movement_strength_is_clamped(self):
        low = build_payload("IDLE", "NEUTRAL", 1.0, -1.0)
        high = build_payload("IDLE", "RIGHT", 1.0, 2.0)

        self.assertEqual(low["movement_strength"], 0.0)
        self.assertEqual(high["movement_strength"], 1.0)


if __name__ == "__main__":
    unittest.main()
