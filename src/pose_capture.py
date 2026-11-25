"""Shared camera opening and synchronous IMAGE-mode Pose construction."""

import platform
from pathlib import Path

from config import POSE_MODEL_PATH


CAMERA_BACKENDS = {"dshow": ("Windows", "CAP_DSHOW"),
                   "msmf": ("Windows", "CAP_MSMF"),
                   "avfoundation": ("Darwin", "CAP_AVFOUNDATION")}


def require_pose_model(model_path=None):
    """Default asset is repository-relative, independent of the working directory."""
    path = Path(model_path) if model_path is not None else POSE_MODEL_PATH
    if not path.is_file():
        raise FileNotFoundError(
            f"Pose Lite model missing: {path}. Obtain the team's pilot-v1 model or "
            "follow docs/gesture-pilot-windows.md (official Lite download + SHA256 check). "
            "Save it as pose_landmarker.task in the repository root; do not use Full/Heavy."
        )
    return path


def open_camera(cv, camera_index, backend="auto"):
    system = platform.system()
    if backend not in ("auto", "any", *CAMERA_BACKENDS):
        raise ValueError(f"Unknown camera backend: {backend}")
    if backend in CAMERA_BACKENDS and CAMERA_BACKENDS[backend][0] != system:
        raise ValueError(f"Camera backend {backend} is not supported on {system}")
    # Preserve macOS's existing default selection (normally AVFOUNDATION).
    choices = ("dshow", "msmf", "any") if system == "Windows" else ("any",)
    if backend != "auto":
        choices = (backend,)
    failures = []
    for name in choices:
        cap = None
        try:
            if name == "any":
                cap = cv.VideoCapture(camera_index)
            else:
                api = getattr(cv, CAMERA_BACKENDS[name][1], None)
                if api is None:
                    failures.append(f"{name}: unavailable in this OpenCV build")
                    continue
                cap = cv.VideoCapture(camera_index, api)
            if cap.isOpened():
                return cap
            failures.append(f"{name}: not opened")
        except Exception as exc:
            failures.append(f"{name}: {exc}")
        if cap is not None:
            cap.release()
    message = f"Cannot open webcam at camera index {camera_index}. Attempts: {'; '.join(failures)}."
    if system == "Darwin":
        message += (
            "\nAllow camera access for the app that starts Python "
            "(VS Code or Terminal) in System Settings > Privacy & Security > Camera."
        )
    elif system == "Windows":
        message += (
            "\nEnable Camera access and desktop app camera access in Windows Settings > "
            "Privacy (Windows 10) / Privacy & security (Windows 11) > Camera. "
            "Close Teams/Zoom/Camera apps, check the privacy shutter and camera index. "
            "Collector options: --camera-backend dshow or --camera-backend msmf."
        )
    raise RuntimeError(message)


def create_pose_landmarker(python, vision, model_path):
    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(model_path)),
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)
