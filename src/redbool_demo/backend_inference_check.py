"""Exercise packaged native Pose libraries on blank RGB, never a camera."""
import argparse
import platform
import sys
import time


def run(argv, assets):
    import cv2 as cv
    import mediapipe as mp
    import numpy as np
    from mediapipe.tasks.python import BaseOptions
    from mediapipe.tasks.python.vision import PoseLandmarker, PoseLandmarkerOptions, RunningMode
    from pathlib import Path
    from .backend_assets import verify_pose_asset
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pose',type=Path,default=assets/'pose_landmarker_lite.task')
    p.add_argument('--cross-pose',type=Path,default=assets/'pose_landmarker_full.task')
    a=p.parse_args(argv)
    verify_pose_asset(a.pose,'Lite');verify_pose_asset(a.cross_pose,'Full')
    image=mp.Image(image_format=mp.ImageFormat.SRGB,data=np.zeros((480,640,3),np.uint8))
    results=[]
    for variant,path,mode in [('Lite',a.pose,RunningMode.VIDEO),
                              ('Full',a.cross_pose,RunningMode.VIDEO),
                              ('Full',a.cross_pose,RunningMode.IMAGE)]:
        begin=time.perf_counter()
        pose=PoseLandmarker.create_from_options(PoseLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(path),delegate=BaseOptions.Delegate.CPU),
            running_mode=mode,num_poses=1,min_pose_detection_confidence=.5,
            min_pose_presence_confidence=.5,min_tracking_confidence=.5))
        initialized=(time.perf_counter()-begin)*1000
        costs=[];counts=[]
        try:
            for frame in range(3):
                begin=time.perf_counter()
                result=pose.detect(image) if mode==RunningMode.IMAGE else pose.detect_for_video(image,frame*33)
                costs.append((time.perf_counter()-begin)*1000)
                counts.append(len(result.pose_landmarks))
        finally:pose.close()
        results.append({'variant':variant,'mode':mode.name,'initialize_ms':initialized,
                        'detect_ms':costs,'detected_pose_counts':counts})
    return {'passed':True,'camera_opened':False,'game_packets':0,'input':'synthetic blank640x480RGB',
            'scope':'native model creation and inference dependency check, not recognition accuracy or game latency',
            'frozen':bool(getattr(sys,'frozen',False)),'platform':platform.system(),
            'architecture':platform.machine(),'python':platform.python_version(),
            'mediapipe':mp.__version__,'opencv':cv.__version__,'numpy':np.__version__,'models':results}
