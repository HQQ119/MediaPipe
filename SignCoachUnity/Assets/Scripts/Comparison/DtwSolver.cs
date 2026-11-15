using System;
using System.Collections.Generic;
using System.Threading;

namespace SignCoach.Comparison
{
    public static class DtwSolver
    {
        public static DtwResult Compare(FeatureFrame[] a,FeatureFrame[] b,ComparisonSettings settings,CancellationToken cancel=default(CancellationToken))
        {
            settings.Validate();int n=a.Length,m=b.Length;
            if(n==0||m==0)throw new ArgumentException("DTW 不能比较空序列");
            if((long)n*m>settings.MaxMatrixCells)throw new ArgumentException("矩阵超过配置的单元数上限，请缩短录制或明确调整上限");
            var costs=new double[n,m];
            for(int i=0;i<n;i++)
            {
                cancel.ThrowIfCancellationRequested();
                for(int j=0;j<m;j++)
                    costs[i,j]=Math.Abs((double)i/Math.Max(1,n-1)-(double)j/Math.Max(1,m-1))<=settings.WindowRatio+1e-12?
                        HandFeatureExtractor.FrameDistance(a[i],b[j],settings,true):double.PositiveInfinity;
            }
            // 根据两段采样密度差异放宽必要重复数，但限制一帧吞掉大段动作。
            int run=Math.Max(settings.MaximumFrameRepeat,(int)Math.Ceiling((double)Math.Max(n,m)/Math.Min(n,m)));
            if(run>32)throw new InvalidOperationException("两段有效数据长度差异过大，请选取相同动作范围后再比较");
            var result=SolveConstrained(costs,run,cancel);
            foreach(var p in result.path){p.frame_a=a[p.i].OriginalIndex;p.frame_b=b[p.j].OriginalIndex;p.time_a_ms=a[p.i].TimeMs;p.time_b_ms=b[p.j].TimeMs;}
            return result;
        }
        public static DtwResult SolveConstrained(double[,] costs,int run,CancellationToken cancel=default(CancellationToken))
        {
            int n=costs.GetLength(0),m=costs.GetLength(1),states=1+2*run;
            if(n==0||m==0||run<1||run>32)throw new ArgumentException("无效约束DTW输入");
            if((long)n*m*states>64000000)throw new ArgumentException("对齐状态过多，请缩短录制");
            var accumulated=new double[n,m];var pred=new byte[n,m,states];
            var prev=new double[m,states];var current=new double[m,states];
            for(int j=0;j<m;j++)for(int s=0;s<states;s++)prev[j,s]=double.PositiveInfinity;
            int last=0;
            for(int i=0;i<n;i++)
            {
                cancel.ThrowIfCancellationRequested();
                for(int j=0;j<m;j++)for(int s=0;s<states;s++)current[j,s]=double.PositiveInfinity;
                for(int j=0;j<m;j++)
                {
                    double value=costs[i,j];if(double.IsNaN(value)||value<0)throw new ArgumentException("距离必须非负且非NaN");
                    if(i==0&&j==0)current[j,0]=value;
                    else
                    {
                        if(i>0&&j>0){double best=double.PositiveInfinity;int k=0;for(int s=0;s<states;s++)if(prev[j-1,s]<best){best=prev[j-1,s];k=s;}current[j,0]=best+value;pred[i,j,0]=(byte)k;}
                        if(i>0)
                        {
                            double best=prev[j,0];int k=0;for(int s=run+1;s<states;s++)if(prev[j,s]<best){best=prev[j,s];k=s;}
                            current[j,1]=best+value;pred[i,j,1]=(byte)k;
                            for(int s=2;s<=run;s++){current[j,s]=prev[j,s-1]+value;pred[i,j,s]=(byte)(s-1);}
                        }
                        if(j>0)
                        {
                            double best=current[j-1,0];int k=0;for(int s=1;s<=run;s++)if(current[j-1,s]<best){best=current[j-1,s];k=s;}
                            current[j,run+1]=best+value;pred[i,j,run+1]=(byte)k;
                            for(int s=run+2;s<states;s++){current[j,s]=current[j-1,s-1]+value;pred[i,j,s]=(byte)(s-1);}
                        }
                    }
                    double minimum=double.PositiveInfinity;int state=0;for(int s=0;s<states;s++)if(current[j,s]<minimum){minimum=current[j,s];state=s;}
                    accumulated[i,j]=minimum;if(i==n-1&&j==m-1)last=state;
                }
                var swap=prev;prev=current;current=swap;
            }
            if(double.IsInfinity(accumulated[n-1,m-1]))throw new InvalidOperationException("时间窗口或重复帧约束内没有可达对齐路径，请检查动作范围或扩大窗口");
            int x=n-1,y=m-1;var path=new List<AlignmentPoint>();int cursor=last;
            while(true)
            {
                path.Add(new AlignmentPoint{i=x,j=y,local_distance=costs[x,y]});if(x==0&&y==0)break;
                int previous=pred[x,y,cursor];if(cursor==0){x--;y--;}else if(cursor<=run)x--;else y--;cursor=previous;
            }
            path.Reverse();return new DtwResult{raw_distance=accumulated[n-1,m-1],normalized_distance=accumulated[n-1,m-1]/path.Count,path=path.ToArray(),LocalCosts=costs,Accumulated=accumulated,maximum_run=run};
        }
        public static DtwResult Solve(double[,] costs,CancellationToken cancel=default(CancellationToken))
        {
            int n=costs.GetLength(0),m=costs.GetLength(1);if(n==0||m==0)throw new ArgumentException("空距离矩阵");
            var accumulated=new double[n,m];var predecessor=new byte[n,m];
            for(int i=0;i<n;i++)
            {
                cancel.ThrowIfCancellationRequested();
                for(int j=0;j<m;j++)
                {
                    double value=costs[i,j];if(double.IsNaN(value)||value<0)throw new ArgumentException("距离必须非负且非 NaN");
                    if(i==0&&j==0){accumulated[i,j]=value;continue;}
                    double best=double.PositiveInfinity;byte previous=0;
                    // 相同代价时优先对角，保证自比较路径简洁、可复现。
                    if(i>0&&j>0){best=accumulated[i-1,j-1];previous=1;}
                    if(i>0&&accumulated[i-1,j]<best){best=accumulated[i-1,j];previous=2;}
                    if(j>0&&accumulated[i,j-1]<best){best=accumulated[i,j-1];previous=3;}
                    accumulated[i,j]=value+best;predecessor[i,j]=previous;
                }
            }
            if(double.IsInfinity(accumulated[n-1,m-1]))throw new InvalidOperationException("DTW 约束内没有可达路径，请扩大窗口");
            int x=n-1,y=m-1;var path=new List<AlignmentPoint>();
            while(true)
            {
                path.Add(new AlignmentPoint{i=x,j=y,local_distance=costs[x,y]});if(x==0&&y==0)break;
                switch(predecessor[x,y]){case 1:x--;y--;break;case 2:x--;break;case 3:y--;break;default:throw new InvalidOperationException("无效回溯路径");}
            }
            path.Reverse();return new DtwResult{raw_distance=accumulated[n-1,m-1],normalized_distance=accumulated[n-1,m-1]/path.Count,
                path=path.ToArray(),LocalCosts=costs,Accumulated=accumulated};
        }
    }
}
