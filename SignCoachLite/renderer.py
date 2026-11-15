"""OpenCV 画面叠加。只在显示层做镜像，不修改输出数据。"""
import cv2
import numpy as np

# 官方 21 点拓扑：五指与闭合掌部，共 21 条连接。
CONNECTIONS = ((0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8),
               (5, 9), (9, 10), (10, 11), (11, 12), (9, 13), (13, 14), (14, 15),
               (15, 16), (13, 17), (0, 17), (17, 18), (18, 19), (19, 20))
COLORS = {"Left": (80, 230, 80), "Right": (255, 180, 40)}


def draw_frame(raw_frame, data, fps):
    frame = cv2.flip(raw_frame, 1)
    height, width = frame.shape[:2]
    for hand in data["hands"]:
        color = COLORS.get(hand["handedness"], (180, 180, 180))
        # 归一化坐标到镜像像素坐标。仅绘制时限制到画面范围。
        points = np.array([[(1 - p["x"]) * (width - 1), p["y"] * (height - 1)] for p in hand["landmarks"]])
        points = np.rint(np.clip(points, [0, 0], [width - 1, height - 1])).astype(int)
        for start, end in CONNECTIONS:
            cv2.line(frame, tuple(points[start]), tuple(points[end]), color, 2, cv2.LINE_AA)
        for p in points:
            cv2.circle(frame, tuple(p), 4, color, -1, cv2.LINE_AA)
        score = hand["handedness_score"]
        label = hand["handedness"] + (f" {score:.2f}" if score is not None else "")
        x, y = points[0]
        cv2.putText(frame, label, (max(0, min(x, width - 140)), max(22, y - 15)), cv2.FONT_HERSHEY_SIMPLEX, .6, color, 2)
    labels = {hand["handedness"] for hand in data["hands"]}
    lines = [f"Hands: {len(data['hands'])} / 2", f"Left: {'detected' if 'Left' in labels else '--'}",
             f"Right: {'detected' if 'Right' in labels else '--'}", f"FPS: {fps:.1f}"]
    panel_left = max(0, width - 226)
    cv2.rectangle(frame, (panel_left, 0), (width - 1, 120), (20, 20, 20), -1)
    for i, text in enumerate(lines):
        cv2.putText(frame, text, (panel_left + 10, 25 + i * 28), cv2.FONT_HERSHEY_SIMPLEX, .6, (240, 240, 240), 1, cv2.LINE_AA)
    if not data["hands"]:
        cv2.putText(frame, "Place hands in camera view", (10, height - 22), cv2.FONT_HERSHEY_SIMPLEX, .6, (0, 230, 255), 2)
    return frame
