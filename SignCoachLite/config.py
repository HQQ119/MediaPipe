"""第一阶段的默认参数，路径均相对于项目目录。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parent
MODEL_PATH = ROOT / "models" / "hand_landmarker.task"
MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
CAMERA_INDEX = 0
WIDTH, HEIGHT = 640, 480
MAX_HANDS = 2
DETECTION_CONFIDENCE = 0.5
PRESENCE_CONFIDENCE = 0.5
TRACKING_CONFIDENCE = 0.5
WINDOW_NAME = "SignCoach Lite - Q: quit | S: snapshot"
