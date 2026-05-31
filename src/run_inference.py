"""Run real-time motion inference from webcam pose landmarks."""

from collections import Counter, deque
import platform
import time

import numpy as np

try:
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    from tensorflow.keras.models import load_model
except ModuleNotFoundError as exc:
    raise SystemExit(
        f"Missing Python dependency: {exc.name}\n"
        "Run inference with the project virtual environment:\n"
        "  source .venv/bin/activate\n"
        "  python src/run_inference.py\n"
        "or use it directly:\n"
        "  .venv/bin/python src/run_inference.py"
    ) from exc

from config import (
    CAMERA_INDEX,
    CONFIDENCE_THRESHOLD,
    LABEL_CLASSES_PATH,
    POSE_MODEL_PATH,
    STABLE_FRAME_COUNT,
    TRAINED_MODEL_PATH,
)
from send_unity import send_action


WRIST_VISIBILITY_THRESHOLD = 0.5
PASSIVE_ACTIONS = {"IDLE", "NONE"}
PASSIVE_SEND_INTERVAL_SECONDS = 0.3

CONNECTIONS = [
    (11, 12),            # shoulders
    (11, 13), (13, 15),  # left arm
    (12, 14), (14, 16),  # right arm
    (11, 23), (12, 24),  # torso
    (23, 24),            # pelvis
    (0, 11), (0, 12),    # nose to shoulders
]


def validate_required_files():
    """Ensure all model assets exist before opening the webcam."""
    missing_files = [
        path
        for path in (POSE_MODEL_PATH, TRAINED_MODEL_PATH, LABEL_CLASSES_PATH)
        if not path.exists()
    ]

    if missing_files:
        missing_text = "\n".join(f"- {path}" for path in missing_files)
        raise FileNotFoundError(
            "Required inference files are missing:\n"
            f"{missing_text}\n"
            "Train the classifier first:\n"
            "  python src/train_mlp.py"
        )


def load_artifacts():
    """Load the trained Keras model and label class names."""
    model = load_model(TRAINED_MODEL_PATH)
    label_classes = np.load(LABEL_CLASSES_PATH, allow_pickle=True)
    return model, label_classes


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

    return np.array(row, dtype=np.float32)


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
    frame, text, pos, scale=0.8, color=(255, 255, 255), thickness=2
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
    """Open the configured webcam with AVFoundation or raise an actionable error."""
    cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_AVFOUNDATION)

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


def wrists_are_not_visible(landmarks) -> bool:
    """Return True when both wrists are too unreliable for action inference."""
    left_wrist = landmarks[15]
    right_wrist = landmarks[16]

    return (
        left_wrist.visibility < WRIST_VISIBILITY_THRESHOLD
        and right_wrist.visibility < WRIST_VISIBILITY_THRESHOLD
    )


def predict_action(model, label_classes, landmarks):
    """Predict one action from pose landmarks and return raw action and confidence."""
    features = normalize_landmarks(landmarks).reshape(1, -1)
    probabilities = model.predict(features, verbose=0)[0]
    predicted_idx = int(np.argmax(probabilities))
    confidence = float(probabilities[predicted_idx])
    raw_action = str(label_classes[predicted_idx])

    if confidence < CONFIDENCE_THRESHOLD or wrists_are_not_visible(landmarks):
        raw_action = "NONE"

    return raw_action, confidence


def get_stable_action(action_buffer) -> str:
    """Return the most common action in the recent prediction buffer."""
    if not action_buffer:
        return "NONE"

    return Counter(action_buffer).most_common(1)[0][0]


def should_send_to_unity(
    final_action: str,
    last_sent_action: str | None,
    last_passive_sent_time: float,
    now: float,
) -> bool:
    """Decide whether the current final action should be sent to Unity."""
    if final_action == last_sent_action:
        return False

    if final_action in PASSIVE_ACTIONS:
        if last_sent_action is None:
            return True
        return now - last_passive_sent_time >= PASSIVE_SEND_INTERVAL_SECONDS

    return True


