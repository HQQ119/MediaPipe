using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SignCoach
{
    [Serializable] public sealed class VocabularyWord
    {
        public string id, name;
        public VocabularyWord(string id, string name) { this.id = id; this.name = name; }
    }
    public static class Vocabulary
    {
        public static readonly VocabularyWord[] Words = {
            new VocabularyWord("goodbye", "再见"),
            new VocabularyWord("thanks", "谢谢"), new VocabularyWord("sorry", "对不起"),
            new VocabularyWord("eat", "吃饭"),
            new VocabularyWord("help", "帮助"), new VocabularyWord("like", "喜欢")
        };
        public static VocabularyWord GetSelectable(string id)
        {
            foreach (var word in Words) if (word.id == id) return word;
            throw new ArgumentException("该词汇不在当前学习列表中");
        }
        public static VocabularyWord Get(string id)
        {
            foreach (var word in Words) if (word.id == id) return word;
            // 仅兼容旧文件读取及既有算法回归数据，不加入学习/录制词汇表。
            if (id == "hello") return new VocabularyWord("hello", "你好");
            if (id == "drink") return new VocabularyWord("drink", "喝水");
            throw new ArgumentException("未知词汇 ID");
        }
    }
    [Serializable] public sealed class RecordedFrame
    {
        public long timestamp;
        public int frame_index;
        public HandData[] hands;
    }
    [Serializable] public class RecordingMetadata
    {
        public string word_id, word_name, take_id, created_at;
        public int frame_count, hand_frame_count, empty_hand_frame_count;
        public long duration_ms, first_timestamp, last_timestamp;
        public string source, source_reference, verification_status;
        public string coordinate_system, handedness_convention;
        public string[] quality_warnings;
    }
    [Serializable] public sealed class WordCatalog : RecordingMetadata { public RecordingMetadata[] takes; }
    public sealed class RecordingEntry { public string takeId, error; public RecordingMetadata metadata; }
    public sealed class RecordingValidation
    {
        public readonly List<string> errors = new List<string>(), warnings = new List<string>();
        public int frameCount, handFrames, emptyFrames;
        public long firstTimestamp = -1, lastTimestamp = -1;
        public bool IsValid { get { return errors.Count == 0; } }
    }
    public sealed class LoadedRecording { public RecordingMetadata metadata; public RecordedFrame[] frames; }

    public sealed class RecordingStore
    {
        public const long MinimumDurationMs = 2000;
        public const int MinimumHandFrames = 5;
        public readonly string Root;
        public string CatalogWarning { get; private set; }
        static readonly Regex TakeFile = new Regex(@"^take_(\d{3,})\.(?:jsonl|metadata\.json|pending)$");
        static readonly Regex TakeId = new Regex(@"^take_\d{3,}$");
        public RecordingStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
        public string WordDirectory(string wordId) { Vocabulary.Get(wordId); return Path.Combine(Root, wordId); }
        public string FramePath(string wordId, string takeId)
        {
            if (!TakeId.IsMatch(takeId ?? "")) throw new ArgumentException("无效 take ID");
            return Path.Combine(WordDirectory(wordId), takeId + ".jsonl");
        }
        public StreamWriter Reserve(string wordId, out string takeId, out string pending)
        {
            var folder = WordDirectory(wordId); Directory.CreateDirectory(folder);
            int number = 0;
            foreach (var file in Directory.GetFiles(folder))
            {
                var match = TakeFile.Match(Path.GetFileName(file)); int value;
                if (match.Success && int.TryParse(match.Groups[1].Value, out value)) number = Math.Max(number, value);
            }
            for (int attempt = 0; attempt < 100; attempt++)
            {
                takeId = "take_" + (++number).ToString("D3", CultureInfo.InvariantCulture);
                pending = Path.Combine(folder, takeId + ".pending");
                try { return new StreamWriter(new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)); }
                catch (IOException) { if (!File.Exists(pending)) throw; }
            }
            throw new IOException("无法分配新的 take 编号");
        }
        public static RecordingValidation Validate(string path, bool keepFrames, out RecordedFrame[] frames)
        {
            var result = new RecordingValidation(); var retained = keepFrames ? new List<RecordedFrame>() : null;
            bool edge = false, sameLabel = false, gap = false;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) throw new FormatException("JSONL 存在空行");
                        var handFrame = HandProtocol.Parse(line);
                        var stored = JsonUtility.FromJson<RecordedFrame>(line);
                        if (stored.frame_index != result.frameCount) throw new FormatException("frame_index 不连续或未从 0 开始");
                        if (result.lastTimestamp >= handFrame.timestamp) throw new FormatException("时间戳未严格递增（可能发生断线或会话重启）");
                        if (result.firstTimestamp < 0) result.firstTimestamp = handFrame.timestamp;
                        if (result.lastTimestamp >= 0 && handFrame.timestamp - result.lastTimestamp > 500) gap = true;
                        result.lastTimestamp = handFrame.timestamp;
                        if (handFrame.hands.Length == 0) result.emptyFrames++; else result.handFrames++;
                        if (handFrame.hands.Length == 2 && handFrame.hands[0].handedness == handFrame.hands[1].handedness) sameLabel = true;
                        foreach (var hand in handFrame.hands)
                        {
                            if (float.IsNaN(hand.handedness_score) || float.IsInfinity(hand.handedness_score) || hand.handedness_score < 0 || hand.handedness_score > 1)
                                throw new FormatException("左右手分类分数异常");
                            foreach (var p in hand.landmarks)
                            {
                                // 不裁剪边缘预测；明显异常阻止保存，轻微越界仅提示。
                                if (p.x < -1 || p.x > 2 || p.y < -1 || p.y > 2 || Math.Abs(p.z) > 5)
                                    throw new FormatException("坐标超出录制校验允许的宽容范围");
                                edge |= p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1;
                            }
                        }
                        result.frameCount++; if (retained != null) retained.Add(stored);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is FormatException || ex is UnauthorizedAccessException)
            { result.errors.Add(ex.Message); }
            if (result.handFrames < MinimumHandFrames) result.errors.Add("有效有手帧少于 5 帧；不能保存为空动作");
            if (result.firstTimestamp < 0 || result.lastTimestamp - result.firstTimestamp < MinimumDurationMs)
                result.errors.Add("传感器时间跨度不足 2 秒");
            if (result.frameCount > 0 && result.handFrames * 2 < result.frameCount) result.warnings.Add("超过一半帧没有检测到手");
            if (edge) result.warnings.Add("部分 x/y 在 0–1 之外，原值保留；请检查是否出画");
            if (sameLabel) result.warnings.Add("有双手同标签帧；保留模型原标签，请人工核对");
            if (gap) result.warnings.Add("相邻帧间隔超过 500ms，请检查采集连续性");
            frames = retained == null ? null : retained.ToArray(); return result;
        }
        public List<RecordingEntry> List(string wordId)
        {
            var entries = new List<RecordingEntry>(); var folder = WordDirectory(wordId);
            if (!Directory.Exists(folder)) return entries;
            foreach (var file in Directory.GetFiles(folder, "take_*.jsonl"))
            {
                var id = Path.GetFileNameWithoutExtension(file); if (!TakeId.IsMatch(id)) continue;
                var entry = new RecordingEntry { takeId = id };
                try
                {
                    entry.metadata = JsonUtility.FromJson<RecordingMetadata>(File.ReadAllText(Path.Combine(folder, id + ".metadata.json"), Encoding.UTF8));
                    if (entry.metadata == null || entry.metadata.word_id != wordId || entry.metadata.take_id != id) throw new FormatException("metadata 身份不匹配");
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is FormatException) { entry.error = ex.Message; }
                entries.Add(entry);
            }
            entries.Sort((a,b) => string.CompareOrdinal(a.takeId, b.takeId)); return entries;
        }
        public LoadedRecording Load(string wordId, string takeId)
        {
            RecordedFrame[] frames; var validation = Validate(FramePath(wordId, takeId), true, out frames);
            if (!validation.IsValid) throw new FormatException(string.Join("；", validation.errors));
            var metadata = JsonUtility.FromJson<RecordingMetadata>(File.ReadAllText(Path.Combine(WordDirectory(wordId), takeId + ".metadata.json"), Encoding.UTF8));
            if (metadata == null || metadata.word_id != wordId || metadata.take_id != takeId || metadata.frame_count != frames.Length ||
                metadata.first_timestamp != validation.firstTimestamp || metadata.last_timestamp != validation.lastTimestamp ||
                metadata.duration_ms != validation.lastTimestamp - validation.firstTimestamp)
                throw new FormatException("metadata 与 JSONL 不一致");
            return new LoadedRecording { metadata = metadata, frames = frames };
        }
        public void Commit(string pending, RecordingMetadata metadata)
        {
            var path = FramePath(metadata.word_id, metadata.take_id);
            var sidecar = Path.Combine(WordDirectory(metadata.word_id), metadata.take_id + ".metadata.json");
            if (File.Exists(path) || File.Exists(sidecar)) throw new IOException("目标 take 已存在，拒绝覆盖");
            // 数据本体与每次录制的 metadata 均采用不覆盖写入；索引可由它们重建。
            var temporaryMetadata = sidecar + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryMetadata, JsonUtility.ToJson(metadata, true), new UTF8Encoding(false));
                File.Move(temporaryMetadata, sidecar);
            }
            finally { if (File.Exists(temporaryMetadata)) File.Delete(temporaryMetadata); }
            try { File.Move(pending, path); }
            catch { File.Delete(sidecar); throw; }
            CatalogWarning = null;
            try { RebuildCatalog(metadata.word_id); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { CatalogWarning = "录制及独立 metadata 已保存，但目录索引更新失败：" + ex.Message; }
        }
        public void Delete(string wordId, string takeId, bool confirmed)
        {
            if (!confirmed) throw new InvalidOperationException("删除必须二次确认");
            File.Delete(FramePath(wordId, takeId));
            File.Delete(Path.Combine(WordDirectory(wordId), takeId + ".metadata.json")); RebuildCatalog(wordId);
        }
        public void RebuildCatalog(string wordId)
        {
            var word = Vocabulary.Get(wordId); var entries = List(wordId);
            var good = new List<RecordingMetadata>(); foreach (var item in entries) if (item.error == null && item.metadata != null) good.Add(item.metadata);
            // 根 metadata 展示最新一次摘要；takes 保存每次录制的完整 metadata。
            var catalog = good.Count == 0 ? new WordCatalog() : JsonUtility.FromJson<WordCatalog>(JsonUtility.ToJson(good[good.Count - 1]));
            catalog.word_id = word.id; catalog.word_name = word.name; catalog.takes = good.ToArray();
            var path = Path.Combine(WordDirectory(wordId), "metadata.json"); Directory.CreateDirectory(WordDirectory(wordId));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
