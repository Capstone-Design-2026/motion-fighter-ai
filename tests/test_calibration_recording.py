"""Exercise the calibration factory and missing-observation CSV contract."""
import csv
import io
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from calibrate_movement import (
    CSV_FIELDNAMES, build_observation_row, create_live_detector,
)
from config import MOVEMENT_CALIBRATION_SECONDS, MOVEMENT_CALIBRATION_MIN_SAMPLES


class CalibrationRecordingTest(unittest.TestCase):
    def test_real_factory_uses_configured_calibration(self):
        detector = create_live_detector()
        self.assertEqual(detector.calibrator.duration_seconds, MOVEMENT_CALIBRATION_SECONDS)
        self.assertEqual(detector.calibrator.min_samples, MOVEMENT_CALIBRATION_MIN_SAMPLES)

    def test_pose_loss_is_preserved_with_empty_score(self):
        result = create_live_detector().reset()
        row = build_observation_row("LEFT", "RECORDING", 17, 123456, 1.2,
                                    False, 0, 0, result)
        output = io.StringIO()
        writer = csv.DictWriter(output, fieldnames=CSV_FIELDNAMES)
        writer.writeheader()
        writer.writerow(row)
        saved = list(csv.DictReader(io.StringIO(output.getvalue())))[0]
        self.assertEqual(saved["frame_id"], "17")
        self.assertEqual(saved["pose_present"], "0")
        self.assertEqual(saved["score_valid"], "0")
        self.assertEqual(saved["rotation_score_raw"], "")
        self.assertEqual(saved["live_final_movement"], "NEUTRAL")

    def test_loss_preserves_baseline_without_inventing_corrected_score(self):
        detector = create_live_detector()
        for i in range(100):
            detector.calibrator.update(0.2, i / 10)
        result = detector.reset()
        row = build_observation_row("RIGHT", "COUNTDOWN", 20, 200000, .5,
                                    False, 0, 0, result)
        self.assertEqual(row["calibration_state"], "READY")
        self.assertEqual(row["neutral_baseline"], .2)
        self.assertIsNone(row["corrected_rotation_score"])
        self.assertEqual(set(row), set(CSV_FIELDNAMES))


if __name__ == "__main__":
    unittest.main()
