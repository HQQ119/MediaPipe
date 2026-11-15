using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SignCoach
{
    public sealed class RecorderSnapshot
    {
        public bool recording, hasDraft;
        public int frames;
        public long elapsedMs;
        public string problem, takeId;
    }
    public sealed class ActionRecorder : IDisposable
    {
        readonly object gate = new object();
        readonly RecordingStore store;
        readonly Stopwatch elapsed = new Stopwatch();
        StreamWriter writer;
        string pending, takeId, wordId, sourceReference, createdAt, problem;
        int count;
        long lastTimestamp = -1;
        long lastReceivedAt;
        string timingWarning;
        RecordingValidation validation;
        public ActionRecorder(RecordingStore store) { this.store = store; }
        public RecorderSnapshot Snapshot()
        {
            lock (gate) return new RecorderSnapshot { recording = writer != null, hasDraft = pending != null,
                frames = count, elapsedMs = elapsed.ElapsedMilliseconds, problem = problem, takeId = takeId };
        }
        public void Start(string selectedWordId, string reference)
        {
            lock (gate)
            {
                if (pending != null) throw new InvalidOperationException("请先保存或取消当前录制");
                Vocabulary.GetSelectable(selectedWordId);
                writer = store.Reserve(selectedWordId, out takeId, out pending);
                wordId = selectedWordId; sourceReference = reference ?? "";
                createdAt = DateTime.UtcNow.ToString("o"); count = 0; lastTimestamp = -1;
                problem = null; validation = null; elapsed.Restart();
                lastReceivedAt = 0; timingWarning = null;
            }
        }
        // 从 TCP 接收线程调用；独立于绘制的 latest mailbox，不漏掉同次接收中的中间帧。
        public void Receive(HandFrame frame, string originalJson)
        {
            lock (gate)
            {
                if (writer == null || problem != null) return;
                if (frame.timestamp <= lastTimestamp) { problem = "录制时间戳重置，请取消后重新录制"; return; }
                try
                {
                    int first = originalJson.IndexOf('{');
                    if (first < 0) throw new FormatException("无效 JSON 对象");
                    // 仅添加存储层 frame_index；复制原始 JSON，保留坐标精度、null 分数和空 hands。
                    writer.WriteLine(originalJson.Insert(first + 1, "\"frame_index\":" + count + ","));
                    count++; lastTimestamp = frame.timestamp; lastReceivedAt = elapsed.ElapsedMilliseconds;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
                { problem = "录制写入失败：" + ex.Message; }
            }
        }
        public void Interrupt(string reason)
        {
            lock (gate) { if (writer != null && problem == null) problem = "录制中断：" + reason + "；请取消后重录"; }
        }
        public RecordingValidation Stop()
        {
            lock (gate)
            {
                if (writer == null) throw new InvalidOperationException("当前未录制");
                writer.Dispose(); writer = null; elapsed.Stop();
                RecordedFrame[] unused; validation = RecordingStore.Validate(pending, false, out unused);
                if (elapsed.ElapsedMilliseconds - lastReceivedAt > 500)
                {
                    timingWarning = "停止前超过 500ms 没有收到新帧，文件只包含实际收到的数据";
                    validation.warnings.Add(timingWarning);
                }
                if (problem != null) validation.errors.Add(problem);
                return validation;
            }
        }
        public RecordingMetadata Save(bool acknowledgeWarnings)
        {
            lock (gate)
            {
                if (writer != null || pending == null || validation == null) throw new InvalidOperationException("请先停止录制");
                // 保存前重新解析文件，避免只校验内存计数或只保存最后一帧。
                RecordedFrame[] unused; var final = RecordingStore.Validate(pending, false, out unused);
                if (problem != null) final.errors.Add(problem);
                if (timingWarning != null) final.warnings.Add(timingWarning);
                if (!final.IsValid) throw new InvalidOperationException(string.Join("；", final.errors));
                if (final.warnings.Count > 0 && !acknowledgeWarnings) throw new InvalidOperationException("存在质量提示，请明确确认后保存测试数据");
                var word = Vocabulary.Get(wordId);
                var metadata = new RecordingMetadata { word_id = word.id, word_name = word.name, take_id = takeId,
                    created_at = createdAt, frame_count = final.frameCount, hand_frame_count = final.handFrames,
                    empty_hand_frame_count = final.emptyFrames, first_timestamp = final.firstTimestamp, last_timestamp = final.lastTimestamp,
                    duration_ms = final.lastTimestamp - final.firstTimestamp, source = "live_camera_unverified",
                    source_reference = sourceReference, verification_status = "unverified_test",
                    coordinate_system = "raw_unmirrored_normalized_image_xy; y_down; mediapipe_relative_z; original_session_timestamp_ms",
                    handedness_convention = "model_output_unchanged; no_position_guessing; no_smoothing",
                    quality_warnings = final.warnings.ToArray() };
                store.Commit(pending, metadata); pending = null; validation = null; return metadata;
            }
        }
        public void Cancel()
        {
            lock (gate)
            {
                if (writer != null) { writer.Dispose(); writer = null; }
                if (pending != null && File.Exists(pending)) File.Delete(pending);
                pending = null; validation = null; elapsed.Reset(); count = 0; problem = null;
            }
        }
        public void Dispose() { Cancel(); }
    }
}
