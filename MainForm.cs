using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ArenaCompanion {
    public sealed partial class MainForm : Form {
        const string DefaultPrompt = "创建一个新HTML，内容是SVG绘制一个鹈鹕骑自行车的2D动画，你不需要任何测试";
        readonly WebView2 browser = new WebView2();
        readonly TextBox prompt = new TextBox();
        readonly CheckBox current = new CheckBox();
        readonly Label status = new Label(), count = new Label(), mode = new Label();
        readonly ListBox log = new ListBox();
        readonly Button open = new Button(), demo = new Button(), start = new Button(), pause = new Button(), resume = new Button(), restart = new Button();
        readonly Timer timer = new Timer();
        readonly bool demoAtStart;
        ConversationRecovery recovery;
        WebPage page;
        RetryController controller;
        bool ready;
        string previousMessage;
        string previousPhase;
        bool capturing;
        string DataDirectory { get { return Environment.GetEnvironmentVariable("ARENA_DATA_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arena筛选助手"); } }
        public MainForm(bool demoAtStart) {
            this.demoAtStart = demoAtStart;
            Text = "Arena 筛选助手 · "+InstanceContext.Name; ClientSize = new Size(1320, 850); MinimumSize = new Size(1080, 740);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(244,247,251); AutoScaleMode = AutoScaleMode.Dpi;
            if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")=="1") {Opacity=0;ShowInTaskbar=false;}
            InitializeTaskSettings();BuildUi();
            Shown += async (s,e) => await Initialize();
            FormClosing += (s,e) => { collectionCancelled=true;if (controller != null) controller.Pause("软件已关闭，自动操作已停止"); if(authFlow!=null)authFlow.Stop("软件已关闭"); timer.Stop();networkTimer.Stop();try {SaveTaskSettings();}catch(IOException){} };
        }
        Label Caption(string text, int size, bool bold, Color color) {
            return new Label { Text=text, AutoSize=false, Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft,
                Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular), ForeColor=color, Margin=new Padding(0) };
        }
        void ConfigureButton(Button b, string text, bool primary) {
            b.Text=text; b.AccessibleName=text; b.Dock=DockStyle.Fill; b.Height=42;
            b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderSize=primary?0:1;
            b.FlatAppearance.BorderColor=Color.FromArgb(208,218,230);
            b.BackColor=primary?Color.FromArgb(35,104,205):Color.White;
            b.ForeColor=primary?Color.White:Color.FromArgb(37,57,81);
            b.Cursor=Cursors.Hand; b.Margin=new Padding(0,3,6,3);
        }
        Control Row(Control a, Control b) {
            var panel = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            panel.Controls.Add(a,0,0);panel.Controls.Add(b,1,0);return panel;
        }
        void BuildUi() {
            var layout = new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(18),Margin=new Padding(0)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,350));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Controls.Add(layout);
            var side = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=18,Padding=new Padding(0,0,18,0),Margin=new Padding(0)};
            int[] heights = {40,48,46,34,90,36,32,46,42,34,76,32,0,26,23,23,42,30};
            foreach(int h in heights) side.RowStyles.Add(new RowStyle(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));
            var ink = Color.FromArgb(27,45,65); var secondary = Color.FromArgb(88,105,123);
            side.Controls.Add(Caption("Arena 筛选助手",21,true,ink),0,0);
            ConfigureButton(autoLogin,"自动注册 / 登录",true);ConfigureButton(cancelLogin,"停止登录",false);
            side.Controls.Add(Row(autoLogin,cancelLogin),0,1);
            ConfigureButton(open,"打开 Arena / 登录",true);ConfigureButton(demo,"离线演示",false);
            side.Controls.Add(Row(open,demo),0,2);
            ConfigureButton(attachmentsButton,"附件设置",false);RefreshAttachments();
            side.Controls.Add(Row(Caption("提示词",11,true,ink),attachmentsButton),0,3);
            attachmentsButton.Click+=(s,e)=>EditAttachments();
            prompt.Multiline=true;prompt.ScrollBars=ScrollBars.Vertical;prompt.Dock=DockStyle.Fill;prompt.Text=taskSettings.Prompt;
            prompt.Leave+=(s,e)=>SaveTaskSettings();
            prompt.AccessibleName="提示词";prompt.BackColor=Color.White;prompt.Margin=new Padding(0,0,5,6);
            side.Controls.Add(prompt,0,4);
            side.Controls.Add(Caption("不限尝试次数 · 可随时暂停",10,false,secondary),0,5);
            current.Text="接着右侧当前对话筛选";current.Checked=false;current.Dock=DockStyle.Fill;current.AccessibleName=current.Text;
            side.Controls.Add(current,0,6);
            ConfigureButton(start,"开始筛选",true);ConfigureButton(pause,"暂停",false);
            side.Controls.Add(Row(start,pause),0,7);
            ConfigureButton(resume,"继续",false);ConfigureButton(restart,"重新开始",false);
            side.Controls.Add(Row(resume,restart),0,8);
            count.Text="已尝试 0 次  ·  已排除 0 次";count.Dock=DockStyle.Fill;count.TextAlign=ContentAlignment.MiddleLeft;count.ForeColor=ink;
            count.Font=new Font(Font,FontStyle.Bold);side.Controls.Add(count,0,9);
            status.Text="正在准备内置浏览器…";status.Dock=DockStyle.Fill;status.BackColor=Color.FromArgb(228,237,250);
            status.ForeColor=Color.FromArgb(24,72,128);status.Padding=new Padding(10);status.AccessibleName="当前状态";
            side.Controls.Add(status,0,10);
            side.Controls.Add(Caption("运行记录",11,true,ink),0,11);
            log.Dock=DockStyle.Fill;log.HorizontalScrollbar=true;log.BorderStyle=BorderStyle.FixedSingle;log.Font=new Font("Microsoft YaHei UI",9F);log.AccessibleName="运行记录";
            side.Controls.Add(log,0,12);
            side.Controls.Add(Caption("回答超过 2 分钟仍持续检测，完成后自动收图。",9,false,secondary),0,13);
            side.Controls.Add(Caption("遇到验证码会自动转到账号页并等待。",9,false,secondary),0,14);
            side.Controls.Add(Caption("无 Thinking 仅为候选，模型需自行确认。",9,false,secondary),0,15);
            ConfigureGalleryControls(side);
            var right = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0)};
            right.RowStyles.Add(new RowStyle(SizeType.Absolute,34));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            mode.Text="欢迎使用 · 操作都在这个窗口里完成";mode.Dock=DockStyle.Fill;mode.ForeColor=secondary;mode.TextAlign=ContentAlignment.MiddleLeft;
            right.Controls.Add(mode,0,0);browser.DefaultBackgroundColor=Color.White;AddBrowserTabs(right);
            layout.Controls.Add(side,0,0);layout.Controls.Add(right,1,0);
            open.Click += (s,e) => OpenWebsite();demo.Click += (s,e) => OpenDemo();
            autoLogin.Click += (s,e) => StartAutoLogin();cancelLogin.Click += (s,e) => {loginTaskStart.Cancel();authFlow.Stop("已停止自动登录，保留当前页面和账号");};
            start.Click += (s,e) => StartRun();restart.Click += (s,e) => StartRun();
            pause.Click += (s,e) => PauseAutomation();
            resume.Click += (s,e) => {try {controller.Resume();}catch(Exception ex){ShowError(ex.Message);}};
            timer.Interval=1000;timer.Tick += async (s,e) => await CollectionTick();
            SetEnabled();
        }
        async Task Initialize() {
            try {
                Directory.CreateDirectory(DataDirectory);
                var env = await CoreWebView2Environment.CreateAsync(null,Path.Combine(DataDirectory,"Browser"));
                await browser.EnsureCoreWebView2Async(env);
                if(!await InitializeLogin(env)){Close();return;}
                browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
                browser.CoreWebView2.Settings.IsGeneralAutofillEnabled=false;
                browser.CoreWebView2.NewWindowRequested += (s,e) => {e.Handled=true;NavigatePopup(e.Uri);};
                browser.CoreWebView2.NavigationCompleted += async (s,e) => {
                    if(!e.IsSuccess) ShowError("网页打开失败："+e.WebErrorStatus+"。请检查网络后重新打开 Arena");
                    else if(page != null && !page.Demo && browser.Source.Host == "arena.ai") {
                        // Responsive layout hides the login/navigation controls.
                        try { await browser.CoreWebView2.ExecuteScriptAsync("(()=>{const b=[...document.querySelectorAll('button')].find(e=>(e.getAttribute('aria-label')||e.textContent||'').trim()==='Expand sidebar');if(b)b.click();})()"); } catch {}
                    }
                    await CaptureForQa();
                };
                browser.CoreWebView2.ProcessFailed += (s,e) => {if(controller!=null)controller.Pause("网页进程异常，请重新打开软件");};
                string assets=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets");
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("arena-demo.local",assets,CoreWebView2HostResourceAccessKind.DenyCors);
                page=new WebPage(browser);attachmentUpload=new AttachmentUpload(browser);controller=new RetryController(page,null,attachmentUpload);controller.Changed+=OnChanged;
                InitializeGallery();
                recovery=new ConversationRecovery(browser,DataDirectory,()=>ready&&!collecting&&!authFlow.Running,message=>CollectionStatus(message),()=>{collectionAttempted=true;controller.CancelForRecovery();});
                FormClosed+=(s,e)=>recovery.Dispose();
                ready=true;timer.Start();networkTimer.Start();
                if(demoAtStart)OpenDemo();else if(Environment.GetEnvironmentVariable("ARENA_AUTO_LOGIN")=="1")BeginAutoLogin();else{browser.CoreWebView2.Navigate("https://arena-demo.local/welcome.html");OnChanged();}
            } catch(Exception ex){ShowError("内置浏览器无法启动："+ex.Message+"。本软件需要系统已有的 Microsoft Edge WebView2。");}
            SetEnabled();
        }
        void StartRun() {
            if(!ready)return;
            try {PrepareTask();controller.Start(prompt.Text,0,current.Checked);}catch(Exception ex){ShowError(ex.Message);}
            SetEnabled();
        }
        void OpenWebsite() {
            if(!ready)return;
            loginTaskStart.Cancel();
            browserTabs.SelectedIndex=0;
            controller.Pause("请在右侧登录 Arena。登录后点“开始筛选”");page.Demo=false;
            mode.Text="在线模式 · Arena 网站需要联网 · 登录状态仅保存在本机";
            browser.CoreWebView2.Navigate("https://arena.ai/agent");
        }
        void OpenDemo() {
            if(!ready)return;
            loginTaskStart.Cancel();
            browserTabs.SelectedIndex=0;
            controller.Pause("离线演示已打开。点“开始筛选”，体验一次自动重试");page.Demo=true;
            mode.Text="离线演示 · 模拟回答，不连接 Arena，不消耗网站额度";
            browser.CoreWebView2.Navigate("https://arena-demo.local/demo.html");
        }
        void OnChanged() {
            status.Text=controller.Message;count.Text="已尝试 "+controller.Attempt+(controller.Limit>0?" / "+controller.Limit:"")+" 次  ·  已排除 "+controller.Rejected+" 次";
            bool logChange=controller.Phase!="cooldown"||previousPhase!="cooldown";previousPhase=controller.Phase;
            if(previousMessage!=controller.Message&&logChange){
                previousMessage=controller.Message;string entry=DateTime.Now.ToString("HH:mm:ss")+"  "+controller.Message;
                log.Items.Add(entry);if(log.Items.Count>200)log.Items.RemoveAt(0);log.TopIndex=log.Items.Count-1;
                try{File.AppendAllText(Path.Combine(DataDirectory,"运行记录.txt"),entry+Environment.NewLine);}catch{}
            }
            SetEnabled();
            QueueQaCapture();
            HandleRateLimitSignal();
            HandleVerificationSignal(false);
        }
        async void QueueQaCapture() { await CaptureForQa(); }
        // Test-only, opt-in capture of this application's own rendered client area.
        // No screen capture, other windows, or remote transport is involved.
        async Task CaptureForQa() {
            string folder=Environment.GetEnvironmentVariable("ARENA_QA_CAPTURE");
            if(String.IsNullOrEmpty(folder)||capturing||!ready||IsDisposed)return;
            capturing=true;
            try {
                await Task.Delay(150);
                using(var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height)) {
                    using(var graphics=Graphics.FromImage(bitmap)) {
                        graphics.Clear(BackColor);PaintNativeChildren(this,graphics);
                        using(var stream=new MemoryStream()) {
                            WebView2 visibleBrowser=browserTabs.SelectedIndex==1?authBrowser:browser;
                            await visibleBrowser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                            stream.Position=0;using(var rendered=Image.FromStream(stream)) {
                                Point point=PointToClient(visibleBrowser.PointToScreen(Point.Empty));
                                graphics.DrawImage(rendered,new Rectangle(point,visibleBrowser.Size));
                            }
                        }
                    }
                    Directory.CreateDirectory(folder);bitmap.Save(Path.Combine(folder,"app-preview.png"),ImageFormat.Png);
                }
            }catch(Exception ex){try{File.WriteAllText(Path.Combine(folder,"capture-error.txt"),ex.Message);}catch{}}
            finally{capturing=false;}
        }
        void PaintNativeChildren(Control parent,Graphics graphics) {
            foreach(Control child in parent.Controls) {
                if(!child.Visible||child==browser)continue;
                if(child.HasChildren && (child is Panel || child is TableLayoutPanel || child is FlowLayoutPanel)) {PaintNativeChildren(child,graphics);continue;}
                using(var part=new Bitmap(Math.Max(1,child.Width),Math.Max(1,child.Height))){child.DrawToBitmap(part,new Rectangle(Point.Empty,child.Size));graphics.DrawImageUnscaled(part,PointToClient(child.PointToScreen(Point.Empty)));}
            }
        }
        void SetEnabled() {
            bool running=replacing||collecting||(controller!=null && controller.Running);
            bool loggingIn=authFlow!=null&&authFlow.Running;
            changeAccount.Enabled=ready&&!loggingIn&&!collecting&&!replacing;
            rateLimitTest.Enabled=ready;
            switchIp.Enabled=ready&&!switchingIp&&!running&&!loggingIn;
            attachmentsButton.Enabled=ready&&!running&&!loggingIn;
            galleryButton.Enabled=ready;collectButton.Enabled=ready&&!running&&!loggingIn;keepCollecting.Enabled=!running;
            autoLogin.Enabled=ready&&!running&&!loggingIn;cancelLogin.Enabled=ready&&loggingIn;
            open.Enabled=demo.Enabled=ready&&!running&&!loggingIn;start.Enabled=ready&&!running&&!loggingIn;
            restart.Enabled=ready&&!running&&!loggingIn;pause.Enabled=ready&&running;
            resume.Enabled=ready&&!running&&!loggingIn&&controller!=null&&!controller.Finished&&controller.Phase!="idle";
            prompt.ReadOnly=running;current.Enabled=!running;
        }
        void ShowError(string text) { status.Text=text;log.Items.Add(text); }
    }
}
