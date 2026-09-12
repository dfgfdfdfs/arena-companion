using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace ArenaCompanion {
    static class RateLimitTests {
        static int passed;
        const string Endpoint="https://arena.ai/nextjs-api/stream/create-chat";
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        [STAThread] static int Main() {
            Application.EnableVisualStyles();int result=0;
            using(var host=new Form {Opacity=0,ShowInTaskbar=false}) {
                host.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);result=1;}finally{host.Close();}};
                Application.Run(host);
            }
            return result;
        }
        static async Task Run() {
            string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","rate-limit-local",DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(root);var now=new DateTime(2026,9,11,4,0,0,DateTimeKind.Utc);
            Check(RateLimitTracker.Parse(" 640 ",null,now)==now.AddSeconds(640),"server delta seconds including header whitespace");
            Check(RateLimitTracker.Parse("Fri, 11 Sep 2026 05:10:40 GMT","Fri, 11 Sep 2026 05:00:00 GMT",now)==now.AddSeconds(640),"HTTP date uses server clock difference rather than local clock skew");
            Check(RateLimitTracker.Parse("Fri, 11 Sep 2026 04:10:40 GMT",null,now)==now.AddSeconds(640),"absolute HTTP date without server Date");
            Check(RateLimitTracker.Parse("0",null,now)==now.AddSeconds(1),"zero wait avoids an immediate tight retry loop");
            foreach(string bad in new[]{null,"","-1","nonsense","99999999999999999999999999999999"})
                Check(!RateLimitTracker.Parse(bad,null,now).HasValue,"unknown or malformed wait is not invented: "+(bad??"missing"));
            var tracker=new RateLimitTracker(root);var state=new PageState();
            tracker.Observe(Endpoint,200,"640",null,now);tracker.Observe("https://other.invalid/nextjs-api/stream/create-chat",429,"640",null,now);tracker.Apply(state);
            Check(state.rateLimitId==0,"unrelated responses cannot impose generation cooldown");
            tracker.Observe(Endpoint,429,"640",null,now);new RateLimitTracker(root).Apply(state);
            Check(state.rateLimitId==1&&state.rateLimitRetryAt==now.AddSeconds(640),"restart retains observed server deadline");
            tracker.Observe(Endpoint,429,null,null,now);new RateLimitTracker(root).Apply(state);
            Check(state.rateLimitId==2&&state.rateLimitRetryAt==DateTime.MinValue,"new 429 without time clears the old deadline");
            await ScriptTransport();await BrowserFlow(root);
            File.WriteAllText(Path.Combine(root,"result.json"),"{\"passed\":"+passed+",\"online\":false}");
            Console.WriteLine("ALL "+passed+" PASSED; "+root);
        }
        static async Task ScriptTransport() {
            var first=new TaskCompletionSource<string>();int overlapping=0;
            var concurrent=new BrowserScript((method,args)=>{overlapping++;return overlapping==1?first.Task:Task.FromResult("{\"result\":{\"type\":\"string\",\"value\":\"2\"}}");},1000);
            var a=concurrent.Execute("1");var b=concurrent.Execute("2");
            await Task.Delay(25);Check(!b.IsCompleted&&overlapping==1,"concurrent normal readers wait instead of receiving a false timeout");
            first.SetResult("{\"result\":{\"type\":\"string\",\"value\":\"1\"}}");
            Check(await a=="1"&&await b=="2"&&overlapping==2,"queued readers each execute exactly once after the first returns");
            int calls=0;var pending=new TaskCompletionSource<string>();
            var scripts=new BrowserScript((method,args)=>{calls++;return pending.Task;},40);
            bool timedOut=false;try{await scripts.Execute("({value:1})");}catch(TimeoutException){timedOut=true;}
            Check(timedOut,"script transport bounds a missing response");
            try{await scripts.Execute("({value:2})");}catch(TimeoutException){}
            Check(calls==1,"an outstanding script request cannot accumulate duplicate requests");
            pending.SetResult("{\"result\":{\"type\":\"string\",\"value\":\"{\\\"ok\\\":true}\"}}");
            Check((await scripts.Execute("({ok:true})")).Contains("true")&&calls==2,"script transport recovers after the original request settles");
            scripts=new BrowserScript((method,args)=>Task.FromResult("{\"exceptionDetails\":{\"text\":\"fixture error\"},\"result\":{}}"));
            bool failed=false;try{await scripts.Execute("throw Error()");}catch(InvalidOperationException){failed=true;}
            Check(failed,"JavaScript failures remain explicit rather than becoming empty successful states");
        }
        static async Task BrowserFlow(string root) {
            using(var host=new Form {Opacity=0,ShowInTaskbar=false,Width=900,Height=700})
            using(var view=new WebView2 {Dock=DockStyle.Fill}) {
                host.Controls.Add(view);host.Show();
                await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"Browser")));
                var transport=BrowserScript.For(view.CoreWebView2);
                string unicode=await transport.Execute("({text:String.fromCharCode(0xdf31)})");
                Check(unicode.Contains("\\udf31"),"actual WebView transports a lone surrogate without hanging");
                int requests=0;var requestTimes=new System.Collections.Generic.List<DateTime>();
                // Every request in this fresh test browser is served locally. No Arena traffic or account is used.
                view.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
                view.CoreWebView2.WebResourceRequested+=(s,e)=>{
                    string body="",headers="Content-Type: text/html\r\n";int code=200;
                    if(e.Request.Uri==Endpoint){requests++;requestTimes.Add(DateTime.UtcNow);code=requests<=2?429:200;body="{}";headers="Content-Type: application/json\r\n";if(code==429)headers+="Retry-After: "+(requests==1?600:300)+"\r\n";}
                    else if(e.Request.Uri=="https://arena.ai/agent")body="<!doctype html><meta charset=utf-8><main><p>LOCAL cooldown fixture</p><div contenteditable=true></div><button aria-label='Send message' onclick='send()'>Send</button><div id=reply></div><div id=error role=alert></div></main><script>async function send(){const r=await fetch('/nextjs-api/stream/create-chat',{method:'POST'});if(!r.ok){document.getElementById('error').textContent='Too many requests';return;}await new Promise(resolve=>setTimeout(resolve,1400));document.getElementById('error').textContent='';if(r.ok){history.replaceState(null,'','/agent/11111111-1111-4111-8111-111111111111');document.querySelector('#reply').innerHTML='<div role=log>hello<br>A completed fixture response with enough visible text.</div><button id=stop aria-label=\"Stop generating\">Stop</button>';setTimeout(()=>document.getElementById('stop').remove(),800);}}</script>";
                    else code=404;
                    e.Response=view.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)),code,code==429?"Too Many Requests":"OK",headers);
                };
                var page=new WebPage(view);var loaded=new TaskCompletionSource<bool>();
                view.CoreWebView2.NavigationCompleted+=(s,e)=>loaded.TrySetResult(e.IsSuccess);view.CoreWebView2.Navigate("https://arena.ai/agent");
                if(await Task.WhenAny(loaded.Task,Task.Delay(10000))!=loaded.Task||!await loaded.Task)throw new Exception("fixture navigation failed");
                var controller=new RetryController(page,null,new AttachmentUpload(view));controller.Start("hello",1,false);DateTime deadline=DateTime.UtcNow.AddSeconds(15);
                while(DateTime.UtcNow<deadline&&controller.Running){await controller.Tick();await Task.Delay(100);}
                Check(!controller.Running&&controller.Finished&&controller.Phase=="cooldown","native 429 stops the active browser task");
                Check(controller.CooldownSeconds==0&&controller.Message.Contains("不会倒计时或自动重试"),"native 429 exposes no countdown or retry schedule");
                int stoppedRequests=requests;await Task.Delay(6500);for(int i=0;i<10;i++)await controller.Tick();
                Check(stoppedRequests==1&&requests==1,"native 429 produces exactly one request and never retries after five seconds");
                var state=await page.Read("hello");
                Check(state.rateLimitId>0&&state.rateLimitRetryAt>DateTime.UtcNow,"server limit details remain observable without driving retries");
                File.WriteAllText(Path.Combine(root,"request-timing.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {requests,stopped=true,countdownSeconds=controller.CooldownSeconds,finished=controller.Finished,online=false}));
            }
        }
    }
}
