using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed class AttachmentDialog : Form {
        readonly ListBox list=new ListBox {Dock=DockStyle.Fill,SelectionMode=SelectionMode.MultiExtended};
        readonly List<BoundAttachment> files;
        readonly TaskSettingsStore store;
        public List<BoundAttachment> Files {get {return files;}}
        public AttachmentDialog(TaskSettingsStore store,IEnumerable<BoundAttachment> existing) {
            this.store=store;files=new List<BoundAttachment>(existing);
            Text="绑定每轮自动上传的附件";ClientSize=new Size(600,350);StartPosition=FormStartPosition.CenterParent;
            var note=new Label {Dock=DockStyle.Top,Height=55,Text="选择一次，软件保留副本；每次新对话自动上传。\n可以添加多个文件，移除绑定不会删除桌面原文件。",Padding=new Padding(10)};
            var row=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=45};
            var add=new Button {Text="添加文件",AutoSize=true};var remove=new Button {Text="移除选中",AutoSize=true};
            var save=new Button {Text="保存绑定",AutoSize=true,DialogResult=DialogResult.OK};
            row.Controls.Add(add);row.Controls.Add(remove);row.Controls.Add(save);
            Controls.Add(list);Controls.Add(note);Controls.Add(row);AcceptButton=save;
            add.Click+=(s,e)=>AddFiles();remove.Click+=(s,e)=>RemoveFiles();RefreshList();
        }
        void RefreshList() {list.Items.Clear();foreach(var file in files)list.Items.Add(file.Name+"  ("+file.Bytes+" 字节)");}
        void AddFiles() {
            using(var picker=new OpenFileDialog {Multiselect=true,Title="选择本轮及以后自动上传的附件",Filter="所有文件|*.*"}) {
                if(picker.ShowDialog(this)!=DialogResult.OK)return;
                try {
                    foreach(string path in picker.FileNames) {
                        BoundAttachment file=store.Import(path);
                        files.RemoveAll(item=>String.Equals(item.Name,file.Name,StringComparison.OrdinalIgnoreCase));files.Add(file);
                    }
                    RefreshList();
                }catch(Exception ex){MessageBox.Show(this,ex.Message,"附件未能绑定");}
            }
        }
        void RemoveFiles() {foreach(int index in list.SelectedIndices.Cast<int>().OrderByDescending(i=>i))files.RemoveAt(index);RefreshList();}
    }
}
