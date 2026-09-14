using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class CandidatePage {
        readonly WebView2 browser;
        readonly BrowserScript scripts;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=8000000};
        public string ExpectedUrl {get;set;}
        public CandidatePage(WebView2 browser) {this.browser=browser;scripts=BrowserScript.For(browser.CoreWebView2);}
        public async Task<Dictionary<string,object>> State() {return (Dictionary<string,object>)await Action("state","");}
        public async Task Rename(string title) {
            var state=await State();if(Convert.ToString(state["title"])==title)return;
            if(!Convert.ToBoolean(state["renameDialog"])) {
                if(!Convert.ToBoolean(state["renameMenu"]))await Action("renameMenu","");
                await WaitFor("renameMenu");await Action("button","Rename");await WaitFor("renameDialog");
            }
            await Action("renameFill",title);await Task.Delay(150);await Action("renameSave","");
            for(int n=0;n<30;n++) {await Task.Delay(200);state=await State();if(!Convert.ToBoolean(state["renameDialog"])&&Convert.ToString(state["title"])==title)return;}
            throw new TimeoutException("重命名尚未确认；图片已保留，可重新收集重试");
        }
        async Task WaitFor(string key) {
            for(int n=0;n<25;n++){if(Convert.ToBoolean((await State())[key]))return;await Task.Delay(200);}
            throw new TimeoutException("等待重命名控件超时");
        }
        public async Task<string> DownloadHtml(string name) {
            await Action("file",name);await Task.Delay(600);
            var inline=(Dictionary<string,object>)await Action("html",name);
            string inlineHtml=Convert.ToString(inline["html"]);
            if(!String.IsNullOrWhiteSpace(inlineHtml)) {
                if(Encoding.UTF8.GetByteCount(inlineHtml)>1900000)throw new InvalidOperationException("HTML 超过当前渲染容量，未生成不完整截图");
                return inlineHtml;
            }
            string temporary=Path.Combine(Path.GetTempPath(),"arena-preview-"+Guid.NewGuid().ToString("N")+".html");
            var completion=new TaskCompletionSource<string>();CoreWebView2DownloadOperation download=null;
            EventHandler<CoreWebView2DownloadStartingEventArgs> handler=(s,e)=>{
                e.Handled=true;
                if(!String.Equals(Path.GetFileName(e.ResultFilePath),name,StringComparison.OrdinalIgnoreCase)) {e.Cancel=true;completion.TrySetException(new InvalidOperationException("下载文件与所选 HTML 不一致"));return;}
                e.ResultFilePath=temporary;download=e.DownloadOperation;
                download.StateChanged+=(sender,args)=>{
                    if(download.State==CoreWebView2DownloadState.Completed)completion.TrySetResult(temporary);
                    if(download.State==CoreWebView2DownloadState.Interrupted)completion.TrySetException(new IOException("HTML 读取中断"));
                };
            };
            browser.CoreWebView2.DownloadStarting+=handler;
            EventHandler<CoreWebView2PermissionRequestedEventArgs> permission=(s,e)=>{
                Uri origin;
                if(e.PermissionKind==CoreWebView2PermissionKind.MultipleAutomaticDownloads&&
                    Uri.TryCreate(e.Uri,UriKind.Absolute,out origin)&&origin.Host=="arena.ai"&&
                    browser.Source!=null&&browser.Source.AbsoluteUri==ExpectedUrl) {
                    e.State=CoreWebView2PermissionState.Allow;e.SavesInProfile=false;e.Handled=true;
                }
            };
            browser.CoreWebView2.PermissionRequested+=permission;
            try {
                await Action("button","Download file");
                if(await Task.WhenAny(completion.Task,Task.Delay(15000))!=completion.Task)throw new TimeoutException("HTML 读取超时");
                await completion.Task;
                if(new FileInfo(temporary).Length>1900000)throw new InvalidOperationException("HTML 超过当前渲染容量，未生成不完整截图");
                return File.ReadAllText(temporary);
            } finally {
                browser.CoreWebView2.DownloadStarting-=handler;
                browser.CoreWebView2.PermissionRequested-=permission;
                if(download!=null&&download.State==CoreWebView2DownloadState.InProgress)download.Cancel();
                if(File.Exists(temporary))File.Delete(temporary);
            }
        }
        public async Task<object> Action(string action,string value) {
            if(browser.Source==null||!CandidateUrl(browser.Source.AbsoluteUri))throw new InvalidOperationException("请先打开一条 Arena 对话");
            if(ExpectedUrl!=null&&browser.Source.AbsoluteUri!=ExpectedUrl)throw new InvalidOperationException("对话地址已经改变，候选操作已停止");
            string script=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","CandidateBridge.js"));
            string result=await scripts.Execute(script+"\nwindow.__arenaCandidate("+json.Serialize(action)+","+json.Serialize(value)+");");
            var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(result);
            if(data==null||data.ContainsKey("error"))throw new InvalidOperationException(data==null?"候选页面操作未确认":Convert.ToString(data["error"]));
            return data;
        }
        public static bool CandidateUrl(string url) {
            Uri u;Guid id;
            return Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="arena.ai"&&u.Port==443&&u.Query==""&&u.Fragment==""&&u.AbsolutePath.StartsWith("/agent/")&&Guid.TryParse(u.AbsolutePath.Substring(7),out id);
        }
    }
}
