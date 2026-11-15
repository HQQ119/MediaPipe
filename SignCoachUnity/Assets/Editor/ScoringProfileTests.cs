using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using SignCoach;
using SignCoach.Comparison;

public static class ScoringProfileTests
{
    static readonly List<string> passed=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception("Scoring profiles: "+name);passed.Add(name);}
    static SequenceInput Clone(SequenceInput x){var y=new SequenceInput{Name=x.Name,Frames=new RecordedFrame[x.Frames.Length]};for(int i=0;i<y.Frames.Length;i++)y.Frames[i]=JsonUtility.FromJson<RecordedFrame>(JsonUtility.ToJson(x.Frames[i]));return y;}
    static bool SamePath(ComparisonResult a,ComparisonResult b)
    {if(a.dtw.raw_distance!=b.dtw.raw_distance||a.dtw.normalized_distance!=b.dtw.normalized_distance||a.dtw.path.Length!=b.dtw.path.Length)return false;for(int i=0;i<a.dtw.path.Length;i++)if(a.dtw.path[i].i!=b.dtw.path[i].i||a.dtw.path[i].j!=b.dtw.path[i].j)return false;return true;}
    public static void Run()
    {
        passed.Clear();var intro=new ComparisonSettings();var exact=new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact};
        Check(intro.ScoringProfile==ComparisonScoringProfile.Introductory,"default explicitly introductory");
        Check(Math.Abs(100*(1-HandFeatureExtractor.LossAtTolerance(1,1,intro))-85)<1e-10,"configured tolerance gives 85 introductory points");
        Check(Math.Abs(100*(1-HandFeatureExtractor.LossAtTolerance(1,1,exact))-50)<1e-10,"exact tolerance retains original 50 points");
        double previous=-1;for(int i=0;i<=100;i++){double loss=HandFeatureExtractor.LossAtTolerance(i*.1,1,intro);Check(loss>=previous&&loss>=0&&loss<=1,"monotonic bounded loss "+i);previous=loss;}
        Check(100*(1-previous)<6,"extreme difference still receives under six points");
        var one=ComparisonRobustnessTests.Fixture(false,false);Check(SequenceComparer.Compare(one,one,intro).similarity>99.99999,"intro identity is 100");
        ComparisonResult small=null,large=null;foreach(float amplitude in new[]{.002f,.02f})
        {
            var noisy=Clone(one);foreach(var f in noisy.Frames)foreach(var p in f.hands[0].landmarks)if(p.id==4||p.id==8||p.id==12||p.id==16||p.id==20)p.x+=amplitude;
            var a=SequenceComparer.Compare(one,noisy,exact);var b=SequenceComparer.Compare(one,noisy,intro);Check(SamePath(a,b),"profiles retain identical DTW distance and path amplitude="+amplitude);
            Check(b.similarity>a.similarity,"intro tolerates modest finger offsets amplitude="+amplitude);if(small==null)small=b;else large=b;
            Check(Math.Abs(b.similarity-100*(1-b.scoring_loss)*b.motion_presence_score/100)<1e-9,"reported scoring loss explains total");
        }
        Check(small.similarity<100&&large.similarity<small.similarity,"larger finger displacement reduces introductory score");
        var still=Clone(one);foreach(var f in still.Frames)f.hands=Clone(one).Frames[0].hands;
        Check(SequenceComparer.Compare(one,still,intro).similarity<1,"stationary hand cannot pass moving gesture in intro");
        var gap=Clone(one);for(int i=20;i<=45;i++)gap.Frames[i].hands=new HandData[0];Check(!SequenceComparer.Compare(one,gap,intro).score_available,"long dropout still blocks intro score");
        var wrong=Clone(one);foreach(var f in wrong.Frames)f.hands[0].handedness="Right";bool rejected=false;try{SequenceComparer.Compare(one,wrong,intro);}catch(InvalidOperationException){rejected=true;}Check(rejected,"intro never silently swaps handedness");
        var both=ComparisonRobustnessTests.Fixture(true,false);var shift=Clone(both);foreach(var f in shift.Frames)foreach(var p in f.hands[1].landmarks)p.x+=.2f;
        var settings=new ComparisonSettings{Hands=ComparisonHands.Both};var dual=SequenceComparer.Compare(both,shift,settings);double weighted=0,total=0;for(int g=0;g<6;g++){double w=HandFeatureExtractor.Weight(g,settings);weighted+=w*dual.component_scores[g];total+=w;}
        Check(Math.Abs(dual.similarity-((1-settings.BimanualWeight)*weighted/total+settings.BimanualWeight*dual.relative_hands_score)*dual.motion_presence_score/100)<1e-8,"two-hand total includes relative placement and motion");
        var results=new List<RealExample>();string app=Path.GetFullPath("../交付材料/SignCoachLite_Unity内置摄像头版_20261115/Windows");string reference=Path.Combine(app,"SignCoachLite_Data/StreamingAssets/Teaching/data/reference/pending_review/goodbye/candidate_001.jsonl");
        if(File.Exists(reference))foreach(string take in new[]{"take_003","take_004","take_005"})
        {
            var a=TeachingSequenceAdapter.ForCameraAspect(SequenceComparer.LoadJsonl(reference),1);var b=SequenceComparer.Load(new RecordingStore(Path.Combine(app,"data/reference")),"goodbye",take);
            var s=new ComparisonSettings{ScoringProfile=ComparisonScoringProfile.Exact};if(take=="take_003"){s.Hands=ComparisonHands.ExplicitSingle;s.ReferenceHand=1;s.PracticeHand=0;}
            var old=SequenceComparer.Compare(a,b,s);s=new ComparisonSettings{Hands=s.Hands,ReferenceHand=s.ReferenceHand,PracticeHand=s.PracticeHand};var current=SequenceComparer.Compare(a,b,s);
            Check(SamePath(old,current),take+" real-file alignment invariant");Check(old.score_available==current.score_available&&old.low_quality==current.low_quality,take+" quality gate invariant");
            results.Add(new RealExample{take=take,exact=old.similarity,introductory=current.similarity,distance=current.dtw.normalized_distance,score_available=current.score_available,quality=current.quality_message,components=current.component_scores});
        }
        else Debug.LogWarning("Scoring profiles: external real recordings unavailable; real-file examples SKIPPED");
        Directory.CreateDirectory("Verification");File.WriteAllLines("Verification/scoring_profile_tests.txt",passed);File.WriteAllText("Verification/scoring_profiles_real.json",JsonUtility.ToJson(new RealExamples{examples=results.ToArray()},true));
        Debug.Log("SIGNCOACH_SCORING_PROFILE_TESTS_PASSED assertions="+passed.Count);
    }
    [Serializable] sealed class RealExample{public string take,quality;public double exact,introductory,distance;public bool score_available;public double[] components;}
    [Serializable] sealed class RealExamples{public RealExample[] examples;}
}
