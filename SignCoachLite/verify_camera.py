"""限时真实窗口验证，自动触发一次 S 截图和 Q 退出；不等同人工按键验收。"""
from unittest.mock import patch
import cv2
import main


if __name__ == "__main__":
    original = cv2.waitKey
    calls = 0

    def bounded_key(delay):
        global calls
        key = original(delay)  # 真实处理 GUI 消息，仍允许用户提前退出。
        calls += 1
        if key & 0xFF in (ord("q"), ord("Q"), 27):
            return key
        if calls == 10:
            return ord("s")
        if calls >= 20:
            return ord("q")
        return key

    with patch("main.cv2.waitKey", side_effect=bounded_key):
        raise SystemExit(main.main())
