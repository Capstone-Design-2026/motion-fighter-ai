"""Assemble a prebuilt Unity player and native local camera backend.

Does not open a camera, download dependencies, or overwrite an existing package.
The result has one game entry point; its supporting folders must stay together.
"""
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys

from build_stamp import snapshot


def assemble(player: Path, backend: Path, output: Path, target: str):
    player, backend, output = (p.resolve() for p in (player, backend, output))
    executable = backend / ('REDBOOLCamera.exe' if target == 'windows' else 'REDBOOLCamera')
    if not executable.is_file() or not (backend / '_internal').is_dir():
        raise ValueError('A complete native REDBOOLCamera onedir folder is required.')
    metadata = json.loads((backend / 'backend-build.json').read_text(encoding='utf-8'))
    expected = 'Windows' if target == 'windows' else 'Darwin'
    if metadata.get('platform') != expected:
        raise ValueError('Backend platform does not match the game target.')
    if metadata.get('release') != 'v36.2':
        raise ValueError('v36.2 requires the matching reviewed camera backend.')
    if metadata.get('recording_schema') != 'redbool.landmarks/1':
        raise ValueError('v34 requires the camera backend with landmark journal support.')
    provenance = json.loads((backend / '_internal/assets/backend-provenance.json').read_text(encoding='utf-8'))
    provenance_fields = ('release', 'recording_schema', 'source_commit', 'source_sha256',
                         'source_dirty', 'source_file_count')
    if any(key not in metadata or provenance.get(key) != metadata[key] for key in provenance_fields):
        raise ValueError('Embedded and external camera build provenance differ.')
    for name in ('pose_landmarker_lite.task', 'pose_landmarker_full.task'):
        model = backend / '_internal/assets' / name
        if not model.is_file() or hashlib.sha256(model.read_bytes()).hexdigest() != metadata.get('models', {}).get(name):
            raise ValueError('Bundled model fingerprint mismatch: ' + name)
    if output.exists():
        raise FileExistsError('Use a new output folder; existing packages are preserved.')
    if output == player or player in output.parents or backend in output.parents:
        raise ValueError('Output must be outside the input trees.')
    if target == 'mac':
        if player.suffix != '.app' or not (player / 'Contents/Info.plist').is_file():
            raise ValueError('Mac player must be a built .app bundle.')
        if sys.platform != 'darwin':
            raise ValueError('Assemble and sign the Mac package on macOS.')
        entry = output / 'REDBOOL.app'
        data = entry / 'Contents/Resources/Data'
        shutil.copytree(player, entry, symlinks=True)
    else:
        entry = output / player.name
        if player.suffix.lower() != '.exe' or not player.is_file():
            raise ValueError('Windows player must be a built .exe.')
        source_data = player.with_name(player.stem + '_Data')
        if not source_data.is_dir() or not (player.parent / 'UnityPlayer.dll').is_file():
            raise ValueError('Unity Windows support files are missing.')
        output.mkdir(parents=True)
        # Copy only Unity runtime outputs, not unrelated files beside an exe.
        for source in player.parent.iterdir():
            if source.is_file() and (source.name in (player.name, 'UnityCrashHandler64.exe') or source.suffix.lower() == '.dll'):
                shutil.copy2(source, output / source.name)
            elif source.is_dir() and source.name in (source_data.name, 'MonoBleedingEdge', 'D3D12'):
                shutil.copytree(source, output / source.name, symlinks=True)
        data = output / source_data.name
    destination = data / 'StreamingAssets/RedboolCamera'
    if destination.exists():
        raise ValueError('The input game already contains a camera backend.')
    shutil.copytree(backend, destination, symlinks=True)
    # Local/CI build paths are useful during assembly, not in the distributed app.
    portable_backend = {k: metadata[k] for k in ('platform', 'architecture', 'python', 'mode', 'models',
        'release', 'recording_schema', 'source_commit', 'source_sha256',
        'source_dirty', 'source_file_count') if k in metadata}
    (destination / 'backend-build.json').write_text(json.dumps(portable_backend, indent=2), encoding='utf-8')
    manifest = snapshot()
    manifest.update(target=target, backend=portable_backend,
                    backend_executable_sha256=hashlib.sha256(executable.read_bytes()).hexdigest(),
                    entry=entry.name, camera_preview='local loopback only; 320x240; 15 fps target, 8 fps under game/preview load',
                    camera_render_budget='3D targets at most 1920x1200; 1280x720 on sustained inference or severe foreground game pressure, including camera startup; native window/UI retained',
                    camera_starts='enter tutorial, combat, or PvP; never on the main menu',
                    recording='full-rate estimated landmarks and ordered engine operations; async bounded gzip; no RGB/video',
                    recognition_recording_max_bytes=134217728,
                    recognition_recording_schema='redbool.landmarks/1',
                    lan_protocol=33)
    (output / '시작 안내.txt').write_text(
        f'REDBOOL {manifest["release"]} · 동작 안내·직접 캐릭터 선택 시험판\n\n'
        'REDBOOL.app(Mac) 또는 REDBOOL.exe(Windows)를 실행하세요.\n'
        'Unity·Python을 별도로 설치하거나 카메라 명령어를 입력할 필요가 없습니다.\n'
        'Windows에서는 폴더 전체를 유지하세요. 실행 파일만 따로 옮기면 실행되지 않습니다.\n\n'
        '스토리/수련/PvP에 들어가면 내 동작 패널과 카메라가 켜집니다.\n'
        '설정에서 소리·전체 화면·속도 우선 화면·카메라 번호를 선택할 수 있습니다.\n'
        '다른 카메라는 번호 선택 후 적용·다시 연결을 누르세요.\n'
        '전투에서는 피격 중단·기 부족만 짧게 보여줍니다. 수련과 운영자 모드에서는 자세히 안내합니다.\n'
        '양손과 팔꿈치가 보이도록 앉아 보정 버튼 또는 C를 누르세요.\n'
        '사람을 바꾸면 C로 다시 보정합니다. 게임을 종료하면 카메라도 종료됩니다.\n'
        '보정이 끝나면 전투 카메라가 작아집니다. 크게/작게로 크기를 바꾸고, 접기로 영상만 숨길 수 있습니다.\n\n'
        '내 동작 미리보기는 최대 15fps이며 게임 처리에 부담이 생기면 8fps로 낮춥니다.\n'
        '전체 화면 크기는 유지하고 카메라와 함께 실행하는 3D 화면의 처리량을 조절합니다.\n'
        '느린 PC는 자동으로 속도 우선 인식과 720p 3D 화면으로 전환합니다.\n'
        '이 모드에서도 X자를 인식하지만, 팔이 가려지면 Full 보조가 있을 때보다 어려울 수 있습니다.\n'
        '모든 화면에서 Esc → 메인 메뉴로 돌아갈 수 있습니다. 메뉴를 열어도 게임은 진행됩니다.\n\n'
        '두 화면 대전: 같은 공유기에 연결한 뒤 두 게임에서 PvP 대전을 엽니다.\n'
        '양쪽 모두 v36.2를 사용하세요. 화면 통신 규약 33이며 기존 v36.1과 함께 접속할 수 없습니다.\n'
        '방장은 대기실의 상대가 입력할 주소를 복사해 전달합니다. 상대는 주소 입력 후 대전 참가를 누릅니다.\n'
        '주소가 여러 개면 상대와 연결한 Wi-Fi·유선·핫스팟에 해당하는 주소를 선택하세요.\n'
        '3개를 넘으면 다른 주소 버튼으로 넘겨 볼 수 있고, 네트워크 변경 뒤에는 새로고침을 누를 수 있습니다.\n'
        'IPv4 주소가 없다는 안내가 뜨면 네트워크 연결부터 확인하세요. 주소 표시가 통신 성공을 보장하지는 않습니다.\n'
        'Windows 방화벽 안내가 뜨면 사용하는 로컬 네트워크에서 게임 통신을 허용하세요.\n'
        '각자 보정 후 방향키·마우스로 인물을 고르고 선택 완료를 누르세요. 수련을 마치면 전투가 시작됩니다. 인터넷 서버는 사용하지 않습니다.\n\n'
        '카메라 접근 권한은 최초 실행 시 허용해야 합니다. 내 영상은 다른 PC로 보내지 않습니다.\n'
        '원본 영상은 녹화하지 않고 관절 좌표·인식·보정·성능 기록을 압축 저장합니다.\n'
        '관절 기록은 카메라 실행마다 최대 128MiB이며, 제한에 도달해도 게임은 계속됩니다.\n'
        '테스트를 마친 뒤 메인 메뉴 → 설정에서 기록 마무리가 끝나면 진단 기록 저장을 누르세요.\n'
        '최근 3개 카메라 실행의 ZIP이 있는 폴더가 열립니다. PvP는 두 PC의 ZIP을 보내주세요.\n'
        '이 패키지는 첫 통합 시험판이며 실제 두 기기 카메라·성능 확인은 별도로 필요합니다.\n',
        encoding='utf-8')
    if target == 'mac':
        plist_path = entry / 'Contents/Info.plist'
        with plist_path.open('rb') as stream:
            info = plistlib.load(stream)
        info['NSCameraUsageDescription'] = '동작을 인식하고 게임 안에 내 모습을 보여주기 위해 카메라를 사용합니다. 영상은 다른 PC로 보내지 않습니다.'
        info['CFBundleDisplayName'] = 'REDBOOL'
        with plist_path.open('wb') as stream:
            plistlib.dump(info, stream, sort_keys=False)
        # Bundling changes the game resources. Ad-hoc signing supports local
        # development testing; it is not Developer ID signing/notarization.
        subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(entry)], check=True)
        subprocess.run(['codesign', '--verify', '--deep', '--strict', str(entry)], check=True)
    installed_executable = destination / executable.name
    manifest['backend_executable_sha256'] = hashlib.sha256(installed_executable.read_bytes()).hexdigest()
    (output / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path, required=True)
    parser.add_argument('--backend', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--target', choices=('mac', 'windows'), required=True)
    args = parser.parse_args()
    print(json.dumps(assemble(args.player, args.backend, args.output, args.target), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
