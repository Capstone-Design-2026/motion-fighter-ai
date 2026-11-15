"""Explicit launch only. --check never opens a camera, socket or game."""
import argparse, datetime, hashlib, json, os, socket, subprocess, sys, tempfile, ipaddress
from pathlib import Path
ROOT=Path(__file__).resolve().parent
LOCAL=Path(os.environ.get('REDBOOL_PYTHON',str(ROOT.parent.parent/'redbool/motion-fighter-ai/.venv/bin/python')))
def python_path():
    choices=[ROOT/'.venv'/('Scripts/python.exe' if os.name=='nt' else 'bin/python'),LOCAL]
    return next((str(p) for p in choices if p.is_file()),sys.executable)
def environment():
    return {**os.environ,'PYTHONPATH':str(ROOT/'src'),'PYTHONDONTWRITEBYTECODE':'1',
            'MPLCONFIGDIR':os.environ.get('MPLCONFIGDIR',str(Path(tempfile.gettempdir())/'redbool-demo-mpl'))}
def verify():
    m=json.loads((ROOT/'release-manifest.json').read_text(encoding='utf-8'))
    for name,digest in m['files'].items():
        p=ROOT/name
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=digest:
            raise RuntimeError('실행 파일 확인 실패: '+name)
    return m

def host_address(given=None):
    path=ROOT/'connection.json'
    saved=json.loads(path.read_text()).get('host','') if path.is_file() else ''
    if not given:
        from lan_discovery import discover
        peers=discover()
        if len(peers)==1:
            given=peers[0];print('LAN 호스트 자동 연결: '+given)
        elif peers:print('여러 호스트 발견: '+', '.join(peers))
    value=given or input('1P 호스트 IPv4'+(f' [{saved}]' if saved else '')+': ').strip() or saved
    try:host=str(ipaddress.IPv4Address(value))
    except ValueError:raise RuntimeError('호스트 노트북의 로컬 IPv4 주소를 입력하세요.')
    path.write_text(json.dumps({'host':host}),encoding='utf-8');return host

def main():
    parser=argparse.ArgumentParser(description='REDBOOL v26 개인 동작 보완 / Windows·Mac 두 화면 대전')
    parser.add_argument('mode',choices=['host','join','pvp','story','p1','p2','story-camera','check'])
    parser.add_argument('--host');parser.add_argument('--camera',type=int,default=0)
    parser.add_argument('--seconds',type=float,default=1800)
    parser.add_argument('--recording',choices=['review','logs','lossless'],default='review')
    a=parser.parse_args();manifest=verify()
    lite=ROOT/'pose_landmarker.task';full=ROOT/'local_data/vision_v2/demo_comparison_20261022/assets/pose_landmarker_full.task'
    camera=[python_path(),'-m','redbool_demo.live','--pose',str(lite),'--cross-pose',str(full),
            '--personal-calibration','--movement','--mirror','--preview-fps','15','--recording',a.recording]
    if a.mode=='check':
        subprocess.run(camera+['--check'],cwd=ROOT,env=environment(),check=True)
        print('v29 파일·모델·모듈 확인 완료. 카메라와 게임을 켜지 않았습니다.');return
    folder=ROOT/'play_records'/datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    folder.mkdir(parents=True)
    (folder/'release.json').write_text(json.dumps({'release':manifest['release'],'mode':a.mode},indent=2))
    if a.mode in ('host','join','pvp','story'):
        binary=ROOT/('REDBOOL.exe' if os.name=='nt' else 'REDBOOL.app/Contents/MacOS/Vision Striker')
        if not binary.is_file():raise RuntimeError('이 운영체제용 전체 게임 패키지가 필요합니다. Windows는 Windows ZIP, Mac은 Mac 폴더를 사용하세요.')
        ports=(5065,5066,5067) if a.mode in ('host','pvp') else () if a.mode=='join' else (5065,)
        for port in ports:
            with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as s:
                try:s.bind(('0.0.0.0',port))
                except OSError:raise RuntimeError(f'{port} 포트가 사용 중입니다. 이전 게임/Unity Play를 종료해주세요.')
        command=[str(binary),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(folder/'Unity.log')]
        if a.mode in ('host','pvp'):command+=['--redbool-pvp']
        if a.mode=='host':command+=['--redbool-require-view']
        if a.mode=='join':command+=['--redbool-pvp-client',host_address(a.host)]
        print('게임을 엽니다. 카메라는 별도 실행하세요. 기록: '+str(folder),flush=True)
        return subprocess.call(command,cwd=ROOT)
    host=host_address(a.host) if a.mode=='p2' else str(ipaddress.IPv4Address(a.host or '127.0.0.1'))
    if not host:raise RuntimeError('호스트 IPv4 주소가 필요합니다.')
    actor,port=('P1',5065) if a.mode=='p1' else ('P2',5066) if a.mode=='p2' else ('DEV01',5065)
    camera+=['--host',host,'--port',str(port),'--actor',actor,'--camera',str(a.camera),'--seconds',str(a.seconds),'--output',str(folder/'camera')]
    print(f'{actor} → {host}:{port}. 카메라 창 C: 새 사람 보정 / Q: 종료.',flush=True)
    print('저장 모드 '+a.recording+' · 인식 기록은 이 노트북에 저장됩니다: '+str(folder),flush=True)
    return subprocess.call(camera,cwd=ROOT,env=environment())
if __name__=='__main__':
    try:sys.exit(main() or 0)
    except (RuntimeError,subprocess.CalledProcessError,KeyboardInterrupt,OSError,ValueError) as exc:
        print('실행 종료: '+str(exc),file=sys.stderr);sys.exit(1)
