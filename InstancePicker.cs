using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed class InstancePicker : Form {
        readonly ListBox list=new ListBox {Dock=DockStyle.Fill};
        readonly TextBox name=new TextBox {Dock=DockStyle.Top};
        public string SelectedName {get;private set;}
        public InstancePicker() {
            Text="Arena 多开 · 选择独立实例";ClientSize=new Size(480,370);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",10);Padding=new Padding(16);
            var hint=new Label {Text="每个实例独立保存邮箱、登录态、任务及图集。\n选择已有实例，或填写新名字。",Dock=DockStyle.Top,Height=72};
            var launch=new Button {Text="打开 / 新建此实例",Dock=DockStyle.Bottom,Height=42};
            Controls.Add(list);Controls.Add(name);Controls.Add(hint);Controls.Add(launch);
            Directory.CreateDirectory(InstanceContext.Root);
            foreach(string folder in Directory.GetDirectories(InstanceContext.Root))list.Items.Add(Path.GetFileName(folder));
            name.Text="实例 "+(list.Items.Count+1);
            list.SelectedIndexChanged+=(s,e)=>{if(list.SelectedItem!=null)name.Text=Convert.ToString(list.SelectedItem);};
            launch.Click+=(s,e)=>{try{InstanceContext.DirectoryFor(InstanceContext.Root,name.Text);SelectedName=name.Text;DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            AcceptButton=launch;
        }
    }
}
