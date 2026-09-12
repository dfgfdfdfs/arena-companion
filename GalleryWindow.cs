using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed class GalleryWindow : Form {
        readonly WebView2 view=new WebView2 {Dock=DockStyle.Fill};
        readonly CandidateStore store;
        readonly Action<CandidateRecord> open;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly SavedConversationStore exports;
        readonly Func<bool> canEdit;
        public event Action Changed;
        public string LastSavedFolder {get;private set;}
        public bool Ready {get;private set;}
        public GalleryWindow(CandidateStore store,Action<CandidateRecord> open,Func<bool> canEdit=null) {
            this.store=store;this.open=open;Text="Arena 候选图集";ClientSize=new Size(1180,840);MinimumSize=new Size(760,560);
            this.canEdit=canEdit??(()=>true);
            exports=new SavedConversationStore(Path.GetFullPath(Path.Combine(store.DirectoryPath,"..","gallery-export-directory.txt")));
            if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")=="1"){Opacity=0;ShowInTaskbar=false;}
            Controls.Add(view);Shown+=async(s,e)=>{
                try {
                    await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.DirectoryPath,"..","GalleryBrowser")));
                    view.CoreWebView2.SetVirtualHostNameToFolderMapping("arena-gallery-images.local",store.DirectoryPath,CoreWebView2HostResourceAccessKind.DenyCors);
                    view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                    view.CoreWebView2.WebMessageReceived+=(sender,args)=>{
                        if(args.Source!="about:blank")return;
                        try {HandleMessage(json.Deserialize<Dictionary<string,object>>(args.WebMessageAsJson));}catch(Exception ex){Notice("操作未完成："+ex.Message);}
                    };
                    view.CoreWebView2.NewWindowRequested+=(sender,args)=>args.Handled=true;
                    bool initialDocument=true;
                    view.CoreWebView2.NavigationStarting+=(sender,args)=>{if(initialDocument){initialDocument=false;return;}if(args.Uri!="about:blank")args.Cancel=true;};
                    view.CoreWebView2.NavigationCompleted+=(sender,args)=>{Ready=args.IsSuccess;if(Ready)Reload();};
                    view.CoreWebView2.NavigateToString(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","gallery.html")));
                }catch(Exception ex){Text="图集加载失败："+ex.Message;}
            };
        }
        void HandleMessage(Dictionary<string,object> message) {
            string action=Convert.ToString(message["action"]);
            if(action=="choose") {
                using(var picker=new FolderBrowserDialog {Description="选择账号与对话的保存位置",SelectedPath=exports.Destination}) {
                    if(picker.ShowDialog(this)==DialogResult.OK)SetDestination(picker.SelectedPath);
                }return;
            }
            if(action=="clear") {
                if(!canEdit())throw new InvalidOperationException("正在收集图片，请收集完成后再清空");
                store.Clear();Reload();if(Changed!=null)Changed();Notice("当前候选图集已清空");return;
            }
            var record=store.Get(Convert.ToString(message["id"]));string file=Convert.ToString(message["file"]);
            if(!record.Images.Any(i=>i.File==file))throw new InvalidOperationException("该图片已不在图集中");
            if((action=="discard"||action=="save")&&!canEdit())throw new InvalidOperationException("正在收集图片，请收集完成后再操作");
            if(action=="open")open(record);
            else if(action=="discard") {store.Discard(record.Id,file);Reload();if(Changed!=null)Changed();Notice("已丢弃该图");}
            else if(action=="save") {LastSavedFolder=exports.Save(record);Notice("账号与对话已保存：\n"+LastSavedFolder);}
            else throw new ArgumentException("未知图集操作");
        }
        void Notice(string text) {view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {notice=text}));}
        public void SetDestination(string path) {exports.SetDestination(path);Reload();}
        public void Clear() {if(!canEdit())throw new InvalidOperationException("正在收集图片，请收集完成后再清空");store.Clear();Reload();if(Changed!=null)Changed();}
        public void Reload() {if(Ready)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {records=store.All(),destination=exports.Destination}));}
        public async Task<object> Inspect() {
            if(!Ready)return new {ready=false};
            return new {lastSavedFolder=LastSavedFolder,destination=exports.Destination,ui=json.DeserializeObject(await view.CoreWebView2.ExecuteScriptAsync("({ready:true,cards:document.querySelectorAll('.card').length,columns:getComputedStyle(document.getElementById('grid')).gridTemplateColumns,selected:[...document.querySelectorAll('.card.selected')].map(e=>({id:e.dataset.id,file:e.dataset.file})),menuVisible:!document.getElementById('menu').hidden,images:[...document.querySelectorAll('.card img')].map(i=>({complete:i.complete,width:i.naturalWidth,height:i.naturalHeight})),text:document.body.innerText.slice(0,2000)})"))};
        }
        public async Task ClickImage(string id) {
            store.Get(id);if(!Ready)throw new InvalidOperationException("图集正在加载");
            await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-id=\"'+"+json.Serialize(id)+"+'\"] .image-link').click()");
        }
        public async Task ContextAction(string id,string file,string action) {
            var record=store.Get(id);if(String.IsNullOrEmpty(file))file=record.Images.First().File;
            if(!record.Images.Any(i=>i.File==file)||!Ready)throw new InvalidOperationException("图片不存在或图集未加载");
            if(action!=""&&action!="open"&&action!="save"&&action!="discard")throw new ArgumentException("未知菜单操作");
            string script="(()=>{const card=[...document.querySelectorAll('.card')].find(e=>e.dataset.id==="+json.Serialize(id)+"&&e.dataset.file==="+json.Serialize(file)+");card.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,clientX:60,clientY:180}));";
            if(action!="")script+="document.querySelector('#menu [data-action=\"'+"+json.Serialize(action)+"+'\"]').click();";
            await view.CoreWebView2.ExecuteScriptAsync(script+"})()");
        }
        public async Task CaptureImage(string path) {using(var file=File.Create(path))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
    }
}
