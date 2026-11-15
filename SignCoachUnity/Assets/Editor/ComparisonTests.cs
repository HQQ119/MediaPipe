using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using SignCoach;
using SignCoach.Comparison;

[Serializable] public sealed class ComparisonTestCase
{
    public string test, description;
    public double raw_distance, normalized_distance, similarity, elapsed_ms;
    public int path_length, used_a, used_b, missing_b;
    public bool low_quality;
}
[Serializable] public sealed class ComparisonTestSummary { public int assertions; public ComparisonTestCase[] cases; public string source; public string[] successful_checks; }
public static class ComparisonTests
{
    static int assertions;
    static readonly HashSet<string> checkNames=new HashSet<string>();
    static void Check(bool value,string name){if(!value)throw new Exception("Comparison test failed: "+name);assertions++;checkNames.Add(name);}
    static void Reject(Action action,string name)
    {bool rejected=false;try{action();}catch(Exception ex)when(ex is FormatException||ex is ArgumentException||ex is InvalidOperationException||ex is OperationCanceledException){rejected=true;}Check(rejected,name);}
    static SequenceInput Clone(SequenceInput input)
    {
        var result=new SequenceInput{Name=input.Name+" [derived]",Metadata=input.Metadata,Frames=new RecordedFrame[input.Frames.Length]};
        for(int i=0;i<result.Frames.Length;i++)result.Frames[i]=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(input.Frames[i]));return result;
    }
    static void Add(List<ComparisonTestCase> output,string name,string description,ComparisonResult result)
    { output.Add(new ComparisonTestCase{test=name,description=description,raw_distance=result.dtw.raw_distance,normalized_distance=result.dtw.normalized_distance,
        similarity=result.similarity,elapsed_ms=result.elapsed_ms,path_length=result.dtw.path.Length,used_a=result.quality_a.used_frames,
        used_b=result.quality_b.used_frames,missing_b=result.quality_b.no_hand_frames,low_quality=result.low_quality}); }
    static void PathCheck(DtwResult result,int n,int m)
    {
        var path=result.path;Check(path[0].i==0&&path[0].j==0&&path[path.Length-1].i==n-1&&path[path.Length-1].j==m-1,"path endpoints");
        double sum=0;for(int k=0;k<path.Length;k++)
        {
            sum+=result.LocalCosts[path[k].i,path[k].j];
            if(k>0){int di=path[k].i-path[k-1].i,dj=path[k].j-path[k-1].j;Check(di>=0&&di<=1&&dj>=0&&dj<=1&&di+dj>0,"path monotonic unit steps");}
        }
        Check(Math.Abs(sum-result.raw_distance)<1e-8,"path sum equals DP optimum");
        Check(Math.Abs(sum/path.Length-result.normalized_distance)<1e-8,"path length normalization");
    }
    static double Oracle(double[,] c,int i,int j)
    {
        if(i==c.GetLength(0)-1&&j==c.GetLength(1)-1)return c[i,j];
        double best=double.PositiveInfinity;
        if(i+1<c.GetLength(0))best=Math.Min(best,Oracle(c,i+1,j));
        if(j+1<c.GetLength(1))best=Math.Min(best,Oracle(c,i,j+1));
        if(i+1<c.GetLength(0)&&j+1<c.GetLength(1))best=Math.Min(best,Oracle(c,i+1,j+1));return c[i,j]+best;
    }
    static string Hash(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
    public static void Run()
    {
        assertions=0;checkNames.Clear();var cases=new List<ComparisonTestCase>();var settings=new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact};
        var store=new RecordingStore(Path.GetFullPath("data/reference"));
        var a=SequenceComparer.Load(store,"hello","take_001");var b=SequenceComparer.Load(store,"hello","take_002");
        string pathA=store.FramePath("hello","take_001"),pathB=store.FramePath("hello","take_002"),hashA=Hash(pathA),hashB=Hash(pathB);
        var self=SequenceComparer.Compare(a,a,settings);Check(self.dtw.normalized_distance<1e-10&&self.similarity>99.999999,"A real sequence self comparison");
        PathCheck(self.dtw,self.quality_a.used_frames,self.quality_b.used_frames);Add(cases,"A","same real JSONL; quality warnings retained; diagnostic score",self);
        var faster=Clone(a);var doubled=new List<RecordedFrame>();long first=a.Frames[0].timestamp;
        foreach(var original in a.Frames)for(int r=0;r<2;r++)
        {var frame=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(original));frame.frame_index=doubled.Count;frame.timestamp=first+2*(original.timestamp-first)+r;doubled.Add(frame);}
        faster.Frames=doubled.ToArray();
        var speedSettings=new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,InterpolationMaxFrames=0,InterpolationMaxSpanMs=0};
        var speed=SequenceComparer.Compare(a,faster,speedSettings);Check(speed.similarity>99.9,"B zero-order hold resampling speed invariant");
        PathCheck(speed.dtw,speed.quality_a.used_frames,speed.quality_b.used_frames);Add(cases,"B","real sequence 2x zero-order hold resampling; interpolation disabled to isolate time warp",speed);
        ComparisonResult smaller=null,larger=null;
        foreach(double amplitude in new[]{.002,.01})
        {
            var noisy=Clone(a);
            for(int i=0;i<noisy.Frames.Length;i++)foreach(var h in noisy.Frames[i].hands)foreach(var p in h.landmarks)
                if(p.id==4||p.id==8||p.id==12||p.id==16||p.id==20){p.x+=(float)(amplitude*Math.Sin(i*.17+p.id));p.y+=(float)(amplitude*Math.Cos(i*.13+p.id));}
            var result=SequenceComparer.Compare(a,noisy,settings);if(smaller==null)smaller=result;else larger=result;
            Add(cases,"C amplitude="+amplitude,"deterministic fingertip perturbation in normalized image coordinates",result);
        }
        Check(smaller.dtw.normalized_distance>0&&larger.dtw.normalized_distance>smaller.dtw.normalized_distance&&larger.similarity<smaller.similarity,"C noise monotonic in tested amplitudes");
        var preparedB=SequencePreparer.Prepare(b,settings);
        Check(preparedB.Quality.no_hand_frames==70&&Math.Abs(preparedB.Quality.detected_ratio-603.0/673)<1e-12,"D raw missing frame statistics");
        bool longGap=false;foreach(var gap in preparedB.Quality.missing_intervals)if(gap.reason=="no_detection"&&gap.long_gap)longGap=true;
        Check(longGap&&preparedB.Quality.trailing_empty_frames>=70&&!preparedB.Quality.has_internal_long_gap,"D trailing no-hand interval is reported and excluded from action quality");
        foreach(var frame in preparedB.Frames)Check(frame.OriginalIndex<603,"D trailing 70 no-hand frames not zero-filled/interpolated");
        var ab=SequenceComparer.Compare(a,b,settings);Add(cases,"D/F","two existing real recordings, different lengths; quality assessed within action interval",ab);
        PathCheck(ab.dtw,ab.quality_a.used_frames,ab.quality_b.used_frames);
        var ordered=Clone(a);foreach(var f in ordered.Frames){Array.Reverse(f.hands);foreach(var h in f.hands)Array.Reverse(h.landmarks);}
        var orderResult=SequenceComparer.Compare(a,ordered,settings);Check(orderResult.dtw.normalized_distance<1e-10,"E hand and landmark array order irrelevant");Add(cases,"E","reversed array order; labels and IDs unchanged",orderResult);
        // 稳定几何夹具来源于真实一帧，变化与缺失均为明确标注的派生数据。
        var fixture=new SequenceInput{Name="derived 80-frame fixture",Frames=new RecordedFrame[80]};
        var template=a.Frames[0];
        for(int i=0;i<80;i++)
        {
            var f=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(template));f.timestamp=i*33;f.frame_index=i;
            foreach(var h in f.hands)foreach(var p in h.landmarks){p.x+=(float)(.02*Math.Sin(i*.08));p.y+=(float)(.01*Math.Cos(i*.06));}
            fixture.Frames[i]=f;
        }
        var one=Clone(fixture);foreach(var f in one.Frames){HandData hand=null;foreach(var h in f.hands)if(h.handedness=="Left")hand=h;f.hands=new[]{hand};}
        var single=SequenceComparer.Compare(one,one,settings);Check(single.similarity>99.99999,"E single left hand does not require right");
        var other=Clone(one);foreach(var f in other.Frames)f.hands[0].handedness="Right";
        Reject(()=>SequenceComparer.Compare(one,other,settings),"E no silent left-right swap");
        var swapped=Clone(fixture);foreach(var f in swapped.Frames)foreach(var h in f.hands)h.handedness=h.handedness=="Left"?"Right":"Left";
        Check(SequenceComparer.Compare(fixture,swapped,settings).dtw.normalized_distance>0,"E meaningful label swap detected");
        var translated=Clone(fixture);foreach(var f in translated.Frames)foreach(var h in f.hands)foreach(var p in h.landmarks){p.x=1.4f*p.x+.08f;p.y=1.4f*p.y-.04f;p.z=1.4f*p.z+.05f;}
        Check(SequenceComparer.Compare(fixture,translated,settings).dtw.normalized_distance<1e-4,"normalization removes uniform scale and translation");
        var rotated=Clone(fixture);foreach(var f in rotated.Frames)foreach(var h in f.hands)foreach(var p in h.landmarks)
        {double x=p.x-.5,y=(p.y-.5)*.75;p.x=(float)(.8660254038*x-.5*y+.5);p.y=(float)((.5*x+.8660254038*y)/.75+.5);}
        Check(SequenceComparer.Compare(fixture,rotated,settings).dtw.normalized_distance>.01,"palm orientation is not normalized away");
        var shortGap=Clone(fixture);shortGap.Frames[10].hands=new HandData[0];var shortPrepared=SequencePreparer.Prepare(shortGap,settings);
        Check(shortPrepared.Quality.interpolated_hands==2&&shortPrepared.Quality.used_frames==80,"bounded short hand gap interpolation");
        Check(shortGap.Frames[10].hands.Length==0,"interpolation does not mutate original input");
        var longMissing=Clone(fixture);for(int i=20;i<=45;i++)longMissing.Frames[i].hands=new HandData[0];var longPrepared=SequencePreparer.Prepare(longMissing,settings);
        Check(longPrepared.Quality.low_quality&&longPrepared.Quality.excluded_frames==26,"long gap quality and exclusion");
        var broken=Clone(fixture);broken.Frames[2].timestamp=broken.Frames[1].timestamp;Reject(()=>SequencePreparer.Prepare(broken,settings),"G timestamp disorder");
        broken=Clone(fixture);broken.Frames[2].hands[0].landmarks=new Landmark[20];Reject(()=>SequencePreparer.Prepare(broken,settings),"G landmark count");
        broken=Clone(fixture);broken.Frames[2].hands[0].landmarks[0].x=float.NaN;Reject(()=>SequencePreparer.Prepare(broken,settings),"G nonfinite coordinates");
        broken=Clone(fixture);foreach(var f in broken.Frames)f.hands=new HandData[0];Reject(()=>SequencePreparer.Prepare(broken,settings),"G no effective action");
        Reject(()=>SequencePreparer.Prepare(new SequenceInput{Frames=new RecordedFrame[0]},settings),"G empty input");
        string temp=Path.Combine(Path.GetTempPath(),"signcoach_comparison_"+Guid.NewGuid().ToString("N")+".jsonl");
        try{File.WriteAllText(temp,"");Reject(()=>SequenceComparer.LoadJsonl(temp),"G empty JSONL");File.WriteAllText(temp,"{broken\n");Reject(()=>SequenceComparer.LoadJsonl(temp),"G malformed JSONL");}
        finally{if(File.Exists(temp))File.Delete(temp);}
        var costs=new double[,]{{1,2,9},{8,1,3}};var solved=DtwSolver.Solve(costs);
        Check(Math.Abs(solved.raw_distance-Oracle(costs,0,0))<1e-12&&solved.raw_distance==5,"DTW optimum vs exhaustive paths");PathCheck(solved,2,3);
        Reject(()=>DtwSolver.Compare(SequencePreparer.Prepare(fixture,settings).Frames,SequencePreparer.Prepare(one,settings).Frames,new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,MaxMatrixCells=1}),"memory guard");
        Reject(()=>DtwSolver.Compare(SequencePreparer.Prepare(fixture,settings).Frames,new[]{SequencePreparer.Prepare(fixture,settings).Frames[0],SequencePreparer.Prepare(fixture,settings).Frames[1]},new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact,WindowRatio=0}),"unreachable window reported");
        var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>SequenceComparer.Compare(a,b,settings,cancel.Token),"cancellation");cancel.Dispose();
        Check(RecordingKinds.Resolve(a.Metadata)==RecordingKind.UnverifiedTest,"existing data never promoted");
        Check(RecordingKinds.Resolve(new RecordingMetadata{verification_status="verified_reference",source_reference="documented external reference"})==RecordingKind.VerifiedReference,"metadata reference type");
        Check(RecordingKinds.Resolve(new RecordingMetadata{source="user_practice"})==RecordingKind.UserPractice,"practice type");
        Check(Hash(pathA)==hashA&&Hash(pathB)==hashB,"original JSONL unchanged");
        Directory.CreateDirectory("Verification");File.WriteAllText("Verification/comparison_tests.json",JsonUtility.ToJson(new ComparisonTestSummary{assertions=assertions,cases=cases.ToArray(),source="actual C# execution; A-F use existing real files or labeled derivations; G controlled malformed data",successful_checks=new List<string>(checkNames).ToArray()},true));
        File.WriteAllText("Verification/comparison_example.json",ab.ToJson());
        File.WriteAllText("Verification/comparison_self_example.json",self.ToJson());
        File.WriteAllText("Verification/dtw_oracle_case.json","{\"costs\":[[1,2,9],[8,1,3]],\"result\":"+JsonUtility.ToJson(solved)+"}");
        Debug.Log("SIGNCOACH_COMPARISON_TESTS_PASSED assertions="+assertions);
    }
}
