using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ArenaCompanion;
static class RecoveryTests {
    sealed class SharedPage : IArenaPage {
        readonly BrowserScript scripts;
        public int Sends;
        public SharedPage(BrowserScript scripts){this.scripts=scripts;}
        public async Task<PageState> Read(string prompt){return new JavaScriptSerializer().Deserialize<PageState>(await scripts.Execute("window.readState"));}
        public Task Act(string name,string prompt){Sends++;throw new Exception("Unexpected page action: "+name);}
        public bool IsAllowed(string url){return url.StartsWith("https://arena.ai/agent/");}
    }
    static int passed;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
    [STAThread] static int Main(){Application.EnableVisualStyles();int code=0;using(var host=new Form {Opacity=0,ShowInTaskbar=false})using(var view=new WebView2 {Dock=DockStyle.Fill}){host.Controls.Add(view);host.Shown+=async(s,e)=>{try{await Run(view);}catch(Exception ex){Console.WriteLine(ex);code=1;}finally{host.Close();}};Application.Run(host);}return code;}
    static async Task Until(Func<bool> predicate){DateTime limit=DateTime.UtcNow.AddSeconds(12);while(!predicate()&&DateTime.UtcNow<limit)await Task.Delay(100);if(!predicate())throw new Exception("Automatic recovery deadline");}
    static async Task Run(WebView2 view){
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","recovery-local",DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(root);
        await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"Browser")));
        var core=view.CoreWebView2;int posts=0;bool wrong=true;
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(s,e)=>{
            string body="",headers="Content-Type: text/html\r\n";int status=200;
            if(e.Request.Uri.EndsWith("/review-feedback")){posts++;status=404;headers="Content-Type: application/json\r\n";body=new JavaScriptSerializer().Serialize(new {error=wrong?"unrelated error":"Assistant node \"01a09096-19bc-709f-aaf3-8fdd293eda81\" not found in session \"01a09075-6027-7520-a0c8-d64be1b603f5\""});}
            else if(e.Request.Uri=="https://arena.ai/agent/01a09075-6027-7520-a0c8-d64be1b603f5")body=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","recovery-fixture.html"));else status=404;
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)),status,status==200?"OK":"Not Found",headers);
        };
        var loaded=new TaskCompletionSource<bool>();core.NavigationCompleted+=(s,e)=>loaded.TrySetResult(e.IsSuccess);
        core.Navigate("https://arena.ai/agent/01a09075-6027-7520-a0c8-d64be1b603f5");await loaded.Task;
        var scripts=BrowserScript.For(core);
        await scripts.Execute("window.readState={url:location.href,main:true,conversation:true,promptConfirmed:true,generating:true,response:true,responseSignature:'completed HTML'}");
        var sharedPage=new SharedPage(scripts);DateTime now=DateTime.UtcNow;
        var controller=new RetryController(sharedPage,()=>now);int readFailures=0;
        controller.Changed+=()=>{if(controller.Message.Contains("暂时无法读取"))readFailures++;};
        using(var monitor=new ConversationRecovery(view,root,()=>true,m=>{})){
            controller.Start("hello",0,true);await controller.Tick();
            await scripts.Execute("window.readState.generating=false");
            for(int i=0;i<12;i++){
                now=now.AddSeconds(1);
                // Both start orders reproduce the production consumers sharing one transport.
                if(i%2==0)await Task.WhenAll(monitor.Check(),controller.Tick());
                else await Task.WhenAll(controller.Tick(),monitor.Check(),scripts.Execute("document.title"));
            }
            Check(readFailures==0,"controller and automatic recovery share live WebView without false read failures");
            Check(controller.CandidateReady&&controller.Finished,"completed HTML reaches collection after ten stable seconds with monitor active");
            Check(sharedPage.Sends==0&&monitor.Repaired==0,"overlapping readers neither resubmit nor alter a healthy conversation");
        }
        using(var repair=new ConversationRecovery(view,root,()=>true,m=>{})){
            await repair.Check();Check(repair.Repaired==0,"no error does not remove a checkpoint");
            await core.ExecuteScriptAsync("triggerError();void 0");await Task.Delay(500);await repair.Check();Check(repair.Repaired==0,"unrelated native 404 does not trigger repair");
            wrong=false;await core.ExecuteScriptAsync("live.status='streaming';triggerError();void 0");await Task.Delay(600);await repair.Check();Check(repair.Repaired==0,"native missing-node error cannot remove an active response");
            await core.ExecuteScriptAsync("live.status='ready'");await Until(()=>repair.Repaired==1);
            Check(await scripts.Execute("calls") == "1","timer repairs automatically once without a manual repair command");
            Check(await scripts.Execute("live.messages.length") == "2","only the missing node is removed");
            Check((await scripts.Execute("document.querySelector('[contenteditable]').innerText")).Contains("保留用户草稿"),"existing draft preserved");
            string backup=File.ReadAllText(Path.Combine(repair.LastBackup,"恢复前完整会话.json"));
            Check(backup.Contains("original tool evidence")&&backup.Contains("initialMessages"),"durable backup includes original tools and server history");
            Check(File.ReadAllText(Path.Combine(repair.LastBackup,"修复结果.json")).Contains("\"repaired\":true"),"repair result verified and recorded");
            await repair.Check();Check(await scripts.Execute("calls")=="1"&&posts==2,"repeated checks neither resubmit nor repair twice");
        }
        string bad=Path.Combine(root,"not-a-directory");File.WriteAllText(bad,"fixture");
        await core.ExecuteScriptAsync("resetCase()");
        using(var repair=new ConversationRecovery(view,bad,()=>true,m=>{})){
            await core.ExecuteScriptAsync("triggerError();void 0");await Until(()=>repair.Message!=null);
            Check(repair.Repaired==0&&await scripts.Execute("calls")=="0","backup failure leaves page messages untouched");
        }
        File.WriteAllText(Path.Combine(root,"result.json"),"{\"passed\":"+passed+",\"traffic\":\"local fixtures only\"}");Console.WriteLine("ALL "+passed+" PASSED; "+root);
    }
}
