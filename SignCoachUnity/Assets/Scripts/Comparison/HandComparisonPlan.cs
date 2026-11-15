using System;

namespace SignCoach.Comparison
{
    public sealed class HandComparisonPlan
    {
        public int ReferenceMask,PracticeMask;
        public bool Manual;
        public string Label;
        static string Name(int mask){return mask==1?"Left（左手）":mask==2?"Right（右手）":"Left + Right（双手）";}
        public static HandComparisonPlan Resolve(PreparedSequence reference,PreparedSequence practice,ComparisonSettings settings)
        {
            int a,b;
            switch(settings.Hands)
            {
                case ComparisonHands.LeftOnly:a=b=1;break;
                case ComparisonHands.RightOnly:a=b=2;break;
                case ComparisonHands.Both:a=b=3;break;
                case ComparisonHands.ExplicitSingle:a=1<<settings.ReferenceHand;b=1<<settings.PracticeHand;break;
                default:
                    a=reference.ActiveLeft?(reference.ActiveRight?3:1):2;
                    if(a==3)
                    {
                        double high=Math.Max(reference.MotionLeft,reference.MotionRight),low=Math.Min(reference.MotionLeft,reference.MotionRight);
                        if(high>=settings.MotionThreshold&&high>=settings.DominanceRatio*Math.Max(.01,low))a=reference.MotionLeft>reference.MotionRight?1:2;
                    }
                    b=a;break;
            }
            if(((a&1)!=0&&!reference.ActiveLeft)||((a&2)!=0&&!reference.ActiveRight))throw new InvalidOperationException("参考缺少所选执行手 "+Name(a)+" 的足够数据，请检查执行手选择。");
            if(((b&1)!=0&&!practice.ActiveLeft)||((b&2)!=0&&!practice.ActiveRight))throw new InvalidOperationException("参考执行手为 "+Name(a)+"；练习有效标签为 "+Name(practice.ActiveLeft?(practice.ActiveRight?3:1):2)+"。两边执行手不一致，暂不评分。请确认模型标签和画面方向；确实使用不同手时，在“手动配对”中明确选择参考手与练习手。不会自动交换左右手。");
            bool manual=settings.Hands==ComparisonHands.ExplicitSingle;
            return new HandComparisonPlan{ReferenceMask=a,PracticeMask=b,Manual=manual,Label="参考 "+Name(a)+" ↔ 练习 "+Name(b)+(manual?"（用户明确配对，仅动作对照）":"（同名标签）")};
        }
        public PreparedSequence Apply(PreparedSequence source,bool reference,ComparisonSettings settings)
        {
            int mask=reference?ReferenceMask:PracticeMask;
            var selected=SequencePreparer.Select(source,mask,settings);
            if(mask!=3)
            {
                var frames=new FeatureFrame[selected.Frames.Length];
                for(int i=0;i<frames.Length;i++){var f=selected.Frames[i];frames[i]=new FeatureFrame{OriginalIndex=f.OriginalIndex,TimeMs=f.TimeMs,Left=mask==1?f.Left:f.Right};}
                // 仅把显式选定的特征放入同一比较槽位；不修改原hands、标签或坐标。
                selected.Frames=frames;
            }
            return selected;
        }
    }
}
