using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArenaCompanion {
    public sealed partial class MainForm {
        readonly Button attachmentsButton=new Button();
        readonly ToolTip attachmentTip=new ToolTip();
        TaskSettingsStore taskStore;
        TaskSettings taskSettings;
        AttachmentUpload attachmentUpload;
        void InitializeTaskSettings() {
            string directory=Environment.GetEnvironmentVariable("ARENA_SETTINGS_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Arena筛选助手");
            taskStore=new TaskSettingsStore(directory);taskSettings=taskStore.Load(DefaultPrompt);
        }
        void RefreshAttachments() {
            attachmentsButton.Text="附件："+taskSettings.Attachments.Count+" 个（设置）";
            attachmentTip.SetToolTip(attachmentsButton,String.Join(Environment.NewLine,taskSettings.Attachments.ConvertAll(f=>f.Name)));
        }
        void EditAttachments() {
            using(var dialog=new AttachmentDialog(taskStore,taskSettings.Attachments)) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                taskSettings.Attachments=dialog.Files;SaveTaskSettings();RefreshAttachments();
            }
        }
        void SaveTaskSettings() {taskSettings.Prompt=prompt.Text;taskSettings.ContinueCollecting=keepCollecting.Checked;taskStore.Save(taskSettings);}
        void PrepareTask() {
            SaveTaskSettings();
            attachmentUpload.Configure(page.Demo?new List<BoundAttachment>():taskSettings.Attachments);
        }
        async Task<object> TaskControl(Dictionary<string,object> request) {
            string command=AuthFlow.Value(request,"command");
            if(command=="settings.get")return new {ok=true,settings=taskSettings};
            if(command=="attachments.inspect")return new {ok=true,details=await attachmentUpload.Inspect()};
            if(authFlow.Running||controller.Running)throw new InvalidOperationException("请先停止当前自动流程");
            if(command=="settings.configure") {
                if(request.ContainsKey("prompt"))prompt.Text=AuthFlow.Value(request,"prompt");
                if(request.ContainsKey("paths")) {
                    var files=new List<BoundAttachment>();
                    foreach(object path in (IEnumerable)request["paths"])files.Add(taskStore.Import(Convert.ToString(path)));
                    TaskSettingsStore.Verify(files);taskSettings.Attachments=files;
                }
                SaveTaskSettings();RefreshAttachments();return new {ok=true,settings=taskSettings};
            }
            if(command=="attachments.stage") {
                PrepareTask();await page.Read(prompt.Text);await attachmentUpload.Stage();return new {ok=true};
            }
            if(command=="terms.accept") {await page.Read(prompt.Text);await page.Act("terms",prompt.Text);return new {ok=true};}
            if(command=="terms.dismiss") {await page.Read(prompt.Text);await page.Act("dismissTerms",prompt.Text);return new {ok=true};}
            if(command=="draft.replace") {await page.ReplaceDraft(AuthFlow.Value(request,"expected"),prompt.Text);return new {ok=true};}
            throw new ArgumentException("未知任务设置操作");
        }
    }
}
