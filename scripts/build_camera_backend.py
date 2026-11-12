"""Build the camera backend on its target OS; never opens a webcam.

Install PyInstaller in the build environment first. Unity packaging copies the
entire REDBOOLCamera directory, preserving symlinks on macOS.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys


def main():
    root=Path(__file__).resolve().parents[1]
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pose',type=Path,required=True)
    p.add_argument('--cross-pose',type=Path,required=True)
    p.add_argument('--output',type=Path,default=root/'dist/camera_backend')
    p.add_argument('--work',type=Path,default=root/'build/camera_backend')
    p.add_argument('--check',action='store_true')
    a=p.parse_args()
    for model in (a.pose,a.cross_pose):
        if not model.is_file():p.error('Missing model: '+str(model))
    # Explicit named assets avoid any working-directory or source-machine path.
    asset_dir=a.work.resolve()/'assets';asset_dir.mkdir(parents=True,exist_ok=True)
    import shutil
    shutil.copy2(a.pose,asset_dir/'pose_landmarker_lite.task')
    shutil.copy2(a.cross_pose,asset_dir/'pose_landmarker_full.task')
    command=[sys.executable,'-m','PyInstaller','--noconfirm','--clean','--onedir','--console',
             '--name','REDBOOLCamera','--paths',str(root/'src'),'--distpath',str(a.output.resolve()),
             '--workpath',str(a.work.resolve()/'pyinstaller'),'--specpath',str(a.work.resolve()),
             '--collect-all','mediapipe','--add-data',str(asset_dir)+':assets',
             '--exclude-module','tensorflow','--exclude-module','torch',
             str(root/'scripts/camera_backend_entry.py')]
    metadata={'platform':platform.system(),'architecture':platform.machine(),
              'python':platform.python_version(),'mode':'native onedir; not cross-compiled',
              'models':{x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in asset_dir.iterdir()},
              'command':command}
    if a.check:print(json.dumps(metadata,indent=2));return
    env={**os.environ,'PYINSTALLER_CONFIG_DIR':str(a.work.resolve()/'cache'),
         'MPLCONFIGDIR':str(a.work.resolve()/'matplotlib'),
         'REDBOOL_CAMERA_CACHE_DIR':str(a.work.resolve()/'runtime-cache')}
    subprocess.run(command,check=True,cwd=root,env=env)
    folder=a.output.resolve()/'REDBOOLCamera'
    (folder/'backend-build.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
    executable=folder/('REDBOOLCamera.exe' if sys.platform=='win32' else 'REDBOOLCamera')
    subprocess.run([str(executable),'--check'],check=True,cwd=a.work,env=env)
    print(str(folder))


if __name__=='__main__':main()
