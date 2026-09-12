using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class ConversationWindow : Form {
        readonly WebView2 view=new WebView2 {Dock=DockStyle.Fill};
        ConversationRecovery recovery;
        public CandidateRecord Record {get;private set;}
        public bool Ready {get;private set;}
        public ConversationWindow(CandidateRecord record) {
            Record=record;Text=record.Title+" · "+record.Email;ClientSize=new Size(1200,850);
            if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")=="1"){Opacity=0;ShowInTaskbar=false;}
            FormClosed+=(s,e)=>{if(recovery!=null)recovery.Dispose();};
            Controls.Add(view);Shown+=async(s,e)=>{
                try {
                    await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(record.Profile,"Browser")));
                    view.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
                    view.CoreWebView2.NewWindowRequested+=(sender,args)=>{args.Handled=true;Uri u;if(Uri.TryCreate(args.Uri,UriKind.Absolute,out u)&&u.Scheme=="https")view.CoreWebView2.Navigate(args.Uri);};
                    view.CoreWebView2.NavigationCompleted+=(sender,args)=>Ready=args.IsSuccess;
                    recovery=new ConversationRecovery(view,record.Profile,()=>Ready,message=>Text=record.Title+" · "+message);
                    view.CoreWebView2.Navigate(record.Url);
                }catch(Exception ex){Text="原对话加载失败："+ex.Message;}
            };
        }
        public async Task<object> Inspect() {
            if(!Ready)return new {ready=false};
            string details=await view.CoreWebView2.ExecuteScriptAsync("({url:location.origin+location.pathname,chatTitle:[...document.querySelectorAll('a[href]')].filter(e=>e.getClientRects().length&&new URL(e.href).pathname===location.pathname).map(e=>e.textContent.trim())[0]||'',accounts:[...document.querySelectorAll('button')].filter(e=>e.getClientRects().length&&/@/.test(e.textContent)).map(e=>e.textContent.trim()),conversation:[...document.querySelectorAll('[role=log]')].filter(e=>e.getClientRects().length).map(e=>e.innerText.slice(0,160))})");
            return new {ready=true,sourceProfile=Record.Profile,expectedUrl=Record.Url,details=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(details)};
        }
    }
}
