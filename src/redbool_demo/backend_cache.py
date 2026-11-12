"""Stable per-user cache for the packaged runtime's plotting dependency."""
import hashlib
import os
from pathlib import Path
import sys


def configure_cache():
    override=os.environ.get('REDBOOL_CAMERA_CACHE_DIR')
    if override:
        root=Path(override).expanduser()
    elif sys.platform=='win32':
        root=Path(os.environ.get('LOCALAPPDATA',Path.home()/'AppData/Local'))/'REDBOOL/CameraCache'
    elif sys.platform=='darwin':
        root=Path.home()/'Library/Caches/REDBOOLCamera'
    else:
        root=Path(os.environ.get('XDG_CACHE_HOME',Path.home()/'.cache'))/'REDBOOLCamera'
    # A moved onedir bundle can contain different font resource paths. Keep
    # caches stable for that installation, separate from unrelated copies.
    bundle=str(Path(getattr(sys,'_MEIPASS',__file__)).resolve())
    key=hashlib.sha256(bundle.encode()).hexdigest()[:12]
    folder=root/('matplotlib-'+key)
    try:
        folder.mkdir(parents=True,exist_ok=True)
    except OSError:
        # An unwritable optional cache must not prevent camera startup.
        return None
    os.environ['MPLCONFIGDIR']=str(folder.resolve())
    return folder
