using System;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace ArenaCompanion {
    public sealed partial class MainForm {
        bool replacing;
        readonly LoginTaskStart loginTaskStart=new LoginTaskStart(Environment.GetEnvironmentVariable("ARENA_RUN_AFTER_LOGIN")=="1");
        readonly Button changeAccount=new Button {Text="更换邮箱并开始任务",AutoSize=true,Height=36};
        readonly Button rateLimitTest=new Button {Text="测试",AutoSize=true,Height=36};
        void AddAccountReplacement(TabPage tab) {
            var header=new TableLayoutPanel {Dock=DockStyle.Top,Height=150,ColumnCount=1,RowCount=2,Padding=new Padding(6),BackColor=System.Drawing.Color.FromArgb(244,247,251)};
            header.RowStyles.Add(new RowStyle(SizeType.Absolute,45));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var bar=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
            bar.Controls.Add(changeAccount);bar.Controls.Add(new Label {Text="新窗口接手后关闭旧窗口；登录后自动开始",AutoSize=true,Padding=new Padding(5,10,0,0)});
            ConfigureNetworkSwitch(bar);bar.Controls.Add(rateLimitTest);header.Controls.Add(bar,0,0);header.Controls.Add(ipStatus,0,1);tab.Controls.Add(header);
            changeAccount.Click+=async(s,e)=>{try{await CreateReplacement();}catch(Exception ex){ShowError(ex.Message);}};
            rateLimitTest.Click+=(s,e)=>{};
        }
        async Task<object> CreateReplacement() {
            if(!ready||authFlow.Running||collecting||replacing)throw new InvalidOperationException("请等待当前登录、收集或账号交接完成");
            replacing=true;loginTaskStart.Cancel();controller.Pause("正在保存任务并更换邮箱，新窗口准备好后关闭此窗口");SetEnabled();
            try {
                SaveTaskSettings();string name=AccountReplacement.Create(InstanceContext.Root,DataDirectory,InstanceContext.Name);
                int pid=await ReplacementHandoff.Launch(InstanceContext.Root,name);
                CloseAfterHandoff();return new {ok=true,instance=name,directory=InstanceContext.DirectoryFor(InstanceContext.Root,name),pid,closingOldWindow=true};
            }catch{replacing=false;SetEnabled();throw;}
        }
        async void CloseAfterHandoff(){await Task.Delay(350);if(!IsDisposed)Close();}
        bool StartTaskAfterLogin() {
            if(!loginTaskStart.Take(authFlow.Running,authFlow.Phase))return false;
            try{current.Checked=false;PrepareTask();controller.Start(prompt.Text,0,false);}
            catch(Exception ex){ShowError("登录已完成，但任务启动失败："+ex.Message);}
            return true;
        }
    }
}
