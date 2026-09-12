using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace ArenaCompanion {
    public static class ReplacementHandoff {
        public static ProcessStartInfo StartInfo(string root,string name) {
            return new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Arena筛选助手.exe"),"--instance \""+name+"\" --root \""+Path.GetFullPath(root)+"\" --register --run-after-login") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        }
        public static async Task<int> Launch(string root,string name) {
            using(var child=Process.Start(StartInfo(root,name))) {
                try{await WaitReady(child,InstanceContext.DirectoryFor(root,name));return child.Id;}
                catch{if(!child.HasExited)child.Kill();throw;}
            }
        }
        public static async Task WaitReady(Process child,string directory) {
            DateTime deadline=DateTime.UtcNow.AddSeconds(45);var json=new JavaScriptSerializer();
            while(DateTime.UtcNow<deadline) {
                if(child.HasExited)throw new InvalidOperationException("新窗口启动失败，旧窗口已保留");
                string endpoint=Path.Combine(directory,"control-endpoint.json");
                try {
                    if(File.Exists(endpoint)) {
                        var record=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(endpoint));
                        if(Convert.ToInt32(record["pid"])==child.Id) {
                            using(var pipe=new NamedPipeClientStream(".",Convert.ToString(record["pipe"]),PipeDirection.InOut,PipeOptions.Asynchronous)) {
                                await pipe.ConnectAsync(500);
                                byte[] request=Encoding.UTF8.GetBytes("{\"command\":\"status\"}\n");await pipe.WriteAsync(request,0,request.Length);
                                using(var reader=new StreamReader(pipe)) {
                                    var read=reader.ReadLineAsync();
                                    if(await Task.WhenAny(read,Task.Delay(2000))==read) {
                                        var state=json.Deserialize<Dictionary<string,object>>(await read);
                                        if(state!=null&&state.ContainsKey("ready")&&Convert.ToBoolean(state["ready"])&&state.ContainsKey("dataDirectory")&&String.Equals(Path.GetFullPath(Convert.ToString(state["dataDirectory"])),Path.GetFullPath(directory),StringComparison.OrdinalIgnoreCase))return;
                                    }
                                }
                            }
                        }
                    }
                }catch(IOException){}catch(TimeoutException){}catch(ArgumentException){}
                await Task.Delay(250);
            }
            throw new TimeoutException("新窗口未在 45 秒内准备好，旧窗口和任务已保留，请重试更换邮箱");
        }
    }
    public sealed class LoginTaskStart {
        public bool Pending {get;private set;}
        public LoginTaskStart(bool pending){Pending=pending;}
        public void Cancel(){Pending=false;}
        public bool Take(bool loginRunning,string phase) {
            if(!Pending||loginRunning||phase!="complete")return false;
            Pending=false;return true;
        }
    }
}
