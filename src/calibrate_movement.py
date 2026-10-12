"""
Measure shoulder rotation scores from the webcam to calibrate movement settings.

This tool does NOT load the MLP action model. It only runs MediaPipe Pose and
records the raw shoulder rotation score for three guided poses, then recommends
config.py values from the measured distributions.

Usage:
    python src/calibrate_movement.py

Outputs:
    data/calibration/rot_log_<timestamp>.csv       per-frame raw samples
    data/calibration/report_<timestamp>.txt        summary to send to the team
"""

from datetime import datetime
from pathlib import Path
import csv
import math
import platform
import time

import numpy as np

try:
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
except ModuleNotFoundError as exc:
    raise SystemExit(
        f"Missing Python dependency: {exc.name}\n"
        "Run this tool with the project virtual environment:\n"
        "  Windows: .venv\\Scripts\\activate\n"
        "  macOS:   source .venv/bin/activate\n"
        "  python src/calibrate_movement.py"
    ) from exc

from config import (
    CAMERA_INDEX,
    DATA_DIR,
    MOVEMENT_CALIBRATION_SECONDS,
    MOVEMENT_CALIBRATION_MIN_SAMPLES,
    MOVEMENT_ENTER_THRESHOLD,
    MOVEMENT_EXIT_THRESHOLD,
    MOVEMENT_MAX_STRENGTH_DELTA,
    MOVEMENT_MIRROR,
    MOVEMENT_STABLE_FRAME_COUNT,
    MOVEMENT_VISIBILITY_THRESHOLD,
    POSE_MODEL_PATH,
)
from movement import (
    LEFT_SHOULDER_INDEX,
    RIGHT_SHOULDER_INDEX,
    ShoulderMovementDetector,
)


CALIBRATION_DIR = DATA_DIR / "calibration"

PHASE_NEUTRAL = "NEUTRAL"
PHASE_LEFT = "LEFT"
PHASE_RIGHT = "RIGHT"

# (phase name, on-screen instruction, recording seconds)
# On-screen text stays ASCII because cv2.putText cannot render Hangul.
PHASES = [
    (PHASE_NEUTRAL, "Fighting stance + swing arms (attack/block/jump)", 15.0),
    (PHASE_LEFT, "Turn LEFT shoulder forward and HOLD", 10.0),
    (PHASE_RIGHT, "Turn RIGHT shoulder forward and HOLD", 10.0),
]

PHASE_GUIDE_KO = {
    PHASE_NEUTRAL: (
        "湲곕낯 �꾪닾 �먯꽭瑜� �좎��섎㈃�� 怨듦꺽/諛⑹뼱/�먰봽 �� �숈옉�� �욎뼱 二쇱꽭��.\n"
        "  (�대룞�� �섏� 留덉꽭��. �붾쭔 ��吏곸��� �뚯쓽 �몄씠利덈� �щ뒗 �④퀎�낅땲��.)"
    ),
    PHASE_LEFT: (
        "�쇱そ �닿묠瑜� 移대찓�� 履쎌쑝濡� �대�怨� �쇱そ�쇰줈 �대룞�� �먯꽭瑜� �좎��섏꽭��.\n"
        "  (�붾떎媛붾떎 諛섎났�섏� 留먭퀬, �뚮┛ �먯꽭 洹몃�濡� 踰꾪떚�몄슂.)"
    ),
    PHASE_RIGHT: (
        "�ㅻⅨ履� �닿묠瑜� 移대찓�� 履쎌쑝濡� �대�怨� �ㅻⅨ履쎌쑝濡� �대룞�� �먯꽭瑜� �좎��섏꽭��.\n"
        "  (�붾떎媛붾떎 諛섎났�섏� 留먭퀬, �뚮┛ �먯꽭 洹몃�濡� 踰꾪떚�몄슂.)"
    ),
}

COUNTDOWN_SECONDS = 3.0
SETTLE_SECONDS = 1.5
MIN_SAMPLES_PER_PHASE = 30

STATE_COUNTDOWN = "COUNTDOWN"
STATE_SETTLE = "SETTLE"
STATE_RECORDING = "RECORDING"

CONNECTIONS = [
    (11, 12),
    (11, 13), (13, 15),
    (12, 14), (14, 16),
    (11, 23), (12, 24),
    (23, 24),
    (0, 11), (0, 12),
]

