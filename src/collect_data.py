"""Collect labeled pose landmarks from the webcam into a raw CSV file."""

import csv
import platform
import time

try:
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
except ModuleNotFoundError as exc:
    raise SystemExit(
        f"Missing Python dependency: {exc.name}\n"
        "Run this collector with the project virtual environment:\n"
        "  source .venv/bin/activate\n"
        "  python src/collect_data.py\n"
        "or use it directly:\n"
        "  .venv/bin/python src/collect_data.py"
    ) from exc

from config import (
    POSE_MODEL_PATH,
    RAW_DATA_DIR,
    RAW_CSV_PATH,
    ACTIONS,
    CAMERA_INDEX,
    COUNTDOWN_SECONDS,
    COLLECT_SECONDS,
)


CONNECTIONS = [
    (11, 12),            # shoulders
    (11, 13), (13, 15),  # left arm
    (12, 14), (14, 16),  # right arm
    (11, 23), (12, 24),  # torso
    (23, 24),            # pelvis
    (0, 11), (0, 12),    # nose to shoulders
]


def ensure_dirs():
    """Create the raw data directory before writing samples."""
    RAW_DATA_DIR.mkdir(parents=True, exist_ok=True)


def create_csv_if_needed():
    """Create the raw landmark CSV with its feature header once."""
    if RAW_CSV_PATH.exists():
        return

    with open(RAW_CSV_PATH, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        header = []
        for i in range(33):
            header += [f"x{i}", f"y{i}", f"z{i}", f"v{i}"]
        header.append("label")
        writer.writerow(header)


def normalize_landmarks(landmarks):
    """
    Nose-centered, shoulder-width normalized pose landmarks.
    33 landmarks * (x, y, z, visibility) = 132 features.
    """
    nose = landmarks[0]
    left_shoulder = landmarks[11]
    right_shoulder = landmarks[12]

    scale = max(abs(left_shoulder.x - right_shoulder.x), 1e-6)

    row = []
    for lm in landmarks:
        row.append((lm.x - nose.x) / scale)
        row.append((lm.y - nose.y) / scale)
        row.append((lm.z - nose.z) / scale)
        row.append(lm.visibility)

    return row


def draw_skeleton(frame, landmarks, width, height):
    """Draw a compact upper-body pose overlay on the preview frame."""
    points = {}

    for idx, lm in enumerate(landmarks):
        x = int((1 - lm.x) * width)
        y = int(lm.y * height)
        points[idx] = (x, y)

    for a, b in CONNECTIONS:
        if a in points and b in points:
            cv2.line(frame, points[a], points[b], (0, 255, 0), 2)

    for point in points.values():
        cv2.circle(frame, point, 4, (0, 200, 255), -1)


def draw_text(  # pylint: disable=too-many-arguments,too-many-positional-arguments
    frame, text, pos, scale=1.0, color=(255, 255, 255), thickness=2
):
    """Draw a preview label on the OpenCV frame."""
    cv2.putText(
        frame,
        text,
        pos,
        cv2.FONT_HERSHEY_SIMPLEX,
        scale,
        color,
        thickness,
        cv2.LINE_AA,
    )


def open_camera():
    """Open the configured webcam or raise an actionable error."""
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


def main():  # pylint: disable=too-many-locals,too-many-branches,too-many-statements
    """Run the interactive data collection loop."""
    ensure_dirs()
    create_csv_if_needed()

    if not POSE_MODEL_PATH.exists():
        raise FileNotFoundError(f"Pose model not found: {POSE_MODEL_PATH}")

    base_options = python.BaseOptions(model_asset_path=str(POSE_MODEL_PATH))
    options = vision.PoseLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
    )

    cap = open_camera()

    current_action_idx = 0
    collecting = False
    collect_start_time = None
    saved_count = 0

    print("=== Motion Fighter Data Collector ===")
    print("T: start collection")
    print("N: next action")
    print("Q: quit")
    print(f"CSV: {RAW_CSV_PATH}")

    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        while True:
            ret, frame = cap.read()
            if not ret:
                break

            now = time.time()
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            result = landmarker.detect(mp_image)

            display_frame = cv2.flip(frame, 1)
            h, w, _ = display_frame.shape

            current_action = ACTIONS[current_action_idx]

            if result.pose_landmarks:
                landmarks = result.pose_landmarks[0]
                draw_skeleton(display_frame, landmarks, w, h)

                if collecting:
                    elapsed_total = now - collect_start_time

                    if elapsed_total > COUNTDOWN_SECONDS:
                        elapsed_collect = elapsed_total - COUNTDOWN_SECONDS

                        if elapsed_collect <= COLLECT_SECONDS:
                            row = normalize_landmarks(landmarks)
                            row.append(current_action)

                            with open(
                                RAW_CSV_PATH, "a", newline="", encoding="utf-8"
                            ) as f:
                                writer = csv.writer(f)
                                writer.writerow(row)

                            saved_count += 1
                        else:
                            collecting = False
                            print(f"[DONE] {current_action}: {saved_count} samples saved")
                            saved_count = 0

            draw_text(
                display_frame,
                f"ACTION: {current_action}",
                (30, 50),
                scale=1.2,
                color=(0, 255, 255),
                thickness=3,
            )

            if collecting:
                elapsed_total = now - collect_start_time

                if elapsed_total < COUNTDOWN_SECONDS:
                    remaining = COUNTDOWN_SECONDS - int(elapsed_total)
                    draw_text(
                        display_frame,
                        f"READY... {remaining}",
                        (170, 260),
                        scale=2.5,
                        color=(0, 0, 255),
                        thickness=5,
                    )
                else:
                    remaining = COLLECT_SECONDS - (elapsed_total - COUNTDOWN_SECONDS)
                    draw_text(
                        display_frame,
                        f"COLLECTING {remaining:.1f}s / {saved_count}",
                        (30, 100),
                        scale=0.9,
                        color=(0, 255, 0),
                        thickness=2,
                    )
            else:
                draw_text(
                    display_frame,
                    "T: Start | N: Next Action | Q: Quit",
                    (30, 100),
                    scale=0.7,
                    color=(255, 255, 255),
                    thickness=2,
                )

            cv2.imshow("Motion Fighter Data Collector", display_frame)

            key = cv2.waitKey(1) & 0xFF

            if key in (ord("q"), ord("Q")):
                break

            if key in (ord("n"), ord("N")) and not collecting:
                current_action_idx = (current_action_idx + 1) % len(ACTIONS)
                print(f"[ACTION] {ACTIONS[current_action_idx]}")

            if key in (ord("t"), ord("T")) and not collecting:
                collecting = True
                collect_start_time = now
                saved_count = 0
                print(f"[START] {current_action}")

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
