using System;
using System.IO;
using UnityEngine;
using SignCoach;

public static class TeachingTests
{
    static int assertions;
    static void Check(bool value,string label){assertions++;if(!value)throw new Exception("Teaching: "+label);}
    static void Reject(Action action,string label){bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,label);}
    public static void Run()
    {
        assertions=0;
        var score=new SignCoach.Comparison.ComparisonResult{similarity=42.125,settings=new SignCoach.Comparison.ComparisonSettings()};
        Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="42.1","score uses existing numeric similarity");
        Check(SignCoach.Comparison.ComparisonScoreView.ScopeText(score).Contains("不是手语正确率"),"normal score scope explicit");
        score.low_quality=true;score.quality_a=new SignCoach.Comparison.SequenceQuality{low_quality=true};score.quality_b=new SignCoach.Comparison.SequenceQuality();
        Check(SignCoach.Comparison.ComparisonScoreView.ScopeText(score).Contains("更换参考")&&!SignCoach.Comparison.ComparisonScoreView.ScopeText(score).Contains("重新录制"),"poor reference does not blame the practice recording");
        score.low_quality=true;var beforeScore=score.ToJson();
        Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="42.1"&&SignCoach.Comparison.ComparisonScoreView.ScopeText(score).Contains("仅有效片段"),"low quality shows limited reference score");
        Check(SignCoach.Comparison.ComparisonScoreView.FormulaText(score).Contains("平均损失")&&score.ToJson()==beforeScore,"score presentation preserves algorithm result and quality");
        score.score_available=false;Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="—","severe data loss cannot masquerade as a numeric grade");score.score_available=true;
        score.similarity=double.NaN;Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="—","invalid score is not fabricated as zero");
        score.similarity=0;Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="0.0","zero score retained");
        score.similarity=100;Check(SignCoach.Comparison.ComparisonScoreView.ScoreText(score)=="100.0","full score retained");
        var hidden=BackgroundVisionProcess.Options("vision.exe","camera.bin");
        Check(!hidden.UseShellExecute&&hidden.CreateNoWindow&&hidden.WindowStyle==System.Diagnostics.ProcessWindowStyle.Hidden,"backend never opens console");
        Check(hidden.Arguments.Contains("--no-display"),"backend never opens OpenCV window");
        Check(hidden.RedirectStandardInput&&hidden.RedirectStandardError&&hidden.RedirectStandardOutput&&hidden.Arguments.Contains("--control-stdin"),"background lifecycle and diagnostics redirected");
        Check(RecordingPanel.DescribeState(new RecorderSnapshot(),false).Contains("尚未开始"),"camera preview is not recording");
        Check(RecordingPanel.DescribeState(new RecorderSnapshot{recording=true,hasDraft=true},false).Contains("正在记录"),"recording state explicit");
        Check(RecordingPanel.DescribeState(new RecorderSnapshot{hasDraft=true},false).Contains("尚未保存"),"stopped data not presented as saved");
        Check(RecordingPanel.DescribeState(new RecorderSnapshot(),true).Contains("已保存"),"saved state explicit");
        string identity=RecordingPanel.Identity(new RecordingMetadata{word_name="谢谢",word_id="thanks",take_id="take_004",created_at="2026-11-15T08:00:00Z",duration_ms=4200,frame_count=84});
        Check(identity.Contains("谢谢")&&identity.Contains("thanks/take_004.jsonl")&&identity.Contains("4.2")&&identity.Contains("84"),"recording identity shows word filename duration and frames");
        Check(!LearningApp.MatchesVideo("","goodbye.mp4","goodbye","thanks"),"empty source cannot reuse old video");
        Check(!LearningApp.MatchesVideo("goodbye.mp4","goodbye.mp4","goodbye","thanks"),"wrong word video rejected");
        Check(!LearningApp.MatchesVideo("thanks.mp4","goodbye.mp4","thanks","thanks"),"stale URL rejected");
        Check(LearningApp.MatchesVideo("thanks.mp4","thanks.mp4","thanks","thanks"),"matching word video accepted");
        string root=Path.Combine(Path.GetTempPath(),"signcoach_teaching_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var item=new TeachingMaterial{id="sample",word_id="goodbye",credibility="A",source_url="https://example.org/fixture",verification_status="verified",license="fixture",processing_allowed=true,landmark_path="frames.jsonl",video_path="sample.mp4"};
            File.WriteAllText(Path.Combine(root,"frames.jsonl"),"fixture");File.WriteAllText(Path.Combine(root,"sample.mp4"),"fixture");
            File.WriteAllText(Path.Combine(root,"catalog.json"),JsonUtility.ToJson(new TeachingManifest{items=new[]{item}}));
            var catalog=new TeachingCatalog(root,Path.Combine(root,"reviews"));item=catalog.Find("sample");string reason;
            Check(catalog.Status(item)=="pending_review","metadata verified alone cannot authenticate");
            Check(!catalog.CanCompare(item,out reason),"pending blocked");
            Reject(()=>catalog.Resolve("../outside.mp4"),"path traversal blocked");
            Reject(()=>catalog.SetReview(item,"verified","","","",false),"no reviewer cannot approve");
            Reject(()=>catalog.SetReview(item,"verified","test reviewer","test professional","test only",false),"professional confirmation required");
            // 仅 GUID 临时夹里的虚构测试素材进行审核测试，绝不认证真实库。
            catalog.SetReview(item,"verified","test reviewer","test qualification","synthetic fixture, not real approval",true);
            Check(catalog.Status(item)=="verified","manual record reload");
            Check(!catalog.CanCompare(item,out reason)&&reason.Contains("时基"),"unknown time/mirror blocked");
            item.timing_verified=true;item.mirror_status="unmirrored";
            Check(catalog.Status(item)=="pending_review","changed content invalidates review");
            catalog.SetReview(item,"verified","test reviewer","test qualification","synthetic fixture",true);
            Check(!catalog.CanCompare(item,out reason)&&reason.Contains("适用范围"),"hands scope approval required");
            item.hand_comparison_approved=true;catalog.SetReview(item,"verified","test reviewer","test qualification","synthetic fixture",true);
            Check(catalog.CanCompare(item,out reason),"complete fixture allowed by eligibility gate, file validation remains separate");
            File.AppendAllText(Path.Combine(root,"frames.jsonl"),"changed");Check(catalog.Status(item)=="pending_review","changed JSONL invalidates approval");
            catalog.SetReview(item,"unsuitable","test reviewer","","fixture retired",false);Check(catalog.Status(item)=="unsuitable","retire");
            catalog.SetReview(item,"pending_review","test reviewer","","need more information",false);Check(catalog.Status(item)=="pending_review","return pending");
            item.credibility="C";Reject(()=>catalog.SetReview(item,"verified","test reviewer","test qualification","fixture",true),"C cannot directly approve");
            Check(File.ReadAllLines(Path.Combine(root,"reviews/audit.jsonl")).Length==5,"audit history retained");
            var r=CameraPreview.Fit(new Rect(0,0,600,400),4f/3);Check(Math.Abs(r.width/r.height-4f/3)<.001,"preview aspect fit");
            Check(r.x>=0&&r.y>=0&&r.xMax<=600&&r.yMax<=400,"preview stays inside viewport");
            Check(CameraPreview.ShouldDrawSkeleton(false,1234,-1),"legacy TCP without preview keeps skeleton");
            Check(CameraPreview.ShouldDrawSkeleton(true,1234,1200),"fresh aligned preview keeps skeleton");
            Check(!CameraPreview.ShouldDrawSkeleton(true,1234,100),"fresh mismatched preview suppresses overlay");
            var point=new Landmark{id=0,x=.2f,y=.4f,z=.01f};
            var source=new SignCoach.Comparison.SequenceInput{Frames=new[]{new RecordedFrame{timestamp=12,frame_index=0,hands=new[]{new HandData{handedness="Right",handedness_score=.8f,landmarks=new[]{point}}}}}};
            var adapted=TeachingSequenceAdapter.ForCameraAspect(source,1);
            Check(Math.Abs(adapted.Frames[0].hands[0].landmarks[0].y-.4/.75)<.00001,"source aspect converted for existing feature metric");
            Check(point.y==.4f&&point.x==.2f&&point.z==.01f,"source coordinates untouched");
            Check(adapted.Frames[0].hands[0].handedness=="Right"&&adapted.Frames[0].timestamp==12,"labels and time unchanged");
            var real=new TeachingCatalog(Path.Combine(Application.streamingAssetsPath,"Teaching"),Path.Combine(root,"real_reviews"));
            foreach(var material in real.Items)Check(material.word_id!="hello"&&material.word_id!="drink","removed words absent from teaching catalog");
            string[] expectedDescriptions={"分别","感谢","歉意","进餐","协助","喜爱"};
            for(int i=0;i<Vocabulary.Words.Length;i++)Check(LearningApp.WordDescription(Vocabulary.Words[i].id).Contains(expectedDescriptions[i]),"word description still matches its ID after removal");
            foreach(var word in Vocabulary.Words){int count=0;foreach(var material in real.Items)if(material.word_id==word.id){count++;Check(real.Status(material)=="pending_review","real source not auto approved");}Check(count>=2,"two candidate records per word");}
            int local=0;foreach(var material in real.Items)if(real.HasVideo(material))
            {local++;Check(material.processing_allowed&&material.license=="CC BY 4.0","local permission recorded");Check(!real.CanCompare(material,out reason),"real candidate formal comparison blocked");Check(File.Exists(real.Resolve(material.landmark_path)),"JSONL extracted");
                var sequence=SignCoach.Comparison.SequenceComparer.LoadJsonl(real.Resolve(material.landmark_path));Check(sequence.Frames.Length==material.frame_count,"real extracted JSONL uses existing parser");}
            Check(local==6,"six distinct local word videos including eat");
            Directory.CreateDirectory("Verification");File.WriteAllText("Verification/teaching_tests.txt","PASS: "+assertions+" teaching assertions; synthetic approval tests only; all real candidates remain pending.\n");
            Debug.Log("SIGNCOACH_TEACHING_TESTS_PASSED assertions="+assertions);
        }
        finally{Directory.Delete(root,true);}
    }
}
