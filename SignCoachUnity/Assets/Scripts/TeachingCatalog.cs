using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace SignCoach
{
    [Serializable] public sealed class TeachingMaterial
    {
        public string id,word_id,word_name,category,source_name,source_url,platform,publisher,teacher_or_performer,source_type,credibility,sign_language_system;
        public string license,license_url,verification_status,review_notes,video_path,landmark_path,mirror_status,extraction_status,coordinate_system,handedness_convention;
        public string nonmanual_requirement,handshape_notes,orientation_notes,location_notes,trajectory_notes,annotation_source;
        public bool processing_allowed,redistribution_allowed,timing_verified,face_information_available,pose_information_available,hand_comparison_approved;
        public int frame_count,width,height;public long duration_ms;public double fps;
    }
    [Serializable] public sealed class TeachingManifest {public TeachingMaterial[] items;}
    [Serializable] public sealed class TeachingReview
    {
        public string material_id,status,reviewer,qualification,notes,created_at,content_sha256;
        public bool professional_attestation;
    }
    public sealed class TeachingCatalog
    {
        public readonly string Root,ReviewRoot;
        public readonly TeachingMaterial[] Items;
        public TeachingCatalog(string root,string reviewRoot)
        {
            Root=Path.GetFullPath(root);ReviewRoot=Path.GetFullPath(reviewRoot);
            var manifest=JsonUtility.FromJson<TeachingManifest>(File.ReadAllText(Path.Combine(Root,"catalog.json"),Encoding.UTF8));
            Items=manifest==null||manifest.items==null?new TeachingMaterial[0]:manifest.items;
            foreach(var item in Items){Vocabulary.Get(item.word_id);if(string.IsNullOrEmpty(item.id)||item.id.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new FormatException("素材 ID 异常");}
        }
        public TeachingMaterial Find(string id){foreach(var item in Items)if(item.id==id)return item;throw new ArgumentException("找不到素材");}
        public string Resolve(string relative)
        {
            if(string.IsNullOrWhiteSpace(relative))throw new InvalidOperationException("素材文件尚未提供");
            var path=Path.GetFullPath(Path.Combine(Root,relative));
            if(!path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("素材路径越界");
            return path;
        }
        public bool HasVideo(TeachingMaterial item){return !string.IsNullOrEmpty(item.video_path)&&File.Exists(Resolve(item.video_path));}
        string ReviewPath(TeachingMaterial item){return Path.Combine(ReviewRoot,item.id+".json");}
        string Digest(TeachingMaterial item)
        {
            using(var hash=SHA256.Create())
            {
                var text=JsonUtility.ToJson(item);
                foreach(var relative in new[]{item.video_path,item.landmark_path})if(!string.IsNullOrEmpty(relative))
                {var file=Resolve(relative);text+=File.Exists(file)?Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(file))):"missing";}
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }
        public TeachingReview Review(TeachingMaterial item)
        {
            if(!File.Exists(ReviewPath(item)))return null;
            try{var r=JsonUtility.FromJson<TeachingReview>(File.ReadAllText(ReviewPath(item),Encoding.UTF8));return r!=null&&r.material_id==item.id?r:null;}
            catch(Exception ex)when(ex is IOException||ex is ArgumentException){return null;}
        }
        public string Status(TeachingMaterial item)
        {
            var r=Review(item);
            // bundled metadata 中的 verified 字符串不足以代替本项目人工审核记录。
            if(r==null)return "pending_review";
            if(r.status=="verified"&&(!r.professional_attestation||string.IsNullOrWhiteSpace(r.reviewer)||string.IsNullOrWhiteSpace(r.qualification)||r.content_sha256!=Digest(item)))return "pending_review";
            return r.status;
        }
        public void SetReview(TeachingMaterial item,string status,string reviewer,string qualification,string notes,bool professional)
        {
            if(status!="verified"&&status!="pending_review"&&status!="unsuitable")throw new ArgumentException("审核状态无效");
            if(string.IsNullOrWhiteSpace(reviewer)||string.IsNullOrWhiteSpace(notes))throw new InvalidOperationException("请填写人工审核人及具体备注");
            if(status=="verified"&&(!professional||string.IsNullOrWhiteSpace(qualification)||item.credibility=="C"||string.IsNullOrWhiteSpace(item.source_url)))
                throw new InvalidOperationException("通过需要专业审核人确认、资质说明和可核实来源；C级参考不能直接成为标准");
            Directory.CreateDirectory(ReviewRoot);
            var r=new TeachingReview{material_id=item.id,status=status,reviewer=reviewer,qualification=qualification,notes=notes,professional_attestation=professional,created_at=DateTime.UtcNow.ToString("o"),content_sha256=Digest(item)};
            var json=JsonUtility.ToJson(r,true);var path=ReviewPath(item);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,json,Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
            File.AppendAllText(Path.Combine(ReviewRoot,"audit.jsonl"),JsonUtility.ToJson(r)+"\n",Encoding.UTF8);
        }
        public bool CanCompare(TeachingMaterial item,out string reason)
        {
            if(Status(item)!="verified"){reason="当前素材尚未经过本项目专业人工审核";return false;}
            if(!item.processing_allowed||string.IsNullOrWhiteSpace(item.license)){reason="尚未确认视频处理许可";return false;}
            if(!item.timing_verified||item.mirror_status!="unmirrored"){reason="原始时基或镜像方向未确认，不能进行标准比较";return false;}
            if(!item.hand_comparison_approved){reason="专业审核尚未确认手部比较的适用范围（含非手部信息限制）";return false;}
            if(string.IsNullOrEmpty(item.landmark_path)||!File.Exists(Resolve(item.landmark_path))){reason="尚无已提取的动作序列";return false;}
            reason="";return true;
        }
    }
}
