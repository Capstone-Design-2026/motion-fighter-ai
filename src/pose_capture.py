"""Shared camera opening and synchronous IMAGE-mode Pose construction."""

import platform


def open_camera(cv, camera_index):
    if platform.system() == "Windows":
        cap = cv.VideoCapture(camera_index, cv.CAP_DSHOW)
        if cap.isOpened():
            return cap
        cap.release()
    cap = cv.VideoCapture(camera_index)
    if cap.isOpened():
        return cap
    cap.release()
    message = f"Cannot open webcam at camera index {camera_index}."
    if platform.system() == "Darwin":
        message += (
            "\nAllow camera access for the app that starts Python "
            "(VS Code or Terminal) in System Settings > Privacy & Security > Camera."
        )
    raise RuntimeError(message)


def create_pose_landmarker(python, vision, model_path):
    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(model_path)),
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
    )
    return vision.PoseLandmarker.create_from_options(options)
