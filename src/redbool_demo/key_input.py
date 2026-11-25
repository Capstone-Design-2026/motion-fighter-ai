"""Suppress repeated calibration presses from a held keyboard key."""


class CalibrationKeyGate:
    def __init__(self, quiet_ns=800_000_000):
        self.quiet_ns = quiet_ns
        self.last_press_ns = None

    def accept(self, now_ns):
        previous = self.last_press_ns
        self.last_press_ns = now_ns
        return previous is None or now_ns - previous >= self.quiet_ns
