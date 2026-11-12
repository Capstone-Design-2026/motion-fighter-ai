"""Unity-owned packaged entry. Launching the game decides when camera starts."""
import argparse
import json
import os
from pathlib import Path
import sys
import time


def packaged_assets():
    return Path(getattr(sys, '_MEIPASS', Path(__file__).resolve().parents[2]))/'assets'


def synthetic_bridge(argv):
    """Explicit fixture mode: never open a webcam, load a model, or send attacks."""
    import numpy as np
    from .local_bridge import LocalBridge, jpeg_encoder
    parser=argparse.ArgumentParser(description='TEST MODE: synthetic local preview; no camera or game input')
    parser.add_argument('--bridge-port',type=int,required=True)
    parser.add_argument('--bridge-token',required=True)
    parser.add_argument('--parent-pid',type=int,required=True)
    parser.add_argument('--preview-fps',type=float,default=8.)
    parser.add_argument('--seconds',type=float,default=86400.)
    parser.add_argument('--output',type=Path)
    args,unused=parser.parse_known_args(argv)
    # Remaining flags are ordinary live role/asset configuration and are unused
    # by this fixture. It deliberately never creates a DemoSender or UDP socket.
    bridge=LocalBridge(args.bridge_port,args.bridge_token,preview_fps=args.preview_fps,
                       parent_pid=args.parent_pid,encoder=jpeg_encoder).start()
    print('TEST MODE: synthetic preview only; NO CAMERA / NO GAME INPUT',flush=True)
    bridge.update(phase='test',synthetic=True,camera_connected=False,calibrated=False,
                  calibration_progress=0.,calibration_reason='SYNTHETIC',
                  calibration_hint='TEST MODE - NO CAMERA',framing={'code':'SYNTHETIC','message':'TEST MODE - NO CAMERA'},
                  stream_id='synthetic-local-preview',output=str(args.output or ''))
    start=time.monotonic();frame=0;commands=[]
    try:
        while not bridge.stop_requested.wait(.025) and time.monotonic()-start<args.seconds:
            frame+=1
            image=np.zeros((240,320,3),np.uint8)
            image[:,:,0]=np.arange(320,dtype=np.uint16)[None,:]%256
            x=(frame*3)%260;image[70:170,x:x+60]=(60,210,110)
            bridge.offer_preview(image,time.monotonic_ns(),mirror=False)
            command=bridge.take_command()
            if command:
                commands.append(command)
                bridge.update(last_command=command,control_count=len(commands))
            bridge.update(frame_id=frame,capture_fps=40.,tracking='TEST_ONLY')
    finally:
        bridge.update(phase='stopped')
        report={'synthetic':True,'camera_opened':False,'game_packets':0,'commands':commands,
                'preview':bridge.snapshot(),'duration_seconds':time.monotonic()-start}
        bridge.close()
        if args.output:
            args.output.mkdir(parents=True,exist_ok=True)
            dest=args.output/('synthetic_bridge_'+str(os.getpid())+'.json')
            dest.write_text(json.dumps(report,indent=2),encoding='utf-8')
        print(json.dumps(report),flush=True)


def main(argv=None):
    args=list(sys.argv[1:] if argv is None else argv)
    if '--check-inference' in args:
        args.remove('--check-inference')
        from .backend_inference_check import run
        print(json.dumps(run(args,packaged_assets())),flush=True)
        return
    if '--synthetic-bridge-check' in args:
        args.remove('--synthetic-bridge-check')
        synthetic_bridge(args)
        return
    assets=packaged_assets()
    defaults=['--headless','--personal-calibration','--movement','--seconds','86400',
              '--preview-fps','8','--recording','diagnostics',
              '--pose',str(assets/'pose_landmarker_lite.task'),
              '--cross-pose',str(assets/'pose_landmarker_full.task')]
    bridge=None
    try:
        if '--check' not in args and '--help' not in args and '-h' not in args:
            # Answer Unity while the native libraries/model runtime are importing.
            # This phase cannot acquire a camera or produce game input.
            from .local_bridge import LocalBridge,jpeg_encoder
            p=argparse.ArgumentParser(add_help=False)
            p.add_argument('--bridge-port',type=int,required=True)
            p.add_argument('--bridge-token',required=True)
            p.add_argument('--parent-pid',type=int,required=True)
            p.add_argument('--preview-fps',type=float,default=8.)
            bootstrap,_=p.parse_known_args(args)
            bridge=LocalBridge(bootstrap.bridge_port,bootstrap.bridge_token,
                parent_pid=bootstrap.parent_pid,preview_fps=bootstrap.preview_fps,
                encoder=jpeg_encoder).start()
            bridge.update(phase='loading_models')
        from .live import main as live_main
        live_main(defaults+args,bridge_instance=bridge)
    finally:
        if bridge:bridge.close()


if __name__=='__main__':main()
