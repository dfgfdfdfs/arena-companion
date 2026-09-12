using System;
using System.Drawing;
using System.Windows.Forms;
namespace ArenaCompanion {
    public sealed class AccountSetupDialog : Form {
        readonly TextBox email=new TextBox {Name="email",Dock=DockStyle.Fill};
        readonly TextBox password=new TextBox {Name="password",UseSystemPasswordChar=true,Dock=DockStyle.Fill};
        readonly TextBox confirmation=new TextBox {Name="confirmation",UseSystemPasswordChar=true,Dock=DockStyle.Fill};
        readonly Label error=new Label {Name="error",ForeColor=Color.Firebrick,Dock=DockStyle.Fill};
        public AccountSetupDialog(AccountStore store) {
            Text="首次使用 · 设置 Arena 账号";ClientSize=new Size(540,410);MinimumSize=Size;
            StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
            if(Environment.GetEnvironmentVariable("ARENA_BACKGROUND")=="1"){Opacity=0;ShowInTaskbar=false;}
            var panel=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=10};
            foreach(int height in new[]{56,25,34,25,34,25,34,48,44,45})panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            panel.Controls.Add(new Label {Text="请输入 Arena 账号信息。邮箱留空时自动获取新邮箱。\n这里设置的是 Arena 注册 / 登录密码，不是邮箱服务的密码。",Dock=DockStyle.Fill},0,0);
            panel.Controls.Add(new Label {Text="邮箱（已有账号请填写；新账号可留空）",Dock=DockStyle.Fill},0,1);panel.Controls.Add(email,0,2);
            panel.Controls.Add(new Label {Text="账号密码",Dock=DockStyle.Fill},0,3);panel.Controls.Add(password,0,4);
            panel.Controls.Add(new Label {Text="再次输入密码",Dock=DockStyle.Fill},0,5);panel.Controls.Add(confirmation,0,6);
            panel.Controls.Add(new Label {Text=AccountSetup.PasswordHint,Dock=DockStyle.Fill},0,7);panel.Controls.Add(error,0,8);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
            var save=new Button {Name="save",Text="保存并进入",Width=130,Height=34};var cancel=new Button {Text="取消",Width=90,Height=34,DialogResult=DialogResult.Cancel};
            actions.Controls.Add(save);actions.Controls.Add(cancel);panel.Controls.Add(actions,0,9);Controls.Add(panel);AcceptButton=save;CancelButton=cancel;
            email.Text=store.Load().Email??"";
            save.Click+=(s,e)=>{try{AccountSetup.Save(store,email.Text,password.Text,confirmation.Text);DialogResult=DialogResult.OK;}catch(ArgumentException ex){error.Text=ex.Message;}catch(System.IO.IOException){error.Text="账号设置未能保存，请检查数据目录是否可写";}};
        }
    }
}
