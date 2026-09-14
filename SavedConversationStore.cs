using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
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
            string accountRoot=Path.Combine(Destination,"账号");Directory.CreateDirectory(accountRoot);
            string folder=Path.Combine(accountRoot,name+" · "+record.Id.Substring(0,8));
            Directory.CreateDirectory(folder);
            var saved=new SavedConversation {Id=record.Id,Title=record.Title,Email=record.Email,Url=record.Url};
            new AccountStore(folder).Save(account);
            File.WriteAllText(Path.Combine(folder,"conversation.json"),json.Serialize(saved));
            File.WriteAllText(Path.Combine(folder,"对话信息.txt"),record.Title+Environment.NewLine+"账号："+record.Email+Environment.NewLine+"原对话："+record.Url+Environment.NewLine+"双击“打开对话”可自动登录并打开原对话。账号密码保存在 account.dpapi，使用本机 Windows 用户加密；链接需要本机筛选助手。当前对话文件夹可以自由重命名。"+Environment.NewLine);
            CreateShortcut(Path.Combine(folder,"打开对话.lnk"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Arena筛选助手.exe"),accountRoot,record.Id);
            return folder;
        }
        public static SavedConversation Load(string folder) {
            var data=new JavaScriptSerializer().Deserialize<SavedConversation>(File.ReadAllText(Path.Combine(folder,"conversation.json")));
            if(data==null||!CandidatePage.CandidateUrl(data.Url))throw new InvalidOperationException("保存的对话地址无效");
            var account=new AccountStore(folder).Load();
            if(!String.Equals(account.Email,data.Email,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(account.Password))throw new InvalidOperationException("保存的账号与对话不匹配");
            return data;
        }
        public static string ResolveById(string root,string id) {
            Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))throw new InvalidOperationException("保存的对话编号无效");
            string full=Path.GetFullPath(root);if(!Directory.Exists(full))throw new DirectoryNotFoundException("账号保存目录不存在");
            string[] matches=Directory.GetDirectories(full).Where(directory=>ConversationId(directory)==id).ToArray();
            if(matches.Length==0)throw new DirectoryNotFoundException("没有找到保存的对话；请确认整个对话文件夹仍在账号目录内");
            if(matches.Length>1)throw new InvalidOperationException("账号目录中存在重复的对话编号，请保留一个副本后重试");
            Load(matches[0]);return matches[0];
        }
        public static string ResolveLegacyFolder(string requested) {
            string full=Path.GetFullPath(requested);if(Directory.Exists(full)){Load(full);return full;}
            string parent=Path.GetDirectoryName(full),name=Path.GetFileName(full);int marker=name.LastIndexOf(" · ",StringComparison.Ordinal);
            string hint=marker<0?"":name.Substring(marker+3);
            if(String.IsNullOrEmpty(parent)||!Directory.Exists(parent)||hint.Length!=8||!hint.All(Uri.IsHexDigit))throw new DirectoryNotFoundException("保存的对话文件夹不存在");
            string[] matches=Directory.GetDirectories(parent).Where(directory=>ConversationId(directory).StartsWith(hint,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)throw new DirectoryNotFoundException("重命名后的对话文件夹无法唯一确认");
            Load(matches[0]);return matches[0];
        }
        static string ConversationId(string directory) {
            try {
                string path=Path.Combine(directory,"conversation.json");if(!File.Exists(path))return "";
                var data=new JavaScriptSerializer().Deserialize<SavedConversation>(File.ReadAllText(path));return data==null?"":data.Id??"";
            } catch {return "";}
        }
        static void CreateShortcut(string path,string executable,string root,string id) {
            object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")),link=null;
            try {
                link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
                var t=link.GetType();
                t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{executable});
                t.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--saved-root \""+root+"\" --saved-id \""+id+"\""});
                t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(executable)});
                t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
            }finally {if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
        }
    }
}
