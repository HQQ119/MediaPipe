using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using System.Runtime.InteropServices;
using SignCoach.Comparison;

namespace SignCoach
{
    [Serializable] public class DemoMaterial { public string word_id,video_file,source; public bool reviewed; }
    [Serializable] public class DemoMaterials { public DemoMaterial[] items; }
    public sealed class LearningApp : MonoBehaviour
    {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
        public string Page { get; private set; } = "home";
        public string Word { get; private set; } = Vocabulary.Words[0].id;
        public string Notice { get; private set; } = "";
        string expectedVideoUrl="",videoWord="";bool videoHasFrame;
        public string LoadedVideoUrl {get{return expectedVideoUrl;}}
        public static bool MatchesVideo(string expected,string actual,string sourceWord,string selectedWord){return !string.IsNullOrEmpty(expected)&&expected==actual&&sourceWord==selectedWord;}
        public bool VideoReady { get { return video!=null&&MatchesVideo(expectedVideoUrl,video.url,videoWord,Word)&&video.isPrepared; } }
        public long VideoFrame { get { return video==null?-1:video.frame; } }
        public TeachingPanel Teaching {get;private set;}
        public double VideoTime {get{return VideoReady?video.time:0;}}
        public double VideoLength {get{return VideoReady?video.length:0;}}
        public bool VideoLoop {get{return video.isLooping;}set{video.isLooping=value;}}
        public void DrawVideo(Rect area){if(VideoReady&&videoHasFrame)GUI.DrawTexture(area,videoTarget,ScaleMode.ScaleToFit);else GUI.Box(area,"「"+Vocabulary.Get(Word).name+"」暂无本地视频\n你仍可直接开始摄像头练习。");}
        public void SetVideo(string path)
        {
            expectedVideoUrl="";videoWord=Word;videoHasFrame=false;video.Stop();video.url="";
            var previous=RenderTexture.active;RenderTexture.active=videoTarget;GL.Clear(true,true,Color.clear);RenderTexture.active=previous;
            if(path==null)return;expectedVideoUrl=new Uri(path).AbsoluteUri;video.url=expectedVideoUrl;video.playbackSpeed=1;video.Prepare();
        }
        public void ReplayVideo(){if(VideoReady){video.time=0;video.Play();}}
        public void SeekVideo(double time){if(VideoReady&&video.canSetTime)video.time=Math.Max(0,Math.Min(time,video.length));}
        SignCoachDebug view;
        Font font;GUISkin skin;
        Texture2D card,button,hover;
        VideoPlayer video;RenderTexture videoTarget;
        DemoMaterials materials=new DemoMaterials{items=new DemoMaterial[0]};
        ComparisonResult observed;
        Vector2 scroll;bool diagnostic;
        BackgroundVisionProcess vision;
        static readonly string[] descriptions={"分别时的告别语。","表达感谢。","表达歉意。","与进餐有关的日常用语。","表达协助或请求协助。","表达喜爱。"};
        public static string WordDescription(string id){return descriptions[Array.FindIndex(Vocabulary.Words,x=>x.id==id)];}
        public void Initialize(SignCoachDebug debug)
        {
            view=debug;font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",17);
            video=gameObject.AddComponent<VideoPlayer>();video.playOnAwake=false;video.isLooping=true;video.audioOutputMode=VideoAudioOutputMode.None;
            video.aspectRatio=VideoAspectRatio.FitInside;
            video.sendFrameReadyEvents=true;
            video.prepareCompleted+=p=>{if(VideoReady)p.Play();};
            video.frameReady+=(p,index)=>{if(VideoReady)videoHasFrame=true;};
            videoTarget=new RenderTexture(960,540,0);video.targetTexture=videoTarget;video.renderMode=VideoRenderMode.RenderTexture;
            video.errorReceived+=(p,e)=>Notice="视频无法播放："+e;
            var path=Path.Combine(Application.streamingAssetsPath,"demonstrations.json");
            if(File.Exists(path))materials=JsonUtility.FromJson<DemoMaterials>(File.ReadAllText(path))??materials;
            view.Recording.Saved+=MarkPractice;
            Teaching=gameObject.AddComponent<TeachingPanel>();Teaching.Initialize(debug,this);
        }
        void MarkPractice(RecordingMetadata metadata)
        {
            // 只标记刚刚通过原录制器保存的新 take；不改旧数据或 JSONL。
            metadata.source="user_practice";metadata.verification_status="user_practice";
            var file=Path.Combine(view.Recording.Store.WordDirectory(metadata.word_id),metadata.take_id+".metadata.json");
            var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,JsonUtility.ToJson(metadata,true));File.Replace(temporary,file,null);view.Recording.Store.RebuildCatalog(metadata.word_id);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public void Navigate(string page)
        {
            if(page=="review")page="library";
            if(view.Recording.Recorder.Snapshot().hasDraft&&page!="practice"&&page!="guided")throw new InvalidOperationException("请先保存或取消当前录制，再切换页面。");
            if(page!="home"&&page!="library"&&page!="detail"&&page!="practice"&&page!="guided"&&page!="review"&&page!="compare"&&page!="result")throw new ArgumentException("未知页面");
            if(page=="result"&&view.Comparison.Result==null)throw new InvalidOperationException("还没有比较结果");
            if((page=="practice"||page=="guided")&&!view.Recording.Recorder.Snapshot().hasDraft)view.Recording.SelectWord(Word);
            if(page=="compare")view.Comparison.SetOpen(true);else view.Comparison.SetOpen(false);
            if(page!="detail"&&page!="guided")video.Pause();Page=page;scroll=Vector2.zero;Notice="";
        }
        public void SelectWord(string id){Vocabulary.GetSelectable(id);Navigate("detail");Word=id;Teaching.SelectWord(id);}
        DemoMaterial Demo()
        {if(materials.items!=null)foreach(var item in materials.items)if(item.word_id==Word&&item.reviewed&&!string.IsNullOrWhiteSpace(item.source)&&!string.IsNullOrWhiteSpace(item.video_file))return item;return null;}
        void PrepareDemo()
        {
            video.Stop();var demo=Demo();if(demo==null)return;
            var path=Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,demo.video_file));
            if(!path.StartsWith(Path.GetFullPath(Application.streamingAssetsPath)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)){Notice="示范视频文件缺失";return;}
            video.url=new Uri(path).AbsoluteUri;video.Prepare();
        }
        // 显式验收模式才可载入播放组件测试片，不进入词汇示范目录。
        public void LoadDiagnosticVideo(string path)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-signcoachEvidence")<0)throw new InvalidOperationException("仅供验收");
            Page="detail";SetVideo(path);Notice="播放组件测试片";
        }
        public void VideoControl(string action)
        {if(!VideoReady)throw new InvalidOperationException("暂无可播放视频");if(action=="play")video.Play();else if(action=="pause")video.Pause();else if(action=="slow"){if(!video.canSetPlaybackSpeed)throw new InvalidOperationException("当前视频不支持变速");video.playbackSpeed=.5f;}else throw new ArgumentException("未知播放操作");}
        public string WordStatus(string id)
        {
            if(Teaching!=null)return Teaching.WordStatus(id);
            if(materials.items!=null)foreach(var item in materials.items)if(item.word_id==id&&item.reviewed&&!string.IsNullOrEmpty(item.source))return "已审核示范（登记）";
            var entries=view.Recording.Store.List(id);foreach(var entry in entries)if(entry.metadata!=null&&RecordingKinds.Resolve(entry.metadata)==RecordingKind.VerifiedReference)return "已审核标准示范（登记）";
            return entries.Count>0?"未审核测试 / 练习数据":"暂无示范数据";
        }
        public void StartVision()
        {StartVisionCore(0);}
        public void VerifyUnavailableCamera()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-signcoachEvidence")<0)throw new InvalidOperationException("仅供验收");
            StartVisionCore(999);
        }
        void StartVisionCore(int camera)
        {
            if(view.IsReceiving){Notice="视觉服务已连接";return;}
            if(vision!=null&&vision.Running){Notice="摄像头正在启动，请稍候。";return;}
            var executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Vision","SignCoachVision.exe");
            if(!File.Exists(executable))executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Vision","SignCoachVision","SignCoachVision.exe");
            if(!File.Exists(executable)){Notice="缺少视觉服务文件。请保留程序旁完整的Vision文件夹。";return;}
            if(vision==null)vision=new BackgroundVisionProcess();vision.Start(executable,Teaching.Preview.Path,camera);
            Notice="正在连接摄像头…画面会直接显示在这里。";
        }
        void Update()
        {
            if(vision!=null)
            {
                if(vision.Failed)Notice=string.IsNullOrEmpty(vision.Error)?"摄像头启动失败，请检查设备占用、权限或Vision文件夹是否完整。":vision.Error;
                else if(view.IsReceiving)Notice="";
            }
            var result=view.Comparison.Result;
            if(result!=null&&!ReferenceEquals(observed,result)){observed=result;string word=view.Comparison.WordB??Word;if(word!=Word){Word=word;Teaching.SelectWord(Word);}diagnostic=false;Page="result";}
        }
        Texture2D Solid(Color color){var t=new Texture2D(128,64);for(int y=0;y<64;y++)for(int x=0;x<128;x++){float dx=Mathf.Max(12-x,x-115),dy=Mathf.Max(12-y,y-51);bool corner=dx>0&&dy>0&&dx*dx+dy*dy>144;t.SetPixel(x,y,corner?Color.clear:color);}t.Apply();return t;}
        static void Paint(Rect rect,Color color){var previous=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=previous;}
        bool Tab(string title,bool active){var style=new GUIStyle(GUI.skin.button);style.normal.background=active?button:Texture2D.whiteTexture;style.normal.textColor=active?Color.white:new Color(.32f,.4f,.36f);style.alignment=TextAnchor.MiddleLeft;style.padding=new RectOffset(18,12,6,6);return GUILayout.Button(title,style,GUILayout.Height(44));}

        public void ApplySkin()
        {
            if(skin==null)
            {
                skin=Instantiate(GUI.skin);card=Solid(Color.white);button=Solid(new Color(.12f,.39f,.31f));hover=Solid(new Color(.18f,.49f,.39f));
                skin.font=font;skin.label.fontSize=17;skin.label.wordWrap=true;skin.label.normal.textColor=new Color(.13f,.2f,.18f);
                skin.toggle.normal.textColor=new Color(.13f,.2f,.18f);skin.toggle.onNormal.textColor=new Color(.13f,.2f,.18f);skin.toggle.fontSize=15;
                skin.button.border=new RectOffset(14,14,14,14);skin.box.border=new RectOffset(14,14,14,14);skin.button.fontSize=17;skin.button.normal.background=button;skin.button.hover.background=hover;skin.button.active.background=hover;
                skin.button.normal.textColor=Color.white;skin.button.padding=new RectOffset(12,12,8,8);skin.button.margin=new RectOffset(4,4,5,5);
                skin.box.normal.background=card;skin.box.normal.textColor=new Color(.36f,.43f,.4f);skin.box.alignment=TextAnchor.MiddleCenter;skin.box.wordWrap=true;skin.box.fontSize=17;skin.box.padding=new RectOffset(18,18,16,16);skin.textField.fontSize=16;
            }
            GUI.skin=skin;
        }
        void Act(Action action){try{action();}catch(Exception e){Notice=e.Message;}}
        void Heading(string title,string subtitle){GUILayout.Label(title,new GUIStyle(GUI.skin.label){fontSize=30});GUILayout.Label(subtitle);GUILayout.Space(16);}
        bool Button(string text){return GUILayout.Button(text,GUILayout.MinHeight(42));}
        void ResultTimeline(string label,long duration,SequenceQuality quality,DifferenceInterval[] intervals)
        {
            GUILayout.Label(label+"  0 → "+(duration/1000.0).ToString("F2")+" 秒");var area=GUILayoutUtility.GetRect(100,20);var color=GUI.color;
            GUI.color=new Color(.15f,.24f,.28f);GUI.DrawTexture(area,Texture2D.whiteTexture);
            foreach(var gap in quality.missing_intervals){GUI.color=new Color(1,.6f,.15f);GUI.DrawTexture(new Rect(area.x+area.width*gap.start_ms/Math.Max(1,duration),area.y,Mathf.Max(2,area.width*(gap.end_ms-gap.start_ms)/Math.Max(1,duration)),20),Texture2D.whiteTexture);}
            foreach(var d in intervals)if(d.sequence==label){GUI.color=new Color(.92f,.28f,.28f);GUI.DrawTexture(new Rect(area.x+area.width*d.start_ms/Math.Max(1,duration),area.y+10,Mathf.Max(2,area.width*(d.end_ms-d.start_ms)/Math.Max(1,duration)),10),Texture2D.whiteTexture);}
            GUI.color=color;
        }
        void OnGUI()
        {
            GUI.depth=10;ApplySkin();
            Paint(new Rect(0,0,Screen.width,Screen.height),new Color(.95f,.96f,.94f));
            Paint(new Rect(0,0,216,Screen.height),Color.white);Paint(new Rect(216,0,1,Screen.height),new Color(.87f,.9f,.87f));
            GUILayout.BeginArea(new Rect(22,28,172,Screen.height-52));
            GUILayout.Label("SIGNCOACH",new GUIStyle(GUI.skin.label){fontSize=23,fontStyle=FontStyle.Bold});GUILayout.Label("手语学习 · Lite",new GUIStyle(GUI.skin.label){fontSize=14});GUILayout.Space(38);
            if(Tab("首页",Page=="home"))Act(()=>Navigate("home"));
            if(Tab("词汇学习",Page=="library"||Page=="detail"))Act(()=>Navigate("library"));
            if(Tab("摄像头练习",Page=="practice"))Act(()=>Teaching.EnterPractice(false));
            if(Tab("动作比较",Page=="compare"||Page=="result"))Act(()=>Navigate("compare"));
            GUILayout.FlexibleSpace();GUILayout.Label("一步一步，练习表达。",new GUIStyle(GUI.skin.label){fontSize=13});if(Button("退出程序"))Application.Quit();GUILayout.EndArea();
            Paint(new Rect(217,0,Screen.width-217,96),Color.white);
            GUI.Label(new Rect(250,26,Screen.width-530,36),Page=="home"?"欢迎回来":Page=="library"?"日常词汇":Page=="detail"?Vocabulary.Get(Word).name:Page=="practice"?"摄像头练习":Page=="guided"?"双屏对照":Page=="compare"?"动作比较":"练习反馈",new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold});
            GUI.Label(new Rect(Screen.width-255,33,230,30),view.IsReceiving?"● 摄像头已连接":"○ 摄像头尚未开启",new GUIStyle(GUI.skin.label){fontSize=15});
            if(Page=="guided"||Page=="review"){Teaching.Draw();return;}
            if(Page=="practice")
            {
                var state=view.Recording.Recorder.Snapshot();
                GUI.Label(new Rect(240,110,Mathf.Max(220,Screen.width-650),45),view.Recording.StatusText,new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold,normal={textColor=state.recording?new Color(.82f,.13f,.14f):new Color(.15f,.3f,.25f)}});
                GUI.Label(new Rect(240,156,Mathf.Max(220,Screen.width-650),55),state.recording?string.Format("已记录 {0:F1} 秒 / {1} 帧 · 文件 {2}.jsonl",state.elapsedMs/1000.0,state.frames,state.takeId):"进入本页只是预览。点击右侧“开始记录动作”才开始记录。\n停止后需点击“保存动作数据”，不保存视频。");
                if(GUI.Button(new Rect(240,Screen.height-64,220,42),"启动视觉服务 / 查看提示"))Act(StartVision);
                if(!string.IsNullOrEmpty(Notice))GUI.Label(new Rect(475,Screen.height-70,Mathf.Max(200,Screen.width-880),64),Notice);
                Teaching.DrawPractice();
                return;
            }
            if(Page=="compare")return;
            GUI.Box(new Rect(232,116,Screen.width-248,Screen.height-132),GUIContent.none);
            GUILayout.BeginArea(new Rect(252,132,Screen.width-290,Screen.height-168));scroll=GUILayout.BeginScrollView(scroll);
            if(Page=="home")
            {
                Heading("看一遍，再练一遍","你的手语练习空间");
                GUILayout.Label("从日常问候开始。先观察视频，再打开摄像头，按自己的节奏练习。");GUILayout.Space(30);
                GUILayout.BeginHorizontal();if(Button("选择词汇，开始学习  →"))Act(()=>Navigate("library"));if(Button("直接打开摄像头"))Act(()=>Teaching.EnterPractice(false));GUILayout.EndHorizontal();
                GUILayout.Space(30);GUILayout.Label("今天想练哪个词？",new GUIStyle(GUI.skin.label){fontSize=23,fontStyle=FontStyle.Bold});GUILayout.Space(12);
                for(int row=0;row<Vocabulary.Words.Length;row+=3){GUILayout.BeginHorizontal();for(int col=0;col<3&&row+col<Vocabulary.Words.Length;col++){var word=Vocabulary.Words[row+col];GUILayout.BeginVertical(GUI.skin.box,GUILayout.Width(Mathf.Max(120,(Screen.width-350)/3)));GUILayout.Label(word.name,new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold});GUILayout.Label(Teaching.WordStatus(word.id),new GUIStyle(GUI.skin.label){fontSize=13});if(GUILayout.Button("学习  →",GUILayout.Height(34))){var id=word.id;Act(()=>SelectWord(id));}GUILayout.EndVertical();}GUILayout.EndHorizontal();GUILayout.Space(12);}
                GUILayout.Space(16);GUILayout.Label("学习路径    01 观察动作    →    02 摄像头练习    →    03 回看动作记录",new GUIStyle(GUI.skin.label){fontSize=15});

            }
            else if(Page=="library")
            {
                Heading("从日常开始","选择一个词，观看动作，或直接进入练习。");
                for(int row=0;row<Vocabulary.Words.Length;row+=2){GUILayout.BeginHorizontal();for(int column=0;column<2&&row+column<Vocabulary.Words.Length;column++){var word=Vocabulary.Words[row+column];GUILayout.BeginVertical(GUI.skin.box,GUILayout.Width(Mathf.Max(180,(Screen.width-330)/2)));GUILayout.Label(Teaching.Category(word.id)+"  /  "+WordStatus(word.id),new GUIStyle(GUI.skin.label){fontSize=13});GUILayout.Space(10);GUILayout.Label(word.name,new GUIStyle(GUI.skin.label){fontSize=29,fontStyle=FontStyle.Bold});GUILayout.Label(WordDescription(word.id));GUILayout.Space(14);if(Button("打开学习  →")){var id=word.id;Act(()=>SelectWord(id));}GUILayout.EndVertical();}GUILayout.EndHorizontal();GUILayout.Space(12);}

            }
            else if(Page=="detail")
            {
                Teaching.DrawLesson();
            }
            else if(Page=="result")
            {
                var r=view.Comparison.Result;Heading("动作反馈","动作序列诊断，不是手语语义判断或正确率。");
                bool standard=view.Comparison.UsesReviewedReference&&view.Comparison.WordA==view.Comparison.WordB&&!r.low_quality;
                ComparisonScoreView.Draw(r);GUILayout.Space(12);
                ComparisonScoreView.DrawBreakdown(r);
                diagnostic=GUILayout.Toggle(diagnostic,"查看分数计算方式");
                if(diagnostic)GUILayout.Label(ComparisonScoreView.FormulaText(r));
                GUILayout.Label(standard?"示范与练习的动作对照":"本地动作序列对照");
                GUILayout.Label("本次比较的内容\n参考："+view.Comparison.ResultInputA+"\n我的动作："+view.Comparison.ResultInputB);
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label("A  动作数据质量",new GUIStyle(GUI.skin.label){fontSize=22});
                GUILayout.Label(string.Format("A：动作区间可靠 {0:P1}，入镜前后排除 {1} 帧，区间内不可用 {2} 帧\nB：动作区间可靠 {3:P1}，入镜前后排除 {4} 帧，区间内不可用 {5} 帧",r.quality_a.action_reliable_ratio,r.quality_a.leading_empty_frames+r.quality_a.trailing_empty_frames,r.quality_a.internal_missing_frames,r.quality_b.action_reliable_ratio,r.quality_b.leading_empty_frames+r.quality_b.trailing_empty_frames,r.quality_b.internal_missing_frames));
                GUILayout.Label(r.quality_message);GUILayout.EndVertical();
                GUILayout.Label(Teaching.ResultExplanation(r));
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label("B  动作序列比较",new GUIStyle(GUI.skin.label){fontSize=22});
                GUILayout.Label(string.Format("{0} / {1}\n时长 {2:F2}s / {3:F2}s；DTW 归一化距离 {4:F6}",r.name_a,r.name_b,r.duration_a_ms/1000.0,r.duration_b_ms/1000.0,r.dtw.normalized_distance));
                GUILayout.Label("橙色：缺失或标签歧义；红色：高局部差异（非手语错误）。");ResultTimeline("A",r.duration_a_ms,r.quality_a,r.differences);ResultTimeline("B",r.duration_b_ms,r.quality_b,r.differences);
                if(Button("查看差异时间轴与完整对齐"))Act(()=>Navigate("compare"));
                int shown=0;foreach(var d in r.differences){if(shown++>=5)break;GUILayout.Label(string.Format("{0}：{1:F2}–{2:F2}s，局部距离 {3:F3}",d.sequence,d.start_ms/1000.0,d.end_ms/1000.0,d.mean_distance));}GUILayout.EndVertical();
                GUILayout.Label("C  后续操作",new GUIStyle(GUI.skin.label){fontSize=22});if(Button("重新练习"))Act(()=>SelectWord(Word));if(Button("再次比较"))Act(()=>Navigate("compare"));if(Button("返回词汇列表"))Act(()=>Navigate("library"));
            }
            if(!string.IsNullOrEmpty(Notice))GUILayout.Label(Notice);GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void OnDestroy()
        {
            if(view!=null&&view.Recording!=null)view.Recording.Saved-=MarkPractice;
            if(vision!=null){vision.Dispose();Debug.Log("SignCoach vision stopped; forced: "+vision.ForcedKill+"; exit: "+vision.LastExitCode);}
            if(videoTarget!=null){videoTarget.Release();Destroy(videoTarget);}if(font!=null)Destroy(font);if(skin!=null)Destroy(skin);
            if(card!=null)Destroy(card);if(button!=null)Destroy(button);if(hover!=null)Destroy(hover);
        }
    }
}
