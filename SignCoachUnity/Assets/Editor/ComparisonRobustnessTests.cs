using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using SignCoach;
using SignCoach.Comparison;

public static class ComparisonRobustnessTests
{
    static readonly List<string> passed=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception("Comparison v2: "+name);passed.Add(name);}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,name);}
    static SequenceInput Clone(SequenceInput x)
    {var y=new SequenceInput{Name=x.Name+" derived",Frames=new RecordedFrame[x.Frames.Length]};for(int i=0;i<y.Frames.Length;i++)y.Frames[i]=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(x.Frames[i]));return y;}
    internal static SequenceInput Fixture(bool both,bool idleRight)
    {
        var real=SequenceComparer.Load(new RecordingStore(Path.GetFullPath("data/reference")),"hello","take_001");
        HandData left=null,right=null;foreach(var h in real.Frames[0].hands){if(h.handedness=="Left")left=h;else if(h.handedness=="Right")right=h;}
        var result=new SequenceInput{Name="controlled derivation of real geometry",Frames=new RecordedFrame[80]};
        for(int i=0;i<80;i++)
        {
            var hands=both?new[]{JsonUtility.FromJson<HandData>(JsonUtility.ToJson(left)),JsonUtility.FromJson<HandData>(JsonUtility.ToJson(right))}:new[]{JsonUtility.FromJson<HandData>(JsonUtility.ToJson(left))};
            for(int j=0;j<hands.Length;j++)if(j==0||!idleRight)foreach(var p in hands[j].landmarks){p.x+=(float)(.08*Math.Sin(i*2*Math.PI/79));p.y+=(float)(.035*Math.Cos(i*2*Math.PI/79));}
            result.Frames[i]=new RecordedFrame{timestamp=i*33,frame_index=i,hands=hands};
        }
        return result;
    }
    static double Oracle(double[,] costs,int i,int j,int direction,int streak,int run)
    {
        if(i==costs.GetLength(0)-1&&j==costs.GetLength(1)-1)return costs[i,j];double best=double.PositiveInfinity;
        if(i+1<costs.GetLength(0)&&j+1<costs.GetLength(1))best=Math.Min(best,Oracle(costs,i+1,j+1,0,0,run));
        if(i+1<costs.GetLength(0)&&(direction!=1||streak<run))best=Math.Min(best,Oracle(costs,i+1,j,1,direction==1?streak+1:1,run));
        if(j+1<costs.GetLength(1)&&(direction!=2||streak<run))best=Math.Min(best,Oracle(costs,i,j+1,2,direction==2?streak+1:1,run));return costs[i,j]+best;
    }
    public static void Run()
    {
        passed.Clear();var settings=new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact};var one=Fixture(false,false);var both=Fixture(true,true);string original=JsonUtility.ToJson(one.Frames[0]);
        var self=SequenceComparer.Compare(one,one,settings);Check(self.similarity>99.99999,"self comparison remains 100");
        var idle=SequenceComparer.Compare(both,one,settings);Check(idle.similarity>99.999&&idle.aligned_missing_hand_pairs==0,"idle companion hand does not penalize the same active hand");
        Check(idle.hand_pairing.Contains("Left")&&!idle.compares_both_hands,"automatic recommendation records the selected same-label hand");
        var moved=Clone(one);foreach(var f in moved.Frames)foreach(var h in f.hands)foreach(var p in h.landmarks){p.x=p.x*1.4f+.07f;p.y=p.y*1.4f-.03f;p.z=p.z*1.4f+.02f;}
        Check(SequenceComparer.Compare(one,moved,settings).similarity>99.99,"translation and camera-scale change preserve the same motion");
        var slow=Clone(one);var repeated=new List<RecordedFrame>();foreach(var f in slow.Frames)for(int k=0;k<3;k++){var v=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(f));v.frame_index=repeated.Count;v.timestamp=v.frame_index*33;repeated.Add(v);}slow.Frames=repeated.ToArray();
        var speed=SequenceComparer.Compare(one,slow,settings);Check(speed.similarity>99.999,"threefold temporal repetition aligns without a score loss");
        int run=0,dir=0;for(int k=1;k<speed.dtw.path.Length;k++){var p=speed.dtw.path[k];var prev=speed.dtw.path[k-1];int next=p.i==prev.i?1:p.j==prev.j?2:0;run=next!=0&&next==dir?run+1:next==0?0:1;dir=next;Check(run<=speed.dtw.maximum_run,"bounded alignment run "+k);}
        var padded=new SequenceInput{Name="leading and trailing no-hand derivation",Frames=new RecordedFrame[200]};
        for(int i=0;i<200;i++){var f=i>=60&&i<140?JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(one.Frames[i-60])):new RecordedFrame{hands=new HandData[0]};f.frame_index=i;f.timestamp=i*33;padded.Frames[i]=f;}
        var edges=SequenceComparer.Compare(one,padded,settings);Check(edges.similarity>99.999&&!edges.quality_b.low_quality&&edges.quality_b.leading_empty_frames==60&&edges.quality_b.trailing_empty_frames==60,"waiting before and after a gesture is excluded rather than treated as poor action");
        var shortGap=Clone(one);shortGap.Frames[25].hands=new HandData[0];shortGap.Frames[26].hands=new HandData[0];var repaired=SequenceComparer.Compare(one,shortGap,settings);
        Check(repaired.quality_b.interpolated_hands==2&&repaired.similarity>98,"bounded short tracking gap is repaired with a high same-action score");
        var longGap=Clone(one);for(int i=20;i<=45;i++)longGap.Frames[i].hands=new HandData[0];var incomplete=SequenceComparer.Compare(one,longGap,settings);
        Check(incomplete.quality_b.has_internal_long_gap&&!incomplete.score_available&&ComparisonScoreView.ScoreText(incomplete)=="—","long internal dropout cannot produce a presented grade");
        var other=Clone(one);foreach(var f in other.Frames)f.hands[0].handedness="Right";
        Reject(()=>SequenceComparer.Compare(one,other,settings),"opposite model labels are never swapped automatically");
        var explicitPair=SequenceComparer.Compare(one,other,new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,Hands=ComparisonHands.ExplicitSingle,ReferenceHand=0,PracticeHand=1});
        Check(explicitPair.manual_hand_pairing&&explicitPair.hand_pairing.Contains("Right")&&one.Frames[0].hands[0].handedness=="Left"&&other.Frames[0].hands[0].handedness=="Right","explicit role pairing is reported and does not relabel source data");
        Reject(()=>SequenceComparer.Compare(both,one,new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,Hands=ComparisonHands.Both}),"explicit two-hand comparison cannot ignore an absent required hand");
        var rotated=Clone(one);foreach(var f in rotated.Frames)foreach(var h in f.hands)foreach(var p in h.landmarks){double x=p.x-.5,y=(p.y-.5)*.75;p.x=(float)(.766044443*x-.64278761*y+.5);p.y=(float)((.64278761*x+.766044443*y)/.75+.5);}
        var pa=SequencePreparer.Prepare(one,settings);var pr=SequencePreparer.Prepare(rotated,settings);var groups=HandFeatureExtractor.GroupDistances(pa.Frames[10].Left,pr.Frames[10].Left);
        Check(groups[2]<1e-5&&groups[3]>.05,"palm rotation affects orientation rather than being charged twice as a shape error");
        Check(SequenceComparer.Compare(one,rotated,settings).similarity<self.similarity-1,"meaningful orientation differences remain measurable");
        var still=Clone(one);foreach(var f in still.Frames)f.hands=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(one.Frames[0])).hands;
        var notMoving=SequenceComparer.Compare(one,still,settings);Check(notMoving.similarity<1&&notMoving.motion_presence_score<1,"a stationary open hand cannot receive a high waving score");
        var backwards=Clone(one);Array.Reverse(backwards.Frames);for(int i=0;i<backwards.Frames.Length;i++){backwards.Frames[i].frame_index=i;backwards.Frames[i].timestamp=i*33;}
        Check(SequenceComparer.Compare(one,backwards,settings).similarity<self.similarity-1,"reverse motion is not erased by time alignment");
        ComparisonResult small=null,large=null;foreach(float amplitude in new[]{.002f,.02f}){var noisy=Clone(one);foreach(var f in noisy.Frames)foreach(var p in f.hands[0].landmarks)if(p.id==4||p.id==8||p.id==12||p.id==16||p.id==20)p.x+=amplitude;var result=SequenceComparer.Compare(one,noisy,settings);if(small==null)small=result;else large=result;}
        Check(small.similarity<100&&large.similarity<small.similarity,"larger controlled finger displacement reduces similarity");
        var dual=Fixture(true,false);var shifted=Clone(dual);foreach(var f in shifted.Frames)foreach(var p in f.hands[1].landmarks)p.x+=.2f;
        var layout=SequenceComparer.Compare(dual,shifted,new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,Hands=ComparisonHands.Both});Check(layout.relative_hands_score<95&&layout.similarity<99,"two-hand relative placement is retained after per-hand trajectory centering");
        var mirror=Clone(one);foreach(var f in mirror.Frames)foreach(var p in f.hands[0].landmarks)p.x=1-p.x;
        var corrected=SequenceComparer.Compare(mirror,one,new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,MirrorReference=true});
        Check(corrected.similarity>99.99&&corrected.reference_mirrored&&SequenceComparer.Compare(mirror,one,settings).similarity<99,"mirroring is corrected only when explicitly selected");
        Check(JsonUtility.ToJson(one.Frames[0])==original&&shortGap.Frames[25].hands.Length==0,"comparison does not rewrite input coordinates, labels or missing frames");
        double weighted=0,total=0;for(int g=0;g<6;g++){double w=HandFeatureExtractor.Weight(g,settings);weighted+=w*idle.component_scores[g];total+=w;}
        Check(Math.Abs(weighted/total-idle.similarity)<1e-8,"component scores explain the total on the same path");
        var costs=new double[,]{{1,.2,9,2,1},{2,.1,.4,1,2},{3,2,.1,.1,.5}};
        foreach(int bound in new[]{1,2})Check(Math.Abs(DtwSolver.SolveConstrained(costs,bound).raw_distance-Oracle(costs,0,0,0,0,bound))<1e-10,"constrained DTW matches independent exhaustive oracle run="+bound);
        using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();bool stopped=false;try{DtwSolver.SolveConstrained(costs,2,cancellation.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"constrained alignment honours cancellation");}
        Directory.CreateDirectory("Verification");File.WriteAllLines("Verification/comparison_v2_tests.txt",passed);
        File.WriteAllText("Verification/comparison_v2_examples.json",JsonUtility.ToJson(new Examples{self=self.similarity,idle_hand=idle.similarity,translated=moved==null?0:SequenceComparer.Compare(one,moved,settings).similarity,slower=speed.similarity,waiting=edges.similarity,short_gap=repaired.similarity,small_noise=small.similarity,large_noise=large.similarity,long_gap_scored=incomplete.score_available},true));
        Debug.Log("SIGNCOACH_COMPARISON_V2_TESTS_PASSED assertions="+passed.Count);
    }
    [Serializable] sealed class Examples{public double self,idle_hand,translated,slower,waiting,short_gap,small_noise,large_noise;public bool long_gap_scored;}
}
