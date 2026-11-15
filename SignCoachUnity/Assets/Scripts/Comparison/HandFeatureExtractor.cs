using System;
using UnityEngine;

namespace SignCoach.Comparison
{
    public static class HandFeatureExtractor
    {
        static readonly int[] Tips = {4,8,12,16,20};
        static readonly int[,] Joints = {{0,1,2},{1,2,3},{2,3,4},{0,5,6},{5,6,7},{6,7,8},{0,9,10},{9,10,11},{10,11,12},
            {0,13,14},{13,14,15},{14,15,16},{0,17,18},{17,18,19},{18,19,20}};
        public static Vector3[] Points(HandData hand, ComparisonSettings settings)
        {
            var result = new Vector3[21]; var seen = new bool[21];
            if(hand==null||hand.landmarks==null||hand.landmarks.Length!=21) throw new FormatException("手部必须包含21点");
            foreach(var p in hand.landmarks)
            {
                if(p==null||p.id<0||p.id>20||seen[p.id]||!Finite(p.x)||!Finite(p.y)||!Finite(p.z)) throw new FormatException("点 ID 或坐标异常");
                seen[p.id]=true;
                result[p.id]=new Vector3(p.x,(float)(p.y*settings.ImageHeightOverWidth),(float)(p.z*settings.DepthWeight));
            }
            return result;
        }
        static bool Finite(float x) { return !float.IsNaN(x)&&!float.IsInfinity(x); }
        public static double PalmScale(Vector3[] p)
        { return ((p[5]-p[0]).magnitude+(p[9]-p[0]).magnitude+(p[17]-p[0]).magnitude)/3.0; }
        public static Vector3 PalmCenter(Vector3[] p) { return (p[0]+p[5]+p[9]+p[13]+p[17])/5; }
        public static HandFeature Extract(Vector3[] p, Vector3 anchor, double sequenceScale, bool interpolated, bool leftHand=false)
        {
            double scale=PalmScale(p); if(scale<1e-7) return null;
            var forward=p[9]-p[0]; var normal=Vector3.Cross(p[5]-p[0],p[17]-p[0]);
            if(forward.sqrMagnitude<1e-14||normal.sqrMagnitude<1e-14) return null;
            var feature=new HandFeature { Angles=new double[15],Tips=new double[10],Shape=new double[63],Direction=new double[6],Center=new double[3],
                Scale=scale/sequenceScale,Interpolated=interpolated };
            forward.Normalize();normal.Normalize();
            var across=Vector3.Cross(forward,normal).normalized;
            var palmNormal=leftHand?-normal:normal;
            // 局部手型单独描述关节形态；掌朝向由Direction保留，避免同一次旋转重复扣两组。
            for(int i=0;i<21;i++) { var relative=(p[i]-p[0])/(float)scale; feature.Shape[3*i]=Vector3.Dot(relative,across);feature.Shape[3*i+1]=Vector3.Dot(relative,forward);feature.Shape[3*i+2]=Vector3.Dot(relative,palmNormal); }
            for(int i=0;i<15;i++)
            {
                var a=p[Joints[i,0]]-p[Joints[i,1]];var b=p[Joints[i,2]]-p[Joints[i,1]];
                double norm=(double)a.magnitude*b.magnitude;
                if(norm<1e-14) return null;
                feature.Angles[i]=Math.Acos(Math.Max(-1,Math.Min(1,Vector3.Dot(a,b)/norm)))/Math.PI;
            }
            int k=0; for(int i=0;i<5;i++) for(int j=i+1;j<5;j++) feature.Tips[k++]=(p[Tips[i]]-p[Tips[j]]).magnitude/scale;
            feature.Direction[0]=forward.x;feature.Direction[1]=forward.y;feature.Direction[2]=forward.z;
            feature.Direction[3]=palmNormal.x;feature.Direction[4]=palmNormal.y;feature.Direction[5]=palmNormal.z;
            var center=(PalmCenter(p)-anchor)/(float)sequenceScale;
            feature.Center[0]=center.x;feature.Center[1]=center.y;feature.Center[2]=0; // wrist-relative z不是相机空间的平移深度。
            return feature;
        }
        static double Rms(double[] a,double[] b)
        { double sum=0;for(int i=0;i<a.Length;i++){double d=a[i]-b[i];sum+=d*d;}return Math.Sqrt(sum/a.Length); }
        // 分组均方根后加权，避免63维相对位置压过15维关节角等较小组。
        public static double[] GroupDistances(HandFeature a,HandFeature b)
        { var values=new double[6];for(int g=0;g<6;g++)values[g]=GroupDistance(a,b,g);return values; }
        public static double GroupDistance(HandFeature a,HandFeature b,int group)
        {
            switch(group){case 0:return Rms(a.Angles,b.Angles);case 1:return Rms(a.Tips,b.Tips);case 2:return Rms(a.Shape,b.Shape);
                case 3:double sum=0;for(int j=0;j<6;j+=3){double dot=0,na=0,nb=0;for(int k=0;k<3;k++){double u=a.Direction[j+k],v=b.Direction[j+k];dot+=u*v;na+=u*u;nb+=v*v;}double cosine=dot/Math.Sqrt(na*nb);if(cosine>1-1e-12)cosine=1;sum+=Math.Acos(Math.Max(-1,Math.Min(1,cosine)))/Math.PI;}return sum/2;
                case 4:return Rms(a.Center,b.Center);default:return Math.Abs(Math.Log(a.Scale/b.Scale));}
        }
        public static double Weight(int group,ComparisonSettings s)
        {switch(group){case 0:return s.AngleWeight;case 1:return s.TipWeight;case 2:return s.ShapeWeight;case 3:return s.DirectionWeight;case 4:return s.TrajectoryWeight;default:return s.ScaleWeight;}}
        public static double Loss(double distance,int group,ComparisonSettings s,bool exact=false)
        {
            double tolerance;switch(group){case 0:tolerance=s.AngleTolerance;break;case 1:tolerance=s.TipTolerance;break;case 2:tolerance=s.ShapeTolerance;break;case 3:tolerance=s.DirectionTolerance;break;case 4:tolerance=s.TrajectoryTolerance;break;default:tolerance=s.ScaleTolerance;break;}
            return LossAtTolerance(distance,tolerance,s,exact);
        }
        public static double LossAtTolerance(double distance,double tolerance,ComparisonSettings settings,bool exact=false)
        {
            if(exact||settings.ScoringProfile==ComparisonScoringProfile.Exact)return distance/(distance+tolerance);
            double ratio=distance/tolerance,k=1/settings.IntroductoryToleranceCredit-1;
            // 入门档：小幅偏差平缓扣分，达到配置容差时保留明确的85分；大差异仍趋近0分。
            return 1-1/(1+k*ratio*ratio);
        }
        public static double RelativeLoss(FeatureFrame a,FeatureFrame b,ComparisonSettings settings,bool exact=false)
        {
            if(a.RelativeHands==null&&b.RelativeHands==null)return 0;
            if(a.RelativeHands==null||b.RelativeHands==null)return 1;
            double distance=Rms(a.RelativeHands,b.RelativeHands);return LossAtTolerance(distance,settings.BimanualTolerance,settings,exact);
        }
        public static double[] FrameLosses(FeatureFrame a,FeatureFrame b,ComparisonSettings settings,out int missing)
        {
            var losses=new double[6];int count=0;missing=0;
            for(int side=0;side<2;side++)
            {
                var x=side==0?a.Left:a.Right;var y=side==0?b.Left:b.Right;
                if(x==null&&y==null)continue;
                count++;bool absent=x==null||y==null;if(absent)missing++;
                for(int g=0;g<6;g++)losses[g]+=absent?settings.MissingHandPenalty:Loss(GroupDistance(x,y,g),g,settings);
            }
            for(int g=0;g<6;g++)losses[g]/=Math.Max(1,count);return losses;
        }
        public static double Weighted(double[] groups,ComparisonSettings settings)
        {
            double[] weights={settings.AngleWeight,settings.TipWeight,settings.ShapeWeight,settings.DirectionWeight,settings.TrajectoryWeight,settings.ScaleWeight};
            double sum=0,total=0;for(int i=0;i<6;i++){sum+=weights[i]*groups[i];total+=weights[i];}return sum/total;
        }
        public static double FrameDistance(FeatureFrame a,FeatureFrame b,ComparisonSettings settings,bool forAlignment=false)
        {
            double sum=0;int count=0;
            for(int side=0;side<2;side++)
            {
                var x=side==0?a.Left:a.Right;var y=side==0?b.Left:b.Right;
                if(x==null&&y==null) continue;
                count++;
                if(x==null||y==null) sum+=settings.MissingHandPenalty;
                else
                {
                    double weighted=0,total=0;for(int g=0;g<6;g++){double w=Weight(g,settings);weighted+=w*Loss(GroupDistance(x,y,g),g,settings,forAlignment);total+=w;}
                    sum+=weighted/total;
                }
            }
            if(count==0) throw new InvalidOperationException("帧无可用手部特征");
            double distance=sum/count;
            return a.RequiresBoth||b.RequiresBoth?(1-settings.BimanualWeight)*distance+settings.BimanualWeight*RelativeLoss(a,b,settings,forAlignment):distance;
        }
    }
}
