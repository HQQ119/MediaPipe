using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SignCoach.Comparison
{
    public static class SequencePreparer
    {
        static void Intervals(bool[] available,RecordedFrame[] raw,string reason,ComparisonSettings settings,List<MissingInterval> output)
        {
            int start=-1;
            for(int i=0;i<=raw.Length;i++)
            {
                bool valid=i==raw.Length||available[i];
                if(!valid&&start<0)start=i;
                if(valid&&start>=0)
                {
                    long duration=raw[i-1].timestamp-raw[start].timestamp;
                    output.Add(new MissingInterval{reason=reason,start_frame=start,end_frame=i-1,start_ms=raw[start].timestamp-raw[0].timestamp,
                        end_ms=raw[i-1].timestamp-raw[0].timestamp,duration_ms=duration,long_gap=duration>settings.LongMissingMs}); start=-1;
                }
            }
        }
        static int Interpolate(Vector3[][] samples,bool[] synthetic,RecordedFrame[] raw,ComparisonSettings settings)
        {
            int added=0;
            for(int i=0;i<samples.Length;i++)
            {
                if(samples[i]!=null)continue;
                int start=i;while(i<samples.Length&&samples[i]==null)i++;
                int end=i-1;
                if(start==0||i==samples.Length||end-start+1>settings.InterpolationMaxFrames||
                    raw[i].timestamp-raw[start-1].timestamp>settings.InterpolationMaxSpanMs)continue;
                for(int j=start;j<=end;j++)
                {
                    double alpha=(double)(raw[j].timestamp-raw[start-1].timestamp)/(raw[i].timestamp-raw[start-1].timestamp);
                    samples[j]=new Vector3[21];for(int p=0;p<21;p++)samples[j][p]=Vector3.LerpUnclamped(samples[start-1][p],samples[i][p],(float)alpha);
                    synthetic[j]=true;added++;
                }
            }
            return added;
        }
        public static PreparedSequence Prepare(SequenceInput input,ComparisonSettings settings,CancellationToken cancel=default(CancellationToken))
        {
            settings.Validate();cancel.ThrowIfCancellationRequested();
            if(input==null||input.Frames==null||input.Frames.Length<2)throw new FormatException("序列为空或不足两帧");
            var raw=input.Frames;int n=raw.Length;
            var left=new Vector3[n][];var right=new Vector3[n][];var detected=new bool[n];var availableL=new bool[n];var availableR=new bool[n];
            var quality=new SequenceQuality{total_frames=n};var scales=new List<double>();
            int countL=0,countR=0;
            for(int i=0;i<n;i++)
            {
                cancel.ThrowIfCancellationRequested();var frame=raw[i];
                if(frame==null||frame.hands==null||frame.hands.Length>2||frame.frame_index!=i||frame.timestamp<0||(i>0&&frame.timestamp<=raw[i-1].timestamp))
                    throw new FormatException("帧结构、索引或时间戳异常");
                detected[i]=frame.hands.Length>0;if(detected[i])quality.detected_frames++;else quality.no_hand_frames++;
                int labelsL=0,labelsR=0;Vector3[] candidateL=null,candidateR=null;
                foreach(var hand in frame.hands)
                {
                    if(hand==null)throw new FormatException("空手部对象");
                    var points=HandFeatureExtractor.Points(hand,settings);
                    if(hand.handedness=="Left"){labelsL++;candidateL=points;}
                    else if(hand.handedness=="Right"){labelsR++;candidateR=points;}
                    else if(hand.handedness=="Unknown")quality.unknown_hand_frames++;
                    else throw new FormatException("无法识别 handedness");
                }
                if(labelsL>1||labelsR>1)quality.ambiguous_frames++;
                if(labelsL==1)left[i]=candidateL;if(labelsR==1)right[i]=candidateR;
                for(int side=0;side<2;side++)
                {
                    var points=side==0?left[i]:right[i];if(points==null)continue;
                    if(HandFeatureExtractor.Extract(points,Vector3.zero,1,false)==null)
                    {quality.degenerate_hands++;if(side==0)left[i]=null;else right[i]=null;}
                    else scales.Add(HandFeatureExtractor.PalmScale(points));
                }
                availableL[i]=left[i]!=null;availableR[i]=right[i]!=null;
                if(availableL[i])countL++;if(availableR[i])countR++;
                if(availableL[i]||availableR[i])quality.reliable_frames++;
            }
            if(scales.Count==0||quality.reliable_frames<2)throw new FormatException("没有足够的有效、可区分左右手的动作帧");
            quality.detected_ratio=(double)quality.detected_frames/n;quality.reliable_ratio=(double)quality.reliable_frames/n;
            bool activeL=countL>=Math.Max(1,quality.reliable_frames*settings.ActiveHandRatio),activeR=countR>=Math.Max(1,quality.reliable_frames*settings.ActiveHandRatio);
            var interpolatedL=new bool[n];var interpolatedR=new bool[n];
            quality.interpolated_hands=Interpolate(left,interpolatedL,raw,settings)+Interpolate(right,interpolatedR,raw,settings);
            double scaleL,scaleR;
            var featuresL=ExtractHand(left,interpolatedL,true,out scaleL);
            var featuresR=ExtractHand(right,interpolatedR,false,out scaleR);
            var frames=new List<FeatureFrame>();
            for(int i=0;i<n;i++)
            {
                var a=featuresL[i];var b=featuresR[i];
                double[] relative=null;
                if(a!=null&&b!=null){var d=(HandFeatureExtractor.PalmCenter(left[i])-HandFeatureExtractor.PalmCenter(right[i]))/(float)((scaleL+scaleR)/2);relative=new[]{(double)d.x,(double)d.y,0.0};}
                if(a!=null||b!=null)frames.Add(new FeatureFrame{OriginalIndex=i,TimeMs=raw[i].timestamp-raw[0].timestamp,Left=a,Right=b,RelativeHands=relative});
            }
            if(frames.Count<2)throw new FormatException("可用于比较的帧不足两帧");
            quality.used_frames=frames.Count;quality.excluded_frames=n-frames.Count;
            var prepared=new PreparedSequence{Input=input,Frames=frames.ToArray(),Quality=quality,DurationMs=raw[n-1].timestamp-raw[0].timestamp,ActiveLeft=activeL,ActiveRight=activeR,
                AvailableLeft=availableL,AvailableRight=availableR,Detected=detected,MotionLeft=Motion(featuresL),MotionRight=Motion(featuresR)};
            return Select(prepared,3,settings);
        }
        static double Quantile(List<double> values,double q)
        {values.Sort();double at=q*(values.Count-1);int lo=(int)at,hi=Math.Min(values.Count-1,lo+1);return values[lo]+(values[hi]-values[lo])*(at-lo);}
        static HandFeature[] ExtractHand(Vector3[][] points,bool[] synthetic,bool isLeft,out double scale)
        {
            var scales=new List<double>();var xs=new List<double>();var ys=new List<double>();
            for(int i=0;i<points.Length;i++)if(points[i]!=null&&!synthetic[i]){scales.Add(HandFeatureExtractor.PalmScale(points[i]));var c=HandFeatureExtractor.PalmCenter(points[i]);xs.Add(c.x);ys.Add(c.y);}
            var output=new HandFeature[points.Length];scale=scales.Count==0?1:Quantile(scales,.5);if(scales.Count==0)return output;
            // 每只手都采用自身全段中位中心与掌尺度，不因另一只手是否入镜改变基准。
            var anchor=new Vector3((float)Quantile(xs,.5),(float)Quantile(ys,.5),0);
            for(int i=0;i<points.Length;i++)if(points[i]!=null)output[i]=HandFeatureExtractor.Extract(points[i],anchor,scale,synthetic[i],isLeft);
            return output;
        }
        static double Motion(HandFeature[] frames)
        {
            var values=new List<double>();double total=0;
            for(int axis=0;axis<2;axis++){values.Clear();foreach(var f in frames)if(f!=null)values.Add(f.Center[axis]);if(values.Count<2)return 0;double range=Quantile(values,.9)-Quantile(values,.1);total+=range*range;}
            double pose=0;for(int j=0;j<15;j++){values.Clear();foreach(var f in frames)if(f!=null)values.Add(f.Angles[j]);double range=Quantile(values,.9)-Quantile(values,.1);pose+=range*range;}
            double orientation=0;for(int j=0;j<6;j++){values.Clear();foreach(var f in frames)if(f!=null)values.Add(f.Direction[j]);double range=Quantile(values,.9)-Quantile(values,.1);orientation+=range*range;}
            return Math.Sqrt(total)+2*Math.Sqrt(pose/15)+Math.Sqrt(orientation/6);
        }
        public static PreparedSequence Select(PreparedSequence source,int mask,ComparisonSettings settings)
        {
            var frames=new List<FeatureFrame>();
            foreach(var f in source.Frames){var l=(mask&1)!=0?f.Left:null;var r=(mask&2)!=0?f.Right:null;if(l!=null||r!=null)frames.Add(new FeatureFrame{OriginalIndex=f.OriginalIndex,TimeMs=f.TimeMs,Left=l,Right=r,RequiresBoth=mask==3,RelativeHands=mask==3?f.RelativeHands:null});}
            if(frames.Count<2)throw new InvalidOperationException("所选执行手没有足够数据，请确认Left/Right标签及画面方向");
            int start=frames[0].OriginalIndex,end=frames[frames.Count-1].OriginalIndex;var raw=source.Input.Frames;
            var old=source.Quality;var q=new SequenceQuality{total_frames=raw.Length,detected_frames=old.detected_frames,no_hand_frames=old.no_hand_frames,ambiguous_frames=old.ambiguous_frames,
                unknown_hand_frames=old.unknown_hand_frames,degenerate_hands=old.degenerate_hands,interpolated_hands=0,used_frames=frames.Count,excluded_frames=raw.Length-frames.Count,
                action_start_frame=start,action_end_frame=end,action_frames=end-start+1,leading_empty_frames=start,trailing_empty_frames=raw.Length-1-end};
            var warnings=new List<string>();var gaps=new List<MissingInterval>();
            Intervals(source.Detected,raw,"no_detection",settings,gaps);
            if((mask&1)!=0)Intervals(source.AvailableLeft,raw,"Left_unavailable_or_ambiguous",settings,gaps);
            if((mask&2)!=0)Intervals(source.AvailableRight,raw,"Right_unavailable_or_ambiguous",settings,gaps);
            for(int i=0;i<raw.Length;i++)
            {
                bool reliable=((mask&1)==0||source.AvailableLeft[i])&&((mask&2)==0||source.AvailableRight[i]);
                if(reliable){q.reliable_frames++;if(i>=start&&i<=end)q.action_reliable_frames++;}
                if(i>=start&&i<=end){int l=0,r=0;foreach(var hand in raw[i].hands){if(hand.handedness=="Left")l++;if(hand.handedness=="Right")r++;}if(((mask&1)!=0&&l>1)||((mask&2)!=0&&r>1))q.action_ambiguous_frames++;}
                if(i>start&&i<=end&&raw[i].timestamp-raw[i-1].timestamp>settings.LongMissingMs)q.has_internal_long_gap=true;
            }
            foreach(var f in frames){if(f.Left!=null&&f.Left.Interpolated)q.interpolated_hands++;if(f.Right!=null&&f.Right.Interpolated)q.interpolated_hands++;}
            foreach(var gap in gaps)
            {
                if(gap.reason=="no_detection"||((mask==1&&gap.reason.StartsWith("Left"))||(mask==2&&gap.reason.StartsWith("Right")))||mask==3)
                {int a=Math.Max(start,gap.start_frame),b=Math.Min(end,gap.end_frame);if(b>=a&&raw[b].timestamp-raw[a].timestamp>settings.LongMissingMs)q.has_internal_long_gap=true;}
            }
            q.internal_missing_frames=q.action_frames-q.action_reliable_frames;
            q.detected_ratio=(double)q.detected_frames/q.total_frames;q.reliable_ratio=(double)q.reliable_frames/q.total_frames;q.action_reliable_ratio=(double)q.action_reliable_frames/q.action_frames;
            q.low_quality=q.action_reliable_ratio<settings.MinimumReliableRatio||q.has_internal_long_gap||(double)q.action_ambiguous_frames/q.action_frames>settings.MaximumAmbiguousRatio;
            if(q.leading_empty_frames+q.trailing_empty_frames>0)warnings.Add("已排除执行手入镜前后空白，不作为动作错误扣分");
            if(q.ambiguous_frames>0)warnings.Add("保留模型标签；重复标签帧不可用，未猜测或交换左右手");
            if(q.interpolated_hands>0)warnings.Add("短缺失仅在有前后锚点时插值，原文件未改变");
            if(q.has_internal_long_gap)warnings.Add("动作区间中途存在长缺失，不能代表完整动作");
            if(q.action_reliable_ratio<settings.MinimumReliableRatio)warnings.Add("所选执行手在动作区间内的可靠比例不足");
            q.warnings=warnings.ToArray();q.missing_intervals=gaps.ToArray();
            return new PreparedSequence{Input=source.Input,Frames=frames.ToArray(),Quality=q,DurationMs=source.DurationMs,ActiveLeft=(mask&1)!=0&&source.ActiveLeft,ActiveRight=(mask&2)!=0&&source.ActiveRight,
                AvailableLeft=source.AvailableLeft,AvailableRight=source.AvailableRight,Detected=source.Detected,MotionLeft=source.MotionLeft,MotionRight=source.MotionRight};
        }
    }
}
