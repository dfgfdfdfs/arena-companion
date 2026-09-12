using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
namespace ArenaCompanion {
    public sealed class BrowserScript {
        static readonly ConditionalWeakTable<CoreWebView2,BrowserScript> shared=new ConditionalWeakTable<CoreWebView2,BrowserScript>();
        public static BrowserScript For(CoreWebView2 core){return shared.GetValue(core,key=>new BrowserScript(key.CallDevToolsProtocolMethodAsync));}
        readonly Func<string,string,Task<string>> call;
        readonly int timeout;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=10000000};
        Task<string> pending;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public bool Busy {get {return gate.CurrentCount==0||(pending!=null&&!pending.IsCompleted);}}
        public BrowserScript(Func<string,string,Task<string>> call,int timeout=15000){this.call=call;this.timeout=timeout;}
        async Task Bound(Task<string> task,Stopwatch budget,string error) {
            int left=Math.Max(0,timeout-(int)budget.ElapsedMilliseconds);
            if(!task.IsCompleted&&await Task.WhenAny(task,Task.Delay(left))!=task)throw new TimeoutException(error);
        }
        public async Task<string> Execute(string script) {
            var budget=Stopwatch.StartNew();
            if(!await gate.WaitAsync(timeout))throw new TimeoutException("网页读取排队超过等待期限");
            try {
                // Normal concurrent readers queue. A genuinely abandoned native request must settle before another is issued.
                if(pending!=null&&!pending.IsCompleted)await Bound(pending,budget,"此前网页请求仍未返回，等待恢复");
                int left=timeout-(int)budget.ElapsedMilliseconds;
                if(left<=0)throw new TimeoutException("网页读取排队超过等待期限");
                var task=call("Runtime.evaluate",json.Serialize(new {expression="JSON.stringify((0,eval)("+json.Serialize(script)+"))",returnByValue=true,awaitPromise=false,timeout=Math.Max(1,left-1)}));pending=task;
                await Bound(task,budget,"网页读取通道超过等待期限");
                var reply=json.Deserialize<Dictionary<string,object>>(await task);
                if(reply.ContainsKey("exceptionDetails"))throw new InvalidOperationException("网页脚本执行失败："+json.Serialize(reply["exceptionDetails"]));
                var result=(Dictionary<string,object>)reply["result"];
                return result.ContainsKey("value")?Convert.ToString(result["value"]):"null";
            }finally{if(pending!=null&&pending.IsCompleted)pending=null;gate.Release();}
        }
    }
}
