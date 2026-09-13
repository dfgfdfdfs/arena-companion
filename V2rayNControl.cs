using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class V2rayNNode {
        public string IndexId;
        public string Name;
        public string Type;
        public string Address;
        public string ResolvedAddress;
        public string CountryCode;
        public int Port;
        public int Delay;
        public bool Current;
    }

    public sealed class V2rayNSnapshot {
        public string CurrentIndexId;
        public List<V2rayNNode> Nodes=new List<V2rayNNode>();
    }

    public sealed class V2rayNControlClient {
        readonly string pipeName;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();

        V2rayNControlClient(string pipeName) {this.pipeName=pipeName;}

        public static async Task<V2rayNControlClient> Discover() {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArenaV2rayNBridge");
            if(!Directory.Exists(directory))throw new InvalidOperationException("当前 v2rayN 没有安装 Arena 本机控制组件");
            string[] files=Directory.GetFiles(directory,"*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            foreach(string file in files) {
                try {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
                    int pid=Convert.ToInt32(data["pid"]);string pipe=Convert.ToString(data["pipe"]);
                    using(var process=Process.GetProcessById(pid)) {
                        if(process.HasExited||!process.ProcessName.StartsWith("v2rayN",StringComparison.OrdinalIgnoreCase))continue;
                    }
                    var client=new V2rayNControlClient(pipe);
                    await client.ReadProfiles();
                    return client;
                } catch {}
            }
            throw new InvalidOperationException("没有找到可连接的 v2rayN；请先启动带本机控制组件的 v2rayN");
        }

        async Task<Dictionary<string,object>> Call(Dictionary<string,object> request,int timeout) {
            using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                pipe.Connect(2500);
                var utf8=new UTF8Encoding(false);
                using(var writer=new StreamWriter(pipe,utf8,4096,true))
                using(var reader=new StreamReader(pipe,utf8,false,4096,true)) {
                    writer.AutoFlush=true;
                    await writer.WriteLineAsync(json.Serialize(request));
                    Task<string> reading=reader.ReadLineAsync();
                    if(await Task.WhenAny(reading,Task.Delay(timeout))!=reading)throw new TimeoutException("v2rayN 切换接口响应超时");
                    string line=await reading;
                    if(String.IsNullOrEmpty(line))throw new IOException("v2rayN 切换接口没有返回结果");
                    var result=json.Deserialize<Dictionary<string,object>>(line);
                    if(result==null)throw new IOException("v2rayN 返回了无法识别的数据");
                    object ok;if(!result.TryGetValue("ok",out ok)||!Convert.ToBoolean(ok)) {
                        object error;throw new InvalidOperationException(result.TryGetValue("error",out error)?Convert.ToString(error):"v2rayN 拒绝了切换操作");
                    }
                    return result;
                }
            }
        }

        public async Task<V2rayNSnapshot> ReadProfiles() {
            var result=await Call(new Dictionary<string,object>{{"Command","profiles"}},10000);
            var snapshot=new V2rayNSnapshot();object current;
            snapshot.CurrentIndexId=result.TryGetValue("currentIndexId",out current)?Convert.ToString(current):"";
            object raw;if(!result.TryGetValue("nodes",out raw))return snapshot;
            foreach(object value in (IEnumerable)raw) {
                var item=value as Dictionary<string,object>;if(item==null)continue;
                var node=new V2rayNNode {
                    IndexId=Value(item,"indexId"),Name=Value(item,"name"),Type=Value(item,"type"),Address=Value(item,"address"),
                    Port=Number(item,"port"),Delay=Number(item,"delay"),Current=Flag(item,"current")
                };
                if(node.IndexId!=""&&node.Address!="")snapshot.Nodes.Add(node);
            }
            return snapshot;
        }

        public Task<Dictionary<string,object>> Activate(string indexId) {
            return Call(new Dictionary<string,object>{{"Command","activate"},{"IndexId",indexId}},45000);
        }

        static string Value(Dictionary<string,object> item,string key) {object value;return item.TryGetValue(key,out value)?Convert.ToString(value):"";}
        static int Number(Dictionary<string,object> item,string key) {object value;return item.TryGetValue(key,out value)?Convert.ToInt32(value):0;}
        static bool Flag(Dictionary<string,object> item,string key) {object value;return item.TryGetValue(key,out value)&&Convert.ToBoolean(value);}
    }
}
