"""Recover a forward punch when its wrist projects inside the torso."""
from collections import deque
from math import hypot, isfinite
from statistics import median


def joint_distance(a, index, b, second_index):
    return hypot(a[index] - b[second_index], a[index+1] - b[second_index+1])


class OverlapPunch:
    """Keep a short, translation-independent history of the player's two arms."""

    def __init__(self):
        self.history = deque(maxlen=120)
        self.last_frame = -1
        self.support = [None, None]
        self.proofs = [deque(maxlen=32), deque(maxlen=32)]
        self.streak = [0, 0]
        self.locked = [False, False]
        self.repaired_side = None
        self.repair_origin = [None, None]
        self.guard_corrections = [{}, {}]
        self.repair_proofs = [deque(maxlen=32), deque(maxlen=32)]
        self.repair_streak = [0, 0]
        self.repair_locked = [False, False]

    def _close_guard_excursions(self, ns, arm):
        # A tracking error can move just the wrist below a stationary elbow for
        # one or two frames. Only after it returns do we repair its *past* guard
        # anchor. The original history, current stroke and R5 stay untouched.
        h = self.history
        for side in range(2):
            edits = self.guard_corrections[side]
            if self.repair_origin[side] is not None and ns - self.repair_origin[side] > 400_000_000:
                edits.clear()
                self.repair_origin[side] = None
            w, e = 8 + 2 * side, 4 + 2 * side
            for i in range(len(h) - 2, 0, -1):
                before, span, pose = h[i][:3]
                if ns - before > 150_000_000:
                    break
                first = h[i + 1]
                if first[0] in edits or first[2][w + 1] - pose[w + 1] < .65 * span:
                    continue
                if joint_distance(arm, w, pose, w) > .22 * span or joint_distance(arm, e, pose, e) > .12 * span:
                    continue
                if any(joint_distance(h[j][2], e, pose, e) > .12 * span for j in range(i + 1, len(h))):
                    continue
                for stamp in tuple(edits):
                    if ns - stamp > 460_000_000:
                        del edits[stamp]
                for j in range(i + 1, len(h)):
                    stamp = h[j][0]
                    mix = (stamp - before) / (ns - before)
                    edits[stamp] = (pose[w] + mix * (arm[w] - pose[w]),
                                    pose[w + 1] + mix * (arm[w + 1] - pose[w + 1]))
                self.repair_origin[side] = first[0]
                break

    def _corrected_guard(self, side, history):
        w = 8 + 2 * side
        edits = self.guard_corrections[side]
        revised = []
        for row in history:
            if row[0] in edits:
                pts = row[2][:w] + edits[row[0]] + row[2][w+2:]
                revised.append(row[:2] + (pts,) + row[3:])
            else:
                revised.append(row)
        return revised

    def observe(self, obs, gates):
        self.repaired_side = None
        ns = obs.capture_ns
        if self.history and (obs.frame_id <= self.last_frame
                or not 0 < ns - self.history[-1][0] <= 100_000_000):
            self.__init__()
        self.last_frame = obs.frame_id
        points, visibility = obs.landmarks, obs.visibility
        required = (0, 7, 8, 11, 12, 13, 14, 15, 16)
        if (not obs.pose_present or not points or len(points) != 33 or not visibility
                or len(visibility) != 33 or obs.width <= 0 or obs.height <= 0
                or any(not visibility[k] >= .55 or len(points[k]) < 3
                       or not (0 <= points[k][0] <= 1 and 0 <= points[k][1] <= 1
                               and isfinite(points[k][2])) for k in required)):
            self.__init__()
            return None
        aspect = obs.height / obs.width
        mid_x = (points[11][0] + points[12][0]) / 2
        mid_y = (points[11][1] + points[12][1]) / 2
        arm = tuple(value for x, y, _ in points[11:17]
                    for value in (x - mid_x, (y - mid_y) * aspect))
        width = hypot(arm[0] - arm[2], arm[1] - arm[3])
        if width < .05:
            self.__init__()
            return None
        depths = tuple(points[k][2] for k in (11, 12, 13, 14))
        ear_width = max(abs(points[7][0] - points[8][0]), .005)
        face_turn = abs(points[0][0] - (points[7][0] + points[8][0]) / 2) / ear_width
        self._close_guard_excursions(ns, arm)
        self.history.append((ns, width, arm, depths, face_turn, mid_x))
        while self.history and ns - self.history[0][0] > 460_000_000:
            self.history.popleft()

        for side, (_, guarded) in enumerate(gates):
            if guarded:
                self.support[side] = ns
        if not any(available for available, _ in gates):
            self.streak = [0, 0]
            self.repair_streak = [0, 0]
            for proof in (*self.proofs, *self.repair_proofs):
                proof.clear()
            return None
        history = [r for r in self.history if 180_000_000 <= ns - r[0] <= 400_000_000]
        if len(history) < 3 or history[-1][0] - history[0][0] < 100_000_000:
            return None
        anchors = (history[0], history[len(history) // 2], history[-1])
        span = median(r[1] for r in anchors)
        home = tuple(map(median, zip(*(r[2] for r in anchors))))
        sign = 1 if home[0] > home[2] else -1
        if not (.72 <= width / span <= 1.25 and (arm[0] - arm[2]) * sign >= .60 * span):
            self.streak = [0, 0]
            self.repair_streak = [0, 0]
            for proof in (*self.proofs, *self.repair_proofs):
                proof.clear()
            return None
        proposals = {}
        for side, (available, _) in enumerate(gates):
            if self._qualifies(side, available, ns, arm, mid_x, depths, history, anchors, home, span, sign, False):
                proposals[side] = False
            origin = self.repair_origin[side]
            if origin is None or not 180_000_000 <= ns - origin <= 400_000_000:
                self.repair_streak[side] = 0
                self.repair_proofs[side].clear()
                self.repair_locked[side] = False
                continue
            repaired = self._corrected_guard(side, history)
            sample = (repaired[0], repaired[len(repaired) // 2], repaired[-1])
            corrected_home = tuple(map(median, zip(*(r[2] for r in sample))))
            if self._qualifies(side, available, ns, arm, mid_x, depths, repaired, sample,
                               corrected_home, span, sign, True):
                proposals.setdefault(side, True)
        if len(proposals) == 1:
            side, repaired = next(iter(proposals.items()))
            label = ('LEFT', 'RIGHT')[side]
            if repaired:
                self.repaired_side = label
            return label
        return None

    def _qualifies(self, side, available, ns, arm, mid_x, depths, history, anchors, home, span, sign, repaired):
        other = 1 - side
        shoulder, elbow, wrist = side * 2, 4 + side * 2, 8 + side * 2
        opposite_wrist = 8 + other * 2
        orient = sign if side == 0 else -sign
        inward = orient * (home[wrist] - arm[wrist]) / span
        other_travel = hypot(arm[opposite_wrist] - home[opposite_wrist],
                             arm[opposite_wrist + 1] - home[opposite_wrist + 1]) / span
        available = available and self.support[side] is not None and ns - self.support[side] <= 100_000_000
        proof = self.repair_proofs[side] if repaired else self.proofs[side]
        streak = self.repair_streak if repaired else self.streak
        locked = self.repair_locked if repaired else self.locked
        while proof and ns - proof[0] > 120_000_000:
            proof.popleft()
        if not available or inward < .04:
            proof.clear()
        if not available:
            streak[side] = 0
            return False
        if inward <= .055 and other_travel <= .18:
            locked[side] = False
        if inward >= .08:
            shoulder_lead = depths[other] - depths[side] - median(r[3][other] - r[3][side] for r in history)
            elbow_lead = depths[other+2] - depths[side+2] - median(r[3][other+2] - r[3][side+2] for r in history)
            if shoulder_lead >= .12 * span and elbow_lead >= .18 * span:
                proof.append(ns)
        if locked[side] or len(proof) < 2 or inward < .16 or other_travel > .16:
            streak[side] = 0
            return False
        previous = self.history[-2][2]
        other_inward = -orient * (home[opposite_wrist] - arm[opposite_wrist]) / span
        hand_delta_y = (arm[wrist+1] - home[wrist+1]) / span
        upper_shorten = (joint_distance(home, elbow, home, shoulder)
                         - joint_distance(arm, elbow, arm, shoulder)) / span
        elbow_lift = ((home[elbow+1] - home[shoulder+1]) - (arm[elbow+1] - arm[shoulder+1])) / span
        tilted = orient * ((home[wrist] - home[elbow]) - (arm[wrist] - arm[elbow])) / span
        # The normal rescue is for fists at the same height. A verified
        # historical excursion also permits a forward fist slightly *above*
        # the guarding one; do not extend the downward/lowering boundary.
        wrist_level = (arm[wrist+1] - arm[opposite_wrist+1]) / span
        similar_height = (-.35 if repaired else -.20) <= wrist_level <= .20
        consistent = (
            inward - max(0, other_inward) >= .10 and tilted >= .08
            # A drop below the supporting fist belongs to the lowering path.
            # A changing lean/roll moves the projected hands even while guarding.
            # A lean that was already held has little translation or tilt change.
            and similar_height
            and abs(mid_x - median(r[5] for r in anchors)) <= .10 * span
            and abs((arm[1] - arm[3]) - (home[1] - home[3])) <= .12 * span
            and (arm[wrist] - arm[opposite_wrist]) * orient >= .17 * span
            and -.50 <= hand_delta_y <= .22 and abs(hand_delta_y) <= max(.32, inward * 1.5)
            and abs(arm[wrist]) <= .63 * span
            and abs(home[wrist] - home[shoulder]) <= .61 * span
            and -.75 <= (home[wrist+1] - home[shoulder+1]) / span <= .56
            and (upper_shorten >= .075 or elbow_lift >= .10)
            and joint_distance(arm, wrist, home, opposite_wrist) - joint_distance(arm, wrist, home, wrist) >= .06 * span
            and joint_distance(arm, wrist, previous, opposite_wrist) - joint_distance(arm, wrist, previous, wrist) >= .12 * span
            and median(r[4] for r in history) <= .38
            and median(joint_distance(r[2], wrist, home, wrist) for r in history) <= .095 * span)
        streak[side] = streak[side] + 1 if consistent else 0
        if streak[side] == 2:
            locked[side] = True
            # The ordinary detector owns the shoulder lanes. Only recover
            # an ambiguous event in the middle half of the torso when the
            # wrists have actually converged from their guard positions.
            central = (abs(arm[wrist]) <= .25 * span
                       and abs(arm[wrist] - arm[opposite_wrist]) <= .65 * span)
            # Use the same upward-dominant handoff as the normal raise path,
            # relative to this observed guard. Do not retry the same stroke.
            corrected_lane = (repaired and abs(arm[wrist]) <= .40 * span
                              and abs(arm[wrist] - arm[opposite_wrist]) <= .80 * span)
            return (central or corrected_lane) and not (-hand_delta_y >= .18 and -hand_delta_y > inward * 1.1)
        return False
