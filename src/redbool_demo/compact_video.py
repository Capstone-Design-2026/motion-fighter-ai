"""Review video only. Inference and stored pose observations retain full rate.

Lossy MP4 is never marked as lossless evidence. Capture timestamps in the
mapping, not the video's nominal frame rate, remain authoritative.
"""
import hashlib
from pathlib import Path


class CompactVideo:
    def __init__(self, cv, recorder, size, fps=15):
        self.cv, self.recorder, self.size = cv, recorder, tuple(size)
        self.path = recorder.directory / 'review.mp4'
        self.writer = cv.VideoWriter(str(self.path), cv.VideoWriter_fourcc(*'mp4v'), fps, self.size)
        if not self.writer.isOpened():
            self.writer.release()
            raise RuntimeError('MP4 recording unavailable. Use --recording logs or install the supported OpenCV package.')
        recorder.enable_video_mapping()
        self.interval_ns = round(1e9 / fps); self.next_ns = None; self.count = 0
        self.metadata = dict(enabled=True, path=self.path.name, codec='mp4v', fps=fps,
            resolution=list(size), mirror=False, lossless_verified=False,
            purpose='visual review; not exact RGB reconstruction',
            timebase='sampled capture timestamps in video_frames.jsonl',
            recognition_fps_unchanged=True, encoded_frame_count=0)
        recorder.update_manifest(video=self.metadata)

    def write(self, bgr, rgb, frame_record, **metadata):
        stamp = frame_record['capture_return_monotonic_ns']
        if self.next_ns is not None and stamp < self.next_ns: return
        self.next_ns = max(stamp, (self.next_ns or stamp) + self.interval_ns)
        if self.count == 0: self.next_ns = stamp + self.interval_ns
        if (bgr.shape[1], bgr.shape[0]) != self.size: raise RuntimeError('Video resolution changed')
        if self.writer.write(bgr) is False: raise RuntimeError('MP4 write failed')
        self.recorder.record_video_frame(frame_record, source_rgb_sha256=hashlib.sha256(rgb.tobytes()).hexdigest(), **metadata)
        self.count += 1

    def close(self):
        if self.writer is None: return
        self.writer.release(); self.writer = None
        decoder = self.cv.VideoCapture(str(self.path)); count = 0
        try:
            if not decoder.isOpened(): raise RuntimeError('Review MP4 cannot be decoded')
            while decoder.read()[0]: count += 1
        finally: decoder.release()
        if count != self.count: raise RuntimeError('Review MP4 frame count differs from mapping')
        self.metadata.update(encoded_frame_count=count, decode_verified=True, bytes=Path(self.path).stat().st_size)
        self.recorder.update_manifest(video=self.metadata)
