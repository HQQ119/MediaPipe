"""对已获处理许可的本地视频提取 JSONL，复用现有识别器和数据结构。"""
import argparse
import json
import math
from pathlib import Path
import cv2
from hand_detector import HandDetector
from data_processor import process_result, to_json


def extract(video, output, mirror="unknown"):
    video, output = Path(video), Path(output)
    if output.exists():
        raise FileExistsError("目标 JSONL 已存在；不会覆盖")
    # Windows 的中文视频路径由 OpenCV/FFmpeg 读取；失败时明确报错。
    capture = cv2.VideoCapture(str(video))
    try:
        if not capture.isOpened():
            raise ValueError("无法打开教学视频")
        fps = capture.get(cv2.CAP_PROP_FPS)
        if not math.isfinite(fps) or fps <= 0 or fps > 1000:
            raise ValueError("容器帧率无效，请先核实视频时基")
        output.parent.mkdir(parents=True, exist_ok=True)
        frames = hands = double = 0
        last = -1
        with output.open("x", encoding="utf-8") as stream, HandDetector() as detector:
            while True:
                ok, raw = capture.read()
                if not ok:
                    break
                timestamp = max(last + 1, round(frames * 1000 / fps))
                last = timestamp
                # 原始输入方向及模型标签完整保留；未知镜像不猜测、不纠正。
                data = process_result(detector.detect(raw, timestamp), timestamp)
                data["frame_index"] = frames
                stream.write(to_json(data) + "\n")
                frames += 1
                hands += bool(data["hands"])
                double += len(data["hands"]) == 2
        if not frames:
            raise ValueError("视频没有可解码帧")
        return dict(frame_count=frames, hand_frame_count=hands, double_hand_frames=double,
                    duration_ms=last, fps=fps, width=int(capture.get(cv2.CAP_PROP_FRAME_WIDTH)),
                    height=int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT)), mirror=mirror,
                    coordinate_system="source_image_normalized_xy; z=MediaPipe relative depth; no display mirror",
                    handedness_convention="MediaPipe model output unchanged; source mirror=" + mirror,
                    verification_status="pending_review")
    except Exception:
        # 失败输出不冒充完整序列，保留 .failed 供诊断。
        if output.exists():
            output.rename(output.with_suffix(output.suffix + ".failed"))
        raise
    finally:
        capture.release()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("video", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--permission-record", type=Path, required=True, help="有明确来源、许可及允许处理声明的本地记录")
    parser.add_argument("--mirror", choices=["unknown", "unmirrored", "mirrored"], default="unknown")
    args = parser.parse_args()
    permission = json.loads(args.permission_record.read_text(encoding="utf-8"))
    if not permission.get("processing_allowed") or not permission.get("source_url") or not permission.get("license"):
        parser.error("必须先记录视频来源和处理许可；公开观看不等于允许下载和处理")
    report = extract(args.video, args.output, args.mirror)
    args.output.with_suffix(".extraction.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
