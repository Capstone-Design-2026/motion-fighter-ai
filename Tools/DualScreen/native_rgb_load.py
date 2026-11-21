"""Saved RGB inference benchmark; does not open a camera."""
from pathlib import Path
import cv2,time,json,sys,os,numpy as np
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python.vision import PoseLandmarker,PoseLandmarkerOptions,RunningMode
from concurrent.futures import ThreadPoolExecutor
pool=ThreadPoolExecutor(max_workers=1)
W=Path(os.environ['REDBOOL_CHECK_OUTPUT']);R=Path(os.environ['REDBOOL_RELEASE']);S=Path(os.environ['REDBOOL_RECORDED_VIDEO']);cv2.setNumThreads(1)
models=[R/'pose_landmarker.task',R/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task']
poses=[PoseLandmarker.create_from_options(PoseLandmarkerOptions(base_options=BaseOptions(model_asset_path=str(m),delegate=BaseOptions.Delegate.CPU),running_mode=RunningMode.VIDEO,num_poses=1)) for m in models]
poses.append(PoseLandmarker.create_from_options(PoseLandmarkerOptions(base_options=BaseOptions(model_asset_path=str(models[-1]),delegate=BaseOptions.Delegate.CPU),running_mode=RunningMode.IMAGE,num_poses=1)))
cap=cv2.VideoCapture(str(S));start=time.monotonic();n=0;cost=[]
try:
 while time.monotonic()-start<40:
  ok,bgr=cap.read()
  if not ok:break
  t=time.monotonic();im=mp.Image(image_format=mp.ImageFormat.SRGB,data=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB))
  future=pool.submit(poses[-1].detect,im)
  for p in poses[:2]:p.detect_for_video(im,n*1000//30)
  future.result()
  cost.append((time.monotonic()-t)*1000);n+=1;time.sleep(max(0,start+n/30-time.monotonic()))
finally:
 elapsed=time.monotonic()-start
 pool.shutdown(wait=True)
 cap.release()
 for p in poses:p.close()
r={'fps':n/elapsed,'frames':n,'dual_pose_ms_p50_p95':np.percentile(cost,[50,95]).tolist(),'camera_opened':False,'scope':'recorded RGB sequential Lite VIDEO + Full VIDEO + Full IMAGE inference; excludes live capture/preview/recording'}
(W/'native_rgb_load.json').write_text(json.dumps(r,indent=2));print(r)
