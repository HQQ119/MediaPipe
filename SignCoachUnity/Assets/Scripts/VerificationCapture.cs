using System;
using System.IO;
using UnityEngine;

namespace SignCoach
{
    [Serializable] public sealed class RecordingVerificationCommand { public string action, word_id, take_id, other_take_id, page, path; public bool acknowledge;public int hand_mode,reference_hand,practice_hand,scoring_profile;public bool mirror_reference; }
    [Serializable] public sealed class VerificationState
    {
        public long applied, elapsed_ms;
        public int visible, recorded_frames, loaded_frames;
        public bool recording, draft;
        public string word_id, message, last_saved_take, root;
        public bool comparison_busy, comparison_ready, comparison_low_quality;
        public string page;public bool video_ready;public long video_frame;public bool preview_fresh;public string teaching_material,teaching_status,preview_path,video_url,recording_status,selected_practice,comparison_input_a,comparison_input_b,vision_notice;
        public bool release_build;
        public string comparison_score_text,comparison_score_scope;
        public string comparison_message,comparison_hand_pairing;
        public bool comparison_score_available;
    }
    // 仅显式传入验证参数时启用，不参与 TCP 协议或正常显示逻辑。
    public sealed class VerificationCapture : MonoBehaviour
    {
        SignCoachDebug view;
        StreamWriter log;
        long previous = -1;
        float started;
        string folder;
        int previousVisible = -1;
        SignCoach.Comparison.ComparisonResult exportedComparison;
        public static void AttachIfRequested(GameObject target)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-signcoachEvidence") >= 0) target.AddComponent<VerificationCapture>();
        }
        void Start()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-signcoachEvidence");
            folder = args[i + 1]; Directory.CreateDirectory(folder);
            log = new StreamWriter(Path.Combine(folder, "unity_applied.jsonl"), false) { AutoFlush = true };
            view = GetComponent<SignCoachDebug>(); started = Time.realtimeSinceStartup;
        }
        void LateUpdate()
        {
            var commandPath = Path.Combine(folder, "recording.request");
            if (File.Exists(commandPath))
            {
                string raw;
                // 验收控制器正在原子替换指令时，延后一帧读取，避免Windows共享锁竞争。
                try { raw = File.ReadAllText(commandPath); File.Delete(commandPath); }
                catch(IOException) { return; }
                try
                {
                    var command = JsonUtility.FromJson<RecordingVerificationCommand>(raw);
                    var panel = view.Recording;
                    switch (command.action)
                    {
                        case "select": panel.SelectWord(command.word_id); break;
                        case "start": panel.Begin(); break;
                        case "stop": panel.StopRecording(); break;
                        case "save": panel.SaveRecording(command.acknowledge); break;
                        case "cancel": panel.CancelRecording(); break;
                        case "rerecord": panel.Rerecord(); break;
                        case "load": panel.LoadRecording(command.take_id); break;
                        case "delete_request": panel.RequestDelete(command.take_id); break;
                        case "delete_confirm": panel.ConfirmDelete(); break;
                        case "compare": view.Comparison.OpenAndCompare(command.word_id,command.take_id,command.word_id,command.other_take_id); break;
                        case "comparison_diagnostics": view.Comparison.ShowDiagnostic=command.acknowledge; break;
                        case "comparison_close": view.Comparison.SetOpen(false); break;
                        case "navigate": view.App.Navigate(command.page); break;
                        case "word": view.App.SelectWord(command.word_id); break;
                        case "video_test": view.App.LoadDiagnosticVideo(command.path); break;
                        case "video_play": view.App.VideoControl("play"); break;
                        case "video_pause": view.App.VideoControl("pause"); break;
                        case "video_slow": view.App.VideoControl("slow"); break;
                        case "vision": view.App.StartVision(); break;
                        case "material": view.App.Teaching.SelectMaterial(command.take_id); break;
                        case "guided": view.App.Teaching.VerificationPractice(true); break;
                        case "learn_practice": view.App.Teaching.VerificationPractice(false); break;
                        case "replay": view.App.ReplayVideo(); break;
                        case "standard_compare": view.App.Teaching.CompareSaved(); break;
                        case "compare_latest": panel.CompareLatest(); break;
                        case "compare_selected": view.Comparison.BeginSelected(); break;
                        case "practice_take": view.Comparison.SelectPractice(command.word_id,command.take_id);break;
                        case "comparison_hands": view.Comparison.ConfigureHands((SignCoach.Comparison.ComparisonHands)command.hand_mode,command.reference_hand,command.practice_hand,command.mirror_reference);break;
                        case "comparison_profile": view.Comparison.ConfigureScoring((SignCoach.Comparison.ComparisonScoringProfile)command.scoring_profile);break;
                        case "camera_unavailable": view.App.VerifyUnavailableCamera(); break;
                        default: throw new ArgumentException("Unknown verification action");
                    }
                    File.WriteAllText(Path.Combine(folder, "recording_command_result.json"), "{\"ok\":true}");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(folder, "recording_command_result.json"), JsonUtility.ToJson(new VerificationState { message = ex.Message }));
                }
            }
            if (previous != view.AppliedFrames)
            {
                previous = view.AppliedFrames;
                if (view.Current != null) log.WriteLine(JsonUtility.ToJson(view.Current));
            }
            // 每秒状态与渲染对象可见数，用于验证无手、超时和断线清除。
            if (Time.frameCount % 30 == 0 || previousVisible != view.VisibleHands)
            {
                previousVisible = view.VisibleHands;
                var rec = view.Recording.Recorder.Snapshot();
                File.WriteAllText(Path.Combine(folder, "unity_state.json"), JsonUtility.ToJson(new VerificationState {
                    applied = view.AppliedFrames, visible = view.VisibleHands, recording = rec.recording, draft = rec.hasDraft,
                    recorded_frames = rec.frames, elapsed_ms = rec.elapsedMs, word_id = view.Recording.SelectedWordId,
                    message = view.Recording.Message, last_saved_take = view.Recording.LastSaved == null ? null : view.Recording.LastSaved.take_id,
                    loaded_frames = view.Recording.LastLoaded == null ? 0 : view.Recording.LastLoaded.frames.Length,
                    root = view.Recording.Store.Root
                    ,vision_notice=view.App.Notice,recording_status=view.Recording.StatusText,selected_practice=view.Comparison.SelectedPractice,comparison_input_a=view.Comparison.ResultInputA,comparison_input_b=view.Comparison.ResultInputB,video_url=view.App.LoadedVideoUrl,preview_path=view.App.Teaching.Preview.Path,preview_fresh=view.App.Teaching.Preview.Fresh,teaching_material=view.App.Teaching.Selected==null?null:view.App.Teaching.Selected.id,teaching_status=view.App.Teaching.Selected==null?null:view.App.Teaching.Catalog.Status(view.App.Teaching.Selected),comparison_busy = view.Comparison.Busy,comparison_ready = view.Comparison.Result != null,
                    comparison_low_quality = view.Comparison.Result != null && view.Comparison.Result.low_quality
                    ,comparison_score_text=view.Comparison.Result==null?null:SignCoach.Comparison.ComparisonScoreView.ScoreText(view.Comparison.Result),comparison_score_scope=view.Comparison.Result==null?null:SignCoach.Comparison.ComparisonScoreView.ScopeText(view.Comparison.Result)
                    ,comparison_message=view.Comparison.Message,comparison_hand_pairing=view.Comparison.Result==null?null:view.Comparison.Result.hand_pairing,comparison_score_available=view.Comparison.Result!=null&&view.Comparison.Result.score_available
                    ,page=view.App.Page,video_ready=view.App.VideoReady,video_frame=view.App.VideoFrame,release_build=!Debug.isDebugBuild
                }));
            }
            if(view.Comparison.Result!=null&&!ReferenceEquals(exportedComparison,view.Comparison.Result))
            {
                exportedComparison=view.Comparison.Result;
                File.WriteAllText(Path.Combine(folder,"comparison_result.json"),exportedComparison.ToJson());
            }
            if (Time.realtimeSinceStartup - started > 2 && !File.Exists(Path.Combine(folder, "unity_started.png")))
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "unity_started.png"));
            var request = Path.Combine(folder, "snapshot.request");
            if (File.Exists(request))
            {
                File.Delete(request);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "unity_snapshot_" + Time.frameCount + ".png"));
            }
            // 测试脚本建立此文件后，主线程正常退出以检查 TCP 线程清理。
            if (File.Exists(Path.Combine(folder, "quit.request"))) Application.Quit();
        }
        void OnDestroy() { if (log != null) log.Dispose(); }
    }
}
