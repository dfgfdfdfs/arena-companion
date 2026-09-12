using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class SavedConversationWindow : Form {
        readonly WebView2 browser=new WebView2 {Dock=DockStyle.Fill},authBrowser=new WebView2();
        readonly Label status=new Label {Dock=DockStyle.Top,Height=36,TextAlign=ContentAlignment.MiddleLeft};
        readonly Button retry=new Button {Dock=DockStyle.Top,Height=30,Text="重新登录并打开对话"};
        readonly Timer timer=new Timer {Interval=700};
        readonly string folder;
        readonly SavedConversation saved;
        readonly List<string> phases=new List<string>();
        ConversationRecovery recovery;
        AuthPages pages;AuthFlow flow;ControlPipe pipe;bool opened,ready;
        public SavedConversationWindow(string directory) {
            folder=Path.GetFullPath(directory);saved=SavedConversationStore.Load(folder);
            Text=saved.Title+" · "+saved.Email;ClientSize=new Size(1200,850);
            if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")=="1"){Opacity=0;ShowInTaskbar=false;}
            Controls.Add(browser);Controls.Add(retry);Controls.Add(status);
            Shown+=async(s,e)=>await Initialize();
            retry.Click+=(s,e)=>{if(flow!=null&&!flow.Running){opened=false;flow.Start();}};
            FormClosed+=(s,e)=>{timer.Stop();timer.Dispose();if(recovery!=null)recovery.Dispose();if(pipe!=null)pipe.Dispose();authBrowser.Dispose();};
        }
        async Task Initialize() {
            try {
                var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"Browser"));
                await browser.EnsureCoreWebView2Async(env);await authBrowser.EnsureCoreWebView2Async(env);
                browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
                browser.CoreWebView2.Settings.IsGeneralAutofillEnabled=false;
                browser.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;Uri u;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="arena.ai")browser.CoreWebView2.Navigate(e.Uri);};
                browser.CoreWebView2.NavigationCompleted+=(s,e)=>{ready=e.IsSuccess;if(ready&&opened&&browser.Source.AbsoluteUri==saved.Url)status.Text="已打开保存的对话 · "+saved.Email;};
                pages=new AuthPages(target=>target=="arena"?browser:authBrowser);
                flow=new AuthFlow(pages,new AccountStore(folder),null,true);
                flow.Changed+=()=>{status.Text=flow.Message;retry.Enabled=!flow.Running;phases.Add(flow.Phase);if(phases.Count>100)phases.RemoveAt(0);};
                recovery=new ConversationRecovery(browser,folder,()=>opened&&!flow.Running,message=>status.Text=message);
                timer.Tick+=async(s,e)=>{
                    await flow.Tick();
                    if(IsDisposed||Disposing)return;
                    if(flow.Phase=="complete"&&!opened){opened=true;status.Text="账号已确认，正在打开保存的对话…";browser.CoreWebView2.Navigate(saved.Url);}
                };
                pipe=new ControlPipe(folder,Dispatch);var serving=pipe.Run();flow.Start();timer.Start();
            }catch(Exception ex){status.Text="打开失败："+ex.Message;}
        }
        Task<object> Dispatch(Dictionary<string,object> request) {
            var completion=new TaskCompletionSource<object>();
            BeginInvoke(new Action(async()=>{try{completion.SetResult(await Control(request));}catch(Exception ex){completion.SetException(ex);}}));return completion.Task;
        }
        async Task<object> Control(Dictionary<string,object> request) {
            string command=AuthFlow.Value(request,"command");
            if(command=="quit"){BeginInvoke(new Action(Close));return new {ok=true};}
            if(command=="recovery.status")return new {ok=true,repaired=recovery.Repaired,message=recovery.Message,backup=recovery.LastBackup};
            if(command!="status")throw new ArgumentException("保存入口仅支持读取状态和关闭");
            var state=await pages.Read("arena");
            string title=await browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('a[href]')].filter(e=>e.getClientRects().length&&new URL(e.href).pathname===location.pathname).map(e=>e.textContent.trim())[0]||''");
            return new {ok=true,ready,phase=flow.Phase,running=flow.Running,message=status.Text,opened,phases=phases.ToArray(),
                expectedUrl=saved.Url,url=browser.Source==null?"":browser.Source.GetLeftPart(UriPartial.Path),
                account=AuthFlow.Value(state,"account"),stage=AuthFlow.Value(state,"stage"),
                title=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(title)};
        }
    }
}
