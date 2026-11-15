using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SignCoach
{
    public sealed class RecordingPanel : MonoBehaviour
    {
        public const float PanelWidth = 374;
        public RecordingStore Store { get; private set; }
        public ActionRecorder Recorder { get; private set; }
        public string SelectedWordId { get { return Vocabulary.Words[selected].id; } }
        public string Message { get; private set; }
        public RecordingMetadata LastSaved { get; private set; }
        public LoadedRecording LastLoaded { get; private set; }
        public event Action<RecordingMetadata> Saved;
        public bool SavedCurrentAttempt {get;private set;}
        public string StatusText {get{return DescribeState(Recorder.Snapshot(),SavedCurrentAttempt);}}
        public static string DescribeState(RecorderSnapshot state,bool saved)
        {return state.recording?"● 正在记录动作":state.hasDraft?"已停止 · 尚未保存":saved?"本次动作已保存":"预览中 · 尚未开始记录";}
        public static string Identity(RecordingMetadata meta)
        {
            if(meta==null)return "文件信息缺失";DateTime date;string when=DateTime.TryParse(meta.created_at,out date)?date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):meta.created_at;
            return meta.word_name+" · "+when+" · "+(meta.duration_ms/1000.0).ToString("F1")+"秒 · "+meta.frame_count+"帧\n"+meta.word_id+"/"+meta.take_id+".jsonl";
        }
        public void CompareLatest()
        {
            if(Recorder.Snapshot().hasDraft)throw new InvalidOperationException("请先停止并保存本次动作");
            if(LastSaved==null||LastSaved.word_id!=SelectedWordId)throw new InvalidOperationException("请先记录并保存当前词汇的动作");
            view.App.Navigate("compare");view.Comparison.SelectPractice(LastSaved.word_id,LastSaved.take_id);
        }
        SignCoachDebug view;
        int selected;
        string sourceReference = "", pendingDelete;
        List<RecordingEntry> history = new List<RecordingEntry>();
        Vector2 scroll;
        RecordingValidation draft;
        bool acknowledgeWarnings;
        Font chinese;
        public void Initialize(SignCoachDebug debug)
        {
            view = debug;
            string root = Path.Combine(Application.persistentDataPath, "data", "reference");
            string bundledRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","reference");
            if(Directory.Exists(bundledRoot))root=bundledRoot;
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-recordingRoot");
            if (i >= 0 && i + 1 < args.Length) root = args[i + 1];
            Store = new RecordingStore(root); Recorder = new ActionRecorder(Store);
            chinese = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16);
            Message = "点击开始，记录完整动作。"; Refresh();
        }
        public void SelectWord(string id)
        {
            if (Recorder.Snapshot().hasDraft) throw new InvalidOperationException("保存或取消当前录制后再切换词汇");
            Vocabulary.GetSelectable(id);
            for (int i = 0; i < Vocabulary.Words.Length; i++) if (Vocabulary.Words[i].id == id) selected = i;
            pendingDelete = null; LastLoaded = null; Refresh();
            SavedCurrentAttempt=false;
        }
        public void Begin()
        {
            if (!view.IsReceiving) throw new InvalidOperationException("尚未收到新鲜的有效 TCP 数据，暂不能开始");
            Recorder.Start(SelectedWordId, sourceReference); draft = null; acknowledgeWarnings = false;
            SavedCurrentAttempt=false;
            pendingDelete = null; Message = "录制中：完整保存收到的有效帧，包括无手帧。";
        }
        public void StopRecording()
        {
            draft = Recorder.Stop();
            Message = draft.IsValid ? "记录已停止，还没有保存。请点击下方“保存动作数据”。" : "不能保存：" + string.Join("；", draft.errors);
        }
        public void SaveRecording(bool acknowledge)
        {
            LastSaved = Recorder.Save(acknowledge); draft = null; Refresh();
            Message = "已保存 " + LastSaved.take_id + "，" + LastSaved.frame_count + " 帧；练习数据。";
            if (Store.CatalogWarning != null) Message += " " + Store.CatalogWarning;
            if(Saved!=null)Saved(LastSaved);
            if(LastSaved.verification_status=="user_practice")Message="本次练习已保存："+LastSaved.take_id+"，"+LastSaved.frame_count+" 帧。";
            SavedCurrentAttempt=true;Message="保存成功：\n"+Identity(LastSaved)+"\n保存的是动作数据，不是摄像头视频。";
        }
        public void CancelRecording()
        {
            Recorder.Cancel(); draft = null; acknowledgeWarnings = false;SavedCurrentAttempt=false; Message = "当前未保存录制已取消；历史录制保留。";
        }
        public void Rerecord()
        {
            CancelRecording(); Begin(); // 已保存的 take 永远不被“重新录制”覆盖。
        }
        public void Refresh() { history = Store.List(SelectedWordId); }
        public void LoadRecording(string id)
        {
            LastLoaded = Store.Load(SelectedWordId, id);
            Message = "读取校验通过：" + id + "，" + LastLoaded.frames.Length + " 帧，原始坐标未镜像。";
        }
        public void RequestDelete(string id)
        {
            if (Recorder.Snapshot().hasDraft) throw new InvalidOperationException("请先保存或取消当前录制");
            Store.FramePath(SelectedWordId, id); pendingDelete = id; scroll.y = float.MaxValue;
        }
        public void ConfirmDelete()
        {
            if (pendingDelete == null) throw new InvalidOperationException("请先选择要删除的录制");
            Store.Delete(SelectedWordId, pendingDelete, true); Message = "已删除 " + pendingDelete;
            if(LastSaved!=null&&LastSaved.word_id==SelectedWordId&&LastSaved.take_id==pendingDelete){LastSaved=null;SavedCurrentAttempt=false;}
            pendingDelete = null; LastLoaded = null; Refresh();
        }
        void Act(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException || ex is FormatException)
            { Message = "操作失败：" + ex.Message; }
        }
        void OnGUI()
        {
            if (Recorder == null) return;
            if(view.App!=null){if(view.App.Page!="practice")return;view.App.ApplySkin();}
            if (view.Comparison != null && view.Comparison.IsOpen) return;
            var state = Recorder.Snapshot();
            var original = GUI.skin.font; GUI.skin.font = chinese;
            int oldLabelSize = GUI.skin.label.fontSize, oldButtonSize = GUI.skin.button.fontSize;
            int oldFieldSize = GUI.skin.textField.fontSize, oldToggleSize = GUI.skin.toggle.fontSize;
            bool oldWrap = GUI.skin.label.wordWrap;
            GUI.skin.label.fontSize = GUI.skin.button.fontSize = GUI.skin.textField.fontSize = 15;
            GUI.skin.toggle.fontSize = 14; GUI.skin.label.wordWrap = true;
            GUILayout.BeginArea(new Rect(Mathf.Max(0, Screen.width - PanelWidth), 130, PanelWidth - 10, Screen.height - 140), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("记录 → 停止 → 保存 → 比较",new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold});
            GUILayout.Label("只保存手部动作关键点，不保存视频。");
            GUILayout.Label(StatusText,new GUIStyle(GUI.skin.label){fontSize=19,normal={textColor=state.recording?new Color(.8f,.16f,.15f):new Color(.14f,.35f,.28f)}});
            GUI.enabled = !state.hasDraft;
            var names = new string[Vocabulary.Words.Length]; for (int i = 0; i < names.Length; i++) names[i] = Vocabulary.Words[i].name;
            var wordButton=new GUIStyle(GUI.skin.button){padding=new RectOffset(6,6,2,2),margin=new RectOffset(2,2,2,2)};
            if(view.App==null){int choice = GUILayout.SelectionGrid(selected, names, 2, wordButton, GUILayout.Height(104));if (choice != selected) Act(() => SelectWord(Vocabulary.Words[choice].id));}
            else GUILayout.Label("当前词汇："+Vocabulary.Get(SelectedWordId).name+"（切换词汇请返回教学页）");
            GUI.enabled = true;
            GUILayout.Label(string.Format("{0}  时长 {1:F1} 秒  帧数 {2}  当前手数 {3}",
                state.recording ? "录制中" : "未录制", state.elapsedMs / 1000.0, state.frames, view.VisibleHands));
            if (state.problem != null) GUILayout.Label(state.problem);
            GUILayout.BeginHorizontal(); GUI.enabled = !state.hasDraft && view.IsReceiving;
            if (GUILayout.Button("1 开始记录动作", GUILayout.Height(42))) Act(Begin);
            GUI.enabled = state.recording;
            if (GUILayout.Button("2 停止记录", GUILayout.Height(42))) Act(StopRecording);
            GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUI.enabled = state.hasDraft;
            if (GUILayout.Button("放弃未保存动作")) Act(CancelRecording);
            GUI.enabled = view.IsReceiving;
            if (GUILayout.Button("放弃本次，重新记录")) Act(Rerecord);
            GUI.enabled = true; GUILayout.EndHorizontal();
            if (draft != null)
            {
                foreach (var warning in draft.warnings) GUILayout.Label("质量提示：" + warning);
                if (draft.warnings.Count > 0) acknowledgeWarnings = GUILayout.Toggle(acknowledgeWarnings, "保留本次练习");
                GUI.enabled = draft.IsValid && (draft.warnings.Count == 0 || acknowledgeWarnings);
            }
            GUI.enabled=draft!=null&&draft.IsValid&&!state.recording&&(draft.warnings.Count==0||acknowledgeWarnings);
            if(GUILayout.Button("3 保存动作数据",GUILayout.Height(42)))Act(()=>SaveRecording(acknowledgeWarnings));GUI.enabled=true;
            GUILayout.Label(Message ?? "");
            if(LastSaved!=null&&LastSaved.word_id==SelectedWordId)
            {
                GUILayout.Label("最近保存\n"+Identity(LastSaved));
                GUI.enabled=!state.hasDraft;
                if(GUILayout.Button("4 比较这次练习",GUILayout.Height(42)))Act(CompareLatest);GUI.enabled=true;
                if(GUILayout.Button("查看文件保存位置"))Act(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+Store.FramePath(LastSaved.word_id,LastSaved.take_id)+"\""){UseShellExecute=true}));
            }
            GUILayout.Label("历史动作（按时间查看）");
            if (GUILayout.Button("刷新历史录制")) Act(Refresh);
            for(int historyIndex=history.Count-1;historyIndex>=0;historyIndex--)
            {
                var entry=history[historyIndex];
                GUILayout.BeginVertical(GUI.skin.box);
                string date = entry.metadata == null ? "metadata 缺失/异常" : entry.metadata.created_at;
                if (entry.metadata != null) { DateTime parsed; if (DateTime.TryParse(date, out parsed)) date = parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); }
                bool latest=LastSaved!=null&&LastSaved.word_id==SelectedWordId&&LastSaved.take_id==entry.takeId;
                GUILayout.Label((latest?"★ 最近保存的这次动作":"历史动作（非本次）")+"\n"+entry.takeId + "  " + date);
                GUILayout.Label(entry.metadata == null ? entry.error : entry.metadata.frame_count + " 帧 / " + (entry.metadata.duration_ms / 1000.0).ToString("F1") + " 秒");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("读取并验证")) { var id = entry.takeId; Act(() => LoadRecording(id)); }
                GUI.enabled = !state.hasDraft;
                if (GUILayout.Button("删除…")) { var id = entry.takeId; Act(() => RequestDelete(id)); }
                GUI.enabled = true; GUILayout.EndHorizontal(); GUILayout.EndVertical();
            }
            if (pendingDelete != null)
            {
                GUILayout.Label("确认永久删除 " + SelectedWordId + "/" + pendingDelete + "？");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("确认删除")) Act(ConfirmDelete);
                if (GUILayout.Button("保留，不删除")) pendingDelete = null;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.skin.font = original;
            GUI.skin.label.fontSize = oldLabelSize; GUI.skin.button.fontSize = oldButtonSize;
            GUI.skin.textField.fontSize = oldFieldSize; GUI.skin.toggle.fontSize = oldToggleSize;
            GUI.skin.label.wordWrap = oldWrap;
        }
        void OnDestroy() { if (Recorder != null) Recorder.Dispose(); if (chinese != null) Destroy(chinese); }
    }
}
