using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SignCoach.Comparison
{
    public static class SequenceComparer
    {
        public static SequenceInput Load(RecordingStore store,string word,string take)
        {
            var loaded=store.Load(word,take);
            return new SequenceInput{Name=loaded.metadata.word_name+" / "+take,Metadata=loaded.metadata,Frames=loaded.frames};
        }
        public static SequenceInput LoadJsonl(string path,RecordingMetadata metadata=null)
        {
            RecordedFrame[] frames;var validation=RecordingStore.Validate(path,true,out frames);
            if(!validation.IsValid)throw new FormatException(string.Join("；",validation.errors));
            return new SequenceInput{Name=System.IO.Path.GetFileName(path),Metadata=metadata,Frames=frames};
        }
        public static ComparisonResult Compare(SequenceInput a,SequenceInput b,ComparisonSettings settings,CancellationToken cancel=default(CancellationToken))
        {
            var watch=Stopwatch.StartNew();settings.Validate();cancel.ThrowIfCancellationRequested();var reference=settings.MirrorReference?MirrorCopy(a,cancel):a;
            var x=SequencePreparer.Prepare(reference,settings,cancel);var y=SequencePreparer.Prepare(b,settings,cancel);
            var plan=HandComparisonPlan.Resolve(x,y,settings);x=plan.Apply(x,true,settings);y=plan.Apply(y,false,settings);
            var dtw=DtwSolver.Compare(x.Frames,y.Frames,settings,cancel);
            var differences=new List<DifferenceInterval>();BuildDifferences("A",x,dtw.path,true,settings,differences);BuildDifferences("B",y,dtw.path,false,settings,differences);
            var means=new double[6];var scores=new double[6];double relation=0;int groupCount=0,missingPairs=0;
            foreach(var p in dtw.path)
            {
                var u=x.Frames[p.i];var v=y.Frames[p.j];int missing;
                var losses=HandFeatureExtractor.FrameLosses(u,v,settings,out missing);missingPairs+=missing;
                for(int g=0;g<6;g++)scores[g]+=100*(1-losses[g]);
                if(plan.ReferenceMask==3)relation+=100*(1-HandFeatureExtractor.RelativeLoss(u,v,settings));
                for(int side=0;side<2;side++)
                {
                    var h=side==0?u.Left:u.Right;var k=side==0?v.Left:v.Right;
                    if(h==null||k==null)continue;var values=HandFeatureExtractor.GroupDistances(h,k);for(int g=0;g<6;g++)means[g]+=values[g];groupCount++;
                }
            }
            if(groupCount>0)for(int g=0;g<6;g++)means[g]/=groupCount;
            for(int g=0;g<6;g++)scores[g]/=dtw.path.Length;
            bool canScore=x.Quality.action_reliable_ratio>=settings.MinimumScoreCoverage&&y.Quality.action_reliable_ratio>=settings.MinimumScoreCoverage
                &&!x.Quality.has_internal_long_gap&&!y.Quality.has_internal_long_gap&&x.Frames.Length>=5&&y.Frames.Length>=5;
            bool low=x.Quality.low_quality||y.Quality.low_quality;
            double motionA=plan.ReferenceMask==1?x.MotionLeft:plan.ReferenceMask==2?x.MotionRight:(x.MotionLeft+x.MotionRight)/2;
            double motionB=plan.PracticeMask==1?y.MotionLeft:plan.PracticeMask==2?y.MotionRight:(y.MotionLeft+y.MotionRight)/2;
            // 开掌静止不能因手型相同而冒充挥动；不按动作幅度线性压低所有动态样本。
            double motionAgreement=Math.Max(motionA,motionB)<settings.MotionThreshold?1:Math.Min(1,Math.Min(motionA,motionB)/Math.Max(1e-9,settings.MotionThreshold));
            double componentTotal=0,weightTotal=0;for(int g=0;g<6;g++){double w=HandFeatureExtractor.Weight(g,settings);componentTotal+=w*scores[g];weightTotal+=w;}
            double baseScore=componentTotal/weightTotal;
            if(plan.ReferenceMask==3)baseScore=(1-settings.BimanualWeight)*baseScore+settings.BimanualWeight*relation/dtw.path.Length;
            watch.Stop();return new ComparisonResult{name_a=a.Name,name_b=b.Name,kind_a=RecordingKinds.Label(a.Metadata),kind_b=RecordingKinds.Label(b.Metadata),
                duration_a_ms=x.DurationMs,duration_b_ms=y.DurationMs,quality_a=x.Quality,quality_b=y.Quality,settings=settings,dtw=dtw,
                similarity=Math.Max(0,Math.Min(100,baseScore))*motionAgreement,scoring_loss=1-baseScore/100,motion_presence_score=100*motionAgreement,elapsed_ms=watch.Elapsed.TotalMilliseconds,
                low_quality=low,score_available=canScore,score_unavailable_reason=canScore?"":"动作区间的有效覆盖不足、中途长缺失或样本过少，暂不评分，请重新录制或更换参考。",
                quality_message=!canScore?"数据不足，暂不评分；仍可查看已对齐片段。":x.Quality.low_quality&&!y.Quality.low_quality?"参考片段质量不足，建议更换参考；不能据此判断你的动作错误。":low?"仅对有效片段提供动作参考分，请结合双方数据质量查看。":"按已选择的执行手进行动作对照，不是手语正确率。",
                hand_pairing=plan.Label,manual_hand_pairing=plan.Manual,reference_mirrored=settings.MirrorReference,compares_both_hands=plan.ReferenceMask==3,
                component_scores=scores,relative_hands_score=plan.ReferenceMask==3?relation/dtw.path.Length:0,alignment_frames_a=x.Frames.Length,alignment_frames_b=y.Frames.Length,
                differences=differences.ToArray(),mean_group_distances=means,aligned_missing_hand_pairs=missingPairs};
        }
        static SequenceInput MirrorCopy(SequenceInput source,CancellationToken cancel)
        {
            if(source==null||source.Frames==null)throw new FormatException("参考序列为空");
            var frames=new RecordedFrame[source.Frames.Length];
            for(int i=0;i<frames.Length;i++){cancel.ThrowIfCancellationRequested();var f=source.Frames[i];if(f==null||f.hands==null)throw new FormatException("参考帧结构异常");var hands=new HandData[f.hands.Length];for(int h=0;h<hands.Length;h++){var original=f.hands[h];if(original==null||original.landmarks==null)throw new FormatException("参考手部结构异常");var points=new Landmark[original.landmarks.Length];for(int j=0;j<points.Length;j++){var p=original.landmarks[j];if(p==null)throw new FormatException("参考关键点为空");points[j]=new Landmark{id=p.id,x=1-p.x,y=p.y,z=p.z};}hands[h]=new HandData{handedness=original.handedness,handedness_score=original.handedness_score,landmarks=points};}frames[i]=new RecordedFrame{timestamp=f.timestamp,frame_index=f.frame_index,hands=hands};}
            return new SequenceInput{Name=source.Name,Metadata=source.Metadata,Frames=frames};
        }
        static void BuildDifferences(string label,PreparedSequence sequence,AlignmentPoint[] path,bool first,ComparisonSettings settings,List<DifferenceInterval> output)
        {
            var sums=new double[sequence.Frames.Length];var counts=new int[sums.Length];
            foreach(var p in path){int i=first?p.i:p.j;sums[i]+=p.local_distance;counts[i]++;}
            DifferenceInterval run=null;double total=0;int length=0;int previousRaw=-2;
            for(int i=0;i<=sums.Length;i++)
            {
                double distance=i<sums.Length?sums[i]/Math.Max(1,counts[i]):0;
                bool high=i<sums.Length&&distance>settings.DifferenceThreshold;
                int raw=i<sums.Length?sequence.Frames[i].OriginalIndex:-1;
                if(run!=null&&(!high||raw!=previousRaw+1)) {run.mean_distance=total/length;output.Add(run);run=null;total=0;length=0;}
                if(high)
                {
                    if(run==null)run=new DifferenceInterval{sequence=label,start_frame=raw,start_ms=sequence.Frames[i].TimeMs};
                    run.end_frame=raw;run.end_ms=sequence.Frames[i].TimeMs;total+=distance;length++;
                }
                previousRaw=raw;
            }
        }
    }
}