CSV_FIELDNAMES = [
    "schema_version", "frame_id", "capture_monotonic_ns", "stage",
    "pose_present", "score_valid", "calibration_state", "neutral_baseline",
    "corrected_rotation_score", "calibration_mad",
    "phase",
    "elapsed",
    "rotation_score_raw",
    "left_visibility",
    "right_visibility",
    "shoulders_visible",
    "live_raw_movement",
    "live_final_movement",
]


# ---------------------------------------------------------------------------
# Pure calculation helpers (no camera required, unit-test friendly)
# ---------------------------------------------------------------------------


def raw_rotation_score(left_shoulder, right_shoulder) -> float:
    """
    Return the un-mirrored shoulder rotation score.

    Mirroring is deliberately NOT applied here so the recommended
    MOVEMENT_MIRROR value does not depend on the current config setting.
    Positive means the right shoulder is closer to the camera.
    """
    shoulder_width = math.hypot(
        left_shoulder.x - right_shoulder.x,
        left_shoulder.y - right_shoulder.y,
    )
    return float((left_shoulder.z - right_shoulder.z) / max(shoulder_width, 1e-6))


def describe_samples(scores: list[float]) -> dict:
    """Return summary statistics for one phase of signed rotation scores."""
    if not scores:
        return {
            "count": 0,
            "signed_mean": 0.0,
            "abs_mean": 0.0,
            "abs_p10": 0.0,
            "abs_p50": 0.0,
            "abs_p95": 0.0,
            "abs_p99": 0.0,
            "abs_max": 0.0,
        }

    signed = np.asarray(scores, dtype=np.float64)
    magnitudes = np.abs(signed)

    return {
        "count": int(signed.size),
        "signed_mean": float(np.mean(signed)),
        "abs_mean": float(np.mean(magnitudes)),
        "abs_p10": float(np.percentile(magnitudes, 10)),
        "abs_p50": float(np.percentile(magnitudes, 50)),
        "abs_p95": float(np.percentile(magnitudes, 95)),
        "abs_p99": float(np.percentile(magnitudes, 99)),
        "abs_max": float(np.max(magnitudes)),
    }


def recommend_mirror(left_stats: dict, right_stats: dict) -> tuple[bool, str]:
    """
    Decide MOVEMENT_MIRROR from the sign observed during the guided phases.

    Without mirroring a positive score means RIGHT, so the LEFT phase should
    produce a negative mean and the RIGHT phase a positive mean.
    """
    left_mean = left_stats["signed_mean"]
    right_mean = right_stats["signed_mean"]

    if left_stats["count"] == 0 or right_stats["count"] == 0:
        return MOVEMENT_MIRROR, "�섑뵆�� 遺�議깊빐 �먯젙 遺덇�. �꾩옱 �ㅼ젙�� �좎��섏꽭��."

    if left_mean < 0 < right_mean:
        return False, "LEFT �뚯닔 / RIGHT �묒닔 �� 遺��멸� 肄붾뱶 湲곕��� �쇱튂�⑸땲��."

    if right_mean < 0 < left_mean:
        return True, "LEFT �묒닔 / RIGHT �뚯닔 �� 醫뚯슦媛� �ㅼ쭛�� �덉뒿�덈떎."

    return (
        MOVEMENT_MIRROR,
        "LEFT/RIGHT 遺��멸� 媛숈� 諛⑺뼢�낅땲��. �대룞 �먯꽭媛� 異⑸텇�� �ъ� �딆븯嫄곕굹 "
        "移대찓�쇨� �덈Т 痢〓㈃�� �� �덉뒿�덈떎. �ㅼ떆 痢≪젙�섏꽭��.",
    )


def recommend_thresholds(neutral_stats: dict, motion_stats: dict) -> dict:
    """
    Derive enter/exit thresholds from the neutral noise ceiling and the
    sustained level of intentional movement.
    """
    noise_ceiling = neutral_stats["abs_p99"]
    motion_floor = motion_stats["abs_p10"]
    gap = motion_floor - noise_ceiling

    if gap <= 0:
        # Measurement is unusable. Fall back to the current config so nobody
        # copies a number that was derived from overlapping distributions.
        return {
            "separable": False,
            "noise_ceiling": noise_ceiling,
            "motion_floor": motion_floor,
            "enter": MOVEMENT_ENTER_THRESHOLD,
            "exit": MOVEMENT_EXIT_THRESHOLD,
            "max_strength": MOVEMENT_MAX_STRENGTH_DELTA,
        }

    enter = noise_ceiling + 0.5 * gap
    exit_threshold = noise_ceiling + 0.15 * gap

    return {
        "separable": True,
        "noise_ceiling": noise_ceiling,
        "motion_floor": motion_floor,
        "enter": round(enter, 3),
        "exit": round(exit_threshold, 3),
        "max_strength": round(max(motion_stats["abs_p95"], 1e-3), 3),
    }


