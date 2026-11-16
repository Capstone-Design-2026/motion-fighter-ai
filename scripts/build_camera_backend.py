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
    sys.path.insert(0,str(root/'src'))
    from redbool_demo.backend_provenance import source_provenance, PROVENANCE_FILE
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pose',type=Path,required=True)
    p.add_argument('--cross-pose',type=Path,required=True)
    p.add_argument('--cross-refine-pose',type=Path,help='Optionally bundle verified Heavy; runtime remains default-off')
    p.add_argument('--output',type=Path,default=root/'dist/camera_backend')
    p.add_argument('--work',type=Path,default=root/'build/camera_backend')
    p.add_argument('--build-variant',choices=('stable','online-candidate'),default='stable',
                   help='Explicit artifact identity; does not change recognition or network defaults')
    p.add_argument('--check',action='store_true')
    a=p.parse_args()
    for model in (a.pose,a.cross_pose):
        if not model.is_file():p.error('Missing model: '+str(model))
    if a.cross_refine_pose:
        from redbool_demo.backend_assets import verify_pose_asset
        verify_pose_asset(a.cross_refine_pose,'Heavy')
    # Explicit named assets avoid any working-directory or source-machine path.
    asset_dir=a.work.resolve()/'assets';asset_dir.mkdir(parents=True,exist_ok=True)
    import shutil
    shutil.copy2(a.pose,asset_dir/'pose_landmarker_lite.task')
    shutil.copy2(a.cross_pose,asset_dir/'pose_landmarker_full.task')
    heavy_target=asset_dir/'pose_landmarker_heavy.task'
    if a.cross_refine_pose:shutil.copy2(a.cross_refine_pose,heavy_target)
    elif heavy_target.exists():heavy_target.unlink()  # generated build workspace only
    provenance=source_provenance(root)
    provenance['build_variant']=a.build_variant
    (asset_dir/PROVENANCE_FILE).write_text(json.dumps(provenance,indent=2),encoding='utf-8')
    command=[sys.executable,'-m','PyInstaller','--noconfirm','--clean','--onedir','--console',
             '--name','REDBOOLCamera','--paths',str(root/'src'),'--distpath',str(a.output.resolve()),
             '--workpath',str(a.work.resolve()/'pyinstaller'),'--specpath',str(a.work.resolve()),
             '--collect-all','mediapipe','--add-data',str(asset_dir)+':assets',
             '--exclude-module','tensorflow','--exclude-module','torch',
             str(root/'scripts/camera_backend_entry.py')]
    metadata={**provenance,'platform':platform.system(),'architecture':platform.machine(),
              'python':platform.python_version(),'mode':'native onedir; not cross-compiled',
              'models':{x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in sorted(asset_dir.glob('*.task'))},
              'optional_cross_refine':{'bundled':bool(a.cross_refine_pose),'default_enabled':False},
              'command':command}
    if a.check:print(json.dumps(metadata,indent=2));return
    env={**os.environ,'PYINSTALLER_CONFIG_DIR':str(a.work.resolve()/'cache'),
         'MPLCONFIGDIR':str(a.work.resolve()/'matplotlib'),
         'REDBOOL_CAMERA_CACHE_DIR':str(a.work.resolve()/'runtime-cache')}
    subprocess.run(command,check=True,cwd=root,env=env)
    if source_provenance(root)['source_sha256']!=provenance['source_sha256']:
        raise RuntimeError('Runtime source changed during packaging; rebuild the frozen source')
    folder=a.output.resolve()/'REDBOOLCamera'
    (folder/'backend-build.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
    executable=folder/('REDBOOLCamera.exe' if sys.platform=='win32' else 'REDBOOLCamera')
    subprocess.run([str(executable),'--check'],check=True,cwd=a.work,env=env)
    print(str(folder))


if __name__=='__main__':main()
