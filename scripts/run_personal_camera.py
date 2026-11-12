"""Camera entry for an existing Python checkout; --check is camera-free."""
import argparse,os,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser()
p.add_argument('--player',choices=['1','2','story'],default='story')
p.add_argument('--host',default='127.0.0.1');p.add_argument('--camera',type=int,default=0)
p.add_argument('--seconds',type=float,default=1800);p.add_argument('--check',action='store_true')
a=p.parse_args();actor='DEV01' if a.player=='story' else 'P'+a.player
args=[sys.executable,'-m','redbool_demo.live','--personal-calibration','--host',a.host,
      '--port','5066' if a.player=='2' else '5065','--actor',actor,'--movement','--mirror',
      '--camera',str(a.camera),'--seconds',str(a.seconds),'--pose',str(root/'pose_landmarker.task'),
      '--cross-pose',str(root/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task'),
      '--output',str(root/'local_data/personal_pvp')]
if a.check:args+=['--check']
raise SystemExit(subprocess.call(args,cwd=root,env={**os.environ,'PYTHONPATH':str(root/'src'),'PYTHONDONTWRITEBYTECODE':'1'}))
