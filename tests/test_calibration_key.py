import unittest
from redbool_demo.key_input import CalibrationKeyGate


class CalibrationKeyTests(unittest.TestCase):
    def test_observed_held_key_burst_requests_only_one_calibration(self):
        gate = CalibrationKeyGate()
        times = [1_000_000_000, 1_222_000_000, 1_431_000_000, 1_577_000_000]
        self.assertEqual([gate.accept(n) for n in times], [True, False, False, False])

    def test_repeat_keeps_gate_closed_until_key_has_been_quiet(self):
        gate = CalibrationKeyGate()
        self.assertTrue(gate.accept(1_000_000_000))
        for n in range(1_200_000_000, 5_000_000_001, 200_000_000):
            self.assertFalse(gate.accept(n))
        self.assertFalse(gate.accept(5_799_999_999))
        self.assertTrue(gate.accept(6_599_999_999))

    def test_deliberate_later_press_starts_a_new_person_calibration(self):
        gate = CalibrationKeyGate()
        self.assertTrue(gate.accept(1_000_000_000))
        self.assertTrue(gate.accept(2_000_000_000))


if __name__ == '__main__': unittest.main()
