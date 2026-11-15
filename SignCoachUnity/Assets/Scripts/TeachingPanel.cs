using System;
using System.IO;
using UnityEngine;
using SignCoach.Comparison;

namespace SignCoach
{
    // 页面编排与审核管理；识别、录制和 DTW 继续使用原模块。
    public sealed class TeachingPanel : MonoBehaviour
    {
        public TeachingCatalog Catalog {get;private set;}
        public TeachingMaterial Selected {get;private set;}
        public CameraPreview Preview {get;private set;}
        LearningApp app;SignCoachDebug view;Vector2 scroll;
        string notice="",reviewer="",qualification="",reviewNotes="",confirmReview;
        bool understood,professional,acknowledge,showSource;
        public void Initialize(SignCoachDebug debug,LearningApp shell)
        {
            view=debug;app=shell;var args=Environment.GetCommandLineArgs();
            string root=Path.Combine(Application.streamingAssetsPath,"Teaching"),reviews=Path.Combine(Application.persistentDataPath,"teaching_reviews");
            int i=Array.IndexOf(args,"-teachingRoot");if(i>=0&&i+1<args.Length)root=args[i+1];
            i=Array.IndexOf(args,"-reviewRoot");if(i>=0&&i+1<args.Length)reviews=args[i+1];
            Catalog=new TeachingCatalog(root,reviews);
            Preview=new CameraPreview(Path.Combine(Application.temporaryCachePath,"camera_preview_"+System.Diagnostics.Process.GetCurrentProcess().Id+".bin"));
            view.Recording.Saved+=Saved;SelectWord(app.Word);
        }
        void Saved(RecordingMetadata data){PlayerPrefs.SetInt("learning_"+data.word_id,2);PlayerPrefs.Save();}
        void Update(){if(app.Page=="guided"||app.Page=="practice")Preview.Poll();}
        public string Category(string word){foreach(var item in Catalog.Items)if(item.word_id==word)return item.category;return "日常交流";}
        public string LearningStatus(string word){int value=PlayerPrefs.GetInt("learning_"+word,0);return value>=2?"已保存练习":value==1?"已浏览":"尚未练习";}
        public string WordStatus(string word)
        {
            foreach(var item in Catalog.Items)if(item.word_id==word&&Catalog.HasVideo(item))return "视频学习";
            return "自由练习";
        }
        public void SelectWord(string word)
        {
            Selected=null;foreach(var item in Catalog.Items)if(item.word_id==word&&Catalog.Status(item)!="unsuitable")
            {if(Selected==null)Selected=item;if(Catalog.HasVideo(item)){Selected=item;break;}}
            understood=false;confirmReview=null;notice="";Prepare();
        }
        public void SelectMaterial(string id){var item=Catalog.Find(id);if(item.word_id!=app.Word)throw new InvalidOperationException("请先选择素材对应词汇");Selected=item;understood=false;confirmReview=null;Prepare();}
        void Prepare(){app.SetVideo(Selected!=null&&Catalog.HasVideo(Selected)?Catalog.Resolve(Selected.video_path):null);}
        void Act(Action action){try{action();notice="";}catch(Exception e){notice=e.Message;}}
        bool Button(string text){return GUILayout.Button(text,GUILayout.MinHeight(36));}
        public void EnterPractice(bool alongside)
        {
            PlayerPrefs.SetInt("learning_"+app.Word,Math.Max(1,PlayerPrefs.GetInt("learning_"+app.Word,0)));PlayerPrefs.Save();
            app.Navigate(alongside?"guided":"practice");app.StartVision();
        }
        // 验收只在显式模式下确认已阅读，不涉及素材审核。
        public void VerificationPractice(bool alongside)
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"-signcoachEvidence")<0)throw new InvalidOperationException("仅供验收");understood=true;EnterPractice(alongside);}
        string MaterialLabel()
        {
            return Selected!=null&&Catalog.HasVideo(Selected)?"跟随视频观察动作 · 可慢放、循环播放":"暂无对应的本地视频 · 可直接练习";
        }
        void VideoControls()
        {
            GUI.enabled=app.VideoReady;GUILayout.BeginHorizontal();if(Button("播放"))Act(()=>app.VideoControl("play"));if(Button("暂停"))Act(()=>app.VideoControl("pause"));if(Button("慢速 0.5×"))Act(()=>app.VideoControl("slow"));if(Button("再看一遍"))Act(app.ReplayVideo);GUILayout.EndHorizontal();
            app.VideoLoop=GUILayout.Toggle(app.VideoLoop,"重复播放");
            double length=app.VideoLength,current=app.VideoTime;
            float sought=GUILayout.HorizontalSlider((float)current,0,(float)Math.Max(.01,length));if(Math.Abs(sought-current)>.15)app.SeekVideo(sought);
            GUILayout.Label(string.Format("播放进度 {0:F1} / {1:F1} 秒",current,length));GUI.enabled=true;
        }
        void SourceInfo()
        {
            if(Selected==null)return;
            GUILayout.Label("来源："+(Selected.publisher??Selected.source_name));
            if(!Selected.timing_verified&&Catalog.HasVideo(Selected))GUILayout.Label("素材由公开采样图片组成；播放速度仅用于观察，不代表原动作速度。");
            if(Button("打开原始教学来源"))Act(()=>{Uri u;if(!Uri.TryCreate(Selected.source_url,UriKind.Absolute,out u)||u.Scheme!="https")throw new InvalidOperationException("原始链接无效");Application.OpenURL(u.AbsoluteUri);});
        }
        void Notes()
        {
            GUILayout.Label("动作说明（仅使用已登记的教学依据）",new GUIStyle(GUI.skin.label){fontSize=21});
            if(Selected==null)return;
            GUILayout.Label("手型："+(Selected.handshape_notes??"待专业审核补充")+"\n手掌方向："+(Selected.orientation_notes??"待专业审核补充")+"\n动作位置："+(Selected.location_notes??"待专业审核补充")+"\n运动轨迹："+(Selected.trajectory_notes??"待专业审核补充"));
            GUILayout.Label("说明依据："+(Selected.annotation_source??"尚无已审核动作说明，不根据关键点编造语言解释")+"\n非手部信息："+Selected.nonmanual_requirement);
        }
        void Readiness()
        {
            GUILayout.Label("练习前：保持双手完整入镜、光线充足，避免双手严重遮挡。当前仅检测手部，不能完整判断依赖面部或身体的表达。");
            understood=GUILayout.Toggle(understood,"我已阅读素材状态与练习提示；待审核素材只用于采集/算法演示");
        }
        public void DrawLesson()
        {
            GUILayout.Label(Vocabulary.Get(app.Word).name+" · "+Category(app.Word),new GUIStyle(GUI.skin.label){fontSize=28});
            GUILayout.Label(LearningApp.WordDescription(app.Word));
            GUILayout.Label(MaterialLabel());GUILayout.Space(14);
            if(Button("开始摄像头练习  →"))Act(()=>EnterPractice(false));GUILayout.Space(12);
            var area=GUILayoutUtility.GetRect(100,Mathf.Clamp((Screen.width-320)*.34f,160,310));app.DrawVideo(area);VideoControls();
            showSource=GUILayout.Toggle(showSource,"查看素材来源与播放说明");if(showSource)SourceInfo();
            GUILayout.Label("练习提示：双手完整入镜、光线充足，尽量减少遮挡。");
            if(Button("返回词汇库"))Act(()=>app.Navigate("library"));if(!string.IsNullOrEmpty(notice))GUILayout.Label(notice);
        }
        public void DrawPractice()
        {
            float width=Mathf.Max(120,Screen.width-RecordingPanel.PanelWidth-264),height=Mathf.Max(120,Screen.height-340);
            Preview.Draw(new Rect(240,215,width,height),view.Current);
            if(GUI.Button(new Rect(240,Screen.height-108,190,34),"返回教学 / 切换模式"))Act(()=>app.Navigate("detail"));
            if(GUI.Button(new Rect(440,Screen.height-108,Mathf.Max(120,width-200),34),"比较刚保存的动作"))Act(view.Recording.CompareLatest);
            if(!string.IsNullOrEmpty(notice))GUI.Label(new Rect(240,Screen.height-160,width,50),notice);
        }
        public void CompareSaved()
        {
            if(Selected==null)throw new InvalidOperationException("当前词汇没有教学来源");
            string reason;if(!Catalog.CanCompare(Selected,out reason))throw new InvalidOperationException(reason+"；可在序列比较页进行未审核数据的算法演示。");
            var saved=view.Recording.LastSaved;
            if(saved==null||saved.word_id!=Selected.word_id||saved.verification_status!="user_practice")throw new InvalidOperationException("请先保存本词汇的用户练习");
            app.Navigate("compare");view.Comparison.CompareReference(Catalog,Selected,saved.take_id);
        }
        public string ResultExplanation(ComparisonResult result)
        {
            string[] names={"关节角度","指尖相对距离","局部手型","手掌方向","相对轨迹","相对尺度"};int index=0;
            if(result.component_scores==null)return "请结合质量信息查看对齐片段。";
            for(int i=1;i<result.component_scores.Length;i++)if(result.component_scores[i]<result.component_scores[index])index=i;
            return "按已选择的执行手和已对齐片段，差异较明显的是“"+names[index]+"”（分项参考分 "+result.component_scores[index].ToString("F1")+"）。可结合时间轴回看，不表示手语语义错误。";
        }
        public void Draw()
        {
            GUI.Box(new Rect(232,116,Screen.width-248,Screen.height-132),GUIContent.none);
            GUILayout.BeginArea(new Rect(248,132,Screen.width-280,Screen.height-156));scroll=GUILayout.BeginScrollView(scroll);
            if(app.Page=="review")DrawReview();else DrawAlongside();
            if(!string.IsNullOrEmpty(notice))GUILayout.Label(notice);if(!string.IsNullOrEmpty(app.Notice))GUILayout.Label(app.Notice);GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void DrawAlongside()
        {
            GUILayout.Label(Vocabulary.Get(app.Word).name+" · 示范伴随练习",new GUIStyle(GUI.skin.label){fontSize=25});GUILayout.Label(MaterialLabel());
            var row=GUILayoutUtility.GetRect(100,Mathf.Clamp((Screen.width-300)*.32f,200,340));float half=(row.width-16)/2;
            app.DrawVideo(new Rect(row.x,row.y,half,row.height));Preview.Draw(new Rect(row.x+half+16,row.y,half,row.height),view.Current);
            GUILayout.Label("左：来源素材（保持原方向）　右：镜像摄像头 + 模型原左右标签骨骼");VideoControls();
            var state=view.Recording.Recorder.Snapshot();GUILayout.Label(string.Format("TCP：{0} · 手数 {1} · 录制 {2:F1}s / {3} 帧",view.IsReceiving?"已连接":"未连接",view.VisibleHands,state.elapsedMs/1000.0,state.frames));
            GUILayout.BeginHorizontal();GUI.enabled=!state.hasDraft&&view.IsReceiving;if(Button("开始录制"))Act(view.Recording.Begin);GUI.enabled=state.recording;if(Button("停止录制"))Act(view.Recording.StopRecording);GUI.enabled=state.hasDraft;if(Button("取消录制"))Act(view.Recording.CancelRecording);GUI.enabled=true;GUILayout.EndHorizontal();
            acknowledge=GUILayout.Toggle(acknowledge,"保留此次练习，即使部分帧未检测到手");
            GUI.enabled=state.hasDraft&&!state.recording;if(Button("保存本次练习"))Act(()=>view.Recording.SaveRecording(acknowledge));GUI.enabled=true;
            GUILayout.Label(view.Recording.Message??"");
            if(Button("选择录制进行动作比较"))Act(()=>app.Navigate("compare"));
            if(Button("返回词汇视频"))Act(()=>app.Navigate("detail"));
        }
        void DrawReview()
        {
            GUILayout.Label("教学素材人工审核",new GUIStyle(GUI.skin.label){fontSize=26});GUILayout.Label("A/B/C 是来源分级。审核通过需专业人员亲自操作；不会自动认证。原始素材与旧录制不改动。");
            foreach(var word in Vocabulary.Words)if(Button(word.name+" · "+WordStatus(word.id)))Act(()=>{app.SelectWord(word.id);app.Navigate("review");});
            if(Selected==null)return;
            foreach(var item in Catalog.Items)if(item.word_id==app.Word&&Button(item.source_name))Act(()=>SelectMaterial(item.id));
            GUILayout.Label("当前："+Selected.word_name+" / "+Selected.id+" / "+Catalog.Status(Selected));SourceInfo();
            var video=GUILayoutUtility.GetRect(100,220);app.DrawVideo(video);VideoControls();
            GUILayout.Label("关键点："+Selected.extraction_status+" / "+Selected.frame_count+" 帧；时基核实："+Selected.timing_verified+"；镜像："+Selected.mirror_status);
            var previous=Catalog.Review(Selected);if(previous!=null)GUILayout.Label("上次审核："+previous.reviewer+" · "+previous.created_at+"\n"+previous.notes);
            GUILayout.Label("审核人");reviewer=GUILayout.TextField(reviewer);GUILayout.Label("专业身份/资质说明");qualification=GUILayout.TextField(qualification);GUILayout.Label("审核依据和备注");reviewNotes=GUILayout.TextArea(reviewNotes,GUILayout.MinHeight(70));
            professional=GUILayout.Toggle(professional,"我是人工专业审核人，已实际查看动作、来源和适用范围");
            GUILayout.BeginHorizontal();if(Button("人工审核通过…"))confirmReview="verified";if(Button("退回待补充…"))confirmReview="pending_review";if(Button("不适用…"))confirmReview="unsuitable";GUILayout.EndHorizontal();
            if(confirmReview!=null){GUILayout.Label("请确认修改 "+Selected.id+" 的状态为 "+confirmReview+"。通过教学审核不等于具备标准比较资格。");if(Button("确认记录此次人工审核"))Act(()=>{Catalog.SetReview(Selected,confirmReview,reviewer,qualification,reviewNotes,professional);confirmReview=null;});if(Button("取消"))confirmReview=null;}
        }
        void OnDestroy(){if(view!=null&&view.Recording!=null)view.Recording.Saved-=Saved;if(Preview!=null)Preview.Dispose();}
    }
}
