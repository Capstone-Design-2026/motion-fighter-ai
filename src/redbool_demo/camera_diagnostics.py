"""Read-only device settings and acquisition cadence, separate from inference."""
import math


def camera_properties(cv, cap):
    """Unsupported driver properties are diagnostic only, never tuned here."""
    result = {}
    try:
        result['backend'] = cap.getBackendName()
    except Exception:
        result['backend'] = None
    for name, constant in [('reported_fps', 'CAP_PROP_FPS'),
                           ('width', 'CAP_PROP_FRAME_WIDTH'),
                           ('height', 'CAP_PROP_FRAME_HEIGHT'),
                           ('exposure', 'CAP_PROP_EXPOSURE'),
                           ('auto_exposure', 'CAP_PROP_AUTO_EXPOSURE'),
                           ('gain', 'CAP_PROP_GAIN'), ('fourcc', 'CAP_PROP_FOURCC')]:
        value = None
        try:
            prop = getattr(cv, constant)
            value = float(cap.get(prop))
            if not math.isfinite(value): value = None
        except Exception:
            pass
        result[name] = value
    result['note'] = 'Driver-reported values may be unsupported; measured_fps is authoritative. Exposure units depend on backend.'
    return result


class CaptureCadence:
    """Counts every successful acquisition, including samples inference skips."""
    def __init__(self, window_ns=5_000_000_000):
        self.window_ns = window_ns
        self.count = 0
        self.first = self.last = self.window_start = None
        self.window_count = 0
        self.max_gap_ns = self.max_read_ns = self.total_read_ns = 0

    def observe(self, stamp, read_start):
        if self.first is None:
            self.first = self.window_start = stamp
            self.window_count = 1
        if self.last is not None:
            self.max_gap_ns = max(self.max_gap_ns, stamp - self.last)
        read_ns = max(0, stamp - read_start)
        self.max_read_ns = max(self.max_read_ns, read_ns)
        self.total_read_ns += read_ns
        self.last = stamp
        self.count += 1
        elapsed = stamp - self.window_start
        if elapsed < self.window_ns: return None
        row = {'timestamp_ns': stamp, 'captured': self.count,
               'window_seconds': elapsed / 1e9,
               'measured_fps': (self.count - self.window_count) * 1e9 / elapsed}
        self.window_start, self.window_count = stamp, self.count
        return row

    def summary(self):
        elapsed = self.last - self.first if self.count > 1 else 0
        return {'captured': self.count,
                'measured_fps': (self.count - 1) * 1e9 / elapsed if elapsed > 0 else None,
                'first_capture_ns': self.first, 'last_capture_ns': self.last,
                'max_capture_gap_ms': self.max_gap_ns / 1e6 if self.count > 1 else None,
                'max_read_ms': self.max_read_ns / 1e6 if self.count else None,
                'mean_read_ms': self.total_read_ns / self.count / 1e6 if self.count else None}