def recommend_stable_frame_count(fps: float) -> int:
    """Target roughly 200 ms of stabilization delay at the measured frame rate."""
    if fps <= 0:
        return MOVEMENT_STABLE_FRAME_COUNT

    return int(min(max(round(fps * 0.2), 2), 8))


def build_report(samples_by_phase: dict, fps: float, invisible_ratio: float,
                 visibility_all_zero: bool) -> str:
    """Build the plain-text summary that gets sent to the team."""
    neutral_stats = describe_samples(samples_by_phase.get(PHASE_NEUTRAL, []))
    left_stats = describe_samples(samples_by_phase.get(PHASE_LEFT, []))
    right_stats = describe_samples(samples_by_phase.get(PHASE_RIGHT, []))
    motion_stats = describe_samples(
        samples_by_phase.get(PHASE_LEFT, []) + samples_by_phase.get(PHASE_RIGHT, [])
    )

    mirror, mirror_reason = recommend_mirror(left_stats, right_stats)
    thresholds = recommend_thresholds(neutral_stats, motion_stats)
    stable_frames = recommend_stable_frame_count(fps)

    lines = []
    lines.append("=" * 68)
    lines.append(" Movement Calibration Report")
    lines.append("=" * 68)
    lines.append(f"痢≪젙 �쒓컖 : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append(f"OS        : {platform.system()} {platform.release()}")
    lines.append(f"移대찓��    : index {CAMERA_INDEX}")
    lines.append(f"痢≪젙 FPS  : {fps:.1f}")
    lines.append("")

    lines.append("[ �쇱꽌 �곹깭 ]")
    if visibility_all_zero:
        lines.append("  !! visibility 媛믪씠 �꾨� 0.0 �낅땲��.")
        lines.append("     �� MediaPipe 踰꾩쟾�� visibility瑜� 梨꾩슦吏� �딆뒿�덈떎.")
        lines.append("     �꾩옱 �ㅼ젙�쇰줈�� �대룞�� ��긽 NEUTRAL�� �⑸땲��.")
        lines.append(
            f"     ����: config.py�� MOVEMENT_VISIBILITY_THRESHOLD瑜� "
            f"{MOVEMENT_VISIBILITY_THRESHOLD} �� 0.0 �쇰줈 �대젮�� �⑸땲��."
        )
    else:
        lines.append(f"  �닿묠 誘몄씤�� �꾨젅�� 鍮꾩쑉 : {invisible_ratio * 100:.1f}%")
        if invisible_ratio > 0.10:
            lines.append("  !! 10%瑜� �섏뒿�덈떎. 議곕챸�대굹 移대찓�� �꾩튂瑜� 議곗젙�섏꽭��.")
        else:
            lines.append("  �뺤긽 踰붿쐞�낅땲��.")
    lines.append("")

    lines.append("[ �④퀎蹂� rotation_score �듦퀎 ]")
    lines.append(
        f"  {'phase':<8}{'n':>6}{'signed_mean':>13}{'|p10|':>9}"
        f"{'|p50|':>9}{'|p95|':>9}{'|p99|':>9}{'|max|':>9}"
    )
    for name, stats in (
        (PHASE_NEUTRAL, neutral_stats),
        (PHASE_LEFT, left_stats),
        (PHASE_RIGHT, right_stats),
    ):
        lines.append(
            f"  {name:<8}{stats['count']:>6}{stats['signed_mean']:>13.3f}"
            f"{stats['abs_p10']:>9.3f}{stats['abs_p50']:>9.3f}"
            f"{stats['abs_p95']:>9.3f}{stats['abs_p99']:>9.3f}"
            f"{stats['abs_max']:>9.3f}"
        )
    lines.append("")

    lines.append("[ 醫뚯슦 諛섏쟾 �먯젙 ]")
    lines.append(f"  MOVEMENT_MIRROR = {mirror}")
    lines.append(f"  洹쇨굅: {mirror_reason}")
    lines.append("")

    lines.append("[ 遺꾨━�� ]")
    lines.append(f"  以묐┰ �몄씠利� �곹븳 |p99| = {thresholds['noise_ceiling']:.3f}")
    lines.append(f"  �대룞 �좎� �섑븳   |p10| = {thresholds['motion_floor']:.3f}")
    if thresholds["separable"]:
        ratio = thresholds["motion_floor"] / max(thresholds["noise_ceiling"], 1e-6)
        verdict = "�묓샇" if ratio >= 2.0 else "二쇱쓽 (�ъ쑀媛� 醫곸뒿�덈떎)"
        lines.append(f"  鍮꾩쑉 = {ratio:.2f}x �� {verdict}")
    else:
        lines.append("  !! 以묐┰ �몄씠利덉� �ㅼ젣 �대룞�� 寃뱀묩�덈떎.")
        lines.append("     threshold濡쒕뒗 �닿껐�섏� �딆뒿�덈떎. �꾨옒 以� �섎굹媛� �꾩슂�⑸땲��:")
        lines.append("     - �대룞 �숈옉�� �� �ш쾶 �섎룄濡� �숈옉 洹쒖튃 蹂�寃�")
        lines.append("     - 移대찓�쇰� �� �뺣㈃/媛�源뚯씠 諛곗튂")
        lines.append("     - �ъ륫�� (�대룞 �먯꽭瑜� 異⑸텇�� �ш쾶 �좎�)")
        lines.append("     異붿쿇媛믪� �좊ː�� �� �놁쑝�� �꾩옱 �ㅼ젙�� 洹몃�濡� �먯꽭��.")
    lines.append("")

    if thresholds["separable"]:
        lines.append("[ 沅뚯옣 config.py 媛� ]")
    else:
        lines.append("[ 沅뚯옣媛� �놁쓬 - �꾨옒�� �꾩옱 �ㅼ젙 洹몃�濡쒖엯�덈떎. �ъ륫�� �꾩슂 ]")
    lines.append(
        f"  MOVEMENT_ENTER_THRESHOLD    = {thresholds['enter']:<8}"
        f"# �꾩옱 {MOVEMENT_ENTER_THRESHOLD}"
    )
    lines.append(
        f"  MOVEMENT_EXIT_THRESHOLD     = {thresholds['exit']:<8}"
        f"# �꾩옱 {MOVEMENT_EXIT_THRESHOLD}"
    )
    lines.append(
        f"  MOVEMENT_MAX_STRENGTH_DELTA = {thresholds['max_strength']:<8}"
        f"# �꾩옱 {MOVEMENT_MAX_STRENGTH_DELTA}"
    )
    lines.append(
        f"  MOVEMENT_STABLE_FRAME_COUNT = {stable_frames:<8}"
        f"# �꾩옱 {MOVEMENT_STABLE_FRAME_COUNT}"
    )
    lines.append(
        f"  MOVEMENT_MIRROR             = {str(mirror):<8}"
        f"# �꾩옱 {MOVEMENT_MIRROR}"
    )
    lines.append("")

    lines.append("[ 吏곸젒 梨꾩썙�� 蹂대궪 寃� ]")
    lines.append("  �뱀틺 紐⑤뜽        : ")
    lines.append("  移대찓�쇱��� 嫄곕━  : ___ cm")
    lines.append("  移대찓�� �믪씠      : �덈넂�� / 媛��대넂�� / 湲고�")
    lines.append("  議곕챸             : 諛앹쓬 / 蹂댄넻 / �대몢��")
    lines.append("  �숈떆 �숈옉 �뚯뒪�� : ATTACK+RIGHT ( ) / BLOCK+LEFT ( ) / JUMP+RIGHT ( )")
    lines.append("  �뱀씠�ы빆         : ")
    lines.append("=" * 68)

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Camera / rendering
# ---------------------------------------------------------------------------


def open_camera():
    """Open the configured webcam on Windows or macOS."""
    if platform.system() == "Windows":
        cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
        if cap.isOpened():
            return cap
        cap.release()

    cap = cv2.VideoCapture(CAMERA_INDEX)
    if cap.isOpened():
        return cap

    cap.release()
    message = f"Cannot open webcam at camera index {CAMERA_INDEX}."
    if platform.system() == "Darwin":
        message += (
            "\nAllow camera access for the app that starts Python "
            "(VS Code or Terminal) in System Settings > Privacy & Security > Camera."
        )
    raise RuntimeError(message)


def create_pose_landmarker():
    """Create the MediaPipe PoseLandmarker used for per-frame detection."""
    base_options = python.BaseOptions(model_asset_path=str(POSE_MODEL_PATH))
    options = vision.PoseLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)


