"""单客户端、非阻塞 JSONL 服务；慢客户端不会拖住摄像头循环。"""
import socket
import time
from data_processor import to_json


class TcpFrameServer:
    def __init__(self, host="127.0.0.1", port=5005):
        self.client = None
        self.pending = b""
        self.latest = None
        self.pending_since = 0.0
        self.listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            self.listener.bind((host, port))
            self.listener.listen(1)
            self.listener.setblocking(False)
        except Exception:
            self.listener.close()
            raise
        self.address = self.listener.getsockname()
        print(f"TCP 服务已监听 {self.address[0]}:{self.address[1]}，等待 Unity 连接。", flush=True)

    def _disconnect(self):
        if self.client is not None:
            self.client.close()
            self.client = None
        self.pending = b""
        self.latest = None

    def send_frame(self, frame_data):
        if self.client is None:
            try:
                self.client, address = self.listener.accept()
                self.client.setblocking(False)
                self.client.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                self.client.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 8192)
                print(f"Unity 客户端已连接：{address}", flush=True)
            except BlockingIOError:
                return
            except OSError:
                self._disconnect()
                return
        self.latest = (to_json(frame_data) + "\n").encode("utf-8")
        try:
            # 最多保留当前未发完的一条和最新一条；不截断半条 JSON。
            for _ in range(4):
                if not self.pending:
                    if self.latest is None:
                        return
                    self.pending, self.latest = self.latest, None
                    self.pending_since = time.monotonic()
                if time.monotonic() - self.pending_since > 1.0:
                    raise ConnectionError("客户端读取过慢")
                sent = self.client.send(self.pending)
                if not sent:
                    raise ConnectionError("客户端已关闭")
                self.pending = self.pending[sent:]
        except BlockingIOError:
            pass
        except OSError:
            print("Unity 连接已断开，继续识别并等待重连。", flush=True)
            self._disconnect()

    def close(self):
        self._disconnect()
        self.listener.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()
