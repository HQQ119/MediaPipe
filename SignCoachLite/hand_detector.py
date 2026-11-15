"""官方 Hand Landmarker Tasks API；模型只初始化一次。"""
from pathlib import Path
import urllib.request
import zipfile
import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision
import config


def ensure_model(path=config.MODEL_PATH):
    path = Path(path)
    if path.is_file():
        return path
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".download")
    print("正在从 Google 官方地址下载手部模型……", flush=True)
    try:
        with urllib.request.urlopen(config.MODEL_URL, timeout=60) as source, temporary.open("wb") as target:
            while chunk := source.read(1024 * 1024):
                target.write(chunk)
        # task 文件是包含两个 TFLite 模型的 ZIP，防止把错误网页保存为模型。
        with zipfile.ZipFile(temporary) as bundle:
            if sum(name.endswith(".tflite") for name in bundle.namelist()) < 2:
                raise ValueError("模型包缺少 TFLite 模型")
            if bundle.testzip() is not None:
                raise ValueError("模型文件校验失败")
        temporary.replace(path)
    except Exception as exc:
        temporary.unlink(missing_ok=True)
        raise RuntimeError(f"官方模型下载失败：{exc}。请按 README 手动下载到 {path}") from exc
    return path


class HandDetector:
    def __init__(self, model_path=config.MODEL_PATH):
        path = ensure_model(model_path)
        options = vision.HandLandmarkerOptions(
            # 用字节加载，避免部分 Windows 原生库无法打开中文路径。
            base_options=python.BaseOptions(model_asset_buffer=path.read_bytes(), delegate=python.BaseOptions.Delegate.CPU),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=config.MAX_HANDS,
            min_hand_detection_confidence=config.DETECTION_CONFIDENCE,
            min_hand_presence_confidence=config.PRESENCE_CONFIDENCE,
            min_tracking_confidence=config.TRACKING_CONFIDENCE,
        )
        self._landmarker = vision.HandLandmarker.create_from_options(options)
        self._last_timestamp = -1

    def detect(self, bgr_frame, timestamp_ms):
        if timestamp_ms <= self._last_timestamp:
            raise ValueError("推理时间戳必须严格递增（毫秒）")
        self._last_timestamp = timestamp_ms
        rgb = cv2.cvtColor(bgr_frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        return self._landmarker.detect_for_video(image, timestamp_ms)

    def close(self):
        if self._landmarker is not None:
            self._landmarker.close()
            self._landmarker = None

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
