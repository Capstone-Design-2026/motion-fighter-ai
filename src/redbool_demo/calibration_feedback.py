"""Bounded diagnostics for existing calibration rules; never accepts a pose."""
from collections import Counter
import numpy as np


MESSAGES = {
    'STALE_FRAME': 'CAMERA DELAY - WAIT FOR A FRESH FRAME',
    'JOINTS_OUTSIDE': 'STAY CENTERED - SHOW ELBOWS AND WRISTS',
    'JOINTS_UNCERTAIN': 'KEEP ELBOWS AND WRISTS VISIBLE',
    'JOINT_HISTORY': 'KEEP ELBOWS AND WRISTS VISIBLE / HOLD STILL',
    'ARMS_MOVING': 'KEEP ARMS VISIBLE / HOLD STILL',
    'FISTS_LOW': 'RAISE RELAXED FISTS TOWARD YOUR CHEEKS',
    'FISTS_HIGH': 'LOWER RELAXED FISTS TOWARD YOUR CHEEKS',
    'ARMS_EXTENDED': 'RELAX A BENT GUARD - KEEP ELBOWS BENT',
    'FISTS_FAR': 'BRING FISTS CLOSER TO YOUR FACE',
    'GUARD_SHIFTED': 'HOLD THE SAME RELAXED GUARD',
    'HOLD_GUARD': 'HOLD GUARD - NO NEED TO PRESS C AGAIN',
}


def calibration_reasons(arms, joint_errors, fresh, shifted=False):
    """Mirror the existing numerical rules for explanation, not decisions."""
    reasons = []
    if not fresh: reasons.append('STALE_FRAME')
    if any('outside' in value for value in joint_errors): reasons.append('JOINTS_OUTSIDE')
    if joint_errors and 'JOINTS_OUTSIDE' not in reasons: reasons.append('JOINTS_UNCERTAIN')
    if set(arms) != {'LEFT','RIGHT'}: reasons.append('JOINT_HISTORY')
    else:
        if any(a['speed_xy'] > .8 for a in arms.values()): reasons.append('ARMS_MOVING')
        if any(a['position'][1] > .4 for a in arms.values()): reasons.append('FISTS_LOW')
        if any(a['position'][1] < -.8 for a in arms.values()): reasons.append('FISTS_HIGH')
        if any(a['angle'] >= 1.5 or a['angle3d'] is None or a['angle3d'] >= 2.4
               for a in arms.values()): reasons.append('ARMS_EXTENDED')
        if any(np.linalg.norm(a['position'][:2]) >= .9 for a in arms.values()): reasons.append('FISTS_FAR')
    if shifted: reasons.append('GUARD_SHIFTED')
    return reasons or ['HOLD_GUARD']


class CalibrationFeedback:
    def __init__(self):
        self.counts = Counter({key:0 for key in MESSAGES})
        self.window_counts = Counter({key:0 for key in MESSAGES})
        self.frames = 0
        self.started_ns = self.last_report_ns = None
        self.reason = 'HOLD_GUARD'
        self.last_ns = None

    @property
    def message(self):
        return MESSAGES[self.reason]

    def observe(self, reasons, now_ns, *, complete=False):
        if self.started_ns is None: self.started_ns = now_ns
        self.last_ns = now_ns
        self.frames += 1
        # The key set is fixed: arbitrary per-frame labels cannot grow memory.
        known = [key for key in reasons if key in MESSAGES] or ['HOLD_GUARD']
        for key in known:
            self.counts[key] += 1
            self.window_counts[key] += 1
        self.reason = known[0]
        due = (self.last_report_ns is None or complete
               or now_ns-self.last_report_ns >= 2_000_000_000)
        if not due: return None
        snapshot = self.summary()
        snapshot['window_counts'] = dict(self.window_counts)
        snapshot['complete'] = complete
        self.window_counts = Counter({key:0 for key in MESSAGES})
        self.last_report_ns = now_ns
        return snapshot

    def summary(self):
        return {'frames':self.frames,'reason_counts':dict(self.counts),'current_reason':self.reason,
                'elapsed_seconds':((self.last_ns-self.started_ns)/1e9
                    if self.last_ns is not None and self.started_ns is not None else None)}
