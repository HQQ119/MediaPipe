"""可选本机画面预览；不改变 TCP JSONL，也不另开摄像头。"""
import os
import struct
import time
from pathlib import Path
import cv2


class FramePreview:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.temporary = self.path.with_suffix(".tmp")
        self.next_at = 0

    def publish(self, bgr, timestamp):
        now = time.monotonic()
        if now < self.next_at:
            return
        self.next_at = now + .1
        ok, jpeg = cv2.imencode(".jpg", bgr, [cv2.IMWRITE_JPEG_QUALITY, 78])
        if not ok:
            raise RuntimeError("摄像头预览编码失败")
        try:
            self.temporary.write_bytes(struct.pack("<q", timestamp) + jpeg.tobytes())
            os.replace(self.temporary, self.path)
        except PermissionError:
            # Unity 正在读取时跳过一次预览，识别和录制仍正常继续。
            pass

    def close(self):
        self.temporary.unlink(missing_ok=True)
        self.path.unlink(missing_ok=True)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
