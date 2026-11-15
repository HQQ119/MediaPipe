"""下载明确以 CC BY 4.0 发布的词目图像序列；保留已有下载记录。

仅访问 Figshare 公开文件，不下载需申请的完整原始视频。源图像采样时基
未明确，所以生成的 5fps 视频仅供审核预览，禁止当作原始动作时间轴。
"""
from pathlib import Path
import io
import json
import urllib.request
import zipfile
import hashlib
import subprocess
import imageio_ffmpeg
import argparse

ROOT = Path(__file__).resolve().parent.parent
RESEARCH = ROOT / "SignCoachUnity/TeachingMaterials/research"
ASSETS = ROOT / "SignCoachUnity/Assets/StreamingAssets/Teaching"


class PublicZip(io.RawIOBase):
    size = 1872346523
    central_start = 1850140283
    url = "https://ndownloader.figshare.com/files/53410652"

    def __init__(self):
        self.pos = 0
        self.blocks = [(self.central_start, (RESEARCH / "participant01_central.bin").read_bytes()),
                       (self.size - 65536, (RESEARCH / "large_tail.bin").read_bytes())]

    def tell(self):
        return self.pos

    def seek(self, offset, whence=0):
        self.pos = offset if whence == 0 else self.pos + offset if whence == 1 else self.size + offset
        return self.pos

    def fetch(self, start, end):
        request = urllib.request.Request(self.url + f"?range={start}", headers={"Range": f"bytes={start}-{end}"})
        with urllib.request.urlopen(request, timeout=40) as response:
            if response.status != 206 or not response.headers.get("Content-Range", "").startswith(f"bytes {start}-"):
                raise ValueError("公开文件未返回所请求范围")
            data = response.read()
        if len(data) != end - start + 1:
            raise ValueError("下载长度不符")
        self.blocks.insert(0, (start, data))

    def read(self, length=-1):
        for start, data in self.blocks:
            if start <= self.pos < start + len(data):
                i = self.pos - start
                result = data[i:] if length < 0 else data[i:i + length]
                self.pos += len(result)
                return result
        raise ValueError("未缓存的 ZIP 区段")


def main():
    words = {"goodbye": ("4046", "再见"), "thanks": ("1325", "谢谢"),
             "sorry": ("5311", "对不起"), "help": ("5094", "帮助"),
             "like": ("2462", "喜欢"), "eat": ("3388", "吃饭")}
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--word", action="append", choices=words, help="仅补充指定词汇，可重复")
    args = parser.parse_args()
    publication = json.loads((RESEARCH / "figshare_27261843.json").read_text(encoding="utf-8"))
    if publication["license"]["name"] != "CC BY 4.0":
        raise ValueError("公开发布许可发生变化，停止处理")
    remote = PublicZip()
    manifest = RESEARCH / "candidate_acquisition.json"
    report = json.loads(manifest.read_text(encoding="utf-8")) if manifest.exists() else []
    existing = {item["word_id"] for item in report}
    with zipfile.ZipFile(remote) as archive:
        for word in args.word or words:
            if word in existing:
                print(word, "already recorded; kept unchanged", flush=True)
                continue
            gloss, name = words[word]
            images = sorted([x for x in archive.infolist() if x.filename.startswith(f"Participant_01/front/{gloss}/") and x.filename.endswith(".jpg")], key=lambda x: x.filename)
            if not images:
                raise ValueError(f"公开档案中没有词目 {gloss}")
            start = min(x.header_offset for x in images)
            end = max(x.header_offset + x.compress_size + len(x.filename.encode()) + 256 for x in images)
            remote.fetch(start, end)
            folder = RESEARCH / "original_frames" / word
            folder.mkdir(parents=True, exist_ok=True)
            hashes = {}
            for item in images:
                data = archive.read(item)  # zipfile 同时验证 CRC
                (folder / Path(item.filename).name).write_bytes(data)
                hashes[item.filename] = hashlib.sha256(data).hexdigest()
            target = ASSETS / "data/reference/pending_review" / word
            target.mkdir(parents=True, exist_ok=True)
            video = target / "candidate_preview.mp4"
            if not video.exists():
                subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-framerate", "5", "-i", str(folder / "%05d.jpg"), "-c:v", "libx264", "-profile:v", "baseline", "-bf", "0", "-pix_fmt", "yuv420p", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-movflags", "+faststart", str(video)], check=True)
            record = dict(word_id=word, word_name=name, gloss_id=gloss, participant="Participant_01", perspective="front", source_frame_count=len(images), frame_hashes=hashes,
                          source_url="https://figshare.com/articles/media/NationalCSL-DP/27261843", archive_file_id=53410652, license="CC BY 4.0", source_fps=None, preview_fps=5,
                          timing_verified=False, mirror_verified=False, verification_status="pending_review", video_path=str(video.relative_to(ASSETS)).replace("\\", "/"),
                          transformation="Published centre-cropped 256x256 frames assembled at 5 fps for review only; original sampling interval unknown. No generated, altered or interpolated gestures.")
            report.append(record)
            if word == "eat":
                record.update(source_word="饭", mapping_basis="GF0020-2018 索引：吃、吃饭、饭均指向词目页142；词典条目饭（吃、吃饭）。仅用于动作观察和序列比较。",
                              mapping_source="https://www.moe.gov.cn/jyb_xwfb/moe_2082/siju/yywzxxgls/ztzl_yywzgfbz/201807/W020180725666828831381.pdf",
                              mapping_evidence_pages=[20,25])
            print(word, len(images), "source frames; review preview only", flush=True)
    manifest.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