def draw_skeleton(frame, landmarks, width, height):
    """Draw a compact upper-body pose overlay on the preview frame."""
    points = {}
    for idx, landmark in enumerate(landmarks):
        points[idx] = (int((1 - landmark.x) * width), int(landmark.y * height))

    for start, end in CONNECTIONS:
        if start in points and end in points:
            cv2.line(frame, points[start], points[end], (0, 255, 0), 2)

    for point in points.values():
        cv2.circle(frame, point, 4, (0, 200, 255), -1)


def draw_text(  # pylint: disable=too-many-arguments,too-many-positional-arguments
    frame, text, pos, scale=0.8, color=(255, 255, 255), thickness=2
):
    """Draw an ASCII label on the OpenCV frame."""
    cv2.putText(
        frame, text, pos, cv2.FONT_HERSHEY_SIMPLEX, scale, color, thickness, cv2.LINE_AA
    )


def draw_progress_bar(frame, ratio, width):
    """Draw a horizontal progress bar for the current recording phase."""
    left, right = 30, width - 30
    top, bottom = 12, 26
    filled = int(left + (right - left) * min(max(ratio, 0.0), 1.0))
    cv2.rectangle(frame, (left, top), (right, bottom), (80, 80, 80), 1)
    cv2.rectangle(frame, (left, top), (filled, bottom), (0, 200, 255), -1)


