"""真实 loopback TCP 测试与非阻塞发送边界测试。"""
import json
import socket
import time
import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock, patch
from tcp_server import TcpFrameServer
import main
from test_pipeline import fixture_result


class TcpServerTests(unittest.TestCase):
    def test_main_tcp_lifecycle_does_not_reinitialize_vision(self):
        import numpy as np
        for camera_failure in (False, True):
            with self.subTest(camera_failure=camera_failure), patch('main.Camera') as camera, \
                    patch('main.HandDetector') as detector, patch('main.TcpFrameServer') as server, \
                    patch('main.cv2.destroyAllWindows'):
                if camera_failure:
                    camera.side_effect = RuntimeError('摄像头读取失败')
                camera.return_value.__enter__.return_value.read.return_value = np.zeros((480,640,3),np.uint8)
                detector.return_value.__enter__.return_value.detect.return_value = fixture_result(2)
                args = SimpleNamespace(download_model=False, tcp=True, host='127.0.0.1', port=5005,
                    camera=0, backend='auto', model='unused.task', json_output=None, no_display=True, max_frames=3)
                if camera_failure:
                    with self.assertRaises(RuntimeError): main.run(args)
                else:
                    self.assertEqual(main.run(args), 0)
                    camera.assert_called_once()
                    detector.assert_called_once()
                    self.assertEqual(server.return_value.__enter__.return_value.send_frame.call_count, 3)
                    detector.return_value.__exit__.assert_called_once()
                    camera.return_value.__exit__.assert_called_once()
                server.return_value.__exit__.assert_called_once()

    def test_no_client_does_not_block(self):
        with TcpFrameServer(port=0) as server:
            start = time.monotonic()
            for timestamp in range(100):
                server.send_frame({"timestamp": timestamp, "hands": []})
            self.assertLess(time.monotonic() - start, 1)

    def test_real_jsonl_disconnect_and_reconnect(self):
        with TcpFrameServer(port=0) as server:
            for timestamp in (1, 2):
                with socket.create_connection(server.address, timeout=1) as client:
                    client.settimeout(1)
                    server.send_frame({"timestamp": timestamp, "hands": []})
                    message = b""
                    while not message.endswith(b"\n"):
                        message += client.recv(7)  # 强制小块读取，实际 TCP 流完整重组。
                    self.assertEqual(json.loads(message), {"timestamp": timestamp, "hands": []})
                    # RST 模拟客户端退出，服务保持可重连，不需重开摄像头。
                    client.shutdown(socket.SHUT_RDWR)
                for _ in range(20):
                    server.send_frame({"timestamp": timestamp, "hands": []})
                    if server.client is None:
                        break
                    time.sleep(.005)
                self.assertIsNone(server.client)

    def test_partial_send_keeps_json_boundary_and_latest(self):
        with TcpFrameServer(port=0) as server:
            fake = MagicMock()
            server.client = fake
            sent = bytearray()

            def send(chunk):
                if len(sent) == 5:
                    raise BlockingIOError()
                size = min(5 - len(sent), len(chunk))
                sent.extend(chunk[:size])
                return size

            fake.send.side_effect = send
            server.send_frame({"timestamp": 1, "hands": []})
            server.send_frame({"timestamp": 2, "hands": []})
            server.send_frame({"timestamp": 3, "hands": []})
            fake.send.side_effect = lambda chunk: (sent.extend(chunk) or len(chunk))
            server.send_frame({"timestamp": 4, "hands": []})
            messages = [json.loads(line) for line in sent.splitlines()]
            self.assertEqual([m["timestamp"] for m in messages], [1, 4])

    def test_stalled_client_is_closed(self):
        with TcpFrameServer(port=0) as server:
            fake = MagicMock(); server.client = fake
            server.pending = b"unfinished"; server.pending_since = time.monotonic() - 2
            server.send_frame({"timestamp": 1, "hands": []})
            fake.close.assert_called_once()
            self.assertIsNone(server.client)


if __name__ == "__main__":
    unittest.main(verbosity=2)
