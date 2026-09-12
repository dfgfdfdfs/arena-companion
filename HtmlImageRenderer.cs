using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    // Renders generated documents in an isolated browser; retains PNG only.
    public sealed class HtmlImageRenderer {
        readonly string profile;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=100000000};
        public HtmlImageRenderer(string profile) {this.profile=profile;}
        public async Task<CandidateImage> Render(string html,string path,string name) {
            using(var form=new Form {ClientSize=new Size(640,800),Opacity=0,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual})
            using(var view=new WebView2 {Dock=DockStyle.Fill}) {
                form.Controls.Add(view);form.Show();
                await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,profile));
                var core=view.CoreWebView2;
                core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
                core.NewWindowRequested+=(s,e)=>e.Handled=true;
                core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
                core.DownloadStarting+=(s,e)=>e.Cancel=true;
                string navigation="";ulong navigationId=0;bool initialDocument=true;
                core.NavigationStarting+=(s,e)=>{
                    if(initialDocument){initialDocument=false;navigation=e.Uri;navigationId=e.NavigationId;}
                    else if(e.Uri!="about:blank")e.Cancel=true;
                };
                await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride","{\"width\":640,\"height\":800,\"deviceScaleFactor\":1,\"mobile\":false}");
                await core.CallDevToolsProtocolMethodAsync("Emulation.setScrollbarsHidden","{\"hidden\":true}");
                var loaded=new TaskCompletionSource<bool>();
                core.NavigationCompleted+=(s,e)=>{if(e.NavigationId==navigationId)loaded.TrySetResult(e.IsSuccess);};
                core.NavigateToString(html);
                if(await Task.WhenAny(loaded.Task,Task.Delay(15000))!=loaded.Task||!await loaded.Task)throw new IOException("HTML 渲染加载未完成："+navigation.Split(',')[0]);
                await WaitForLayout(core);
                return await Capture(core,path,name);
            }
        }
        async Task WaitForLayout(CoreWebView2 core) {
            for(int n=0;n<40;n++) {
                string ready=await core.ExecuteScriptAsync("document.readyState==='complete'&&document.fonts.status==='loaded'&&[...document.images].every(i=>i.complete)");
                if(ready=="true"){await Task.Delay(700);return;}
                await Task.Delay(250);
            }
            throw new TimeoutException("HTML 字体或图像仍在加载，未保存不完整截图");
        }
        async Task<CandidateImage> Capture(CoreWebView2 core,string path,string name) {
            var layout=json.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics","{}"));
            var size=(Dictionary<string,object>)layout["cssContentSize"];
            int width=(int)Math.Ceiling(Convert.ToDouble(size["width"])),height=(int)Math.Ceiling(Convert.ToDouble(size["height"]));
            if(width>4096||height>20000||(long)width*height>40000000)throw new InvalidOperationException("页面过大，未截断保存；请在原对话查看");
            string args=json.Serialize(new {format="png",captureBeyondViewport=true,fromSurface=true,clip=new {x=0,y=0,width,height,scale=1}});
            var shot=json.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",args));
            byte[] bytes=Convert.FromBase64String(Convert.ToString(shot["data"]));
            File.WriteAllBytes(path+".tmp",bytes);
            if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
            return new CandidateImage {Name=name,File=Path.GetFileName(path),Width=width,Height=height};
        }
    }
}