def draw_overlay(  # pylint: disable=too-many-arguments,too-many-positional-arguments
    frame, phase_index, phase_name, instruction, state, remaining, score, sample_count
):
    """Draw the guided calibration HUD."""
    height, width, _ = frame.shape

    draw_text(
        frame,
        f"STEP {phase_index + 1}/{len(PHASES)}: {phase_name}",
        (30, 65),
        scale=1.0,
        color=(0, 255, 255),
        thickness=3,
    )
    draw_text(frame, instruction, (30, 100), scale=0.6, color=(255, 255, 255))

    if state == STATE_COUNTDOWN:
        draw_text(
            frame,
            f"GET READY {int(remaining) + 1}",
            (150, 260),
            scale=2.2,
            color=(0, 0, 255),
            thickness=5,
        )
    elif state == STATE_SETTLE:
        draw_text(
            frame, "HOLD THE POSE...", (30, 145), scale=0.9, color=(0, 165, 255),
            thickness=2,
        )
    else:
        draw_text(
            frame,
            f"RECORDING {remaining:4.1f}s   n={sample_count}",
            (30, 145),
            scale=0.9,
            color=(0, 255, 0),
            thickness=2,
        )

    draw_text(
        frame,
        f"ROT: {score:+.3f}" if score is not None else "ROT: (no pose)",
        (30, 190),
        scale=0.9,
        color=(255, 255, 255),
    )
    draw_text(
        frame,
        "R: redo this step    Q: quit",
        (30, height - 25),
        scale=0.6,
        color=(200, 200, 200),
        thickness=1,
    )
    return width


# ---------------------------------------------------------------------------
# Main loop
# ---------------------------------------------------------------------------


def print_phase_intro(phase_index, phase_name, seconds):
    """Print the Korean instruction for a phase to the console."""
    print()
    print(f"--- [{phase_index + 1}/{len(PHASES)}] {phase_name} ({seconds:.0f}珥�) ---")
    print(f"  {PHASE_GUIDE_KO[phase_name]}")


def save_outputs(rows, report_text):
    """Write the per-frame CSV and the summary report, returning both paths."""
    CALIBRATION_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    csv_path = CALIBRATION_DIR / f"rot_log_{stamp}.csv"
    with open(csv_path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=CSV_FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)

    report_path = CALIBRATION_DIR / f"report_{stamp}.txt"
    report_path.write_text(report_text, encoding="utf-8")

    return csv_path, report_path


