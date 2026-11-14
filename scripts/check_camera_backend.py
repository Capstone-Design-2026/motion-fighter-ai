"""Check a native packaged backend without a webcam or game packets."""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--executable',type=Path,required=True)
    p.add_argument('--skip-assets',action='store_true')
    p.add_argument('--output',type=Path)
    a=p.parse_args();executable=a.executable.resolve()
    report={'camera_opened':False,'game_packets':0,'checks':[]}
    with tempfile.TemporaryDirectory(prefix='redbool-backend-check-') as folder:
        cwd=Path(folder)
        if not a.skip_assets:
            result=subprocess.run([str(executable),'--check'],cwd=cwd,capture_output=True,text=True,timeout=120)
            if result.returncode:raise RuntimeError(result.stdout+result.stderr)
            report['capabilities']=json.loads(result.stdout.strip().splitlines()[-1])
            assert report['capabilities']['release']=='v34.1'
            assert report['capabilities']['recording_schema']=='redbool.landmarks/1'
            assert report['capabilities']['frozen'] and not report['capabilities']['camera_opened']
            metadata=json.loads((executable.parent/'backend-build.json').read_text(encoding='utf-8'))
            for key in ('release','recording_schema','source_sha256','source_commit','source_dirty'):
                assert report['capabilities'][key]==metadata[key], 'Bundled provenance differs: '+key
            report['checks'].append('v34 bundled landmark journal schema and source provenance')
            report['checks'].append('bundled assets and imports outside source directory')
            result=subprocess.run([str(executable),'--check-inference'],cwd=cwd,capture_output=True,text=True,timeout=120)
            if result.returncode:raise RuntimeError(result.stdout+result.stderr)
            report['inference']=json.loads(result.stdout.strip().splitlines()[-1])
            assert report['inference']['passed'] and report['inference']['frozen']
            report['checks'].append('native Lite VIDEO and Full VIDEO/IMAGE inference on synthetic RGB')
        with socket.socket() as reserve:
            reserve.bind(('127.0.0.1',0));port=reserve.getsockname()[1]
        token=secrets.token_hex(24)
        command=[str(executable),'--synthetic-bridge-check','--bridge-port',str(port),
                 '--bridge-token',token,'--parent-pid',str(os.getpid()),'--seconds','20','--output',str(cwd)]
        with (cwd/'backend.log').open('w') as logfile:
            child=subprocess.Popen(command,cwd=cwd,stdout=logfile,stderr=subprocess.STDOUT)
            def request(path,body=None):
                headers={'X-REDBOOL-Token':token}
                if body is not None:headers['Content-Type']='application/json'
                r=urllib.request.Request('http://127.0.0.1:'+str(port)+path,
                    data=json.dumps(body).encode() if body is not None else None,headers=headers)
                return urllib.request.urlopen(r,timeout=2)
            try:
                deadline=time.monotonic()+12
                while True:
                    try:
                        with request('/status') as response:status=json.load(response)
                        if status.get('phase') == 'test':break
                        if time.monotonic()>deadline:
                            raise RuntimeError('Synthetic backend stayed in '+str(status.get('phase')))
                        time.sleep(.05)
                    except (OSError,urllib.error.URLError):
                        if time.monotonic()>deadline or child.poll() is not None:
                            raise RuntimeError('Synthetic backend did not start: '+(cwd/'backend.log').read_text())
                        time.sleep(.05)
                assert status['phase']=='test' and status['synthetic']
                assert not status['camera_connected'] and not status['calibrated']
                with request('/control',{'command':'calibrate','request_id':'fixture-c'}) as response:
                    assert json.load(response)['accepted']
                time.sleep(.25)
                with request('/status') as response:status=json.load(response)
                assert status['last_command']=='calibrate' and not status['calibrated']
                with request('/preview.jpg') as response:
                    image=response.read();sequence=response.headers['X-REDBOOL-Sequence']
                assert image.startswith(b'\xff\xd8') and len(image)<256*1024
                report['checks']+=['synthetic mode stays uncalibrated','authenticated calibration control','latest JPEG preview']
                report['preview_bytes']=len(image);report['preview_sequence']=int(sequence)
                with request('/control',{'command':'stop','request_id':'fixture-q'}) as response:response.read()
                assert child.wait(timeout=8)==0
                report['checks'].append('graceful stop exits packaged child')
                report['fixture']=json.loads(next(cwd.glob('synthetic_bridge_*.json')).read_text())
            finally:
                if child.poll() is None:
                    child.terminate()
                    try:child.wait(timeout=5)
                    except subprocess.TimeoutExpired:child.kill();child.wait()
    report['passed']=True
    if a.output:a.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
