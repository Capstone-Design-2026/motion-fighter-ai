"""One precise monotonic timebase for an explicitly configured live session.

Python 3.11 uses coarse GetTickCount64 for time.monotonic_ns on Windows.
QueryPerformanceCounter (perf_counter_ns) avoids 15/16 ms timestamp jumps.
Pass now_ns to both the recognizer and sender; never mix their epochs.
Other platforms retain their previous monotonic clock. Recorded replays keep
their original timestamps and do not call this clock.
"""
import sys
import time


def now_ns():
    return time.perf_counter_ns() if sys.platform == 'win32' else time.monotonic_ns()


def metadata():
    name = 'perf_counter' if sys.platform == 'win32' else 'monotonic'
    info = time.get_clock_info(name)
    return {'name': name + '_ns', 'implementation': info.implementation,
            'resolution_ns': info.resolution * 1e9, 'monotonic': info.monotonic,
            'adjustable': info.adjustable, 'python': sys.version.split()[0],
            'platform': sys.platform,
            'scope': 'capture, models, recognizer, publisher and UDP use this same clock'}
