using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ArenaCompanion;
static class SavedRecoveryTests {
 static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static async Task Until(Func<bool> test){DateTime end=DateTime.UtcNow.AddSeconds(20);while(!test()&&DateTime.UtcNow<end)await Task.Delay(100);if(!test())throw new Exception("Saved-window integration timed out");}
 [STAThread] static int Main(){Application.EnableVisualStyles();Environment.SetEnvironmentVariable("ARENA_BACKGROUND","1");int result=0;using(var host=new Form {Opacity=0,ShowInTaskbar=false}){host.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);result=1;}finally{host.Close();}};Application.Run(host);}return result;}
 static async Task Run(){
  string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","qa","saved-recovery-local",DateTime.Now.ToString("yyyyMMdd-HHmmss")));
  Directory.CreateDirectory(root);string session="01a09075-6027-7520-a0c8-d64be1b603f5",url="https://arena.ai/agent/"+session;
  new AccountStore(root).Save(new AccountData {Email="fixture@example.invalid",Password="FixtureOnly!"});
  File.WriteAllText(Path.Combine(root,"conversation.json"),new JavaScriptSerializer().Serialize(new SavedConversation {Id="fixture",Email="fixture@example.invalid",Title="恢复集成测试",Url=url}));
  using(var form=new SavedConversationWindow(root)){
   foreach(var view in new[]{Field<WebView2>(form,"browser"),Field<WebView2>(form,"authBrowser")}){
    view.CoreWebView2InitializationCompleted+=(s,e)=>{
     var core=view.CoreWebView2;core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
     core.WebResourceRequested+=(sender,args)=>{
      string body="",headers="Content-Type: text/html\r\n";int code=200;
      if(args.Request.Uri.EndsWith("/review-feedback")){code=404;headers="Content-Type: application/json\r\n";body=new JavaScriptSerializer().Serialize(new {error="Assistant node \"01a09096-19bc-709f-aaf3-8fdd293eda81\" not found in session \""+session+"\""});}
      else if(args.Request.Uri==url||args.Request.Uri=="https://arena.ai/agent")body=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","recovery-fixture.html"))+"<button>fixture@example.invalid</button>";else code=404;
      args.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)),code,code==200?"OK":"Not Found",headers);
     };
    };
   }
   form.Show();await Until(()=>Field<bool>(form,"opened")&&Field<WebView2>(form,"browser").Source.AbsoluteUri==url);await Task.Delay(500);
   var browser=Field<WebView2>(form,"browser");var recovery=Field<ConversationRecovery>(form,"recovery");
   if(recovery==null)throw new Exception("Production window did not install automatic recovery");
   await browser.CoreWebView2.ExecuteScriptAsync("triggerError();void 0");
   await Until(()=>recovery.Repaired==1);
   if(await BrowserScript.For(browser.CoreWebView2).Execute("calls")!="1")throw new Exception("Recovery mutation count mismatch");
   if(!Field<Label>(form,"status").Text.Contains("已自动修复"))throw new Exception("Recovery not visible in production UI");
   if(!File.Exists(Path.Combine(recovery.LastBackup,"恢复前完整会话.json")))throw new Exception("Backup absent");
   File.WriteAllText(Path.Combine(root,"result.json"),"{\"productionSavedWindow\":true,\"automaticRepair\":true,\"backup\":true,\"uiStatus\":true,\"online\":false}");
   Console.WriteLine("PASS production saved window automatically repairs native 404, records backup and displays result; local fixture only");
   form.Close();
  }
 }
}
