using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed partial class MainForm {
        readonly WebView2 authBrowser=new WebView2();
        readonly TabControl browserTabs=new TabControl {Dock=DockStyle.Fill};
        readonly Button autoLogin=new Button();
        readonly Button cancelLogin=new Button();
        AuthPages authPages;
        AuthFlow authFlow;
        AccountStore accountStore;
        ControlPipe controlPipe;
        bool verificationSignalHandled;
        bool rateLimitSignalHandled;
        void AddBrowserTabs(Control parent) {
            var arenaTab=new TabPage("Arena");var authTab=new TabPage("邮箱 / 账号验证");
            browser.Dock=authBrowser.Dock=DockStyle.Fill;
            arenaTab.Controls.Add(browser);authTab.Controls.Add(authBrowser);
            AddAccountReplacement(authTab);
            browserTabs.TabPages.Add(arenaTab);browserTabs.TabPages.Add(authTab);parent.Controls.Add(browserTabs);
        }
        async Task<bool> InitializeLogin(CoreWebView2Environment env) {
            await authBrowser.EnsureCoreWebView2Async(env);
            authBrowser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
            authBrowser.CoreWebView2.Settings.IsGeneralAutofillEnabled=false;
            authBrowser.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;NavigatePopup(e.Uri);};
            accountStore=new AccountStore(DataDirectory);
            if(!EnsureInstancePassword())return false;
            authPages=new AuthPages(target=>{
                browserTabs.SelectedIndex=target=="arena"?0:1;
                return target=="arena"?browser:authBrowser;
            });
            authFlow=new AuthFlow(authPages,accountStore);
            authFlow.Changed+=()=>{
                status.Text=authFlow.Message;
                log.Items.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+authFlow.Message);
                log.TopIndex=log.Items.Count-1;
                try {File.AppendAllText(Path.Combine(DataDirectory,"登录记录.txt"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+authFlow.Phase+"  "+authFlow.Message+Environment.NewLine);}catch(IOException){}
                SetEnabled();
                QueueQaCapture();
                HandleVerificationSignal(true);
            };
            controlPipe=new ControlPipe(DataDirectory,DispatchOnUi);
            var serving=controlPipe.Run();
            FormClosed+=(s,e)=>controlPipe.Dispose();
            return true;
        }
        bool EnsureInstancePassword() {
            if(!String.IsNullOrEmpty(accountStore.Load().Password))return true;
            string defaults=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","account-defaults","account.dpapi");
            if(File.Exists(defaults)){accountStore.EnsurePassword();return true;}
            return PasswordSetupDialog.Configure(this,accountStore);
        }
        void HandleVerificationSignal(bool loginFlow) {
            bool waiting=loginFlow?authFlow.WaitingForVerification:controller.WaitingForVerification;
            if(!waiting){verificationSignalHandled=false;return;}
            browserTabs.SelectedIndex=1;
            if(verificationSignalHandled)return;
            verificationSignalHandled=true;
            changeAccount.PerformClick();
        }
        void HandleRateLimitSignal() {
            bool detected=controller!=null&&controller.Phase=="cooldown"&&controller.Finished&&!controller.Running;
            if(!detected){rateLimitSignalHandled=false;return;}
            browserTabs.SelectedIndex=1;
            if(rateLimitSignalHandled)return;
            rateLimitSignalHandled=true;
            rateLimitTest.PerformClick();
        }
        void NavigatePopup(string url) {
            Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https")return;
            if(controller!=null)controller.Pause("登录或新页面已在单独标签页打开");
            browserTabs.SelectedIndex=1;authBrowser.CoreWebView2.Navigate(url);
        }
        void StartAutoLogin() {
            try {
                if(authFlow.Running)return;
                BeginAutoLogin();
            } catch(Exception ex) {ShowError(ex.Message);}
        }
        void BeginAutoLogin() {
            if(!EnsureInstancePassword())throw new InvalidOperationException("必须先设置邮箱账号密码");
            controller.Pause("自动登录处理中");page.Demo=false;
            mode.Text="在线模式 · 自动注册 / 登录 · 账号在本机加密保存";
            authFlow.Start();SetEnabled();
        }
        Task<object> DispatchOnUi(Dictionary<string,object> request) {
            var completion=new TaskCompletionSource<object>();
            BeginInvoke(new Action(async ()=>{
                try {completion.SetResult(await Control(request));}
                catch(Exception ex) {completion.SetException(ex);}
            }));
            return completion.Task;
        }
        object CurrentStatus() {
            if(!ready)return new {ok=true,ready=false};
            return new {ok=true,ready,instance=InstanceContext.Name,dataDirectory=DataDirectory,browserDirectory=browser.CoreWebView2.Environment.UserDataFolder,mode=page.Demo?"demo":"online",phase=controller.Phase,running=controller.Running,
                finished=controller.Finished,attempt=controller.Attempt,limit=controller.Limit,unlimited=controller.Limit==0,rejected=controller.Rejected,collected=controller.Collected,collecting,message=status.Text,
                cooldownSeconds=controller.CooldownSeconds,retryAt=controller.RetryAt.HasValue?controller.RetryAt.Value.ToString("o"):null,
                replacing,startAfterLogin=loginTaskStart.Pending,waitingForVerification=controller.WaitingForVerification,
                network=new {switching=switchingIp,ok=lastIpSwitchOk,message=ipStatus.Text},
                auth=new {running=authFlow.Running,waitingForVerification=authFlow.WaitingForVerification,phase=authFlow.Phase,message=authFlow.Message,email=authFlow.Email}};
        }
        async Task<object> Control(Dictionary<string,object> request) {
            string command=AuthFlow.Value(request,"command");
            if(command=="status")return CurrentStatus();
            if(command=="recovery.status")return new {ok=true,repaired=recovery==null?0:recovery.Repaired,message=recovery==null?null:recovery.Message,backup=recovery==null?null:recovery.LastBackup};
            if(command=="show") {Opacity=1;ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;return CurrentStatus();}
            if(command=="quit") {BeginInvoke(new Action(Close));return new {ok=true};}
            if(!ready)throw new InvalidOperationException("软件正在初始化，请稍后读取状态");
            if(command=="account.replace")return await CreateReplacement();
            if(replacing&&command!="page.read"&&command!="page.inspect")throw new InvalidOperationException("正在交接到新账号窗口，请稍候");
            if(command.StartsWith("gallery."))return await GalleryControl(request);
            if(collecting&&command!="pause"&&command!="page.read"&&command!="page.inspect"&&command!="candidate.inspect")throw new InvalidOperationException("正在收集候选，请等待收集结束");
            if(command=="candidate.inspect")return new {ok=true,details=await new CandidatePage(browser).Action("inspect","")};
            if(command=="candidate.action"||command=="conversation.open") {
                if(authFlow.Running||controller.Running)throw new InvalidOperationException("请先停止当前自动流程");
                if(command=="candidate.action")return await new CandidatePage(browser).Action(AuthFlow.Value(request,"action"),AuthFlow.Value(request,"value"));
                string url=AuthFlow.Value(request,"url");if(!CandidatePage.CandidateUrl(url))throw new ArgumentException("无效的对话地址");
                page.Demo=false;browserTabs.SelectedIndex=0;browser.CoreWebView2.Navigate(url);return new {ok=true};
            }
            if(command.StartsWith("settings.")||command.StartsWith("attachments.")||command=="draft.replace"||command.StartsWith("terms."))return await TaskControl(request);
            if(command=="auth.stop") {loginTaskStart.Cancel();authFlow.Stop("已通过接口停止登录");return CurrentStatus();}
            if(command=="pause") {PauseAutomation();return CurrentStatus();}
            if(command=="page.read")return new {ok=true,state=await page.Read(controller.Prompt ?? "")};
            if(command=="page.inspect")return new {ok=true,details=await page.Inspect()};
            if(command=="capture") {await CaptureForQa();return new {ok=true};}
            if(command=="network.show") {browserTabs.SelectedIndex=1;await CaptureForQa();return new {ok=true};}
            if(command=="network.switch") {
                if(authFlow.Running||controller.Running||collecting||replacing)throw new InvalidOperationException("请先停止当前登录、任务或收集流程");
                string message=await SwitchToNextIp();return new {ok=lastIpSwitchOk,message=message};
            }
            if(command=="auth.read") {
                var state=await authPages.Read(AuthFlow.Value(request,"target")=="auth"?"auth":"arena");
                bool hasVerification=AuthFlow.Value(state,"verifyUrl")!="";
                state.Remove("verifyUrl");state["hasVerificationLink"]=hasVerification;return new {ok=true,state};
            }
            if(authFlow.Running||controller.Running)throw new InvalidOperationException("请先停止当前自动流程");
            if(command=="account.configure") {
                var data=accountStore.Load();
                string email=AuthFlow.Value(request,"email"), password=AuthFlow.Value(request,"password");
                if(password.Length<8)throw new ArgumentException("密码至少需要 8 个字符");
                data.Email=email;data.Password=password;data.Name="Kai";data.Verified=false;accountStore.Save(data);
                return new {ok=true,email=data.Email,passwordStored=true};
            }
            if(command=="auth.start")BeginAutoLogin();
            else if(command=="demo")OpenDemo();
            else if(command=="open")OpenWebsite();
            else if(command=="start") {
                string value=AuthFlow.Value(request,"prompt");if(value!="")prompt.Text=value;
                int attemptLimit=request.ContainsKey("limit")?Convert.ToInt32(request["limit"]):0;
                if(request.ContainsKey("current"))current.Checked=Convert.ToBoolean(request["current"]);
                PrepareTask();controller.Start(prompt.Text,attemptLimit,current.Checked);
            } else if(command=="resume")controller.Resume();
            else throw new ArgumentException("未知接口操作");
            return CurrentStatus();
        }
    }
}
