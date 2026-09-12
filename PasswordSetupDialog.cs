using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed class PasswordSetupDialog : Form {
        readonly TextBox password=new TextBox {Dock=DockStyle.Fill,UseSystemPasswordChar=true};
        readonly TextBox confirmation=new TextBox {Dock=DockStyle.Fill,UseSystemPasswordChar=true};
        readonly Label error=new Label {Dock=DockStyle.Fill,ForeColor=Color.Firebrick,TextAlign=ContentAlignment.MiddleLeft};
        public string SelectedPassword {get;private set;}
        public PasswordSetupDialog() {
            Text="首次使用 · 设置邮箱账号密码";ClientSize=new Size(540,285);StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;Font=new Font("Microsoft YaHei UI",10F);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=2,RowCount=6};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            var hint=new Label {Text="请设置软件注册和更换邮箱账号时使用的密码。至少 8 位，并包含一个大写字母和一个符号。密码只在本机加密保存。",Dock=DockStyle.Fill,AutoSize=false};
            layout.Controls.Add(hint,0,0);layout.SetColumnSpan(hint,2);
            layout.Controls.Add(new Label {Text="输入密码",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,1);layout.Controls.Add(password,1,1);
            layout.Controls.Add(new Label {Text="再次输入",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,2);layout.Controls.Add(confirmation,1,2);
            layout.Controls.Add(error,0,3);layout.SetColumnSpan(error,2);
            var save=new Button {Text="保存并进入软件",Dock=DockStyle.Right,Width=165,Height=36};
            save.Click+=(s,e)=>SavePassword();layout.Controls.Add(save,1,5);Controls.Add(layout);AcceptButton=save;
        }
        void SavePassword() {
            string problem=PasswordPolicy.Error(password.Text);
            if(problem==""&&password.Text!=confirmation.Text)problem="两次输入的密码不一致";
            if(problem!=""){error.Text=problem;password.Focus();return;}
            SelectedPassword=password.Text;DialogResult=DialogResult.OK;
        }
        public static bool Configure(IWin32Window owner,AccountStore store) {
            using(var dialog=new PasswordSetupDialog()) {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return false;
                var account=store.Load();account.Password=dialog.SelectedPassword;store.Save(account);return true;
            }
        }
    }
}
