"""Camera entry for an existing Python checkout; --check is camera-free."""
import argparse,os,subprocess,sys,tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser()
p.add_argument('--player',choices=['1','2','story'],default='story')
p.add_argument('--host');p.add_argument('--camera',type=int,default=0)
p.add_argument('--network-profile',choices=['lan','online'],default='lan')
p.add_argument('--seconds',type=float,default=1800);p.add_argument('--check',action='store_true')
p.add_argument('--recording',choices=['review','logs','lossless'],default='review')
a=p.parse_args()
if a.player=='2' and not a.host and not a.check:
    from lan_discovery import discover
    peers=discover()
    if len(peers)==1:a.host=peers[0];print('LAN host: '+a.host)
    else:a.host=input('Host IPv4: ').strip()
    import ipaddress
    a.host=str(ipaddress.IPv4Address(a.host))
a.host=a.host or '127.0.0.1'
actor='DEV01' if a.player=='story' else 'P'+a.player
args=[sys.executable,'-m','redbool_demo.live','--personal-calibration','--host',a.host,
      '--network-profile',a.network_profile,
      '--port','5066' if a.player=='2' else '5065','--actor',actor,'--movement','--mirror',
      '--recording',a.recording,'--camera',str(a.camera),'--seconds',str(a.seconds),'--pose',str(root/'pose_landmarker.task'),
      '--cross-pose',str(root/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task'),
      '--output',str(root/'local_data/personal_pvp')]
if a.check:args+=['--check']
raise SystemExit(subprocess.call(args,cwd=root,env={**os.environ,'PYTHONPATH':str(root/'src'),'PYTHONDONTWRITEBYTECODE':'1','MPLCONFIGDIR':os.environ.get('MPLCONFIGDIR',str(Path(tempfile.gettempdir())/'redbool-demo-mpl'))}))
