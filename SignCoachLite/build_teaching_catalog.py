"""将核实的来源与有许可的本地片段编入教学库；不修改录制或认证素材。"""
from pathlib import Path
import json
from extract_teaching_video import extract

ROOT = Path(__file__).resolve().parent.parent / "SignCoachUnity"
ASSETS = ROOT / "Assets/StreamingAssets/Teaching"
RESEARCH = ROOT / "TeachingMaterials/research"


def main():
    acquisitions = {x["word_id"]: x for x in json.loads((RESEARCH / "candidate_acquisition.json").read_text(encoding="utf-8"))}
    items = []
    words = [("goodbye", "再见", "问候"), ("thanks", "谢谢", "礼貌"), ("sorry", "对不起", "礼貌"), ("eat", "吃饭", "日常需求"), ("help", "帮助", "日常交流"), ("like", "喜欢", "日常交流")]
    legal = ("常用手语教学｜第一集——你好、谢谢、对不起", "https://www.bilibili.com/video/BV1Rs4y1v7sg/", "手护有你（页面简介称西南政法大学相关团队）", "B", "词汇见视频标题；专业规范依据及演示质量待核。页面直接访问412，检索索引可见，未下载。")
    course = lambda part: ("大家学手语100句 · " + part, "https://www.bilibili.com/video/BV1vv4y1C7QV/", "锻炼身心（上传者）；页面称葛玉红课程", "B", "章节标题符合候选主题，具体独立词汇片段待人工定位；未核实上传授权，未下载。")
    national = ("NationalCSL-DP · Participant_01 · front", "https://figshare.com/articles/media/NationalCSL-DP/27261843", "Siyuan Jing / NationalCSL-DP 研究团队", "A", "官方数据发布和论文可交叉核验；词表精确对应。采样图片预览不具有已确认原始时基，镜像未知，项目尚未专业审核。")
    sources = {
        "goodbye": [national, course("第9课 道别")],
        "thanks": [legal, ("指趣小课堂：国家通用手语（十六）", "https://www.zcse.edu.cn/info/1696/50291.htm", "浙江特殊教育职业学院特殊教育资源中心、特殊教育系", "A", "页面对话明确包含谢谢；属于情景对话而非独立单词。许可未知，仅原站观看。")],
        "sorry": [legal, course("第8课 道歉")],
        "help": [national, course("第13课 请求帮助 / 第14课 提供帮助")],
        "like": [national, ("指趣小课堂：国家通用手语（二十三）", "https://www.zcse.edu.cn/info/1696/53291.htm", "浙江特殊教育职业学院特殊教育资源中心、特殊教育系", "A", "页面转写明确含喜欢；情景句子，待定位词汇片段。许可未知，仅原站观看。")]
    }
    sources['thanks']=[national,legal]
    sources['sorry']=[national,legal]
    sources['eat']=[national,("指趣小课堂：国家通用手语（二十二）晚饭吃什么", "https://www.zcse.edu.cn/info/1696/53041.htm", "浙江特殊教育职业学院", "A", "完整情景对话含吃、晚饭等词；仅原站观看，不冒充单词吃饭的独立示范。")]
    for word, name, category in words:
        for i, source in enumerate(sources[word]):
            title, url, publisher, grade, notes = source
            local = word in acquisitions and i == 0
            item = dict(id=word + "_" + str(i + 1), word_id=word, word_name=name, category=category,
                        source_name=title, source_url=url, platform="Figshare" if local else "高校官网" if "zcse" in url else "Bilibili",
                        publisher=publisher, teacher_or_performer="Participant_01（实名未知）" if local else None,
                        source_type="research_dataset" if local else "public_teaching_candidate", credibility=grade,
                        sign_language_system="国家通用手语（来源声明）" if local or "zcse" in url else None,
                        license="CC BY 4.0" if local else None, license_url="https://creativecommons.org/licenses/by/4.0/" if local else None,
                        processing_allowed=local, redistribution_allowed=local, verification_status="pending_review",
                        review_notes=notes, inspected_at="2026-11-11", video_path=None, landmark_path=None,
                        frame_count=0, duration_ms=None, width=None, height=None, fps=None, source_fps=None,
                        timing_verified=False, mirror_status="unknown", full_action_visible=None, occlusion=None,
                        face_information_available=False, pose_information_available=False, nonmanual_requirement="尚待专业审核；当前仅检测手部",
                        handshape_notes=None, orientation_notes=None, location_notes=None, trajectory_notes=None,
                        annotation_source=None, extraction_status="not_authorized_or_not_downloaded", coordinate_system=None, handedness_convention=None)
            if local:
                acquisition = acquisitions[word]
                item["video_path"] = acquisition["video_path"]
                video = ASSETS / item["video_path"]
                output = video.parent / "candidate_001.jsonl"
                if not output.exists():
                    report = extract(video, output)
                    output.with_suffix(".extraction.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
                report = json.loads(output.with_suffix(".extraction.json").read_text(encoding="utf-8"))
                item.update({k: report[k] for k in ["frame_count", "duration_ms", "fps", "width", "height", "coordinate_system", "handedness_convention"]})
                item.update(landmark_path=str(output.relative_to(ASSETS)).replace("\\", "/"), extraction_status="preview_timebase_only", preview_fps=5,
                            transformation=acquisition["transformation"], source_gloss_id=acquisition["gloss_id"])
                if word == "eat":
                    item.update(source_word=acquisition["source_word"], mapping_basis=acquisition["mapping_basis"],
                                mapping_source=acquisition["mapping_source"], mapping_evidence_pages=acquisition["mapping_evidence_pages"],
                                annotation_source=acquisition["mapping_source"], inspected_at="2026-11-15")
                (video.parent / "metadata.json").write_text(json.dumps(item, ensure_ascii=False, indent=2), encoding="utf-8")
            items.append(item)
    (ASSETS / "catalog.json").write_text(json.dumps(dict(schema_version=1, items=items), ensure_ascii=False, indent=2), encoding="utf-8")
    for word, _, _ in words:
        folder = ASSETS / "data/reference/pending_review" / word
        folder.mkdir(parents=True, exist_ok=True)
        if word not in acquisitions:
            record = next(x.copy() for x in items if x["word_id"] == word)
            record["candidate_ids"] = [x["id"] for x in items if x["word_id"] == word]
            (folder / "metadata.json").write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    for category in ["verified", "unverified"]:
        p = ASSETS / "data/reference" / category
        p.mkdir(parents=True, exist_ok=True)
        (p / ".gitkeep").touch()
    print("Recorded",len(items),"source records;",len(acquisitions),"local word videos.")


if __name__ == "__main__":
    main()
