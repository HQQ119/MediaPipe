using System;
using System.Diagnostics;
using System.Text;

namespace SignCoach
{
    // 仅管理本应用创建的进程；stdout/stderr异步排空，不阻塞Unity逐帧更新。
    public sealed class BackgroundVisionProcess : IDisposable
    {
        Process process;
        volatile string error="";
        public bool ForcedKill {get;private set;}
        public int LastExitCode {get;private set;}=-1;
        public string Error {get{return error;}}
        public bool Running {get{return process!=null&&!process.HasExited;}}
        public bool Failed {get{return process!=null&&process.HasExited&&process.ExitCode!=0;}}
        public static ProcessStartInfo Options(string executable,string previewPath,int camera=0)
        {
            var options=new ProcessStartInfo(executable,"--tcp --no-display --control-stdin --camera "+camera+" --preview-file \""+previewPath+"\"")
            {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            options.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";options.EnvironmentVariables["PYTHONUTF8"]="1";return options;
        }
        public void Start(string executable,string previewPath,int camera=0)
        {
            if(Running)return;Dispose();error="";ForcedKill=false;LastExitCode=-1;
            process=new Process{StartInfo=Options(executable,previewPath,camera)};
            process.OutputDataReceived+=(sender,line)=>{};
            process.ErrorDataReceived+=(sender,line)=>{if(line.Data!=null&&line.Data.StartsWith("错误：",StringComparison.Ordinal))error=line.Data;};
            try{if(!process.Start())throw new InvalidOperationException("无法启动视觉服务");}
            catch{process.Dispose();process=null;throw;}
            process.BeginOutputReadLine();process.BeginErrorReadLine();
        }
        public void Dispose()
        {
            if(process==null)return;
            try
            {
                if(!process.HasExited)
                {
                    try{process.StandardInput.WriteLine("quit");process.StandardInput.Flush();}catch(System.IO.IOException){}catch(InvalidOperationException){}
                    if(!process.WaitForExit(3000)){ForcedKill=true;process.Kill();process.WaitForExit(1000);}
                }
                if(process.HasExited)LastExitCode=process.ExitCode;
            }
            finally{process.Dispose();process=null;}
        }
    }
}
