using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    // Owns detection, durable backup and the guarded two-phase page repair.
    public sealed class ConversationRecovery : IDisposable {
        readonly WebView2 view;
        readonly BrowserScript scripts;
        readonly string folder,bridge;
        readonly Func<bool> allowed;
        readonly Action<string> report;
        readonly Action beforeRepair;
        readonly Timer timer=new Timer {Interval=2000};
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=10000000};
        bool busy,disposed;
        string evidence="",evidenceUrl="",lastError="";
        DateTime expires;
        public string Message {get;private set;}
        public int Repaired {get;private set;}
        public string LastBackup {get;private set;}
        public ConversationRecovery(WebView2 view,string folder,Func<bool> allowed,Action<string> report,Action beforeRepair=null) {
            this.view=view;this.folder=folder;this.allowed=allowed;this.report=report;this.beforeRepair=beforeRepair;
            scripts=BrowserScript.For(view.CoreWebView2);
            bridge=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","ConversationRecovery.js"));
            view.CoreWebView2.WebResourceResponseReceived+=Observe;
            timer.Tick+=async(s,e)=>await Check();timer.Start();
        }
        static string Value(Dictionary<string,object> v,string k){return v.ContainsKey(k)?Convert.ToString(v[k]):"";}
        void Say(string message){Message=message;if(report!=null)report(message);}
        async void Observe(object sender,CoreWebView2WebResourceResponseReceivedEventArgs e) {
            Uri uri;
            if(disposed||e.Response.StatusCode!=404||!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||uri.Host!="arena.ai"||uri.Scheme!="https"||!Regex.IsMatch(uri.AbsolutePath,@"^/api/chat/[0-9a-f-]{36}/review-feedback$"))return;
            string url="https://arena.ai/agent/"+uri.Segments[3].TrimEnd('/');
            try {
                var reading=e.Response.GetContentAsync();if(await Task.WhenAny(reading,Task.Delay(8000))!=reading)return;
                using(var stream=await reading)using(var reader=new StreamReader(stream)) {
                    char[] chars=new char[8192];int count=await reader.ReadBlockAsync(chars,0,chars.Length);
                    var body=json.Deserialize<Dictionary<string,object>>(new string(chars,0,count));
                    string error=Value(body,"error");
                    if(!Regex.IsMatch(error,"Assistant node \"[0-9a-f-]{36}\" not found in session \"[0-9a-f-]{36}\""))return;
                    if(disposed||view.Source==null||view.Source.AbsoluteUri!=url)return;
                    evidence=error;evidenceUrl=url;expires=DateTime.UtcNow.AddMinutes(2);
                }
            }catch(Exception){/* DOM detection remains available if a response body is unavailable. */}
        }
        async Task<Dictionary<string,object>> Call(string action,string value) {
            string code=bridge+"\nwindow.__arenaConversationRecovery."+action+"("+json.Serialize(value)+");";
            return json.Deserialize<Dictionary<string,object>>(await scripts.Execute(code));
        }
        public async Task Check() {
            if(disposed||busy||scripts.Busy||!allowed()||view.Source==null||!CandidatePage.CandidateUrl(view.Source.AbsoluteUri))return;
            busy=true;
            try {
                string hint=view.Source.AbsoluteUri==evidenceUrl&&DateTime.UtcNow<expires?evidence:"";
                var snapshot=await Call("prepare",hint);
                if(disposed||!allowed()||snapshot==null||!snapshot.ContainsKey("eligible")||!Convert.ToBoolean(snapshot["eligible"]))return;
                string directory=Path.Combine(folder,"故障恢复备份",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
                Directory.CreateDirectory(directory);
                string backup=Path.Combine(directory,"恢复前完整会话.json"), content=json.Serialize(snapshot["backup"]);
                using(var file=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file)){writer.Write(content);writer.Flush();file.Flush(true);}
                if(File.ReadAllText(backup)!=content)throw new IOException("备份回读不一致");
                File.WriteAllText(Path.Combine(directory,"未入库回复.txt"),Value(snapshot,"text"));
                LastBackup=directory;
                if(disposed||!allowed())return;
                if(beforeRepair!=null)beforeRepair();
                var applied=await Call("apply",Value(snapshot,"token"));
                if(!Convert.ToBoolean(applied["applied"])){Say("已保留备份；页面发生变化，本次未自动修复");return;}
                await Task.Delay(150);
                var verified=await Call("verify",Value(snapshot,"token"));
                bool ok=Convert.ToBoolean(verified["repaired"]);
                File.WriteAllText(Path.Combine(directory,"修复结果.json"),json.Serialize(new {url=Value(snapshot,"url"),nodeId=Value(snapshot,"nodeId"),repaired=ok,backup=backup}));
                if(!ok){Say("会话恢复结果尚未确认，原回复已备份："+directory);return;}
                Repaired++;evidence="";lastError="";
                Say("已自动修复超时后的无效回复节点，可继续输入；原回复及工具记录已备份："+directory);
            }catch(Exception ex) {
                if(!disposed&&!(ex is TimeoutException)&&lastError!=ex.Message){lastError=ex.Message;Say("自动修复未完成，已保留当前页面："+ex.Message);}
            }finally{busy=false;}
        }
        public void Dispose(){disposed=true;timer.Stop();timer.Dispose();view.CoreWebView2.WebResourceResponseReceived-=Observe;}
    }
}
