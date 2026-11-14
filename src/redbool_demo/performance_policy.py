"""Bounded auxiliary-model fallback; primary recognition thresholds never change."""
from collections import deque


class InferencePolicy:
    """Latch off optional Full models only after repeated measured overload.

    Full VIDEO and Full IMAGE serve same-frame X recovery. On a slow machine
    their critical-path extension can make even the primary result stale. The
    latch lasts until this live process ends, including across C recalibration,
    so the application does not oscillate between fresh and stale input.
    """
    WINDOW = 10
    REQUIRED = 8
    PRESSURE_WINDOW = 20
    PRESSURE_REQUIRED = 8
    GAP_NS = 100_000_000
    WATCHDOG_NS = 150_000_000
    AGE_NS = 120_000_000
    EXTENSION_NS = 20_000_000

    def __init__(self, auxiliary_configured):
        self.auxiliary_configured = bool(auxiliary_configured)
        self.lite_only = False
        self.observed = 0
        self._bad = deque(maxlen=self.WINDOW)
        self._pressure = deque(maxlen=self.PRESSURE_WINDOW)
        self._previous_capture = None
        self.transition = None

    @classmethod
    def description(cls):
        return dict(name='primary_calibration_then_same_frame_auxiliary_with_overload_latch',
            primary='every processed camera frame; recognition thresholds unchanged',
            auxiliary='configured Full VIDEO/IMAGE only when calibrated at frame start',
            resume='next processed frame after calibration unless overload latch is set',
            skipped='null observations; no stale auxiliary reuse',
            overload=dict(window=cls.WINDOW,required=cls.REQUIRED,
                age_ms=cls.AGE_NS/1e6,minimum_auxiliary_extension_ms=cls.EXTENSION_NS/1e6,
                pressure_window=cls.PRESSURE_WINDOW,pressure_required=cls.PRESSURE_REQUIRED,
                gap_ms=cls.GAP_NS/1e6,watchdog_ms=cls.WATCHDOG_NS/1e6,
                reset='new live process only; calibration and pause retain latch'))

    def run_auxiliary(self, calibrated):
        return self.auxiliary_configured and bool(calibrated) and not self.lite_only

    def observe(self, *, auxiliary_ran, capture_ns, primary_end_ns, auxiliary_end_ns, processing_end_ns):
        previous = self._previous_capture
        self._previous_capture = capture_ns
        if self.lite_only or not self.auxiliary_configured:
            return None
        if not auxiliary_ran:
            # Recalibration may clear pending evidence, never an existing latch.
            self._bad.clear();self._pressure.clear()
            return None
        age = processing_end_ns - capture_ns
        extension = auxiliary_end_ns - primary_end_ns
        # A clock discontinuity is not evidence that optional model work caused
        # load. It neither arms the latch nor revives previous observations.
        if not capture_ns <= primary_end_ns <= processing_end_ns or auxiliary_end_ns > processing_end_ns:
            return None
        self.observed += 1
        extends = extension >= self.EXTENSION_NS
        self._bad.append(age > self.AGE_NS and extends)
        gap = capture_ns-previous if previous is not None else 0
        pressure = previous is not None and gap > 0 and (
            gap > self.GAP_NS or processing_end_ns-previous >= self.WATCHDOG_NS)
        self._pressure.append(bool(pressure and extends))
        acute = len(self._bad) == self.WINDOW and sum(self._bad) >= self.REQUIRED
        sustained = len(self._pressure) == self.PRESSURE_WINDOW and sum(self._pressure) >= self.PRESSURE_REQUIRED
        if acute or sustained:
            self.lite_only = True
            self.transition = dict(mode='lite_overload',reason='AUXILIARY_DISABLED_FOR_LATENCY',
                trigger='stale_frames' if acute else 'continuity_pressure',
                observed_auxiliary_frames=self.observed,window=len(self._bad),bad_frames=sum(self._bad),
                pressure_window=len(self._pressure),pressure_frames=sum(self._pressure),
                frame_age_ms=age/1e6,auxiliary_extension_ms=max(0,extension)/1e6)
            return dict(self.transition)
        return None

    def snapshot(self, calibrated):
        mode = ('lite_overload' if self.lite_only else 'lite' if not self.auxiliary_configured
            else 'full' if calibrated else 'lite_calibration')
        return dict(inference_mode=mode,
            inference_warning='AUXILIARY_DISABLED_FOR_LATENCY' if self.lite_only else '',
            observed_auxiliary_frames=self.observed,window=len(self._bad),bad_frames=sum(self._bad),
            pressure_window=len(self._pressure),pressure_frames=sum(self._pressure),
            overload_latched=self.lite_only,transition=dict(self.transition) if self.transition else None)
