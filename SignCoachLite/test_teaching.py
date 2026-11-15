"""预览隔离、源数据及真实候选提取结果的回归检查。"""
from pathlib import Path
import json
import struct
import tempfile
import unittest
import hashlib
import numpy as np
import cv2
from frame_preview import FramePreview
from extract_teaching_video import extract


class TeachingTests(unittest.TestCase):
    def test_preview_uses_raw_pixels_and_cleans_up(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "preview.bin"
            image = np.zeros((80, 120, 3), np.uint8)
            image[:, :60] = (0, 0, 255)
            with FramePreview(path) as preview:
                preview.publish(image, 1234)
                data = path.read_bytes()
                self.assertEqual(struct.unpack("<q", data[:8])[0], 1234)
                decoded = cv2.imdecode(np.frombuffer(data[8:], np.uint8), cv2.IMREAD_COLOR)
                self.assertGreater(int(decoded[40, 15, 2]), 230)
                self.assertLess(int(decoded[40, 105, 2]), 20)  # 未在 Python 预览里重复镜像
                preview.publish(image, 9999)
                self.assertEqual(path.read_bytes(), data)  # 节流不影响推理帧
            self.assertFalse(path.exists())

    def test_existing_jsonl_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "keep.jsonl"
            path.write_text("original", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                extract("missing.mp4", path)
            self.assertEqual(path.read_text(), "original")

    def test_invalid_video_does_not_create_valid_sequence(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "empty.jsonl"
            with self.assertRaises(ValueError):
                extract(Path(folder) / "missing.mp4", path)
            self.assertFalse(path.exists())

    def test_actual_public_candidates_keep_complete_frames(self):
        root = Path(__file__).resolve().parent.parent / "SignCoachUnity/Assets/StreamingAssets/Teaching"
        catalog = json.loads((root / "catalog.json").read_text(encoding="utf-8"))
        self.assertEqual({item['word_id'] for item in catalog['items']}, {'goodbye', 'thanks', 'sorry', 'eat', 'help', 'like'})
        local = [x for x in catalog["items"] if x["landmark_path"]]
        self.assertEqual({item['word_id'] for item in local}, {'goodbye', 'thanks', 'sorry', 'eat', 'help', 'like'})
        # 避免不同词条再次绑定同一个视频或同一份关键点序列。
        self.assertEqual(len({hashlib.sha256((root / item['video_path']).read_bytes()).digest() for item in local}), len(local))
        self.assertEqual(len({hashlib.sha256((root / item['landmark_path']).read_bytes()).digest() for item in local}), len(local))
        for item in local:
            data = [json.loads(line) for line in (root / item["landmark_path"]).read_text(encoding="utf-8").splitlines()]
            self.assertEqual(len(data), item["frame_count"])
            self.assertEqual([x["frame_index"] for x in data], list(range(len(data))))
            self.assertTrue(all(a["timestamp"] < b["timestamp"] for a, b in zip(data, data[1:])))
            self.assertTrue(all(len(h["landmarks"]) == 21 for frame in data for h in frame["hands"]))
            self.assertFalse(item["timing_verified"])
            self.assertEqual(item["mirror_status"], "unknown")
            self.assertEqual(item["verification_status"], "pending_review")
            self.assertTrue(any(x["hands"] for x in data))  # 实际视频确有模型检出

        eat = next(item for item in local if item['word_id'] == 'eat')
        self.assertEqual(eat['source_gloss_id'], '3388')
        self.assertEqual(eat['source_word'], '饭')
        self.assertIn('moe.gov.cn/', eat['mapping_source'])
        self.assertEqual(eat['mapping_evidence_pages'], [20, 25])


if __name__ == "__main__":
    unittest.main()
