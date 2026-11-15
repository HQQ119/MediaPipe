using System;
using SignCoach.Comparison;

namespace SignCoach
{
    public static class TeachingSequenceAdapter
    {
        // 仅为算法创建副本。把不同视频像素纵横比映射到现有特征器使用的度量，
        // 不修改保存的 JSONL，不交换标签、不添加镜像或平滑算法。
        public static SequenceInput ForCameraAspect(SequenceInput source,double sourceHeightOverWidth,double cameraHeightOverWidth=.75)
        {
            if(double.IsNaN(sourceHeightOverWidth)||double.IsInfinity(sourceHeightOverWidth)||sourceHeightOverWidth<=0||cameraHeightOverWidth<=0)throw new ArgumentException("视频纵横比无效");
            var frames=new RecordedFrame[source.Frames.Length];
            for(int i=0;i<frames.Length;i++)
            {
                var original=source.Frames[i];var hands=new HandData[original.hands.Length];
                for(int h=0;h<hands.Length;h++)
                {
                    var hand=original.hands[h];var points=new Landmark[hand.landmarks.Length];
                    for(int p=0;p<points.Length;p++){var x=hand.landmarks[p];points[p]=new Landmark{id=x.id,x=x.x,y=(float)(x.y*sourceHeightOverWidth/cameraHeightOverWidth),z=x.z};}
                    hands[h]=new HandData{handedness=hand.handedness,handedness_score=hand.handedness_score,landmarks=points};
                }
                frames[i]=new RecordedFrame{timestamp=original.timestamp,frame_index=original.frame_index,hands=hands};
            }
            return new SequenceInput{Name=source.Name,Metadata=source.Metadata,Frames=frames};
        }
    }
}
