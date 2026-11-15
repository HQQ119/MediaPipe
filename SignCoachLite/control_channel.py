"""Unity 启动的后台进程通过标准输入请求退出，不改变 TCP 动作协议。"""
import sys
import threading


def start_stdin_control(stream=None):
    stop = threading.Event()

    def listen():
        try:
            for line in (stream if stream is not None else sys.stdin):
                if line.strip().lower() == "quit":
                    break
        finally:
            # 宿主退出、管道关闭时也释放摄像头，避免留下后台占用。
            stop.set()

    threading.Thread(target=listen, name="unity-control", daemon=True).start()
    return stop