def build_observation_row(phase, stage, frame_id, capture_ns, elapsed,
                          pose_present, left_visibility, right_visibility, result):
    """Preserve every observed frame; unavailable scores are empty CSV cells."""
    return {
        "schema_version": 2, "frame_id": frame_id,
        "capture_monotonic_ns": capture_ns, "stage": stage,
        "phase": phase, "elapsed": round(elapsed, 3),
        "pose_present": int(pose_present), "score_valid": int(result.score_valid),
        "rotation_score_raw": result.raw_rotation_score if result.score_valid else None,
        "left_visibility": left_visibility, "right_visibility": right_visibility,
        "shoulders_visible": int(pose_present and
            left_visibility >= MOVEMENT_VISIBILITY_THRESHOLD and
            right_visibility >= MOVEMENT_VISIBILITY_THRESHOLD),
        "live_raw_movement": result.raw_movement,
        "live_final_movement": result.final_movement,
        "calibration_state": result.calibration_state,
        "neutral_baseline": result.neutral_baseline,
        "corrected_rotation_score": result.corrected_rotation_score,
        "calibration_mad": result.calibration_mad,
    }


def create_live_detector() -> ShoulderMovementDetector:
    """Create a detector using the current config, for the on-screen preview."""
    return ShoulderMovementDetector(
        visibility_threshold=MOVEMENT_VISIBILITY_THRESHOLD,
        enter_threshold=MOVEMENT_ENTER_THRESHOLD,
        exit_threshold=MOVEMENT_EXIT_THRESHOLD,
        stable_frame_count=MOVEMENT_STABLE_FRAME_COUNT,
        mirror=MOVEMENT_MIRROR,
        max_strength_delta=MOVEMENT_MAX_STRENGTH_DELTA,
        calibration_seconds=MOVEMENT_CALIBRATION_SECONDS,
        calibration_min_samples=MOVEMENT_CALIBRATION_MIN_SAMPLES,
    )


