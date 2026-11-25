"""Authenticated loopback control and latest-only preview for the Unity owner.

No camera, pose model, OpenCV window, or LAN image transport is created here.
The preview encoder is injected and always runs outside the inference thread.
"""
from collections import deque
import ctypes
import hmac
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import math
import os
import threading
import time


class ParentProcess:
    """Hold the Windows process object; use parent identity on POSIX."""
    def __init__(self, pid):
        if type(pid) is not int or pid <= 0 or pid == os.getpid():
            raise ValueError('parent-pid must identify the owning game process')
        self.pid = pid
        self.direct_parent = os.getppid() == pid
        self.handle = None
        if os.name == 'nt':
            from ctypes import wintypes
            self.api = ctypes.WinDLL('kernel32', use_last_error=True)
            self.api.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
            self.api.OpenProcess.restype = wintypes.HANDLE
            self.api.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
            self.api.WaitForSingleObject.restype = wintypes.DWORD
            self.api.CloseHandle.argtypes = [wintypes.HANDLE]
            self.api.CloseHandle.restype = wintypes.BOOL
            self.handle = self.api.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE
            if not self.handle:
                raise OSError(ctypes.get_last_error(), 'Cannot monitor owning game process')

    def alive(self):
        if self.handle is not None:
            return self.api.WaitForSingleObject(self.handle, 0) == 0x00000102  # WAIT_TIMEOUT
        if self.direct_parent and os.getppid() != self.pid:
            return False
        try:
            os.kill(self.pid, 0)  # POSIX existence check only; never used on Windows.
            return True
        except ProcessLookupError:
            return False
        except PermissionError:
            return True

    def close(self):
        if self.handle is not None:
            self.api.CloseHandle(self.handle)
            self.handle = None


