using System;
using System.IO;
using UnityEngine;

namespace SignCoach
{
    // 本机 JPEG 预览文件与 TCP 分离；Python 使用同一摄像头帧发布，绝不重复打开设备。
    public sealed class CameraPreview : IDisposable
    {
        public string Path {get;private set;}
        Texture2D texture;long timestamp=-1;float lastRead=-100,nextRead;
        readonly Vector2[] points=new Vector2[21];
        public CameraPreview(string path){Path=path;texture=new Texture2D(2,2);}
        public void Poll()
        {
            if(Time.realtimeSinceStartup<nextRead)return;nextRead=Time.realtimeSinceStartup+.1f;
            try
            {
                if(!File.Exists(Path))return;
                byte[] data;using(var input=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {if(input.Length<12||input.Length>2000000)return;data=new byte[(int)input.Length];int offset=0;while(offset<data.Length){int n=input.Read(data,offset,data.Length-offset);if(n==0)return;offset+=n;}}
                long stamp=BitConverter.ToInt64(data,0);if(stamp==timestamp)return;
                var jpeg=new byte[data.Length-8];Buffer.BlockCopy(data,8,jpeg,0,jpeg.Length);
                if(texture.LoadImage(jpeg)){timestamp=stamp;lastRead=Time.realtimeSinceStartup;}
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        public bool Fresh {get{return Time.realtimeSinceStartup-lastRead<.5f;}}
        public static bool ShouldDrawSkeleton(bool previewFresh,long frameTimestamp,long previewTimestamp)
        {return !previewFresh||Math.Abs(frameTimestamp-previewTimestamp)<=250;}
        public static Rect Fit(Rect area,float aspect)
        {float width=Mathf.Min(area.width,area.height*aspect),height=Mathf.Min(area.height,width/aspect);return new Rect(area.x+(area.width-width)/2,area.y+(area.height-height)/2,width,height);}
        public void Draw(Rect area,HandFrame frame)
        {
            GUI.Box(area,GUIContent.none);
            bool fresh=Fresh;var fit=Fit(area,fresh?(float)texture.width/texture.height:4f/3);
            // GUI 顶端为 y=0；仅在显示时对图像和关键点做一次横向镜像。
            if(fresh)GUI.DrawTextureWithTexCoords(fit,texture,new Rect(1,0,-1,1));
            else GUI.Label(new Rect(area.x+8,area.y+6,area.width-16,70),"摄像头背景尚未连接 / 已过期；继续显示 TCP 骨骼。\n需背景时请用随包视觉服务，或配置 --preview-file。");
            if(frame==null||!ShouldDrawSkeleton(fresh,frame.timestamp,timestamp))return;
            foreach(var hand in frame.hands)
            {
                var color=hand.handedness=="Left"?new Color(.31f,.9f,.31f):hand.handedness=="Right"?new Color(.16f,.7f,1):Color.gray;
                foreach(var landmark in hand.landmarks)points[landmark.id]=Point(fit,landmark);
                for(int i=0;i<HandSkeleton.Edges.GetLength(0);i++)Line(points[HandSkeleton.Edges[i,0]],points[HandSkeleton.Edges[i,1]],color);
                var saved=GUI.color;GUI.color=color;foreach(var point in hand.landmarks){var p=Point(fit,point);GUI.DrawTexture(new Rect(p.x-3,p.y-3,6,6),Texture2D.whiteTexture);}GUI.color=saved;
            }
        }
        static Vector2 Point(Rect r,Landmark p){return new Vector2(r.x+(1-p.x)*r.width,r.y+p.y*r.height);}
        static void Line(Vector2 a,Vector2 b,Color color)
        {
            if(Event.current.type!=EventType.Repaint)return;var matrix=GUI.matrix;var saved=GUI.color;GUI.color=color;var d=b-a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);GUI.DrawTexture(new Rect(a.x,a.y,d.magnitude,2),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=saved;
        }
        public void Dispose(){if(texture!=null)UnityEngine.Object.Destroy(texture);}
    }
}
