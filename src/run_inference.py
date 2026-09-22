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
    ACTIONS,
    CAMERA_INDEX,
    CONFIDENCE_THRESHOLD,
    LABEL_CLASSES_PATH,
    MOVEMENT_CALIBRATION_SECONDS,
    MOVEMENT_CALIBRATION_MIN_SAMPLES,
    MOVEMENT_ENTER_THRESHOLD,
    MOVEMENT_EXIT_THRESHOLD,
    MOVEMENT_MAX_STRENGTH_DELTA,
    MOVEMENT_MIRROR,
    MOVEMENT_STABLE_FRAME_COUNT,
    MOVEMENT_VISIBILITY_THRESHOLD,
    POSE_MODEL_PATH,
    STABLE_FRAME_COUNT,
    TRAINED_MODEL_PATH,
)
from movement import CALIBRATING, MovementResult, ShoulderMovementDetector
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
    validate_model_artifacts(model, label_classes)
    return model, label_classes


def get_model_output_dimension(model) -> int:
    """Return the classifier output dimension from a loaded Keras model."""
    output_shape = getattr(model, "output_shape", None)
    if isinstance(output_shape, list):
        output_shape = output_shape[0]

    if not output_shape or len(output_shape) < 2 or output_shape[-1] is None:
        raise RuntimeError(
            "Cannot determine model output dimension from loaded model. "
            f"output_shape={output_shape}"
        )

    return int(output_shape[-1])


def validate_model_artifacts(model, label_classes):
    """Validate that model outputs and saved LabelEncoder classes are compatible."""
    output_dim = get_model_output_dimension(model)
    labels = [str(label) for label in label_classes]

    if output_dim != len(labels):
        raise RuntimeError(
            "Model output dimension does not match label_classes.npy.\n"
            f"Model output dimension: {output_dim}\n"
            f"Saved label count: {len(labels)}\n"
            "Retrain the model or restore the matching label_classes.npy file."
        )

    duplicate_labels = sorted({label for label in labels if labels.count(label) > 1})
    expected_labels = set(ACTIONS)
    saved_labels = set(labels)
    missing_labels = sorted(expected_labels - saved_labels)
    unexpected_labels = sorted(saved_labels - expected_labels)

    if duplicate_labels or missing_labels or unexpected_labels:
        raise RuntimeError(
            "Saved label_classes.npy does not match the configured ACTIONS.\n"
            f"Expected labels: {sorted(expected_labels)}\n"
            f"Saved labels: {labels}\n"
            f"Missing labels: {missing_labels}\n"
            f"Unexpected labels: {unexpected_labels}\n"
            f"Duplicate labels: {duplicate_labels}\n"
            "Retrain the model with the current action classes."
        )


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
    final_movement: str,
    last_sent_action: str | None,
    last_sent_movement: str | None,
    last_passive_sent_time: float,
    now: float,
) -> bool:
    """Decide whether the current final action should be sent to Unity."""
    if final_action != last_sent_action:
        return True

    if final_movement != last_sent_movement:
        return True

    if final_action in PASSIVE_ACTIONS:
        return now - last_passive_sent_time >= PASSIVE_SEND_INTERVAL_SECONDS

    return False


def send_to_unity_if_needed(
    final_action: str,
    final_movement: str,
    confidence: float,
    movement_strength: float,
    last_sent_action: str | None,
    last_sent_movement: str | None,
    last_passive_sent_time: float,
    last_sent_payload_label: str,
):
    """Send changed actions to Unity and return updated send state."""
    now = time.time()

    if not should_send_to_unity(
        final_action,
        final_movement,
        last_sent_action,
        last_sent_movement,
        last_passive_sent_time,
        now,
    ):
        return (
            last_sent_action,
            last_sent_movement,
            last_passive_sent_time,
            last_sent_payload_label,
        )

    try:
        sent = send_action(final_action, final_movement, confidence, movement_strength)
    except OSError as exc:
        print(
            "[UDP ERROR] Failed to send action to Unity: "
            f"{final_action}/{final_movement} ({exc})"
        )
        return (
            last_sent_action,
            last_sent_movement,
            last_passive_sent_time,
            last_sent_payload_label,
        )

    if not sent:
        return (
            last_sent_action,
            last_sent_movement,
            last_passive_sent_time,
            last_sent_payload_label,
        )

    if final_action in PASSIVE_ACTIONS:
        last_passive_sent_time = now

    last_sent_payload_label = (
        f"{final_action}/{final_movement} "
        f"C{confidence:.2f} M{movement_strength:.2f}"
    )

    return final_action, final_movement, last_passive_sent_time, last_sent_payload_label


