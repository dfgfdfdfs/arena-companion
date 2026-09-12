using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed class InstancePicker : Form {
        readonly ListBox list=new ListBox {Dock=DockStyle.Fill};
        readonly TextBox name=new TextBox {Dock=DockStyle.Top};
        readonly Button rename=new Button {Text="重命名选中实例",Dock=DockStyle.Fill};
        readonly Button delete=new Button {Text="删除选中实例",Dock=DockStyle.Fill};
        public string SelectedName {get;private set;}
        public InstancePicker() {
            Text="Arena 多开 · 选择独立实例";ClientSize=new Size(540,470);MinimumSize=new Size(500,430);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",10);Padding=new Padding(16);
            var hint=new Label {Text="每个实例独立保存邮箱、登录态、任务及图集。\n选择已有实例，或填写新名字。重命名会保留全部数据；删除会移入回收站。",Dock=DockStyle.Top,Height=72};
            var actions=new TableLayoutPanel {Dock=DockStyle.Bottom,Height=88,ColumnCount=2,RowCount=2,Padding=new Padding(0,8,0,0)};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute,36));actions.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            var launch=new Button {Text="打开 / 新建此实例",Dock=DockStyle.Fill};
            actions.Controls.Add(rename,0,0);actions.Controls.Add(delete,1,0);actions.Controls.Add(launch,0,1);actions.SetColumnSpan(launch,2);
            Controls.Add(list);Controls.Add(name);Controls.Add(hint);Controls.Add(actions);
            Directory.CreateDirectory(InstanceContext.Root);
            ReloadInstances(null);
            list.SelectedIndexChanged+=(s,e)=>{bool selected=list.SelectedItem!=null;rename.Enabled=delete.Enabled=selected;if(selected)name.Text=Convert.ToString(list.SelectedItem);};
            launch.Click+=(s,e)=>{try{InstanceContext.DirectoryFor(InstanceContext.Root,name.Text);SelectedName=name.Text;DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
            rename.Click+=(s,e)=>RenameSelected();delete.Click+=(s,e)=>DeleteSelected();
            AcceptButton=launch;
            Shown+=(s,e)=>CaptureForQa();
        }
        void ReloadInstances(string selectedName) {
            list.Items.Clear();
            foreach(string folder in Directory.GetDirectories(InstanceContext.Root))list.Items.Add(Path.GetFileName(folder));
            rename.Enabled=delete.Enabled=false;
            if(!String.IsNullOrEmpty(selectedName)) {
                int index=list.Items.IndexOf(selectedName);if(index>=0)list.SelectedIndex=index;
            }
            if(list.SelectedItem==null)name.Text="实例 "+(list.Items.Count+1);
        }
        void RenameSelected() {
            if(list.SelectedItem==null)return;
            string current=Convert.ToString(list.SelectedItem);
            try {string renamed=InstanceManager.Rename(InstanceContext.Root,current,name.Text);ReloadInstances(renamed);}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"无法重命名实例",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        void DeleteSelected() {
            if(list.SelectedItem==null)return;
            string selected=Convert.ToString(list.SelectedItem);
            string message="确定删除实例“"+selected+"”吗？\n\n邮箱账号、浏览器登录态、任务设置和候选图集都会移入回收站。";
            if(MessageBox.Show(this,message,"删除实例",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            try {InstanceManager.Delete(InstanceContext.Root,selected,true);ReloadInstances(null);}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"无法删除实例",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
        void CaptureForQa() {
            string path=Environment.GetEnvironmentVariable("ARENA_PICKER_QA_CAPTURE");
            if(String.IsNullOrWhiteSpace(path))return;
            try {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path);}}
            catch(IOException){}
        }
    }
}
