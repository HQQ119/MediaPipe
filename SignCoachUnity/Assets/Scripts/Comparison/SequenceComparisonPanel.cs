using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SignCoach.Comparison
{
    public sealed class SequenceComparisonPanel : MonoBehaviour
    {
        sealed class Choice { public string Word,Take,Label; }
        RecordingPanel recording;
        readonly List<Choice> choices=new List<Choice>();
        int selectedA=-1,selectedB=-1;
        bool useVideo;
        public string ResultInputA {get;private set;}
        public string ResultInputB {get;private set;}
        public string SelectedPractice {get{return selectedB>=0&&selectedB<choices.Count?choices[selectedB].Word+"/"+choices[selectedB].Take:"";}}
        bool open,showDiagnostic,advanced;
        string window="0.25",threshold="0.35";
        int handMode,referenceHand=1,practiceHand=1,scoringProfile;bool mirrorReference;
        string message="选择两段录制；相似度不是手语正确率。";
        Task<ComparisonResult> task;
        CancellationTokenSource cancellation;
        Font font;
        Texture2D heatmap;
        Vector2 scroll;
        public bool IsOpen{get{return open;}}
        public bool Busy{get{return task!=null;}}
        public ComparisonResult Result{get;private set;}
        public string WordA{get;private set;} public string WordB{get;private set;}
        public bool UsesReviewedReference {get;private set;}
        public bool ShowDiagnostic{get{return showDiagnostic;}set{showDiagnostic=value;}}
        public string Message{get{return message;}}
        public void CancelComparison(){if(cancellation!=null)cancellation.Cancel();}
        public void ConfigureHands(ComparisonHands mode,int reference,int practice,bool mirror)
        {if(Busy)throw new InvalidOperationException("请先取消当前比较");handMode=(int)mode;referenceHand=reference;practiceHand=practice;mirrorReference=mirror;Result=null;message="执行手设置已更新，请核对后开始比较。";}
        public void ConfigureScoring(ComparisonScoringProfile profile)
        {if(Busy)throw new InvalidOperationException("请先取消当前比较");if(!Enum.IsDefined(typeof(ComparisonScoringProfile),profile))throw new ArgumentException("无效评分档位");scoringProfile=(int)profile;Result=null;message="对照档位已更新，请重新开始比较。";}
        public void Initialize(RecordingPanel panel)
        {recording=panel;font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",16);Refresh();}
        void Refresh()
        {
            choices.Clear();foreach(var word in Vocabulary.Words)foreach(var entry in recording.Store.List(word.id))
                choices.Add(new Choice{Word=word.id,Take=entry.takeId,Label=RecordingPanel.Identity(entry.metadata)+(entry.metadata!=null&&RecordingKinds.Resolve(entry.metadata)==RecordingKind.UserPractice?" · 练习录制":" · 历史/示例，非本次")});
            selectedA=Math.Min(selectedA,choices.Count-1);selectedB=Math.Min(selectedB,choices.Count-1);
        }
        string CurrentWord {get{var app=GetComponent<LearningApp>();return app==null?recording.SelectedWordId:app.Word;}}
        TeachingMaterial CurrentMaterial {get{var app=GetComponent<LearningApp>();var m=app==null?null:app.Teaching.Selected;return m!=null&&m.word_id==CurrentWord?m:null;}}
        bool HasVideoReference {get{var m=CurrentMaterial;var app=GetComponent<LearningApp>();return m!=null&&!string.IsNullOrEmpty(m.landmark_path)&&System.IO.File.Exists(app.Teaching.Catalog.Resolve(m.landmark_path));}}
        public void SelectPractice(string word,string take)
        {
            selectedB=-1;for(int i=0;i<choices.Count;i++)if(choices[i].Word==word&&choices[i].Take==take)selectedB=i;
            if(selectedB<0)throw new ArgumentException("找不到刚保存的动作");
            useVideo=HasVideoReference;
        }
        void Defaults()
        {
            selectedA=selectedB=-1;var last=recording.LastSaved;
            if(last!=null&&last.word_id==CurrentWord)SelectPractice(last.word_id,last.take_id);
            else
            {
                RecordingMetadata newest=null;foreach(var entry in recording.Store.List(CurrentWord))
                    if(entry.metadata!=null&&RecordingKinds.Resolve(entry.metadata)==RecordingKind.UserPractice&&(newest==null||string.CompareOrdinal(entry.metadata.created_at,newest.created_at)>0))newest=entry.metadata;
                if(newest!=null)SelectPractice(newest.word_id,newest.take_id);
            }
            useVideo=HasVideoReference;
        }
        public void SetOpen(bool value)
        {
            if(value&&recording.Recorder.Snapshot().hasDraft)throw new InvalidOperationException("请先保存或取消录制，再打开比较界面");
            open=value;if(value){Refresh();Defaults();message=useVideo?"参考已选为当前词汇的视频动作。请核对自己的文件，再开始比较。":"请手动选择两份同词汇动作；不会自动选择旧示例。";}else if(cancellation!=null)cancellation.Cancel();
        }
        public void OpenAndCompare(string wordA,string takeA,string wordB,string takeB)
        {
            SetOpen(true);bool a=false,b=false;
            for(int i=0;i<choices.Count;i++){if(choices[i].Word==wordA&&choices[i].Take==takeA){selectedA=i;a=true;}if(choices[i].Word==wordB&&choices[i].Take==takeB){selectedB=i;b=true;}}
            if(!a||!b)throw new ArgumentException("找不到指定录制");useVideo=false;Begin();
        }
        public void CompareReference(TeachingCatalog catalog,TeachingMaterial material,string practiceTake)
        {
            string reason;if(!catalog.CanCompare(material,out reason))throw new InvalidOperationException(reason);
            if(Busy)throw new InvalidOperationException("比较正在进行");
            SetOpen(true);WordA=WordB=material.word_id;Result=null;UsesReviewedReference=true;
            cancellation=new CancellationTokenSource();var token=cancellation.Token;
            var path=catalog.Resolve(material.landmark_path);var store=recording.Store;
            var settings=new ComparisonSettings();
            message="加载已审核标准与本词练习，后台校验质量并比较…";
            task=Task.Run(()=>{
                var meta=new RecordingMetadata{word_id=material.word_id,word_name=material.word_name,verification_status="verified_reference",source_reference=material.source_url};
                var reference=TeachingSequenceAdapter.ForCameraAspect(SequenceComparer.LoadJsonl(path,meta),(double)material.height/Math.Max(1,material.width));var practice=SequenceComparer.Load(store,material.word_id,practiceTake);
                if(RecordingKinds.Resolve(practice.Metadata)!=RecordingKind.UserPractice)throw new InvalidOperationException("标准流程需要用户练习数据");
                var result=SequenceComparer.Compare(reference,practice,settings,token);
                if(result.low_quality||!result.score_available)throw new InvalidOperationException("标准或练习数据质量不足，请补充标准素材或重新录制");
                return result;
            },token);
        }
        void Begin()
        {
            if(Busy)throw new InvalidOperationException("比较正在进行");
            if(choices.Count==0)throw new InvalidOperationException("没有可比较的录制");
            UsesReviewedReference=false;
            var settings=new ComparisonSettings{ScoringProfile=(ComparisonScoringProfile)scoringProfile,Hands=(ComparisonHands)handMode,ReferenceHand=referenceHand,PracticeHand=practiceHand,MirrorReference=mirrorReference,WindowRatio=double.Parse(window,CultureInfo.InvariantCulture),DifferenceThreshold=double.Parse(threshold,CultureInfo.InvariantCulture)};
            settings.Validate();
            if(selectedB<0||selectedB>=choices.Count)throw new InvalidOperationException("请先录制、保存并选择自己的动作");
            if(!useVideo&&(selectedA<0||selectedA>=choices.Count))throw new InvalidOperationException("请选择用于参考的历史动作");
            var a=useVideo?null:choices[selectedA];var b=choices[selectedB];var store=recording.Store;
            if(!useVideo&&a.Word!=b.Word)throw new InvalidOperationException("请选择同一个词汇的动作进行比较");
            var material=CurrentMaterial;var app=GetComponent<LearningApp>();
            if(useVideo&&(!HasVideoReference||material.word_id!=b.Word))throw new InvalidOperationException("此词汇没有对应的视频动作参考");
            WordA=useVideo?material.word_id:a.Word;WordB=b.Word;
            ResultInputA=useVideo?material.word_name+" · 当前词汇视频提取的手部动作\n"+material.source_name:a.Label;
            ResultInputB=b.Label;
            cancellation=new CancellationTokenSource();var token=cancellation.Token;Result=null;message="后台提取特征并计算 DTW…";
            bool videoReference=useVideo;string videoPath=videoReference?app.Teaching.Catalog.Resolve(material.landmark_path):null;
            task=Task.Run(()=>{
                var reference=videoReference?TeachingSequenceAdapter.ForCameraAspect(SequenceComparer.LoadJsonl(videoPath),(double)material.height/Math.Max(1,material.width)):SequenceComparer.Load(store,a.Word,a.Take);
                if(videoReference)reference.Name=material.word_name+" · 视频动作参考";
                return SequenceComparer.Compare(reference,SequenceComparer.Load(store,b.Word,b.Take),settings,token);
            },token);
        }
        int DrawChoices(int selected,GUIStyle button)
        {
            var indexes=new List<int>();for(int i=0;i<choices.Count;i++)if(choices[i].Word==CurrentWord)indexes.Add(i);
            if(indexes.Count==0){GUILayout.Label("还没有此词汇的录制。请先进入摄像头练习，记录并保存动作。");return -1;}
            int local=indexes.IndexOf(selected);string[] labels=new string[indexes.Count];for(int i=0;i<labels.Length;i++)labels[i]=(indexes[i]==selected?"✓ 已选中\n":"")+choices[indexes[i]].Label;
            var choiceStyle=new GUIStyle(button){wordWrap=true,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(14,14,10,10)};
            choiceStyle.onNormal.background=button.normal.background;choiceStyle.onNormal.textColor=Color.white;
            choiceStyle.normal.background=Texture2D.whiteTexture;choiceStyle.normal.textColor=new Color(.16f,.3f,.24f);
            int chosen=GUILayout.SelectionGrid(local,labels,1,choiceStyle);return chosen>=0&&chosen<indexes.Count?indexes[chosen]:-1;
        }
        public void BeginSelected(){Begin();}
        void Update()
        {
            if(task==null||!task.IsCompleted)return;
            if(task.IsCanceled)message="比较已取消。";
            else if(task.IsFaulted)message="比较失败："+task.Exception.GetBaseException().Message;
            else {Result=task.Result;message=Result.quality_message;BuildHeatmap();}
            task=null;cancellation.Dispose();cancellation=null;
        }
        void BuildHeatmap()
        {
            if(heatmap!=null)Destroy(heatmap);heatmap=new Texture2D(128,128,TextureFormat.RGBA32,false);heatmap.filterMode=FilterMode.Point;
            var c=Result.dtw.LocalCosts;int n=c.GetLength(0),m=c.GetLength(1);var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                double cost=c[(int)((n-1)*x/127.0),(int)((m-1)*(127-y)/127.0)];
                pixels[y*128+x]=double.IsInfinity(cost)?Color.gray:Color.Lerp(new Color(.04f,.12f,.25f),new Color(.9f,.22f,.12f),(float)(cost/(1+cost)));
            }
            heatmap.SetPixels(pixels);heatmap.Apply();
        }
        public string SaveReport(string folder)
        {
            if(Result==null)throw new InvalidOperationException("尚无比较结果");Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"comparison_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
            using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew)))writer.Write(Result.ToJson());return path;
        }
        void Act(Action action){try{action();}catch(Exception ex)when(ex is ArgumentException||ex is InvalidOperationException||ex is IOException||ex is FormatException||ex is OverflowException){message=ex.Message;}}
        static void Line(Vector2 a,Vector2 b,Color color,float width=1.5f)
        {
            if(Event.current.type!=EventType.Repaint)return;var matrix=GUI.matrix;var saved=GUI.color;GUI.color=color;
            var d=b-a;GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);GUI.DrawTexture(new Rect(a.x,a.y,Mathf.Max(1,d.magnitude),width),Texture2D.whiteTexture);
            GUI.matrix=matrix;GUI.color=saved;
        }
        static void Timeline(Rect area,long duration,SequenceQuality quality,DifferenceInterval[] differences,string label)
        {
            var saved=GUI.color;GUI.color=new Color(.18f,.25f,.3f);GUI.DrawTexture(area,Texture2D.whiteTexture);
            foreach(var gap in quality.missing_intervals)
            {
                GUI.color=new Color(1,.58f,.12f,.85f);float x=area.x+area.width*gap.start_ms/Math.Max(1,duration);
                float w=Mathf.Max(2,area.width*(gap.end_ms-gap.start_ms)/Math.Max(1,duration));GUI.DrawTexture(new Rect(x,area.y,w,area.height),Texture2D.whiteTexture);
            }
            foreach(var d in differences)if(d.sequence==label)
            {
                GUI.color=new Color(.95f,.2f,.22f);float x=area.x+area.width*d.start_ms/Math.Max(1,duration);
                GUI.DrawTexture(new Rect(x,area.y+area.height/2,Mathf.Max(2,area.width*(d.end_ms-d.start_ms)/Math.Max(1,duration)),area.height/2),Texture2D.whiteTexture);
            }
            GUI.color=saved;
        }
        void OnGUI()
        {
            if(recording==null)return;
            var app=GetComponent<LearningApp>();
            if(app!=null){if(app.Page!="compare")return;app.ApplySkin();}
            var button=new GUIStyle(GUI.skin.button){font=font,fontSize=15};
            if(app==null&&GUI.Button(new Rect(Screen.width-180,6,168,30),open?"返回追踪 / 录制":"动作序列比较",button))Act(()=>SetOpen(!open));
            if(!open)return;
            var text=new GUIStyle(GUI.skin.label){font=font,fontSize=15,wordWrap=true};
            var warning=new GUIStyle(text){normal={textColor=new Color(1,.68f,.3f)}};
            GUI.Box(new Rect(app==null?8:232,116,Screen.width-(app==null?16:248),Screen.height-132),GUIContent.none);
            GUILayout.BeginArea(new Rect(app==null?20:240,138,Screen.width-(app==null?40:260),Screen.height-154));scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Vocabulary.Get(CurrentWord).name+" · 比较动作",new GUIStyle(text){fontSize=23,fontStyle=FontStyle.Bold});
            if(Result!=null){GUILayout.Label("已完成的比较："+Result.name_a+" ↔ "+Result.name_b,text);ComparisonScoreView.Draw(Result,font);ComparisonScoreView.DrawBreakdown(Result);GUILayout.Label("切换下方动作文件后，需要再次点击开始比较，分数才会更新。",text);GUILayout.Space(10);}
            GUILayout.Label("比较的是手部关键点序列，不是视频画面。先确认下面的两份动作，再点击开始比较。",text);
            GUI.enabled=!Busy;
            GUILayout.Label("执行手：自动只提供几何建议；双手动作请明确选双手。",text);
            var selection=new GUIStyle(button){wordWrap=true,fixedHeight=0};
            selection.onNormal.background=button.normal.background;selection.onNormal.textColor=Color.white;
            selection.onHover.background=button.hover.background;selection.onHover.textColor=Color.white;
            selection.onActive.background=button.active.background;selection.onActive.textColor=Color.white;
            selection.normal.background=Texture2D.whiteTexture;selection.normal.textColor=new Color(.16f,.3f,.24f);
            string[] modeLabels={"自动按参考动作","只比较左手","只比较右手","双手分别比较","手动配对（单手）"};modeLabels[handMode]="✓ "+modeLabels[handMode];
            string[] scoreLabels={"入门练习 · 允许小幅偏差","精细对照 · 原评分尺度"};scoreLabels[scoringProfile]="✓ "+scoreLabels[scoringProfile];
            int profile=GUILayout.SelectionGrid(scoringProfile,scoreLabels,2,selection);if(profile!=scoringProfile)ConfigureScoring((ComparisonScoringProfile)profile);
            int mode=GUILayout.SelectionGrid(handMode,modeLabels,3,selection);
            int ra=referenceHand,pb=practiceHand;bool mirrored=mirrorReference;
            if(mode==(int)ComparisonHands.ExplicitSingle)
            {
                GUILayout.BeginHorizontal();GUILayout.Label("参考执行手",text);ra=GUILayout.SelectionGrid(ra,new[]{"Left 左手","Right 右手"},2,selection);GUILayout.Label("练习执行手",text);pb=GUILayout.SelectionGrid(pb,new[]{"Left 左手","Right 右手"},2,selection);GUILayout.EndHorizontal();
                GUILayout.Label("不同手的配对由你明确指定，仅比较所选执行手动作；原始左右标签保持不变。",text);
            }
            mirrored=GUILayout.Toggle(mirrored,"已确认参考画面水平镜像：仅在计算副本中还原x，不改变模型标签");
            if(mode!=handMode||ra!=referenceHand||pb!=practiceHand||mirrored!=mirrorReference)ConfigureHands((ComparisonHands)mode,ra,pb,mirrored);
            if(message.StartsWith("比较失败"))GUILayout.Label(message,new GUIStyle(text){normal={textColor=new Color(.65f,.22f,.1f)}});
            if(HasVideoReference){GUILayout.BeginHorizontal();if(GUILayout.Button("参考：本词汇视频动作",button))useVideo=true;if(GUILayout.Button("参考：另一次历史练习",button))useVideo=false;GUILayout.EndHorizontal();}
            else{useVideo=false;GUILayout.Label("此词汇暂无视频动作参考，可手动选择两次同词练习进行比较。",warning);}
            GUILayout.Label("A · 参考动作",new GUIStyle(text){fontSize=19});
            if(useVideo)GUILayout.Label(CurrentMaterial.word_name+" · 当前学习视频提取的手部关键点\n来源："+CurrentMaterial.source_name,text);else selectedA=DrawChoices(selectedA,button);
            GUILayout.Space(12);GUILayout.Label("B · 我的动作（已优先选中最近保存的练习）",new GUIStyle(text){fontSize=19});selectedB=DrawChoices(selectedB,button);
            GUILayout.Space(10);GUILayout.Label("即将比较：\n参考："+(useVideo?CurrentMaterial.word_name+" · 视频动作":selectedA>=0?choices[selectedA].Label:"尚未选择")+"\n我的动作："+(selectedB>=0?choices[selectedB].Label:"尚未保存或选择"),text);
            GUI.enabled=!Busy&&selectedB>=0&&(useVideo||(selectedA>=0&&selectedA!=selectedB));
            if(GUILayout.Button("确认上述两份动作，开始比较",button,GUILayout.Height(42)))Act(Begin);GUI.enabled=!Busy;
            advanced=GUILayout.Toggle(advanced,"高级比较参数");if(advanced){
            GUILayout.BeginHorizontal();GUILayout.Label("时间窗口 0–1",text);window=GUILayout.TextField(window,GUILayout.Width(65));
            GUILayout.Label("差异阈值（工程参数）",text);threshold=GUILayout.TextField(threshold,GUILayout.Width(65));
            GUILayout.EndHorizontal();}if(GUILayout.Button("刷新录制",button))Act(()=>{Refresh();Defaults();});GUI.enabled=true;
            if(Busy&&GUILayout.Button("取消计算",button))cancellation.Cancel();
            GUILayout.Label(message,warning);
            if(Result!=null)
            {
                var r=Result;
                GUILayout.Label("本次实际使用的动作\nA："+ResultInputA+"\nB："+ResultInputB,text);
                GUILayout.Label(string.Format("A：{0:F3}s；动作区间可靠 {1:P1}，参与 {2}/{3} 帧，入镜前后排除 {4} 帧，插值 {5} 只手",r.duration_a_ms/1000.0,r.quality_a.action_reliable_ratio,r.quality_a.used_frames,r.quality_a.total_frames,r.quality_a.leading_empty_frames+r.quality_a.trailing_empty_frames,r.quality_a.interpolated_hands),text);
                GUILayout.Label(string.Format("B：{0:F3}s；动作区间可靠 {1:P1}，参与 {2}/{3} 帧，入镜前后排除 {4} 帧，插值 {5} 只手",r.duration_b_ms/1000.0,r.quality_b.action_reliable_ratio,r.quality_b.used_frames,r.quality_b.total_frames,r.quality_b.leading_empty_frames+r.quality_b.trailing_empty_frames,r.quality_b.interpolated_hands),text);
                showDiagnostic=GUILayout.Toggle(showDiagnostic,"查看分数计算方式",new GUIStyle(GUI.skin.toggle){font=font,fontSize=15});
                if(showDiagnostic)GUILayout.Label(ComparisonScoreView.FormulaText(r),text);
                GUILayout.Label(string.Format("DTW 累计距离 {0:F6}；归一化 {1:F6}；路径 {2} 点；比较耗时 {3:F1}ms",r.dtw.raw_distance,r.dtw.normalized_distance,r.dtw.path.Length,r.elapsed_ms),text);
                GUILayout.Label(string.Format("匹配手的分组距离：角度 {0:F3} / 指尖 {1:F3} / 形状 {2:F3} / 方向 {3:F3} / 轨迹 {4:F3} / 尺度 {5:F3}；缺手配对 {6}",
                    r.mean_group_distances[0],r.mean_group_distances[1],r.mean_group_distances[2],r.mean_group_distances[3],r.mean_group_distances[4],r.mean_group_distances[5],r.aligned_missing_hand_pairs),text);
                foreach(var q in r.quality_a.warnings)GUILayout.Label("A · "+q,warning);foreach(var q in r.quality_b.warnings)GUILayout.Label("B · "+q,warning);
                GUILayout.Label("原始时间轴：橙色=缺失/标签歧义；红色下栏=对齐后局部距离高于配置阈值",text);
                GUILayout.Label(string.Format("A：0 s → {0:F3} s",r.duration_a_ms/1000.0),text);
                Rect a=GUILayoutUtility.GetRect(100,22);Timeline(a,r.duration_a_ms,r.quality_a,r.differences,"A");
                GUILayout.Label(string.Format("B：0 s → {0:F3} s",r.duration_b_ms/1000.0),text);
                Rect b=GUILayoutUtility.GetRect(100,22);Timeline(b,r.duration_b_ms,r.quality_b,r.differences,"B");
                GUILayout.Label("帧间距离矩阵与 DTW 路径（A 横轴 / B 纵轴；均为参与比较帧的序号）",text);
                var plot=GUILayoutUtility.GetRect(200,220);var square=new Rect(plot.x,plot.y,Mathf.Min(plot.width,500),220);
                if(heatmap!=null)GUI.DrawTexture(square,heatmap,ScaleMode.StretchToFill);
                var path=r.dtw.path;
                for(int i=1;i<path.Length;i++)Line(new Vector2(square.x+square.width*path[i-1].i/Math.Max(1,r.dtw.LocalCosts.GetLength(0)-1),square.y+square.height*path[i-1].j/Math.Max(1,r.dtw.LocalCosts.GetLength(1)-1)),
                    new Vector2(square.x+square.width*path[i].i/Math.Max(1,r.dtw.LocalCosts.GetLength(0)-1),square.y+square.height*path[i].j/Math.Max(1,r.dtw.LocalCosts.GetLength(1)-1)),Color.cyan);
                GUILayout.Label("差异片段（原始录制相对时间，不代表手语错误）",text);
                if(r.differences.Length==0)GUILayout.Label("当前工程阈值下无高差异片段。",text);
                foreach(var d in r.differences)GUILayout.Label(string.Format("{0}：{1:F3}–{2:F3}s；局部均值 {3:F4}",d.sequence,d.start_ms/1000.0,d.end_ms/1000.0,d.mean_distance),text);
                if(GUILayout.Button("保存比较报告（不修改录制）",button))Act(()=>message="报告已保存："+SaveReport(Path.Combine(recording.Store.Root,"..","comparisons")));
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void OnDestroy()
        {
            if(cancellation!=null){var source=cancellation;source.Cancel();if(task!=null)task.ContinueWith(_=>source.Dispose());else source.Dispose();}
            if(font!=null)Destroy(font);if(heatmap!=null)Destroy(heatmap);
        }
    }
}