class LocalBridge:
    def __init__(self, port, token, *, preview_fps=8., parent_pid=None, encoder=None,
                 parent=None, clock=time.monotonic):
        if not 0 <= port <= 65535 or not isinstance(token, str) or len(token) < 16:
            raise ValueError('Valid loopback port and bridge-token of at least16characters required')
        if not 1 <= preview_fps <= 15:
            raise ValueError('Headless preview_fps must be between1and15')
        self.token = token
        self.clock = clock
        self.requested_preview_fps = float(preview_fps)
        self.interval = 1. / preview_fps
        self.encoder = encoder
        self.parent = parent or (ParentProcess(parent_pid) if parent_pid else None)
        self.lock = threading.Lock()
        self.stop_requested = threading.Event()
        self.closed = threading.Event()
        self.frame_ready = threading.Event()
        self.pending_frame = None
        self.jpeg = None
        self.preview_sequence = 0
        self.preview_capture_ns = 0
        self.offered = self.dropped = self.encoded = 0
        self.encode_ms = deque(maxlen=256)
        self.pending_command = None
        self.request_ids = deque(maxlen=64)
        self.status = dict(schema=1, phase='starting', camera_connected=False,
                           calibrated=False, calibration_progress=0., calibration_hint='',
                           tracking='UNAVAILABLE', framing_code='', framing_message='',
                           event='', pose_ms=0., capture_fps=0., stream_id='', output='', error='')
        bridge = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = 'HTTP/1.0'

            def setup(self):
                super().setup()
                self.connection.settimeout(.5)

            def log_message(self, *args):
                pass

            def reply(self, code, body=b'', kind='application/json', headers=None):
                self.send_response(code)
                self.send_header('Content-Type', kind)
                self.send_header('Content-Length', str(len(body)))
                self.send_header('Cache-Control', 'no-store')
                self.send_header('Connection', 'close')
                for key, value in (headers or {}).items():
                    self.send_header(key, str(value))
                self.end_headers()
                if body:
                    self.wfile.write(body)

            def authorized(self):
                value = self.headers.get('X-REDBOOL-Token', '')
                if not hmac.compare_digest(value.encode(), bridge.token.encode()):
                    self.reply(403)
                    return False
                return True

            def do_GET(self):
                if not self.authorized():
                    return
                if self.path == '/status':
                    self.reply(200, json.dumps(bridge.snapshot(), ensure_ascii=False).encode('utf-8'))
                elif self.path == '/preview.jpg':
                    with bridge.lock:
                        image, sequence = bridge.jpeg, bridge.preview_sequence
                    self.reply(200 if image else 204, image or b'', 'image/jpeg',
                               {'X-REDBOOL-Sequence': sequence})
                else:
                    self.reply(404)

            def do_POST(self):
                if not self.authorized():
                    return
                if self.path != '/control':
                    self.reply(404)
                    return
                try:
                    length = int(self.headers.get('Content-Length', '-1'))
                    if not 0 < length <= 1024 or self.headers.get('Transfer-Encoding'):
                        self.reply(413)
                        return
                    data = json.loads(self.rfile.read(length))
                    command, request_id = data['command'], data['request_id']
                    if command not in ('calibrate', 'pause', 'stop'):
                        raise ValueError('Unknown command')
                    if not isinstance(request_id, str) or not 1 <= len(request_id) <= 128:
                        raise ValueError('Invalid request_id')
                    duplicate = bridge.command(command, request_id)
                    self.reply(202, json.dumps(dict(accepted=True, duplicate=duplicate,
                                                   request_id=request_id)).encode())
                except (ValueError, KeyError, TypeError, TimeoutError):
                    self.reply(400)

        class BoundedServer(HTTPServer):
            request_queue_size = 2
            allow_reuse_address = False
            def handle_error(self, request, client_address):
                # A canceled Unity preview request is not an inference failure.
                pass

        try:
            self.server = BoundedServer(('127.0.0.1', port), Handler)
        except BaseException:
            if self.parent:
                self.parent.close()
            raise
        self.port = self.server.server_address[1]
        self.threads = [threading.Thread(target=self.server.serve_forever,
                                        kwargs={'poll_interval': .1}, name='unity-local-http', daemon=True),
                        threading.Thread(target=self._preview_loop, name='unity-preview', daemon=True),
                        threading.Thread(target=self._parent_loop, name='unity-parent', daemon=True)]

    def start(self):
        for worker in self.threads:
            worker.start()
        return self

    def update(self, **values):
        with self.lock:
            self.status.update(values)

    def limit_preview_fps(self, maximum_fps):
        """Reduce producer work for this process; never raise a caller's limit.

        This is an internal performance action, not an HTTP control. Calibration
        and pause commands cannot restore the higher encoder rate.
        """
        if (isinstance(maximum_fps, bool)
                or not isinstance(maximum_fps, (int, float))
                or not math.isfinite(maximum_fps) or not 1 <= maximum_fps <= 15):
            raise ValueError('Preview limit must be finite and between1and15')
        with self.lock:
            self.interval = max(self.interval, 1. / maximum_fps)
            return 1. / self.interval

    def snapshot(self):
        with self.lock:
            return {**self.status, 'preview_sequence': self.preview_sequence,
                    'preview_capture_ns': self.preview_capture_ns,
                    'preview_encoded': self.encoded, 'preview_dropped': self.dropped,
                    'preview_requested_fps': self.requested_preview_fps,
                    'preview_target_fps': 1. / self.interval,
                    'preview_encode_ms_max': max(self.encode_ms, default=0.)}

    def command(self, command, request_id):
        with self.lock:
            if request_id in self.request_ids:
                return True
            self.request_ids.append(request_id)
            if command == 'stop':
                self.stop_requested.set()
                self.pending_command = None
            elif not self.stop_requested.is_set():
                self.pending_command = command
        return False

    def take_command(self):
        with self.lock:
            value, self.pending_command = self.pending_command, None
            return value

    def offer_preview(self, frame, capture_ns, *, mirror=True, landmarks=None):
        # The caller hands over immutable-by-convention pixels; keep only the latest.
        with self.lock:
            if self.pending_frame is not None:
                self.dropped += 1
            self.pending_frame = (frame, capture_ns, mirror, landmarks)
            self.offered += 1
        self.frame_ready.set()

    def _preview_loop(self):
        last_begin = None
        while not self.closed.is_set():
            self.frame_ready.wait(.1)
            with self.lock:
                next_at = 0. if last_begin is None else last_begin+self.interval
            if self.closed.wait(max(0., next_at-self.clock())):
                break
            with self.lock:
                # A new lower rate may arrive while waiting at the previous
                # cadence. Recheck before consuming or encoding any pixels.
                if last_begin is not None and self.clock() < last_begin+self.interval:
                    continue
                item, self.pending_frame = self.pending_frame, None
                self.frame_ready.clear()
            if item is None or self.encoder is None:
                continue
            begin = self.clock()
            last_begin = begin
            try:
                encoded = self.encoder(item[0], item[2], item[3])
                if not isinstance(encoded, bytes) or not 0 < len(encoded) <= 256*1024:
                    raise ValueError('Preview encoder produced an invalid image')
                with self.lock:
                    self.jpeg = encoded
                    self.preview_sequence += 1
                    self.preview_capture_ns = item[1]
                    self.encoded += 1
                    self.encode_ms.append((self.clock()-begin)*1000.)
            except Exception as exc:
                self.update(preview_error=str(exc))

    def _parent_loop(self):
        while not self.closed.wait(.25):
            if self.parent and not self.parent.alive():
                self.update(phase='stopping', stop_reason='owning_game_exited')
                self.stop_requested.set()
                return

    def close(self):
        if self.closed.is_set():
            return
        self.closed.set()
        self.frame_ready.set()
        if self.threads[0].ident is not None:
            self.server.shutdown()
        self.server.server_close()
        for worker in self.threads:
            if worker.ident is not None:
                worker.join(timeout=2)
        if self.parent:
            self.parent.close()


def jpeg_encoder(frame, mirror, landmarks):
    """Small mirrored preview only; inference always receives original pixels."""
    import cv2 as cv
    view = cv.resize(frame, (320, 240), interpolation=cv.INTER_AREA)
    if mirror:
        view = cv.flip(view, 1)
    # Unity owns calibration text and controls; avoid a second dense text panel.
    if landmarks:
        for a, b in ((11, 12), (11, 13), (13, 15), (12, 14), (14, 16)):
            if landmarks[a][2] < .5 or landmarks[b][2] < .5:
                continue
            points = []
            for index in (a, b):
                x, y, _ = landmarks[index]
                points.append((round((1-x if mirror else x)*319), round(y*239)))
            cv.line(view, points[0], points[1], (100, 240, 120), 2, cv.LINE_AA)
    ok, image = cv.imencode('.jpg', view, [cv.IMWRITE_JPEG_QUALITY, 65])
    if not ok:
        raise RuntimeError('JPEG preview encode failed')
    return image.tobytes()
