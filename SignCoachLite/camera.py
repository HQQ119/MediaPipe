"""摄像头只负责采集，保持原始方向供模型识别。"""
import os
import cv2


class Camera:
    def __init__(self, index=0, width=640, height=480, backend="auto"):
        self.capture = None
        backends = {"dshow": cv2.CAP_DSHOW, "msmf": cv2.CAP_MSMF}
        choices = ([cv2.CAP_DSHOW, cv2.CAP_MSMF] if os.name == "nt" else [cv2.CAP_ANY]) if backend == "auto" else [backends[backend]]
        for api in choices:
            cap = cv2.VideoCapture(index, api)
            if cap.isOpened():
                self.capture = cap
                cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
                cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
                break
            cap.release()
        if self.capture is None:
            raise RuntimeError(f"无法打开摄像头（索引 {index}）。请检查连接、Windows 摄像头隐私权限，关闭占用摄像头的软件，或用 --camera 1 切换设备。")

    def read(self):
        ok, frame = self.capture.read()
        if not ok or frame is None or frame.size == 0:
            raise RuntimeError("摄像头读取失败，设备可能已断开或被占用。请重新连接后重启程序，也可尝试 --backend msmf。")
        return frame

    def close(self):
        if self.capture is not None:
            self.capture.release()
            self.capture = None

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
