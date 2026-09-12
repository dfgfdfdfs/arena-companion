using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed partial class MainForm {
        readonly Button galleryButton=new Button(),collectButton=new Button();
        readonly CheckBox keepCollecting=new CheckBox {Text="收集后继续筛选（不限次数）",Dock=DockStyle.Fill};
        CandidateStore candidateStore;
        CandidateCollector collector;
        GalleryWindow gallery;
        ConversationWindow lastConversation;
        bool collecting,collectionAttempted,collectionCancelled;
        string collectionMessage="";
        CandidateRecord lastCollected;
        void InitializeGallery() {
            string shared=Environment.GetEnvironmentVariable("ARENA_SETTINGS_DIRECTORY")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Arena筛选助手");
            candidateStore=new CandidateStore(Path.Combine(shared,"候选图集"));
            collector=new CandidateCollector(candidateStore,new CandidatePage(browser),new HtmlImageRenderer(Path.Combine(shared,"RenderBrowser")));
            RefreshGalleryButton();
        }
        void ConfigureGalleryControls(TableLayoutPanel side) {
            keepCollecting.Checked=taskSettings.ContinueCollecting;
            keepCollecting.CheckedChanged+=(s,e)=>SaveTaskSettings();
            ConfigureButton(galleryButton,"候选图集",false);ConfigureButton(collectButton,"收集当前结果",false);
            side.Controls.Add(Row(galleryButton,collectButton),0,16);side.Controls.Add(keepCollecting,0,17);
            galleryButton.Click+=(s,e)=>OpenGallery();collectButton.Click+=async(s,e)=>await CollectCurrent(false);
        }
        void RefreshGalleryButton() {if(candidateStore!=null)galleryButton.Text="候选图集（"+candidateStore.All().FindAll(r=>r.Images.Count>0).Count+"）";}
        void OpenGallery() {
            if(gallery==null||gallery.IsDisposed){gallery=new GalleryWindow(candidateStore,OpenCandidate,()=>!collecting);gallery.Changed+=RefreshGalleryButton;gallery.Show(this);}
            else {gallery.Reload();gallery.Activate();}
        }
        void OpenCandidate(CandidateRecord record) {lastConversation=new ConversationWindow(record);lastConversation.Show(this);}
        void CollectionStatus(string message) {
            if(IsDisposed||Disposing)return;
            collectionMessage=message;status.Text=message;log.Items.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+message);log.TopIndex=log.Items.Count-1;
        }
        async Task CollectionTick() {
            if(collecting)return;
            if(authFlow!=null&&(authFlow.Running||authFlow.WaitingForVerification)){await authFlow.Tick();return;}
            if(authFlow!=null&&StartTaskAfterLogin())return;
            if(controller==null)return;await controller.Tick();
            if(!controller.CandidateReady)collectionAttempted=false;
            if(controller.CandidateReady&&!collectionAttempted&&!page.Demo){collectionAttempted=true;await CollectCurrent(true);}
        }
        async Task CollectCurrent(bool automatic) {
            if(collecting||controller.Running||authFlow.Running)return;
            collecting=true;collectionCancelled=false;lastCollected=null;SetEnabled();CollectionStatus("正在读取 HTML 并收集完整图像…");
            try {
                var initial=await page.Read(prompt.Text);string url=initial.url;
                Func<Task> verify=async()=>{
                    if(collectionCancelled||IsDisposed||Disposing)throw new InvalidOperationException("收集已取消");
                    var state=await page.Read(prompt.Text);
                    if(state.url!=url||!CandidatePage.CandidateUrl(state.url)||state.generating||state.thinking||state.failed||!state.response||!state.promptConfirmed||!String.IsNullOrEmpty(state.blocker))throw new InvalidOperationException("当前对话已改变、未完成或出现 Thinking，已停止收集");
                };
                await verify();await Task.Delay(500);await verify();
                lastCollected=await collector.Collect(url,DataDirectory,accountStore.Load().Email,prompt.Text,verify);
                RefreshGalleryButton();if(gallery!=null&&!gallery.IsDisposed)gallery.Reload();
                if(!lastCollected.Renamed)CollectionStatus("图片已入图集；重命名未确认："+lastCollected.RenameError);
                else {
                    if(controller.CandidateReady&&controller.CandidateUrl==url)controller.AfterCollection(keepCollecting.Checked);
                    CollectionStatus(lastCollected.Title+"：已保存 "+lastCollected.Images.Count+" 张图片并重命名"+(controller.Running?"，继续筛选":"，点击候选图集查看"));
                }
            } catch(Exception ex){CollectionStatus("收集暂停："+ex.Message+"。可点击“收集当前结果”重试");}
            finally {collecting=false;SetEnabled();}
        }
        void PauseAutomation() {
            loginTaskStart.Cancel();
            if(collecting){collectionCancelled=true;CollectionStatus("正在停止收集，已保存的图片会保留");}
            else controller.Pause("已暂停；网页正在生成的回答可以继续。点“继续”接着筛选");
        }
        async Task<object> GalleryControl(Dictionary<string,object> request) {
            string command=AuthFlow.Value(request,"command");
            if(command=="gallery.list")return new {ok=true,collecting,message=collectionMessage,records=candidateStore.All()};
            if(command=="gallery.open"){OpenGallery();return new {ok=true};}
            if(command=="gallery.inspect")return new {ok=true,details=gallery==null?null:await gallery.Inspect(),conversation=lastConversation==null?null:await lastConversation.Inspect()};
            if(command=="gallery.click"){if(gallery==null)throw new InvalidOperationException("请先打开图集");await gallery.ClickImage(AuthFlow.Value(request,"id"));return new {ok=true};}
            if(command=="gallery.context"){if(gallery==null)throw new InvalidOperationException("请先打开图集");await gallery.ContextAction(AuthFlow.Value(request,"id"),AuthFlow.Value(request,"file"),AuthFlow.Value(request,"action"));return new {ok=true};}
            if(command=="gallery.directory"){if(gallery==null)throw new InvalidOperationException("请先打开图集");gallery.SetDestination(AuthFlow.Value(request,"path"));return new {ok=true};}
            if(command=="gallery.clear"){if(gallery==null)throw new InvalidOperationException("请先打开图集");gallery.Clear();RefreshGalleryButton();return new {ok=true,records=0};}
            if(command=="gallery.capture"){
                if(gallery==null)throw new InvalidOperationException("请先打开图集");
                string folder=Path.Combine(DataDirectory,"GalleryQA");Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,"gallery-preview.png");await gallery.CaptureImage(path);return new {ok=true,path};
            }
            if(command=="gallery.collect") {
                if(collecting||controller.Running||authFlow.Running)throw new InvalidOperationException("请先等待或停止当前自动流程");
                BeginInvoke(new Action(async()=>await CollectCurrent(false)));return new {ok=true,started=true};
            }
            if(command=="gallery.continue") {keepCollecting.Checked=Convert.ToBoolean(request["enabled"]);return new {ok=true,enabled=keepCollecting.Checked};}
            throw new ArgumentException("未知图集操作");
        }
    }
}