def main():  # pylint: disable=too-many-locals,too-many-branches,too-many-statements
    """Run the guided calibration session."""
    if not POSE_MODEL_PATH.exists():
        raise SystemExit(
            f"Pose model not found: {POSE_MODEL_PATH}\n"
            "Download pose_landmarker.task into the project root first. "
            "See README.md > MediaPipe Model Download."
        )

    detector = create_live_detector()
    cap = open_camera()

    samples_by_phase = {name: [] for name, _, _ in PHASES}
    rows = []
    visibility_values = []
    invisible_frames = 0
    total_observed_frames = 0
    frame_count = 0

    phase_index = 0
    state = STATE_COUNTDOWN
    phase_start = time.monotonic()
    session_start = time.monotonic()
    completed = False

    print("=== Motion Fighter Movement Calibration ===")
    print("�붾㈃�� �덈궡瑜� �곕씪 3�④퀎瑜� 吏꾪뻾�⑸땲��. 珥� �� 45珥덉엯�덈떎.")
    print("  R: �꾩옱 �④퀎 �ㅼ떆 �섍린 / Q: 以묐떒")
    print_phase_intro(phase_index, PHASES[phase_index][0], PHASES[phase_index][2])

    try:
        with create_pose_landmarker() as landmarker:
            while True:
                ret, frame = cap.read()
                if not ret:
                    break

                capture_monotonic_ns = time.monotonic_ns()
                frame_count += 1
                phase_name, instruction, record_seconds = PHASES[phase_index]

                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = landmarker.detect(mp_image)

                display_frame = cv2.flip(frame, 1)
                height, width, _ = display_frame.shape

                score = None
                live_raw = "NONE"
                live_final = "NONE"
                left_visibility = 0.0
                right_visibility = 0.0
                shoulders_visible = False

                if result.pose_landmarks:
                    landmarks = result.pose_landmarks[0]
                    draw_skeleton(display_frame, landmarks, width, height)

                    left_shoulder = landmarks[LEFT_SHOULDER_INDEX]
                    right_shoulder = landmarks[RIGHT_SHOULDER_INDEX]
                    left_visibility = float(getattr(left_shoulder, "visibility", 0.0))
                    right_visibility = float(getattr(right_shoulder, "visibility", 0.0))
                    visibility_values.extend([left_visibility, right_visibility])

                    shoulders_visible = (
                        left_visibility >= MOVEMENT_VISIBILITY_THRESHOLD
                        and right_visibility >= MOVEMENT_VISIBILITY_THRESHOLD
                    )
                    score = raw_rotation_score(left_shoulder, right_shoulder)

                    live_result = detector.update(landmarks)
                    live_raw = live_result.raw_movement
                    live_final = live_result.final_movement

                else:
                    live_result = detector.reset()

                total_observed_frames += 1
                if not live_result.score_valid:
                    invisible_frames += 1

                elapsed = time.monotonic() - phase_start
                rows.append(build_observation_row(
                    phase_name, state, frame_count, capture_monotonic_ns, elapsed,
                    bool(result.pose_landmarks), left_visibility, right_visibility,
                    live_result,
                ))

                if state == STATE_COUNTDOWN:
                    remaining = COUNTDOWN_SECONDS - elapsed
                    if remaining <= 0:
                        state = STATE_SETTLE
                        phase_start = time.monotonic()
                        remaining = SETTLE_SECONDS
                elif state == STATE_SETTLE:
                    remaining = SETTLE_SECONDS - elapsed
                    if remaining <= 0:
                        state = STATE_RECORDING
                        phase_start = time.monotonic()
                        remaining = record_seconds
                else:
                    remaining = record_seconds - elapsed

                    if live_result.score_valid:
                        samples_by_phase[phase_name].append(score)

                    if remaining <= 0:
                        collected = len(samples_by_phase[phase_name])
                        print(f"  [�꾨즺] {phase_name}: {collected} �섑뵆")
                        if collected < MIN_SAMPLES_PER_PHASE:
                            print(
                                f"  [寃쎄퀬] �섑뵆�� {collected}媛쒕퓧�낅땲��. "
                                "�ъ쫰媛� �≫엳吏� �딆븯�� �� �덉뒿�덈떎."
                            )

                        phase_index += 1
                        if phase_index >= len(PHASES):
                            completed = True
                            break

                        state = STATE_COUNTDOWN
                        phase_start = time.monotonic()
                        detector.reset()
                        print_phase_intro(
                            phase_index, PHASES[phase_index][0], PHASES[phase_index][2]
                        )
                        continue

                progress = 0.0
                if state == STATE_RECORDING and record_seconds > 0:
                    progress = 1 - (remaining / record_seconds)
                draw_progress_bar(display_frame, progress, width)
                draw_overlay(
                    display_frame,
                    phase_index,
                    phase_name,
                    instruction,
                    state,
                    max(remaining, 0.0),
                    score,
                    len(samples_by_phase[phase_name]),
                )

                cv2.imshow("Movement Calibration", display_frame)

                key = cv2.waitKey(1) & 0xFF
                if key in (ord("q"), ord("Q")):
                    print("\n[以묐떒] �ъ슜�먭� Q瑜� �뚮��듬땲��.")
                    break
                if key in (ord("r"), ord("R")):
                    print(f"  [�ㅼ떆] {phase_name} �④퀎瑜� 泥섏쓬遺��� 吏꾪뻾�⑸땲��.")
                    samples_by_phase[phase_name].clear()
                    rows = [row for row in rows if row["phase"] != phase_name]
                    state = STATE_COUNTDOWN
                    phase_start = time.monotonic()
                    detector.reset()
    finally:
        cap.release()
        cv2.destroyAllWindows()

    if not completed:
        print("\n痢≪젙�� �꾨즺�섏� �딆븘 由ы룷�몃� 留뚮뱾吏� �딆븯�듬땲��. �ㅼ떆 �ㅽ뻾�섏꽭��.")
        return

    duration = max(time.monotonic() - session_start, 1e-6)
    fps = frame_count / duration
    invisible_ratio = invisible_frames / max(total_observed_frames, 1)
    visibility_all_zero = bool(visibility_values) and max(visibility_values) == 0.0

    report_text = build_report(samples_by_phase, fps, invisible_ratio, visibility_all_zero)
    csv_path, report_path = save_outputs(rows, report_text)

    print()
    print(report_text)
    print()
    print(f"CSV    : {csv_path}")
    print(f"由ы룷�� : {report_path}")
    print("由ы룷�� �뚯씪 �댁슜�� 洹몃�濡� 蹂듭궗�댁꽌 ���� 蹂대궡�몄슂. CSV�� 泥⑤��섎㈃ �⑸땲��.")


if __name__ == "__main__":
    main()