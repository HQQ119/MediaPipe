"""接口、清理和真实 CPU 推理测试；不把模拟结果当成摄像头验收。"""
import argparse
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch, MagicMock
import cv2
import numpy as np
from camera import Camera
from data_processor import process_result, to_json
from hand_detector import HandDetector
from renderer import draw_frame
import config
import main


def fixture_result(count):
    points = [SimpleNamespace(x=i / 30, y=i / 25, z=-i / 100) for i in range(21)]
    return SimpleNamespace(hand_landmarks=[points for _ in range(count)], handedness=[
        [SimpleNamespace(category_name=label, score=.95)] for label in ("Left", "Right")[:count]])


class DataTests(unittest.TestCase):
    def test_empty_one_two_hands_json(self):
        for count in (0, 1, 2):
            data = json.loads(to_json(process_result(fixture_result(count), 123)))
            self.assertEqual(data["timestamp"], 123)
            self.assertEqual(len(data["hands"]), count)
            for hand in data["hands"]:
                self.assertEqual([p["id"] for p in hand["landmarks"]], list(range(21)))
                self.assertEqual(hand["landmarks"][20]["z"], -.2)
                self.assertEqual(hand["handedness_score"], .95)
        self.assertEqual(process_result(fixture_result(2), 1)["hands"][0]["handedness"], "Left")

    def test_bad_landmarks_rejected(self):
        result = fixture_result(1)
        result.hand_landmarks[0].pop()
        with self.assertRaises(ValueError):
            process_result(result, 1)
        result = fixture_result(1)
        result.hand_landmarks[0][0].x = float("nan")
        with self.assertRaises(ValueError):
            process_result(result, 2)

    def test_mirror_does_not_mutate_data(self):
        raw = np.zeros((480, 640, 3), np.uint8)
        data = process_result(fixture_result(2), 1)
        before = to_json(data)
        rendered = draw_frame(raw, data, 30)
        self.assertEqual(rendered.shape, raw.shape)
        self.assertGreater(int(rendered.sum()), 0)
        self.assertEqual(to_json(data), before)

    def test_camera_failure_releases_handle(self):
        with patch("camera.cv2.VideoCapture") as factory:
            factory.return_value.isOpened.return_value = False
            with self.assertRaisesRegex(RuntimeError, "无法打开摄像头"):
                Camera()
            self.assertEqual(factory.return_value.release.call_count, factory.call_count)

    def test_status_panel_top_right_preserves_left_view(self):
        for width in (640, 1280):
            with self.subTest(width=width):
                raw = np.full((480, width, 3), (40, 80, 120), np.uint8)
                data = process_result(fixture_result(0), 1)
                before = to_json(data)
                rendered = draw_frame(raw, data, 30)
                # 左上原画面不再被状态框覆盖；右上角是状态框背景。
                np.testing.assert_array_equal(rendered[:121, :width - 226], cv2.flip(raw, 1)[:121, :width - 226])
                np.testing.assert_array_equal(rendered[0, width - 226:width], np.tile((20, 20, 20), (226, 1)))
                # 状态文字随框一起移动，而不是只移动黑色背景。
                self.assertTrue(np.any(rendered[10:115, width - 216:width - 5] > 20))
                self.assertEqual(to_json(data), before)

    def test_window_close_releases_actual_resource_wrappers(self):
        with patch("camera.cv2.VideoCapture") as factory:
            capture = factory.return_value
            capture.isOpened.return_value = True
            capture.read.return_value = (True, np.zeros((480, 640, 3), np.uint8))
            camera = Camera()
        # 使用真实的 __exit__/close，只有底层摄像头和模型句柄为 mock。
        detector = HandDetector.__new__(HandDetector)
        detector._landmarker = MagicMock()
        model = detector._landmarker
        detector.detect = MagicMock(return_value=fixture_result(2))
        args = argparse.Namespace(download_model=False, camera=0, backend="auto", model=config.MODEL_PATH,
                                  json_output=None, no_display=False, max_frames=0)
        with patch("main.Camera", return_value=camera), patch("main.HandDetector", return_value=detector), \
                patch("main.cv2.imshow"), patch("main.cv2.waitKey", return_value=-1), \
                patch("main.cv2.getWindowProperty", return_value=0) as visible, \
                patch("main.cv2.destroyAllWindows") as destroy:
            self.assertEqual(main.run(args), 0)
        visible.assert_called_once_with(config.WINDOW_NAME, cv2.WND_PROP_VISIBLE)
        detector.detect.assert_called_once()
        capture.release.assert_called_once()
        model.close.assert_called_once()
        destroy.assert_called_once()
        self.assertIsNone(camera.capture)
        self.assertIsNone(detector._landmarker)

    def test_q_and_read_failure_cleanup(self):
        for fail in (False, True):
            with self.subTest(read_failure=fail), patch("main.Camera") as camera, patch("main.HandDetector") as detector, \
                    patch("main.cv2.imshow"), patch("main.cv2.waitKey", return_value=ord("q")), \
                    patch("main.cv2.destroyAllWindows") as destroy:
                camera.return_value.__enter__.return_value.read.return_value = np.zeros((480, 640, 3), np.uint8)
                if fail:
                    camera.return_value.__enter__.return_value.read.side_effect = RuntimeError("断开")
                detector.return_value.__enter__.return_value.detect.return_value = fixture_result(0)
                args = argparse.Namespace(download_model=False, camera=0, backend="auto", model=config.MODEL_PATH,
                                          json_output=None, no_display=False, max_frames=0)
                if fail:
                    with self.assertRaises(RuntimeError):
                        main.run(args)
                else:
                    self.assertEqual(main.run(args), 0)
                camera.return_value.__exit__.assert_called_once()
                detector.return_value.__exit__.assert_called_once()
                destroy.assert_called_once()


@unittest.skipUnless(config.MODEL_PATH.is_file(), "需先下载官方模型")
class InferenceTests(unittest.TestCase):
    def test_real_cpu_empty_and_timestamp(self):
        detector = HandDetector()
        with detector:
            for timestamp in (0, 33, 66):
                result = detector.detect(np.zeros((480, 640, 3), np.uint8), timestamp)
                self.assertEqual(process_result(result, timestamp)["hands"], [])
            with self.assertRaises(ValueError):
                detector.detect(np.zeros((480, 640, 3), np.uint8), 66)
        self.assertIsNone(detector._landmarker)
        detector.close()

    def test_official_images_if_present(self):
        folder = config.ROOT / "output" / "test_assets"
        if not all((folder / name).is_file() for name in ("thumb_up.jpg", "right_hands.jpg")):
            self.skipTest("未下载官方测试图片")
        for name, expected in (("thumb_up.jpg", 1), ("right_hands.jpg", 2)):
            image = cv2.imdecode(np.fromfile(str(folder / name), np.uint8), cv2.IMREAD_COLOR)
            with HandDetector() as detector:
                data = process_result(detector.detect(image, 0), 0)
            self.assertEqual(len(data["hands"]), expected)
            for hand in data["hands"]:
                self.assertEqual(len(hand["landmarks"]), 21)
                self.assertIn(hand["handedness"], ("Left", "Right"))
            (folder.parent / f"{Path(name).stem}_result.json").write_text(to_json(data), encoding="utf-8")


if __name__ == "__main__":
    unittest.main(verbosity=2)
