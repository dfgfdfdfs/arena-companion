using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace ArenaCompanion {
    public sealed class SavedConversation {
        public string Id {get;set;}
        public string Title {get;set;}
        public string Email {get;set;}
        public string Url {get;set;}
    }
    public sealed class SavedConversationStore {
        readonly string preferenceFile;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        public string Destination {get;private set;}
        public SavedConversationStore(string preferences) {
            preferenceFile=preferences;Destination=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if(File.Exists(preferences)) {string saved=File.ReadAllText(preferences).Trim();if(Directory.Exists(saved))Destination=saved;}
        }
        public void SetDestination(string path) {
            string full=Path.GetFullPath(path);
            if(!Directory.Exists(full))throw new DirectoryNotFoundException("保存位置不存在");
            File.WriteAllText(preferenceFile,full);Destination=full;
        }
        public string Save(CandidateRecord record) {
            if(!CandidatePage.CandidateUrl(record.Url))throw new InvalidOperationException("无效的原对话地址");
            var account=new AccountStore(record.Profile).Load();
            if(!String.Equals(account.Email,record.Email,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(account.Password))throw new InvalidOperationException("未找到与该候选一致的已保存账号密码");
            string name=record.Title;foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
            string folder=Path.Combine(Destination,name+" · "+record.Id.Substring(0,8));
            Directory.CreateDirectory(folder);
            var saved=new SavedConversation {Id=record.Id,Title=record.Title,Email=record.Email,Url=record.Url};
            new AccountStore(folder).Save(account);
            File.WriteAllText(Path.Combine(folder,"conversation.json"),json.Serialize(saved));
            File.WriteAllText(Path.Combine(folder,"对话信息.txt"),record.Title+Environment.NewLine+"账号："+record.Email+Environment.NewLine+"原对话："+record.Url+Environment.NewLine+"双击“打开对话”可自动登录并打开原对话。账号密码保存在 account.dpapi，使用本机 Windows 用户加密；链接需要本机筛选助手。请保留整个文件夹。"+Environment.NewLine);
            CreateShortcut(Path.Combine(folder,"打开对话.lnk"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Arena筛选助手.exe"),folder);
            return folder;
        }
        public static SavedConversation Load(string folder) {
            var data=new JavaScriptSerializer().Deserialize<SavedConversation>(File.ReadAllText(Path.Combine(folder,"conversation.json")));
            if(data==null||!CandidatePage.CandidateUrl(data.Url))throw new InvalidOperationException("保存的对话地址无效");
            var account=new AccountStore(folder).Load();
            if(!String.Equals(account.Email,data.Email,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(account.Password))throw new InvalidOperationException("保存的账号与对话不匹配");
            return data;
        }
        static void CreateShortcut(string path,string executable,string folder) {
            object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")),link=null;
            try {
                link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
                var t=link.GetType();
                t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{executable});
                t.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--saved \""+folder+"\""});
                t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(executable)});
                t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
            }finally {if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
        }
    }
}
