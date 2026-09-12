using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class ControlPipe : IDisposable {
        readonly Func<Dictionary<string,object>,Task<object>> dispatch;
        readonly string endpoint;
        readonly string pipeName;
        NamedPipeServerStream active;
        bool stopped;
        public ControlPipe(string directory,Func<Dictionary<string,object>,Task<object>> dispatch) {
            this.dispatch=dispatch; pipeName="ArenaCompanion."+Process.GetCurrentProcess().Id;
            endpoint=Path.Combine(directory,"control-endpoint.json");
            File.WriteAllText(endpoint,new JavaScriptSerializer().Serialize(new {pipe=pipeName,pid=Process.GetCurrentProcess().Id}),Encoding.UTF8);
        }
        public async Task Run() {
            var security=new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
            while(!stopped) {
                try {
                    using(var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)) {
                        active=pipe; await pipe.WaitForConnectionAsync();
                        var reading=ReadRequest(pipe);
                        if(await Task.WhenAny(reading,Task.Delay(10000))!=reading)continue;
                        string request=await reading;
                        object result;
                        try {result=await dispatch(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(request));}
                        catch(Exception ex) {result=new {ok=false,error=ex.Message};}
                        byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(result)+"\n");
                        await pipe.WriteAsync(bytes,0,bytes.Length);
                    }
                } catch(ObjectDisposedException) {if(stopped)return;}
                catch(IOException) {if(stopped)return;}
                catch(Exception) {if(stopped)return;}
                if(!stopped)await Task.Delay(25);
            }
        }
        static async Task<string> ReadRequest(Stream stream) {
            var bytes=new List<byte>(); var one=new byte[1];
            while(bytes.Count<65536) {
                if(await stream.ReadAsync(one,0,1)==0)throw new IOException("Incomplete request");
                if(one[0]==10)return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add(one[0]);
            }
            throw new IOException("Request too large");
        }
        public void Dispose() {
            stopped=true; if(active!=null)active.Dispose();
            try {if(File.Exists(endpoint)&&File.ReadAllText(endpoint).Contains(pipeName))File.Delete(endpoint);}catch(IOException){}
        }
    }
}
