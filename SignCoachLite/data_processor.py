"""模型结果 -> 每帧数据 -> JSON，下一阶段可以直接调用。"""
import json
import numpy as np


def process_result(result, timestamp_ms):
    """坐标保留未镜像输入的归一化值，标签直接取模型输出。"""
    hands = []
    for points, categories in zip(result.hand_landmarks, result.handedness, strict=True):
        if len(points) != 21:
            raise ValueError(f"手部关键点应为 21 个，实际为 {len(points)}")
        xyz = np.asarray([(p.x, p.y, p.z) for p in points], dtype=np.float64)
        if not np.isfinite(xyz).all():
            raise ValueError("关键点包含非有限坐标")
        category = max(categories, key=lambda c: c.score) if categories else None
        hands.append({
            "handedness": category.category_name if category else "Unknown",
            # 这是左右手分类置信度，不是每个点的检测置信度。
            "handedness_score": float(category.score) if category else None,
            "landmarks": [dict(id=i, x=float(p[0]), y=float(p[1]), z=float(p[2])) for i, p in enumerate(xyz)],
        })
    return {"timestamp": int(timestamp_ms), "hands": hands}


def to_json(frame_data):
    """不含网络通信；可直接作为后续发送端的序列化入口。"""
    return json.dumps(frame_data, ensure_ascii=False, allow_nan=False, separators=(",", ":"))
