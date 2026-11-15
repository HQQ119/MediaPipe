using System;
using System.Globalization;
using UnityEngine;

namespace SignCoach.Comparison
{
    public enum ComparisonHands { Automatic, LeftOnly, RightOnly, Both, ExplicitSingle }
    public enum ComparisonScoringProfile { Introductory, Exact }
    [Serializable] public sealed class ComparisonSettings
    {
        public double ImageHeightOverWidth = .75, DepthWeight = .35;
        public int InterpolationMaxFrames = 3;
        public long InterpolationMaxSpanMs = 150, LongMissingMs = 500;
        public double MinimumReliableRatio = .7, MaximumAmbiguousRatio = .2, ActiveHandRatio = .1;
        public double AngleWeight = .25, TipWeight = .15, ShapeWeight = .2, DirectionWeight = .15, TrajectoryWeight = .2, ScaleWeight = .05;
        public double MissingHandPenalty = 1, DifferenceThreshold = .35, WindowRatio = .25;
        public double SimilaritySigma = .5; // 旧调用兼容字段；v2总分不使用指数映射。
        public ComparisonHands Hands = ComparisonHands.Automatic;
        public int ReferenceHand = 1, PracticeHand = 1, MaximumFrameRepeat = 3;
        public bool MirrorReference;
        public ComparisonScoringProfile ScoringProfile = ComparisonScoringProfile.Introductory;
        public double IntroductoryToleranceCredit = .85;
        // 容差是可解释的工程尺度，不是经过手语教师样本校准的对错阈值。
        public double AngleTolerance = 15.0/180, TipTolerance = .35, ShapeTolerance = .20;
        public double DirectionTolerance = 30.0/180, TrajectoryTolerance = 1, ScaleTolerance = .20;
        public double BimanualWeight = .15, BimanualTolerance = 1, MotionThreshold = .35, DominanceRatio = 3;
        public double MinimumScoreCoverage = .5;
        public int MaxMatrixCells = 4000000;
        public void Validate()
        {
            double[] values = { ImageHeightOverWidth, DepthWeight, MinimumReliableRatio, MaximumAmbiguousRatio, ActiveHandRatio,
                AngleWeight, TipWeight, ShapeWeight, DirectionWeight, TrajectoryWeight, ScaleWeight, MissingHandPenalty, SimilaritySigma, DifferenceThreshold, WindowRatio,
                AngleTolerance,TipTolerance,ShapeTolerance,DirectionTolerance,TrajectoryTolerance,ScaleTolerance,BimanualWeight,BimanualTolerance,MotionThreshold,DominanceRatio,MinimumScoreCoverage,IntroductoryToleranceCredit };
            foreach(var x in values) if(double.IsNaN(x)||double.IsInfinity(x)||x<0) throw new ArgumentException("比较参数必须为有限非负数");
            if(ImageHeightOverWidth<=0||DepthWeight<=0||SimilaritySigma<=0||WindowRatio>1||MinimumReliableRatio>1||MaximumAmbiguousRatio>1||ActiveHandRatio>1||
                InterpolationMaxFrames<0||InterpolationMaxSpanMs<0||LongMissingMs<1||MaxMatrixCells<1||
                AngleWeight+TipWeight+ShapeWeight+DirectionWeight+TrajectoryWeight+ScaleWeight<=0) throw new ArgumentException("无效比较参数");
            if(AngleTolerance<=0||TipTolerance<=0||ShapeTolerance<=0||DirectionTolerance<=0||TrajectoryTolerance<=0||ScaleTolerance<=0||BimanualTolerance<=0||BimanualWeight>1||DominanceRatio<1||MinimumScoreCoverage>1||MissingHandPenalty>1||MaximumFrameRepeat<1||MaximumFrameRepeat>32||ReferenceHand<0||ReferenceHand>1||PracticeHand<0||PracticeHand>1||!Enum.IsDefined(typeof(ComparisonHands),Hands))throw new ArgumentException("无效执行手、容差或对齐约束");
            if(!Enum.IsDefined(typeof(ComparisonScoringProfile),ScoringProfile)||IntroductoryToleranceCredit<=0||IntroductoryToleranceCredit>=1)throw new ArgumentException("无效评分档位或入门容差分值");
        }
    }
    public enum RecordingKind { UnverifiedTest, VerifiedReference, UserPractice }
    public static class RecordingKinds
    {
        public static RecordingKind Resolve(RecordingMetadata meta)
        {
            if(meta!=null && meta.verification_status=="verified_reference" && !string.IsNullOrWhiteSpace(meta.source_reference)) return RecordingKind.VerifiedReference;
            if(meta!=null && (meta.source=="user_practice"||meta.verification_status=="user_practice")) return RecordingKind.UserPractice;
            return RecordingKind.UnverifiedTest;
        }
        public static string Label(RecordingMetadata meta)
        {
            switch(Resolve(meta)) { case RecordingKind.VerifiedReference:return "已审核标准示范（metadata 声明）"; case RecordingKind.UserPractice:return "用户练习录制"; default:return "未审核测试录制"; }
        }
    }
    [Serializable] public sealed class MissingInterval
    {
        public string reason;
        public int start_frame, end_frame;
        public long start_ms, end_ms, duration_ms;
        public bool long_gap;
    }
    [Serializable] public sealed class SequenceQuality
    {
        public int total_frames, detected_frames, no_hand_frames, reliable_frames, ambiguous_frames, unknown_hand_frames, degenerate_hands;
        public int interpolated_hands, used_frames, excluded_frames;
        public double detected_ratio, reliable_ratio;
        public int action_start_frame,action_end_frame,action_frames,action_reliable_frames,leading_empty_frames,trailing_empty_frames,internal_missing_frames,action_ambiguous_frames;
        public double action_reliable_ratio;
        public bool has_internal_long_gap;
        public bool low_quality;
        public string[] warnings;
        public MissingInterval[] missing_intervals;
    }
    public sealed class SequenceInput
    {
        public string Name;
        public RecordingMetadata Metadata;
        public RecordedFrame[] Frames;
    }
    public sealed class HandFeature
    {
        public double[] Angles, Tips, Shape, Direction, Center;
        public double Scale;
        public bool Interpolated;
    }
    public sealed class FeatureFrame
    {
        public int OriginalIndex;
        public long TimeMs;
        public HandFeature Left, Right;
        public double[] RelativeHands;
        public bool RequiresBoth;
    }
    public sealed class PreparedSequence
    {
        public SequenceInput Input;
        public FeatureFrame[] Frames;
        public SequenceQuality Quality;
        public long DurationMs;
        public bool ActiveLeft, ActiveRight;
        public bool[] AvailableLeft,AvailableRight,Detected;
        public double MotionLeft,MotionRight;
    }
    [Serializable] public sealed class AlignmentPoint
    {
        public int i, j, frame_a, frame_b;
        public long time_a_ms, time_b_ms;
        public double local_distance;
    }
    [Serializable] public sealed class DtwResult
    {
        public double raw_distance, normalized_distance;
        public int maximum_run;
        public AlignmentPoint[] path;
        [NonSerialized] public double[,] LocalCosts, Accumulated;
    }
    [Serializable] public sealed class DifferenceInterval
    {
        public string sequence;
        public int start_frame, end_frame;
        public long start_ms, end_ms;
        public double mean_distance;
    }
    [Serializable] public sealed class ComparisonResult
    {
        public string name_a, name_b, kind_a, kind_b;
        public long duration_a_ms, duration_b_ms;
        public SequenceQuality quality_a, quality_b;
        public ComparisonSettings settings;
        public DtwResult dtw;
        public double similarity, elapsed_ms;
        public bool low_quality;
        public string similarity_scope = "valid_feature_segments_only; uncalibrated_engineering_index; not_sign_correctness";
        public string quality_message;
        public DifferenceInterval[] differences;
        public double[] mean_group_distances;
        public string group_order = "angles,tips,relative_points,palm_direction,center_trajectory,relative_scale; matched_hands_only";
        public int aligned_missing_hand_pairs;
        public string algorithm_version="2.1",hand_pairing,score_unavailable_reason;
        public bool score_available=true,manual_hand_pairing,reference_mirrored;
        public int alignment_frames_a,alignment_frames_b;
        public double[] component_scores;
        public double relative_hands_score;
        public bool compares_both_hands;
        public double motion_presence_score=100;
        public double scoring_loss;
        public string ToJson() { return JsonUtility.ToJson(this,true); }
        public string SimilarityText { get { return similarity.ToString("F2",CultureInfo.InvariantCulture); } }
    }
}