def create_movement_detector() -> ShoulderMovementDetector:
    """Create the shoulder-based rule detector for movement inference."""
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


def movement_debug_lines(result: MovementResult) -> list[str]:
    """Format calibration diagnostics independently of camera/UI rendering."""
    raw = f"{result.raw_rotation_score:.3f}" if result.score_valid else "N/A"
    corrected = (
        f"{result.corrected_rotation_score:.3f}"
        if result.corrected_rotation_score is not None else "N/A"
    )
    if result.calibration_state == CALIBRATING:
        return [
            "CALIBRATING - Face forward and hold still",
            f"VALID SAMPLES: {result.calibration_sample_count} / {MOVEMENT_CALIBRATION_MIN_SAMPLES}",
            f"RAW ROT: {raw}",
            "BASE: N/A",
            "CORR ROT: N/A",
            "Keep both shoulders visible; collecting valid samples",
        ]
    return [
        "READY",
        f"RAW ROT: {raw}",
        f"BASELINE: {result.neutral_baseline:.3f}",
        f"CORRECTED ROT: {corrected}",
        f"CALIBRATION MAD: {result.calibration_mad:.3f}",
        f"RAW MOVE: {result.raw_movement}",
        f"FINAL MOVE: {result.final_movement}",
        f"MOVE STR: {result.movement_strength:.2f}",
    ]


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
    movement_detector = create_movement_detector()
    last_sent_action = None
    last_sent_movement = None
    last_passive_sent_time = 0.0
    last_sent_payload_label = "NONE/NEUTRAL C0.00 M0.00"

    print("=== Motion Fighter Real-time Inference ===")
    print("Q: quit")
    print(f"Confidence threshold: {CONFIDENCE_THRESHOLD}")
    print(f"Stable frame count: {STABLE_FRAME_COUNT}")
    print(
        "Movement enter/exit threshold: "
        f"{MOVEMENT_ENTER_THRESHOLD}/{MOVEMENT_EXIT_THRESHOLD}"
    )
    print(f"Movement stable frame count: {MOVEMENT_STABLE_FRAME_COUNT}")
    print(f"Movement mirror: {MOVEMENT_MIRROR}")

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
                    movement_result = movement_detector.update(landmarks, now=time.monotonic())
                else:
                    action_buffer.append("NONE")
                    final_action = get_stable_action(action_buffer)
                    movement_result = movement_detector.reset()

                (
                    last_sent_action,
                    last_sent_movement,
                    last_passive_sent_time,
                    last_sent_payload_label,
                ) = send_to_unity_if_needed(
                    final_action,
                    movement_result.final_movement,
                    confidence,
                    movement_result.movement_strength,
                    last_sent_action,
                    last_sent_movement,
                    last_passive_sent_time,
                    last_sent_payload_label,
                )

                # Keep these values explicit for future Unity UDP integration.
                unity_action = final_action
                unity_movement = movement_result.final_movement
                unity_confidence = confidence
                unity_movement_strength = movement_result.movement_strength

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
                for row, label in enumerate(movement_debug_lines(movement_result)):
                    draw_text(
                        display_frame, label, (30, 160 + row * 27), scale=0.55,
                    )
                draw_text(
                    display_frame,
                    f"LAST UDP: {last_sent_payload_label}",
                    (30, 403), scale=0.55, color=(255, 200, 0),
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
