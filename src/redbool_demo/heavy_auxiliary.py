"""Optional same-frame helper. No camera, model imports, sockets or FSM changes.

The caller owns interpretation/release policy. A missing result is unknown,
never an uncross pose. In particular, dropping this channel must not rearm an
X which only this channel could see.
"""
from collections import Counter, deque
from dataclasses import dataclass
import math
import threading
import time


@dataclass(frozen=True)
class FrameKey:
    actor_id: str
    capture_epoch: str
    frame_id: int
    capture_ns: int
    width: int
    height: int
    source_mirrored: bool = False
    calibration_generation: int = 0


@dataclass(frozen=True)
class Ticket:
    key: FrameKey
    generation: int
    serial: int


@dataclass(frozen=True)
class HeavyResult:
    key: FrameKey
    generation: int
    started_ns: int
    finished_ns: int
    value: object


def refinement_owned(*, owned, latched, refinement_latched):
    """Estimate only a pending ordinary X or an unresolved refined repeat latch.

    A normal X that already fired without optional evidence keeps the original
    three-model return. The caller samples these booleans before processing the
    next frame; a result used on the actual firing frame is unaffected.
    """
    return bool((owned and not latched) or refinement_latched)


class BoundedHeavyAuxiliary:
    WINDOW = 30
    HEALTHY_AGE_NS = 45_000_000
    HEALTHY_GAP_NS = 60_000_000
    SOURCE_GAP_NS = 100_000_000
    EVIDENCE_NS = 120_000_000
    WAIT_NS = 10_000_000
    PENDING_NS = 300_000_000
    FAILURE_LIMIT = 3

    def __init__(self, loader, infer, *, warm=None, clock_ns=time.monotonic_ns):
        self.loader, self.infer, self.warm = loader, infer, warm
        self.clock_ns = clock_ns
        self.condition = threading.Condition()
        self.closed = False
        self.disabled = False
        self.disable_reason = ''
        self.thread = None
        self.ready = False
        self.warming = False
        self.healthy = False
        self.active = False
        self.generation = self.serial = 0
        self.context = self.previous_key = None
        self.performance_capture = None
        self.performance = deque(maxlen=self.WINDOW)
        self.job = None
        self.planned = None
        self.running = None
        self.abandoned = None
        self.completed = None
        self.failures = 0
        self.counts = Counter()
        self.last_job_ms = 0.

    @staticmethod
    def _context(key):
        return (key.actor_id, key.capture_epoch, key.width, key.height,
                key.source_mirrored, key.calibration_generation)

    def _invalidate_locked(self, reason, *, reset_health=True, clear_active=True):
        self.generation += 1
        self.job = self.completed = self.planned = None
        self.abandoned = self.running
        self.counts['reset_' + reason] += 1
        if reset_health:
            self.performance.clear()
            self.performance_capture = None
            self.healthy = False
        if clear_active:
            self.active = False

    def reset(self, reason='control'):
        """C/person/stop invalidation; never touches the normal SPECIAL latch."""
        with self.condition:
            self._invalidate_locked(reason)
            self.previous_key = self.context = None
            self.condition.notify_all()

    def _disable_locked(self, reason):
        self.disabled = True
        self.disable_reason = reason
        self._invalidate_locked('disabled', clear_active=False)
        self.condition.notify_all()

    def _failure_locked(self, reason):
        self.counts[reason] += 1
        self.failures += 1
        if self.failures >= self.FAILURE_LIMIT:
            self._disable_locked(reason)

    def observe_performance(self, *, capture_ns, primary_end_ns, processing_end_ns,
                            calibrated, overload):
        """One observation per processed frame. Never loads on the caller."""
        with self.condition:
            if self.closed or self.disabled:
                return False
            if overload:
                self._disable_locked('existing_inference_overload')
                return False
            valid = (calibrated and all(type(x) is int for x in
                     (capture_ns, primary_end_ns, processing_end_ns))
                     and 0 <= capture_ns <= primary_end_ns <= processing_end_ns)
            gap = None if not valid or self.performance_capture is None else capture_ns-self.performance_capture
            if not valid or (gap is not None and gap <= 0):
                self.performance.clear();self.performance_capture = None
                self.healthy = False
                return False
            self.performance_capture = capture_ns
            self.performance.append((primary_end_ns-capture_ns, processing_end_ns-capture_ns, gap))
            ages = sorted(x[1] for x in self.performance)
            self.healthy = (len(ages) == self.WINDOW
                and ages[math.ceil(.95*len(ages))-1] <= self.HEALTHY_AGE_NS
                and all(x[0] <= self.HEALTHY_AGE_NS for x in self.performance)
                and all(x[2] is None or 0 < x[2] <= self.HEALTHY_GAP_NS for x in self.performance))
            if self.healthy and self.thread is None:
                self.warming = True
                self.thread = threading.Thread(target=self._worker, name='optional-heavy-aux', daemon=True)
                self.thread.start()
            return self.healthy

    def plan(self, key, *, calibrated, owned, latched, pending_since_ns, now_ns=None):
        """Plan before primary inference; state comes from the previous result.

        Source/control changes discard tickets. A >100ms source gap does not
        rearm the normal pose or erase the caller's Heavy-owned release guard.
        """
        now = self.clock_ns() if now_ns is None else now_ns
        if not isinstance(key, FrameKey):
            raise TypeError('FrameKey required')
        with self.condition:
            context = self._context(key)
            if self.context is None:
                self.context = context
            elif context != self.context:
                self._invalidate_locked('identity')
                self.context = context;self.previous_key = None
            if self.previous_key is not None:
                if key.frame_id <= self.previous_key.frame_id or key.capture_ns <= self.previous_key.capture_ns:
                    self._invalidate_locked('order', clear_active=False)
                    self.counts['plan_rejected_order'] += 1
                    return None
                if key.capture_ns-self.previous_key.capture_ns > self.SOURCE_GAP_NS:
                    self._invalidate_locked('source_gap', clear_active=False)
            self.previous_key = key
            self.planned = None
            if not calibrated:
                self._invalidate_locked('uncalibrated')
                return None
            if not owned:
                self.active = False
            elif (not latched and type(pending_since_ns) is int
                    and 0 <= pending_since_ns <= key.capture_ns
                    and key.capture_ns-pending_since_ns >= self.PENDING_NS):
                self.active = True
            if (self.closed or self.disabled or not self.ready or not self.healthy or not self.active
                    or not 0 <= now-key.capture_ns < self.EVIDENCE_NS):
                return None
            self.serial += 1
            self.planned = Ticket(key,self.generation,self.serial)
            return self.planned

    def submit(self, ticket, image):
        """One pending/running job, no queue. Pixels must remain immutable."""
        now = self.clock_ns()
        with self.condition:
            if (not isinstance(ticket,Ticket) or ticket != self.planned or ticket.generation != self.generation
                    or self.previous_key != ticket.key or self.closed or self.disabled
                    or not self.ready or not self.healthy
                    or not 0 <= now-ticket.key.capture_ns < self.EVIDENCE_NS):
                self.counts['submit_rejected'] += 1
                return False
            if self.job is not None or self.running is not None:
                self._failure_locked('busy')
                return False
            self.planned = None
            self.completed = None
            self.abandoned = None
            self.job = (ticket,image)
            self.counts['submitted'] += 1
            self.condition.notify_all()
            return True

    def result(self, ticket):
        """Consume only this current frame; wait at most10ms and source+120ms."""
        if not isinstance(ticket,Ticket):return None
        wall_end = time.monotonic() + self.WAIT_NS/1e9
        with self.condition:
            while True:
                now = self.clock_ns()
                if (self.closed or self.disabled or ticket.generation != self.generation
                        or self.previous_key != ticket.key):
                    return None
                remaining = min((ticket.key.capture_ns+self.EVIDENCE_NS-now)/1e9,
                                wall_end-time.monotonic())
                if self.completed is not None and self.completed[0] == ticket:
                    _,value = self.completed;self.completed = None
                    if (remaining >= 0 and ticket.key.capture_ns <= value.started_ns
                            <= value.finished_ns <= now
                            and value.finished_ns <= ticket.key.capture_ns+self.EVIDENCE_NS):
                        self.failures = 0;self.counts['accepted'] += 1
                        return value
                    self._failure_locked('late_result')
                    return None
                present = self.running == ticket or (self.job is not None and self.job[0] == ticket)
                if not present:return None
                if remaining <= 0:
                    self.abandoned = ticket
                    if self.job is not None and self.job[0] == ticket:self.job = None
                    self._failure_locked('result_wait_expired')
                    return None
                self.condition.wait(remaining)

    def _worker(self):
        model = None
        try:
            model = self.loader()
            if self.warm is not None:self.warm(model)
            with self.condition:
                self.warming = False
                if self.closed or self.disabled:return
                self.ready = True
                self.condition.notify_all()
            while True:
                with self.condition:
                    while self.job is None and not self.closed and not self.disabled:self.condition.wait()
                    if self.closed or self.disabled:return
                    ticket,image = self.job;self.job = None;self.running = ticket
                begin = self.clock_ns()
                value = None;error = None
                try:
                    if 0 <= begin-ticket.key.capture_ns < self.EVIDENCE_NS:
                        value = self.infer(model,image)
                    else:error = 'job_started_late'
                except Exception:
                    error = 'model_error'
                finish = self.clock_ns()
                with self.condition:
                    self.last_job_ms = max(0,finish-begin)/1e6
                    current = ticket.generation == self.generation and self.abandoned != ticket
                    self.running = None
                    if current and not self.closed and not self.disabled:
                        if error:self._failure_locked(error)
                        elif not ticket.key.capture_ns <= begin <= finish <= ticket.key.capture_ns+self.EVIDENCE_NS:
                            self._failure_locked('late_result')
                        else:self.completed = (ticket,HeavyResult(ticket.key,ticket.generation,begin,finish,value))
                    else:self.counts['discarded_old_job'] += 1
                    self.condition.notify_all()
        except Exception:
            with self.condition:
                self.warming = False
                if not self.closed:self._disable_locked('model_load_error')
        finally:
            if model is not None and hasattr(model,'close'):
                try:model.close()
                except Exception:pass

    def snapshot(self):
        with self.condition:
            return dict(ready=self.ready,warming=self.warming,healthy=self.healthy,active=self.active,
                disabled=self.disabled,disable_reason=self.disable_reason,generation=self.generation,
                queued=int(self.job is not None),running=int(self.running is not None),
                last_job_ms=self.last_job_ms,counts=dict(self.counts),
                worker_alive=bool(self.thread and self.thread.is_alive()))

    def close(self, timeout=.2):
        """Bounded owner shutdown. A hung native call stays daemon-owned."""
        if not isinstance(timeout,(int,float)) or not math.isfinite(timeout) or not 0<=timeout<=1:
            raise ValueError('close timeout must be between0and1seconds')
        with self.condition:
            self.closed = True
            self._invalidate_locked('closed',clear_active=False)
            self.condition.notify_all()
        if self.thread is not None:self.thread.join(timeout)
        return self.snapshot()
