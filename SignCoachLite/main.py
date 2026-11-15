"""SignCoach Lite 第一阶段入口。"""
import argparse
from contextlib import ExitStack
from pathlib import Path
import sys
import time
import cv2
import config
from camera import Camera
from hand_detector import HandDetector, ensure_model
from data_processor import process_result, to_json
from renderer import draw_frame
from tcp_server import TcpFrameServer
from frame_preview import FramePreview
from control_channel import start_stdin_control


def parse_args():
    parser = argparse.ArgumentParser(description="SignCoach Lite 双手实时关键点识别（CPU）")
    parser.add_argument("--camera", type=int, default=config.CAMERA_INDEX, help="摄像头索引，默认 0")
    parser.add_argument("--backend", choices=["auto", "dshow", "msmf"], default="auto")
    parser.add_argument("--model", type=Path, default=config.MODEL_PATH, help="官方 .task 模型路径")
    parser.add_argument("--download-model", action="store_true", help="仅准备模型，然后退出")
    parser.add_argument("--json-output", type=Path, help="每帧写入 JSONL 文件（覆盖指定文件）")
    parser.add_argument("--no-display", action="store_true", help="无窗口测试，通过 Ctrl+C 或帧数限制退出")
    parser.add_argument("--max-frames", type=int, default=0, help="自动退出的帧数；0 表示不限")
    parser.add_argument("--tcp", action="store_true", help="向 Unity 提供非阻塞 TCP JSONL 服务")
    parser.add_argument("--host", default="127.0.0.1", help="TCP 监听地址")
    parser.add_argument("--port", type=int, default=5005, help="TCP 端口，默认 5005")
    parser.add_argument("--preview-file", type=Path, help="可选本机原始画面预览文件；不改变 TCP 协议")
    parser.add_argument("--control-stdin", action="store_true", help="由Unity通过标准输入管理后台进程退出")
    args = parser.parse_args()
    if args.camera < 0 or args.max_frames < 0:
        parser.error("摄像头索引和帧数不能为负数")
    if not 1 <= args.port <= 65535:
        parser.error("TCP 端口必须在 1–65535 之间")
    return args


def run(args):
    if getattr(args, "control_stdin", False):
        for stream in (sys.stdout, sys.stderr):
            if hasattr(stream, "reconfigure"):
                stream.reconfigure(encoding="utf-8", errors="replace")
    if args.download_model:
        print(f"模型已就绪：{ensure_model(args.model)}")
        return 0
    try:
        control_stop = start_stdin_control() if getattr(args, "control_stdin", False) else None
        with ExitStack() as stack:
            server = stack.enter_context(TcpFrameServer(args.host, args.port)) if getattr(args, "tcp", False) else None
            camera = stack.enter_context(Camera(args.camera, config.WIDTH, config.HEIGHT, args.backend))
            detector = stack.enter_context(HandDetector(args.model))
            preview = stack.enter_context(FramePreview(args.preview_file)) if getattr(args, "preview_file", None) else None
            output = None
            if args.json_output:
                args.json_output.parent.mkdir(parents=True, exist_ok=True)
                output = stack.enter_context(args.json_output.open("w", encoding="utf-8"))
            start = time.perf_counter_ns()
            last_timestamp = -1
            previous = time.perf_counter()
            fps = 0.0
            frames = 0
            print("识别已启动。请将双手放入画面；窗口内按 Q/Esc 退出、S 保存截图，终端内 Ctrl+C 退出。", flush=True)
            while True:
                if control_stop is not None and control_stop.is_set():
                    break
                raw = camera.read()
                # 同一会话的单调毫秒时间，防止系统时钟调整和同毫秒重号。
                timestamp = max((time.perf_counter_ns() - start) // 1_000_000, last_timestamp + 1)
                last_timestamp = timestamp
                data = process_result(detector.detect(raw, timestamp), timestamp)
                if preview is not None:
                    preview.publish(raw, timestamp)
                if server is not None:
                    server.send_frame(data)
                if output:
                    output.write(to_json(data) + "\n")
                now = time.perf_counter()
                instant = 1 / max(now - previous, 1e-9)
                fps = instant if frames == 0 else .9 * fps + .1 * instant
                previous = now
                frames += 1
                if not args.no_display:
                    display = draw_frame(raw, data, fps)
                    cv2.imshow(config.WINDOW_NAME, display)
                    key = cv2.waitKey(1) & 0xFF
                    if key in (ord("q"), ord("Q"), 27):
                        break
                    if key in (ord("s"), ord("S")):
                        path = config.ROOT / "output" / f"capture_{time.time_ns()}.png"
                        path.parent.mkdir(parents=True, exist_ok=True)
                        ok, image = cv2.imencode(".png", display)
                        if not ok:
                            raise RuntimeError("截图编码失败")
                        image.tofile(str(path))
                        path.with_suffix(".json").write_text(to_json(data), encoding="utf-8")
                        print(f"已保存截图及对应 JSON：{path}")
                    if cv2.getWindowProperty(config.WINDOW_NAME, cv2.WND_PROP_VISIBLE) < 1:
                        break
                if args.max_frames and frames >= args.max_frames:
                    break
            print(f"已处理 {frames} 帧，最终平滑 FPS {fps:.1f}。")
    finally:
        cv2.destroyAllWindows()
    return 0


def main():
    try:
        return run(parse_args())
    except KeyboardInterrupt:
        print("\n用户中止，资源已释放。")
        return 0
    except Exception as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