def send_to_unity_if_needed(
    final_action: str,
    confidence: float,
    last_sent_action: str | None,
    last_passive_sent_time: float,
):
    """Send changed actions to Unity and return updated send state."""
    now = time.time()

    if not should_send_to_unity(
        final_action,
        last_sent_action,
        last_passive_sent_time,
        now,
    ):
        return last_sent_action, last_passive_sent_time

    try:
        send_action(final_action, confidence)
    except OSError as exc:
        print(f"[UDP ERROR] Failed to send action to Unity: {final_action} ({exc})")
        return last_sent_action, last_passive_sent_time

    if final_action in PASSIVE_ACTIONS:
        last_passive_sent_time = now

    return final_action, last_passive_sent_time


def create_pose_landmarker():
    """Create the MediaPipe PoseLandmarker used for per-frame inference."""
    base_options = python.BaseOptions(model_asset_path=str(POSE_MODEL_PATH))
    options = vision.PoseLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)


def main():  # pylint: disable=too-many-locals
    """Run the webcam inference loop."""
    validate_required_files()
    model, label_classes = load_artifacts()
    cap = open_camera()
    action_buffer = deque(maxlen=STABLE_FRAME_COUNT)
    last_sent_action = None
    last_passive_sent_time = 0.0

    print("=== Motion Fighter Real-time Inference ===")
    print("Q: quit")
    print(f"Confidence threshold: {CONFIDENCE_THRESHOLD}")
    print(f"Stable frame count: {STABLE_FRAME_COUNT}")

    try:
        with create_pose_landmarker() as landmarker:
            while True:
                ret, frame = cap.read()
                if not ret:
                    break

                raw_action = "NONE"
                final_action = "NONE"
                confidence = 0.0

                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = landmarker.detect(mp_image)

                display_frame = cv2.flip(frame, 1)
                height, width, _ = display_frame.shape

                if result.pose_landmarks:
                    landmarks = result.pose_landmarks[0]
                    draw_skeleton(display_frame, landmarks, width, height)

                    raw_action, confidence = predict_action(
                        model,
                        label_classes,
                        landmarks,
                    )
                    action_buffer.append(raw_action)
                    final_action = get_stable_action(action_buffer)
                else:
                    action_buffer.append("NONE")
                    final_action = get_stable_action(action_buffer)

                last_sent_action, last_passive_sent_time = send_to_unity_if_needed(
                    final_action,
                    confidence,
                    last_sent_action,
                    last_passive_sent_time,
                )

                # Keep these values explicit for future Unity UDP integration.
                unity_action = final_action
                unity_confidence = confidence

                draw_text(
                    display_frame,
                    f"RAW: {raw_action}",
                    (30, 45),
                    scale=0.9,
                    color=(0, 255, 255),
                    thickness=2,
                )
                draw_text(
                    display_frame,
                    f"FINAL: {unity_action}",
                    (30, 85),
                    scale=1.0,
                    color=(0, 255, 0),
                    thickness=3,
                )
                draw_text(
                    display_frame,
                    f"CONF: {unity_confidence:.2f}",
                    (30, 125),
                    scale=0.8,
                    color=(255, 255, 255),
                    thickness=2,
                )
                draw_text(
                    display_frame,
                    f"SENT: {last_sent_action or 'NONE'}",
                    (30, 165),
                    scale=0.8,
                    color=(255, 200, 0),
                    thickness=2,
                )
                draw_text(
                    display_frame,
                    "Q: Quit",
                    (30, height - 30),
                    scale=0.7,
                    color=(255, 255, 255),
                    thickness=2,
                )

                cv2.imshow("Motion Fighter Inference", display_frame)

                key = cv2.waitKey(1) & 0xFF
                if key in (ord("q"), ord("Q")):
                    break

    finally:
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
